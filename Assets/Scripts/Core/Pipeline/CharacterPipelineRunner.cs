using UnityEngine;
using Project.Core.Arbitration;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Presentation;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Core.Pipeline
{
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

        [SerializeField] private Transform playerCamera;

        private IInputSource _inputSource;
        private IMovementIntentSource _movementIntentSource;
        private IMovementModel _movementModel;
        private PlayerRuntimeData _runtimeData;

        public PlayerRuntimeData RuntimeData => _runtimeData;

        [Header("StateMachine Setup")]
        [SerializeField] private StateMachineConfigSO stateMachineConfig;

        private FullBodyStateMachine _stateMachine;

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
            public bool FireButtonDown;
            public bool SprintButtonHeld;
            public bool WalkButtonHeld;
            public bool WalkButtonDown;
        }

        private InputDebugSnapshot _inputDebug;
        public InputDebugSnapshot InputDebug => _inputDebug;

        // 🆕 記錄上一次播放的狀態，避免每幀重複 Play
        private StateType _lastPlayedState = StateType.None;

        // 🆕 改用 None 當哨兵值，不再借用 Idle
        public StateType CurrentState => _stateMachine?.CurrentState?.Type ?? StateType.None;

        private void Awake()
        {
            _inputSource = inputSourceComponent as IInputSource;
            if (_inputSource == null)
            {
                Debug.LogError($"[{gameObject.name}] inputSourceComponent 沒有實作 IInputSource 介面！", this);
            }

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
            _presentationPipeline = new PresentationPipeline(GetComponentsInChildren<IPresentationController>());

            // 🆕（輪 4）仲裁來源同樣一次性收集，同樣放在狀態機檢查之前——缺 Config 時**建構**仍會完成。
            // ⚠️ 但與 PresentationPipeline 不同，仲裁的 Tick（順序 4.5）住在 Update 內，會被上方
            //    「缺狀態機就 return」那條防線一併擋下，**不是**真的完全獨立於狀態機配置。
            //    這可接受：同一條防線也擋掉了順序 1～3，整條輸入管線都沒在跑，此時「封鎖輸入」本無意義。
            // ⚠️ 收集只在此處發生——執行期 Tick 不得再 GetComponents／配置任何集合（零 GC 熱路徑紀律）。
            _arbiterPipeline = new ArbiterPipeline(GetComponentsInChildren<IArbiterSource>());

            if (stateMachineConfig == null)
            {
                Debug.LogError($"[{gameObject.name}] 未綁定 StateMachineConfigSO 配置檔！", this);
                return;
            }

            _stateMachine = new FullBodyStateMachine();
            // 🆕（ADR-003 Stage 2）連同 active model 一併注入：狀態機是 model 的**唯一持有點**，
            // 由它發給所有 state，確保跨幀平滑狀態全域唯一（Idle↔Move 切換不重置收步）。
            _stateMachine.Initialize(stateMachineConfig, _runtimeData, _movementModel); // 安全送入黑板
        }

        private void Update()
        {
            if (_inputSource == null || _stateMachine == null) return; // 🆕 補上 _stateMachine 的 null 檢查

            // 【順序 1】InputPipeline - 在 Stack 上配置預設結構體體
            // 透過 ref 傳遞，讓輸入源直接改寫此 stack 變數，達成真正零 GC Alloc
            InputData inputData = default;
            _inputSource.FetchRawInput(ref inputData);
            // === [新增：在此處將 ref struct 的資料複製一份給除錯快照] ===
            // ⚠️（輪 4）快照刻意留在 BlockInput 閘門**之前**＝永遠是**原始**輸入：
            //    封鎖期間 Inspector 仍看得到「我按著 W 但被擋下」，除錯資訊不失真。
            _inputDebug.MoveInput = inputData.MoveInput;
            _inputDebug.LookInput = inputData.LookInput;
            _inputDebug.JumpButtonDown = inputData.JumpButtonDown;
            _inputDebug.RollButtonDown = inputData.RollButtonDown;
            _inputDebug.FireButtonDown = inputData.FireButtonDown;
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

            // 【順序 5】AnimationFacade 同步 (預留位置，後續實作接上)
            SyncAnimation();
        }

        private void SyncAnimation()
        {
            if (animationFacade == null || _stateMachine.CurrentState == null) return;

            BaseState current = _stateMachine.CurrentState;
            if (current.Type != _lastPlayedState)
            {
                animationFacade.Play(current.AnimationKey);
                _lastPlayedState = current.Type;
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

                // 🆕（v0.8）IsGrounded 的黑板同步已收斂進 MotionDriver.GetGravityThisFrame，
                // 只要上面這行 OnUpdateMotion 實際呼叫了任一個移動方法（ExecuteBaseMovement /
                // ExecuteBakedCurveMovement / ApplyBakedCompensation）就會自動更新，
                // 不再需要像 v0.7 那樣額外呼叫一次 SyncGroundedState。
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
        /// Intent Processor 邏輯（當前內嵌於 Runner，重構訊號：超過 10-15 行時抽離）
        /// </summary>
        private void ProcessIntents(ref InputData input)
        {
            if (input.JumpButtonDown) _runtimeData.Intent.JumpRequested = true;
            if (input.RollButtonDown) _runtimeData.Intent.RollRequested = true;
            if (input.FireButtonDown) _runtimeData.Intent.FireRequested = true;

            // 字串（尤其帶 richtext tag）每次觸發都會產生 GC Alloc，與專案零 GC 目標矛盾。
            // 包進 UNITY_EDITOR 後，Release 建置會被編譯器直接移除，Editor 內除錯體驗不變。
#if UNITY_EDITOR
            if (input.JumpButtonDown) Debug.Log("<color=lime>[Intent] 跳躍意圖已被黑板捕獲！</color>");
            if (input.RollButtonDown) Debug.Log("<color=cyan>[Intent] 翻滾意圖已被黑板捕獲！</color>");
            if (input.FireButtonDown) Debug.Log("<color=orange>[Intent] 開火意圖已被黑板捕獲！</color>");
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