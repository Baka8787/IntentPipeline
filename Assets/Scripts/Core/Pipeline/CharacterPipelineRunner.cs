using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Arbitration;
using Project.Core.Blackboard;
using Project.Core.Combat;
using Project.Core.Environment;
using Project.Core.Facing;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Core.Survivability;
using Project.Presentation;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Core.Pipeline
{
    /// <summary>
    /// Inspector 上的 ActionSlot → lifecycle sink 接線。身分只負責選擇接收者；
    /// sink 本身仍維持無參數介面，不知道自己屬於哪個技能。
    /// </summary>
    [Serializable]
    public struct ActionSinkBinding
    {
        public ActionSlot Slot;
        public MonoBehaviour Sink;
    }

    // 只有「所有角色都必備、且 Runner 從同物件解析」的具體元件能列在這裡。
    // CharacterFacingSource 保證 ADR-007 D3 的單一 facing authority；MotionDriver 是所有角色共用的
    // 位移／rotation 執行者。PlayerCombatContextSource 與 IAimSource 都是玩家專屬，⛔ 不得順手加入——
    // RequireComponent 無法表達「只有玩家需要」，那條角色分類契約繼續由 PrefabWiringTests.W12 守住。
    [RequireComponent(typeof(CharacterFacingSource), typeof(MotionDriver))]
    public class CharacterPipelineRunner : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] private MonoBehaviour inputSourceComponent;

        // 🆕（ADR-003 D2）Movement 意圖 producer 的注入點（DIP）：Runner 依賴 IMovementIntentSource 介面，
        // 不認識任何具體 policy。換 AI／Replay／Network 驅動＝在 Inspector 換掛別的元件，**本檔零改**（OCP）。
        [Tooltip("實作 IMovementIntentSource 的元件（預設＝同物件上的 PlayerLocomotionPolicy）。留空時自動 GetComponent。")]
        [SerializeField] private MonoBehaviour movementIntentSourceComponent;

        // 🆕（ADR-003 D3／D4 Stage 2）active Movement Model 的注入點（DIP）：Runner 依賴 IMovementModel 介面，
        // **不認識 locomotion**（沒有 MoveSpeed、沒有平滑時間、沒有 gait）。換 Swim／Vehicle model＝換掛元件，本檔零改。
        // ⚠️ 未來 MovementContext（env-driven，ADR-003 §9-L2／Stage 3）落地時，換的是「誰填這個欄位」，不是這裡的形狀。
        [Tooltip("實作 IMovementModel 的元件（預設＝同物件上的 LocomotionModel）。留空時自動 GetComponent。")]
        [SerializeField] private MonoBehaviour movementModelComponent;

        // 🆕（2026-07-27）角色階層**之外**的仲裁來源注入點（DIP）：應用層的全域狀態
        // （首例＝GamePauseController 的暫停要求封鎖輸入）依定義不掛在角色階層上，
        // Start 的 GetComponentsInChildren 掃不到它。以 Inspector 明確引用收進來——
        // 方向正確（**角色收外部給的 source**，而非角色去查詢全域），也不需要 Singleton。
        [Tooltip("角色階層外、實作 IArbiterSource 的元件（首例＝場景中的 GamePauseController）。\n" +
                 "留空＝只用角色身上的仲裁來源，行為與加入本欄位前完全等價。")]
        [SerializeField] private MonoBehaviour[] externalArbiterSources;

        [SerializeField] private Transform playerCamera;

        private IInputSource _inputSource;
        private IMovementIntentSource _movementIntentSource;
        private IMovementModel _movementModel;
        private PlayerCombatContextSource _combatContextSource;
        private TraversalProbe _traversalProbe;

        // 🆕（ADR-009 D1）可選的生存能力。缺席時 Survivability 恆為 default ⇒ 行為與導入前完全相同。
        private CharacterHealth _characterHealth;
        private CharacterFacingSource _facingSource;
        private PlayerRuntimeData _runtimeData;

        public PlayerRuntimeData RuntimeData => _runtimeData;

        /// <summary>
        /// 🆕（2026-09-15，HUD）指定 slot 的冷卻進度：**1 ＝ 剛進冷卻、0 ＝ 可用**。
        ///
        /// <para><b>⭐ 為什麼冷卻走這條唯讀查詢，而不是加一個黑板欄位</b></para>
        /// `docs/17` §3.3 **紅線 2 明文禁止「為了顯示而新增黑板欄位」**，而同一節也點名
        /// 既有的 <c>public InputDebugSnapshot InputDebug</c> **就是正確的 pattern**：
        /// 需要被觀測的東西以**唯讀屬性／查詢**曝露，不要擠進跨系統的黑板。
        /// 本方法是那個 pattern 的第二個使用者。
        ///
        /// <para><b>只讀不寫，也不是新權威</b></para>
        /// 「能不能出手」的唯一回答者仍是 `ActionState`（ADR-004 D2）；這裡只是把問題轉送過去。
        /// ⛔ 呼叫端**不得**用它來決定要不要出手——那會讓 gate 有第二個回答者。它只回答「畫多滿」。
        ///
        /// 尚未 `Awake`／沒有狀態機時安靜回 0，HUD 不必自己判 null。
        /// </summary>
        public float GetActionCooldownNormalized(ActionSlot slot)
            => _stateMachine != null ? _stateMachine.GetActionCooldownNormalized(slot) : 0f;

        [Header("StateMachine Setup")]
        [SerializeField] private StateMachineConfigSO stateMachineConfig;
        [Tooltip("External gameplay event 的單格 Action request endpoint。留空時從同物件自動尋找；Player 可不掛。")]
        [SerializeField] private ActionRequestTarget actionRequestTarget;

        [Tooltip("每個 ActionSlot 對應的 IActionLifecycleSink。只要清單有任何一筆，就完全忽略下方 legacy 單顆欄位。")]
        [SerializeField] private List<ActionSinkBinding> actionSinkBindings = new List<ActionSinkBinding>();

        // 欄位名稱刻意保留：既有 prefab 已按名稱序列化 ThrowProjectileEmitter，改名會清空接線。
        // 新清單為空時，這顆 sink 仍比照舊行為接收所有 slot；開始填清單後則 all-or-nothing。
        [Tooltip("Legacy 單顆 sink 相容欄位。Action Sink Bindings 為空時才使用；既有 prefab 不需立即遷移。")]
        [SerializeField] private MonoBehaviour actionReleaseSinkComponent;

        private FullBodyStateMachine _stateMachine;

        // 🆕 2026-09-14：jagged，外層索引＝slot、內層＝該 slot 的 sink 清單（binding 順序）。
        private IActionLifecycleSink[][] _actionLifecycleSinks;

        // 🆕（M2）表現層驅動骨架：Start 一次性收集，LateUpdate 順序 6.5 集中 Tick。
        private PresentationPipeline _presentationPipeline;

        // 🆕（輪 4）仲裁管線：Start 一次性收集所有 IArbiterSource，Update 順序 4.5 集中 Tick。
        // Runner 只認識管線與介面，**不認識任何具體封鎖語意**（UI 模式／死亡／過場都是 source 的事）。
        private ArbiterPipeline _arbiterPipeline;

        [Header("Presentation Setup")]
        [SerializeField] private AnimationFacadeBase animationFacade; // 💡 規格：掛載 AnimancerFacade 的組件
        [SerializeField] private MotionDriver motionDriver;

        // === [新增：供 Editor 跨幀讀取的普通結構體快照] ===
        public struct InputDebugSnapshot
        {
            public Vector2 MoveInput;
            public Vector2 LookInput;
            public bool JumpButtonDown;
            public bool RollButtonDown;
            public bool Slot1ButtonDown;
            public bool SprintButtonHeld;
            public bool WalkButtonHeld;
            public bool WalkButtonDown;
        }

        private InputDebugSnapshot _inputDebug;
        public InputDebugSnapshot InputDebug => _inputDebug;

        // 🆕 記錄上一次播放的狀態，避免每幀重複 Play
        private string _lastPlayedKey;

        // 🆕 改用 None 當哨兵值，不再借用 Idle
        public StateType CurrentState => _stateMachine?.CurrentState?.Type ?? StateType.None;

        private void Awake()
        {
            _inputSource = inputSourceComponent as IInputSource;
            if (inputSourceComponent != null && _inputSource == null)
            {
                Debug.LogError($"[{gameObject.name}] inputSourceComponent 沒有實作 IInputSource 介面！", this);
            }

            if (actionRequestTarget == null) actionRequestTarget = GetComponent<ActionRequestTarget>();
            _combatContextSource = GetComponent<PlayerCombatContextSource>();
            _traversalProbe = GetComponent<TraversalProbe>();
            _characterHealth = GetComponent<CharacterHealth>();
            _facingSource = GetComponent<CharacterFacingSource>();
            ResolveActionLifecycleSinks();

            // === 🆕（ADR-003 D2）Movement 意圖 producer 解析：明確指派優先，其次同物件自動尋找 ===
            if (movementIntentSourceComponent != null)
            {
                _movementIntentSource = movementIntentSourceComponent as IMovementIntentSource;
                if (_movementIntentSource == null)
                {
                    Debug.LogError($"[{gameObject.name}] movementIntentSourceComponent 沒有實作 IMovementIntentSource 介面！", this);
                }
            }

            if (_movementIntentSource == null)
            {
                _movementIntentSource = GetComponent<IMovementIntentSource>(); // 同物件補洞（比照 motionDriver 防禦線）
                if (_movementIntentSource == null)
                {
                    Debug.LogError($"[{gameObject.name}] 缺少 Movement 意圖 producer：請在本物件掛上 PlayerLocomotionPolicy" +
                                   "（或任一 IMovementIntentSource 實作）。缺少時 MovementIntent 恆為 0，角色不會移動。", this);
                }
            }

            // === 🆕（ADR-003 D3／D4）active Movement Model 解析：同上，明確指派優先、其次同物件自動尋找 ===
            if (movementModelComponent != null)
            {
                _movementModel = movementModelComponent as IMovementModel;
                if (_movementModel == null)
                {
                    Debug.LogError($"[{gameObject.name}] movementModelComponent 沒有實作 IMovementModel 介面！", this);
                }
            }

            if (_movementModel == null)
            {
                _movementModel = GetComponent<IMovementModel>();
                if (_movementModel == null)
                {
                    Debug.LogError($"[{gameObject.name}] 缺少 Movement Model：請在本物件掛上 LocomotionModel" +
                                   "（或任一 IMovementModel 實作）。缺少時無運動輸出，角色不會移動也不會進 Move 狀態。", this);
                }
            }

            // === 💡 新增：MotionDriver 記憶體漏拖防禦線 ===
            if (motionDriver == null)
            {
                motionDriver = GetComponent<MotionDriver>(); // 試著在自己身上找組件補洞
                if (motionDriver == null)
                {
                    Debug.LogError($"[{gameObject.name}] Presentation Setup 缺少 MotionDriver，且未在 Inspector 綁定！", this);
                }
            }

            // === 💡 新增：AnimationFacade 記憶體漏拖防禦線 ===
            if (animationFacade == null)
            {
                animationFacade = GetComponent<AnimationFacadeBase>();
                if (animationFacade == null)
                {
                    Debug.LogError($"[{gameObject.name}] Presentation Setup 缺少 AnimationFacadeBase，且未在 Inspector 綁定！", this);
                }
            }

            _runtimeData = new PlayerRuntimeData
            {
                CameraTransform = playerCamera != null ? playerCamera : Camera.main?.transform
            };
        }

        /// <summary>
        /// 💡 修正：利用 Start 順序解耦，安全傳遞黑板實例，杜絕 Null 合約風險
        /// </summary>
        private void Start()
        {
            // 🆕（M2）建立表現層驅動骨架：一次性收集角色階層下所有 IPresentationController。
            // 放在狀態機檢查之前——表現管線獨立於狀態機配置，不因缺 Config 連坐停擺。
            // GetComponentsInChildren 僅在 Start 配置一次（預設不含未啟用物件），執行期零 GC。
            // 🆕（M3.x-B）第二個陣列＝表現層事件來源。收集點與時機與 Controller 完全相同，
            // **不新增管線階段**——發布發生在既有順序 6.5 的末尾（見 PresentationPipeline.Tick）。
            _presentationPipeline = new PresentationPipeline(
                GetComponentsInChildren<IPresentationController>(),
                GetComponentsInChildren<IPresentationEventSource>());

            // 🆕（輪 4）仲裁來源同樣一次性收集，同樣放在狀態機檢查之前——缺 Config 時**建構**仍會完成。
            // ⚠️ 但與 PresentationPipeline 不同，仲裁的 Tick（順序 4.5）住在 Update 內，會被上方
            //    「缺狀態機就 return」那條防線一併擋下，**不是**真的完全獨立於狀態機配置。
            //    這可接受：同一條防線也擋掉了順序 1～3，整條輸入管線都沒在跑，此時「封鎖輸入」本無意義。
            // ⚠️ 收集只在此處發生——執行期 Tick 不得再 GetComponents／配置任何集合（零 GC 熱路徑紀律）。
            _arbiterPipeline = new ArbiterPipeline(CollectArbiterSources());

            if (stateMachineConfig == null)
            {
                Debug.LogError($"[{gameObject.name}] 未綁定 StateMachineConfigSO 配置檔！", this);
                return;
            }

            _stateMachine = new FullBodyStateMachine();
            // 🆕（ADR-003 Stage 2）連同 active model 一併注入：狀態機是 model 的**唯一持有點**，
            // 由它發給所有 state，確保跨幀平滑狀態全域唯一（Idle↔Move 切換不重置收步）。
            // IAimSource 注入鏈保留給 ActionReleaseContext 的無 combat target fallback；
            // Runner 只認 Core seam，不再因組裝 Presentation 實作而把 CameraControl 洩漏進 Core。
            // facing request 已收斂到順序 4.6 的 CharacterFacingSource。
            _stateMachine.Initialize(
                stateMachineConfig, _runtimeData, _movementModel, actionRequestTarget, _actionLifecycleSinks,
                GetComponent<IAimSource>(), _traversalProbe, motionDriver);
            _facingSource?.Initialize(motionDriver, _stateMachine);
        }

        /// <summary>
        /// 組裝期把 Inspector 清單轉成 **per-slot 的 sink 陣列**（jagged，外層索引＝slot）；
        /// 執行期只做 O(1) 索引 ＋ 一次短迴圈。清單採 all-or-nothing，
        /// 避免同一 slot 同時受新舊兩個接線來源影響。
        ///
        /// <para><b>🆕 2026-09-14（使用者裁決）：一個 slot 可以有多顆 sink</b></para>
        /// 舊版把「同 slot 綁兩顆」當成錯誤。那條限制不是設計，只是還沒遇到第二個使用者——
        /// 一個 Action 本來就可能同時有命中判定、武器顯隱、刀光、VFX、音效等**彼此獨立**的副作用，
        /// 它們共用同一組 `Begin` → `Release` → `Cleanup` 時點，卻沒有理由互相認識。
        ///
        /// <list type="bullet">
        /// <item><b>順序穩定</b>：通知順序 ＝ Inspector 上的 binding 順序。
        /// sink 之間**不應該**互相依賴順序，但順序必須可預期，否則除錯時無從對照。</item>
        /// <item><b>仍然禁止的是「同一顆 sink 在同一個 slot 註冊兩次」</b>——那必定是接線手滑，
        /// 且症狀是「命中判定跑兩次／傷害變兩倍」這種很難聯想到接線的東西。</item>
        /// <item><b>同一顆 sink 綁到不同 slot 是合法的</b>（例如共用的音效 sink）。</item>
        /// <item><b>既有單顆 binding 完全相容</b>：一個 slot 一列，結果就是長度 1 的陣列。</item>
        /// </list>
        ///
        /// ⚠️ 零 GC：全部配置發生在此（組裝期）。執行期不配置、不搜尋元件。
        /// 空 slot 填 <see cref="Array.Empty{T}"/> 而非 null，讓派送端不必判 null。
        /// </summary>
        private void ResolveActionLifecycleSinks()
        {
            int slotCount = ActionState.SlotCount;
            _actionLifecycleSinks = new IActionLifecycleSink[slotCount][];
            for (int i = 0; i < slotCount; i++)
                _actionLifecycleSinks[i] = Array.Empty<IActionLifecycleSink>();

            if (actionSinkBindings != null && actionSinkBindings.Count > 0)
            {
                ResolveFromBindings(slotCount);
                return;
            }

            IActionLifecycleSink legacySink = actionReleaseSinkComponent != null
                ? actionReleaseSinkComponent as IActionLifecycleSink
                : GetComponent<IActionLifecycleSink>();
            if (actionReleaseSinkComponent != null && legacySink == null)
            {
                Debug.LogError(
                    $"[{gameObject.name}] actionReleaseSinkComponent 沒有實作 IActionLifecycleSink 介面！",
                    this);
                return;
            }

            // 舊版只有一顆 sink，語意就是所有 Action 共用。填入新清單後才改採逐 slot 路由。
            // 每個 slot 各自持有長度 1 的陣列——**刻意不共用同一個陣列實例**，
            // 讓「slot 的 sink 清單」在除錯時永遠是可獨立檢視的東西。
            if (legacySink == null) return;
            for (int i = 1; i < slotCount; i++)
                _actionLifecycleSinks[i] = new[] { legacySink };
        }

        /// <summary>
        /// 兩趟掃描：先驗證並計數，再配置剛好大小的陣列並依 binding 順序填入。
        /// 分兩趟是為了避免中途成長陣列；binding 數量是個位數，O(n²) 的重複檢查完全不是問題。
        /// </summary>
        private void ResolveFromBindings(int slotCount)
        {
            int bindingCount = actionSinkBindings.Count;
            var accepted = new bool[bindingCount];
            var countPerSlot = new int[slotCount];

            for (int i = 0; i < bindingCount; i++)
            {
                ActionSinkBinding binding = actionSinkBindings[i];
                int index = (int)binding.Slot;
                if (binding.Slot == ActionSlot.None || index < 0 || index >= slotCount)
                {
                    Debug.LogError($"[{gameObject.name}] Action Sink Binding #{i} 的 Slot 無效。", this);
                    continue;
                }

                IActionLifecycleSink sink = binding.Sink as IActionLifecycleSink;
                if (sink == null)
                {
                    Debug.LogError(
                        $"[{gameObject.name}] ActionSlot.{binding.Slot} 的 Sink 沒有實作 IActionLifecycleSink。",
                        this);
                    continue;
                }

                if (IsAlreadyAcceptedForSlot(accepted, i, binding.Slot, binding.Sink))
                {
                    Debug.LogError(
                        $"[{gameObject.name}] ActionSlot.{binding.Slot} 重複註冊了同一顆 sink " +
                        $"（{binding.Sink.GetType().Name}）；第 {i} 列已忽略。" +
                        "同一個 slot 可以有多顆**不同的** sink，但同一顆不得註冊兩次——" +
                        "那會讓該 sink 的效果（命中判定、傷害、特效）在一次 Action 裡發生兩遍。",
                        this);
                    continue;
                }

                accepted[i] = true;
                countPerSlot[index]++;
            }

            for (int slot = 1; slot < slotCount; slot++)
            {
                if (countPerSlot[slot] > 0)
                    _actionLifecycleSinks[slot] = new IActionLifecycleSink[countPerSlot[slot]];
            }

            var writeCursor = new int[slotCount];
            for (int i = 0; i < bindingCount; i++)
            {
                if (!accepted[i]) continue;
                ActionSinkBinding binding = actionSinkBindings[i];
                int index = (int)binding.Slot;
                _actionLifecycleSinks[index][writeCursor[index]++] = (IActionLifecycleSink)binding.Sink;
            }
        }

        /// <summary>同一個 slot 是否已經接受過**這一顆**元件實例。以實例比較，不是型別比較。</summary>
        private bool IsAlreadyAcceptedForSlot(
            bool[] accepted, int upToExclusive, ActionSlot slot, MonoBehaviour sinkComponent)
        {
            for (int j = 0; j < upToExclusive; j++)
            {
                if (!accepted[j]) continue;
                ActionSinkBinding earlier = actionSinkBindings[j];
                if (earlier.Slot == slot && ReferenceEquals(earlier.Sink, sinkComponent)) return true;
            }

            return false;
        }

        private void Update()
        {
            // IInputSource 是可選的：AI／Replay 等非玩家角色可以直接由順序 2.5 producer
            // 產生 domain intent。唯一會讓整條角色管線停下的是狀態機尚未完成組裝。
            if (_stateMachine == null) return;

            // 【順序 0.5】🆕（ADR-009 D1）Survivability 發布 —— Survivability 區的唯一寫入者。
            // ⚠️ 必須在順序 1（輸入）與順序 4（狀態機）**之前**：傷害是在 Physics 階段
            //    （OnTriggerEnter，早於 Update）結算的，因此本幀發布的就是最新真相
            //    ⇒ 死亡能在**同一幀**同時封鎖輸入並讓 DeathState 接管，不會出現
            //    「死了還走一幀」或「死後又出手一次」。
            // Runner 只負責排程；它不知道傷害、受擊或死亡動畫是什麼（比照順序 2.7 的 Probe）。
            _characterHealth?.PublishTo(_runtimeData);

            // 【順序 1】InputPipeline - 在 Stack 上配置預設結構體體
            // 透過 ref 傳遞，讓輸入源直接改寫此 stack 變數，達成真正零 GC Alloc
            InputData inputData = default;
            _inputSource?.FetchRawInput(ref inputData);
            // === [新增：在此處將 ref struct 的資料複製一份給除錯快照] ===
            // ⚠️（輪 4）快照刻意留在 BlockInput 閘門**之前**＝永遠是**原始**輸入：
            //    封鎖期間 Inspector 仍看得到「我按著 W 但被擋下」，除錯資訊不失真。
            _inputDebug.MoveInput = inputData.MoveInput;
            _inputDebug.LookInput = inputData.LookInput;
            _inputDebug.JumpButtonDown = inputData.JumpButtonDown;
            _inputDebug.RollButtonDown = inputData.RollButtonDown;
            _inputDebug.Slot1ButtonDown = inputData.Slot1ButtonDown;
            _inputDebug.SprintButtonHeld = inputData.SprintButtonHeld;
            _inputDebug.WalkButtonHeld = inputData.WalkButtonHeld;
            _inputDebug.WalkButtonDown = inputData.WalkButtonDown;

            // 【順序 2 閘門】🆕（輪 4，dev-spec §7-M5 結案）BlockInput 的語意定為
            // 「**本幀管線看不到任何輸入**」——把輸入整份歸零，順序 2 與 2.5 由此自動同時失效，
            // 不需要兩套規則、也不需要在下面每一步各補一個 if。
            //
            // ⚠️ 為什麼是「歸零」而不是「跳過順序 2.5」：MovementIntent 是**連續型**意圖，
            //    刻意不參與順序 7 復位（§1.5）。跳過 producer ≠ 意圖歸零，而是**意圖凍結在最後一幀**——
            //    若封鎖瞬間正按著 W 全速跑，角色會以全速無限前進且放不下來。必須主動歸零。
            // ⚠️ 為什麼歸零的是 InputData 而不是 MovementIntent：後者需要 MovementIntent 的第二寫入者
            //    （直接違反 §7-A5 單一寫入者）。歸零輸入則讓 PlayerLocomotionPolicy 依然是唯一寫入者，
            //    且 producer **完全不需要知道「封鎖」這個概念存在**（ADR-003 D2 context-free 毫髮無傷）。
            // 手感（輪 4 使用者裁決）：意圖歸零 → LocomotionModel 的 B9 減速時間常數把速度收到 0，
            //    ＝與「放開 WASD」完全同款的滑行收步，零新增機制、不動 IMovementModel 介面。
            if (_runtimeData.Arbitration.BlockInput)
            {
                inputData = default;
            }

            // 【順序 2】Intent Processor（封鎖時輸入全 false ⇒ 不寫入任何意圖，與舊「跳過」等價）
            ProcessIntents(ref inputData); // 改為傳址

            // 【順序 2.5】🆕（ADR-003 D2）Movement Intent Producer —— 唯一寫入 MovementIntent 的環節。
            // Runner 只認識介面，不知道「移動策略」長什麼樣（Shift=Sprint 這類規則全在 policy＋GaitProfileSO）。
            // 封鎖時吃到的是零輸入 ⇒ DesiredSpeedNormalized 歸零（B9 收步）；而 WalkButtonDown 同為 false，
            // 故 Ctrl toggle 的持久型態 WalkModeActive 不會被誤翻，封鎖解除後型態原樣保留。
            _movementIntentSource?.ProduceIntent(ref inputData, _runtimeData);

            // 【順序 2.6】Combat Context Producer —— 順序 2 已寫好 Action intent，
            // external ActionRequestTarget 也尚未在順序 4 評估後清除。Runner 只負責排程具體 S3a producer。
            _combatContextSource?.Tick(_runtimeData, Time.time);

            // 【順序 2.7】Traversal Environment Probe —— 可選角色能力，只快取本元件的 Candidate。
            // 不寫黑板、不觸發狀態；缺席時整段跳過，敵人與既有角色的執行路徑維持不變。
            _traversalProbe?.Tick(_runtimeData);

            // 【順序 3】🆕（ADR-003 D3／D4 Stage 2）Movement Model Tick —— 推進 active model 的 dynamics。
            // Runner 只呼介面方法：**不知道**平滑、MoveSpeed、gait 是什麼（原 DeriveMovementParameters 已整段遷出）。
            // ⚠️ 兩個時序理由讓這一步必須留在 Update、且**每幀無條件**（不看當前狀態）：
            //    1. Jump／Roll 期間仍須推進——JumpState 的空中控制吃的正是 model 的運動輸出，
            //       若隨 ambient 狀態才更新，落地會拿起跳時的殘值續走＝滑步。
            //    2. model 在此驅動自己的動畫參數；Animator 評估卡在 Update 與 LateUpdate 之間，
            //       移到 LateUpdate 會讓動畫參數比位移晚一幀。
            _movementModel?.Tick(_runtimeData, animationFacade, Time.deltaTime);

            // 【順序 4】狀態機 Tick (預留位置，後續實作接上)
            // 讀取黑板中的 Intent，讀完即可視為被狀態機消耗
            _stateMachine.Tick(_runtimeData, Time.deltaTime);

            // 【順序 4.5】🆕（輪 4）ArbiterPipeline —— Arbitration 區的唯一寫入者。
            // ⚠️ 時序脆弱點：必須卡在狀態機（順序 4）**之後**、動畫表現層（順序 5）**之前**——
            //    在後，是為了讓仲裁讀得到當幀**更新後**的狀態；在前，是為了讓動畫讀得到當幀最新旗標。
            //    代價是 BlockInput 有**一幀延遲**（本幀寫入 → 下一幀順序 2 的閘門才看到），
            //    這是刻意的時序取捨，**不得為了消除延遲而把 4.5 提前**（見 dev-spec §2.1 脆弱點第 2／7 條）。
            // Runner 只呼叫管線、不認識任何 IArbiterSource 實作（比照順序 6.5 的 PresentationPipeline）。
            _arbiterPipeline?.Tick(_runtimeData);

            // 【順序 4.6】單一 facing source —— FSM 已確定 Action commitment，LateUpdate 尚未消費 request。
            _facingSource?.Tick(_runtimeData);

            // 【順序 5】AnimationFacade 同步 (預留位置，後續實作接上)
            SyncAnimation();
        }

        private void SyncAnimation()
        {
            if (animationFacade == null || _stateMachine.CurrentState == null) return;

            BaseState current = _stateMachine.CurrentState;
            string animationKey = current.AnimationKey;
            if (!string.Equals(animationKey, _lastPlayedKey, StringComparison.Ordinal))
            {
                animationFacade.Play(animationKey);
                _lastPlayedKey = animationKey;
            }

            // 🆕（ADR-003 D4 Stage 2）**動畫參數同步已遷出本方法**：每個 model 驅動自己的參數
            // （Locomotion→MoveSpeed、未來 Swim→StrokeRate）於順序 3 完成，共用同一支通用 Facade。
            // 這裡只剩「狀態 → 動畫鍵」的通用播放請求——它是 FSM 的表現，不是任何 model 的內部量。
            // Facade 維持通用抽象、不加 IAnimationModel（ADR-003 §3-D4；docs/04 §14.3）。
        }

        private void LateUpdate()
        {
            // =================================================================
            // 【順序 6】MotionDriver 位移表現更新 - 保持單一、乾淨的唯一步行驅動點
            // =================================================================
            if (_stateMachine != null && _stateMachine.CurrentState != null && motionDriver != null)
            {
                _stateMachine.CurrentState.OnUpdateMotion(motionDriver, animationFacade, _runtimeData);

                // 🆕（Traversal V1）接觸快照由 MotionDriver.SyncGroundedState 統一發布；
                // 重力路徑與 committed 垂直曲線路徑共用，Runner 不另行同步。
            }

            // =================================================================
            // 【順序 6.5】PresentationPipeline —— 表現層集中消費黑板（含單幀事件 JustLanded）
            // ⚠️ 時序脆弱點：必須在 MotionDriver（順序 6，單幀事件觸發源）之後、
            // 統一復位（順序 7）之前，否則事件會在被消費前清空。勿調換。
            // =================================================================
            _presentationPipeline?.Tick(_runtimeData);

            // =================================================================
            // ⚠️ v0.2 順序脆弱點防禦線：死守在最後清空意圖
            // =================================================================
            // 🆕（M2）【順序 7】統一復位所有單幀事件（意圖 + JustLanded/JustLeftGround），一致生命週期。
            _runtimeData.ResetTransientState();
        }

        /// <summary>
        /// 🆕（2026-07-27）一次性收集所有仲裁來源：角色階層內的 ＋ Inspector 指定的階層外來源。
        /// ⚠️ **只在 Start 呼叫**——執行期 Tick 不得再 `GetComponents` 或配置任何集合（零 GC 熱路徑紀律）。
        /// 刻意不用 LINQ（§7-A3 禁令），純索引迴圈。
        /// </summary>
        private IArbiterSource[] CollectArbiterSources()
        {
            IArbiterSource[] local = GetComponentsInChildren<IArbiterSource>();

            int externalCount = 0;
            if (externalArbiterSources != null)
            {
                for (int i = 0; i < externalArbiterSources.Length; i++)
                {
                    if (externalArbiterSources[i] == null) continue;

                    if (externalArbiterSources[i] is IArbiterSource)
                    {
                        externalCount++;
                    }
                    else
                    {
                        Debug.LogError($"[{gameObject.name}] externalArbiterSources[{i}]" +
                                       $"（{externalArbiterSources[i].GetType().Name}）沒有實作 IArbiterSource，已略過。", this);
                    }
                }
            }

            if (externalCount == 0) return local; // 常見情況：沒有外部來源，直接沿用原陣列、不多配置

            var combined = new IArbiterSource[local.Length + externalCount];
            for (int i = 0; i < local.Length; i++) combined[i] = local[i];

            int next = local.Length;
            for (int i = 0; i < externalArbiterSources.Length; i++)
            {
                if (externalArbiterSources[i] is IArbiterSource source) combined[next++] = source;
            }

            return combined;
        }

        /// <summary>
        /// Intent Processor 邏輯（當前內嵌於 Runner，重構訊號：超過 10-15 行時抽離）
        /// </summary>
        private void ProcessIntents(ref InputData input)
        {
            if (input.JumpButtonDown) _runtimeData.Intent.JumpRequested = true;
            if (input.RollButtonDown) _runtimeData.Intent.RollRequested = true;

            // 🆕（ADR-005 D1）**按鍵 → ActionSlot 的映射住在這裡**，不在 raw input 層、也不在 ActionState。
            // 理由：順序 2 的職責就是「把這一幀的輸入翻譯成意圖」——「左鍵代表 Primary」正是一句翻譯。
            // raw input 只回報按鍵狀態（不知道技能），ActionState 只認 Slot（不知道按鍵），兩端都保持乾淨。
            // ⚠️ 同幀多鍵：先寫者勝（Slot1 > Slot2 > Slot3）。單格 intent 不排隊，
            //    與既有 mailbox 的「不排隊、不重試」語意一致。
            if (input.Slot1ButtonDown) _runtimeData.Intent.RequestedActionSlot = ActionSlot.Slot1;
            else if (input.Slot2ButtonDown) _runtimeData.Intent.RequestedActionSlot = ActionSlot.Slot2;
            else if (input.Slot3ButtonDown) _runtimeData.Intent.RequestedActionSlot = ActionSlot.Slot3;

            // 字串（尤其帶 richtext tag）每次觸發都會產生 GC Alloc，與專案零 GC 目標矛盾。
            // 包進 UNITY_EDITOR 後，Release 建置會被編譯器直接移除，Editor 內除錯體驗不變。
#if UNITY_EDITOR
            if (input.JumpButtonDown) Debug.Log("<color=lime>[Intent] 跳躍意圖已被黑板捕獲！</color>");
            if (input.RollButtonDown) Debug.Log("<color=cyan>[Intent] 翻滾意圖已被黑板捕獲！</color>");
            if (input.Slot1ButtonDown) Debug.Log("<color=orange>[Intent] Slot1 意圖已被黑板捕獲！</color>");
#endif
        }

        // 🗑️（ADR-003 Stage 2，2026-07-25）原 DeriveMovementParameters（順序 3：B9 平滑 ＋ 運動輸出導出）
        //     已整段遷入 LocomotionModel.Tick，本檔自此**不再認識任何 locomotion 概念**（§9-L1 結案）。
        //     順序 3 本身沒有消失，只是換人執行：Runner 呼 IMovementModel.Tick，內容由 model 決定。
        //
        // ✨（既有）身體轉向同樣不在此硬編碼：完全收斂至 LateUpdate 內 CurrentState.OnUpdateMotion，
        //     實現「單一決策、單一物理出口」的架構潔淨。
    }
}
