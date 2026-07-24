using UnityEngine;
using Project.Core.Blackboard;
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
        [SerializeField] private Transform playerCamera;

        private IInputSource _inputSource;
        private PlayerRuntimeData _runtimeData;

        public PlayerRuntimeData RuntimeData => _runtimeData;

        [Header("StateMachine Setup")]
        [SerializeField] private StateMachineConfigSO stateMachineConfig;

        private FullBodyStateMachine _stateMachine;

        // 🆕（M2）表現層驅動骨架：Start 一次性收集，LateUpdate 順序 6.5 集中 Tick。
        private PresentationPipeline _presentationPipeline;

        [Header("Presentation Setup")]
        [SerializeField] private AnimationFacadeBase animationFacade; // 💡 規格：掛載 AnimancerFacade 的組件
        [SerializeField] private MotionDriver motionDriver;

        // 🆕（B9 Game Feel）MoveSpeed 平滑：鍵盤 0/1 輸入直送會讓 1D Mixer 一幀從 Idle 跳 Sprint、
        // 中間 Walk/Run tier 踩不到。以 SmoothDamp 讓 MoveSpeed 隨時間爬升/回落，平順經過各速度 tier。
        // 動畫混合與實際位移共用同一平滑值（MotionDriver: currentSpeed = MoveSpeed × moveSpeed），加減速全程不滑步。
        [Header("Move Speed Smoothing (B9)")]
        [Tooltip("MoveSpeed 0→滿 的加速平滑時間（秒，SmoothDamp）。越小越 snappy。")]
        [SerializeField] private float moveSpeedAccelTime = 0.12f;
        [Tooltip("MoveSpeed 滿→0 的減速平滑時間（秒）。通常略大於加速＝放開後自然滑行收步。")]
        [SerializeField] private float moveSpeedDecelTime = 0.18f;

        // === [新增：供 Editor 跨幀讀取的普通結構體快照] ===
        public struct InputDebugSnapshot
        {
            public Vector2 MoveInput;
            public Vector2 LookInput;
            public bool JumpButtonDown;
            public bool RollButtonDown;
            public bool FireButtonDown;
        }

        private InputDebugSnapshot _inputDebug;
        public InputDebugSnapshot InputDebug => _inputDebug;

        // 🆕 記錄上一次播放的狀態，避免每幀重複 Play
        private StateType _lastPlayedState = StateType.None;

        // 🆕（B9）MoveSpeed 平滑狀態（寫入者僅本類別＝Parameter Processor，不新增黑板 writer）。
        private float _smoothedMoveSpeed;
        private float _moveSpeedVelocity;  // SmoothDamp 的 ref 速度緩存
        private Vector2 _lastMoveDir;      // 減速滑行期保留最後移動方向，避免方向瞬歸零造成「身體停、動畫動」的滑步

        // 🆕 改用 None 當哨兵值，不再借用 Idle
        public StateType CurrentState => _stateMachine?.CurrentState?.Type ?? StateType.None;

        private void Awake()
        {
            _inputSource = inputSourceComponent as IInputSource;
            if (_inputSource == null)
            {
                Debug.LogError($"[{gameObject.name}] inputSourceComponent 沒有實作 IInputSource 介面！", this);
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

            if (stateMachineConfig == null)
            {
                Debug.LogError($"[{gameObject.name}] 未綁定 StateMachineConfigSO 配置檔！", this);
                return;
            }

            _stateMachine = new FullBodyStateMachine();
            _stateMachine.Initialize(stateMachineConfig, _runtimeData); // 安全送入黑板
        }

        private void Update()
        {
            if (_inputSource == null || _stateMachine == null) return; // 🆕 補上 _stateMachine 的 null 檢查

            // 【順序 1】InputPipeline - 在 Stack 上配置預設結構體體
            // 透過 ref 傳遞，讓輸入源直接改寫此 stack 變數，達成真正零 GC Alloc
            InputData inputData = default;
            _inputSource.FetchRawInput(ref inputData);
            // === [新增：在此處將 ref struct 的資料複製一份給除錯快照] ===
            _inputDebug.MoveInput = inputData.MoveInput;
            _inputDebug.LookInput = inputData.LookInput;
            _inputDebug.JumpButtonDown = inputData.JumpButtonDown;
            _inputDebug.RollButtonDown = inputData.RollButtonDown;
            _inputDebug.FireButtonDown = inputData.FireButtonDown;

            // 【順序 2】Intent Processor 
            // 規格書防禦：若黑板仲裁區標記 BlockInput，則跳過意圖寫入
            if (!_runtimeData.Arbitration.BlockInput)
            {
                ProcessIntents(ref inputData); // 改為傳址
            }

            // 【順序 3】Parameter Processor - 更新黑板連續參數
            ProcessParameters(ref inputData);

            // 【順序 4】狀態機 Tick (預留位置，後續實作接上)
            // 讀取黑板中的 Intent，讀完即可視為被狀態機消耗
            _stateMachine.Tick(_runtimeData, Time.deltaTime);

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

            // 🆕（v0.16 F2）黑板 → 動畫圖參數同步：MoveSpeed 每幀送入動畫圖（§1.1 權限表既定 Reader = AnimationFacade），
            // 由 Locomotion Transition 資產內的 ParameterName 綁定驅動 1D Mixer 混合，本層不認識任何 Mixer。
            // M1 裁決（2026-07-17）：不做平滑、原值直送；Game Feel（加減速/SmoothDamp）屬後續專門調整輪。
            animationFacade.SetFloat(AnimationFacadeBase.ParamMoveSpeed, _runtimeData.MoveSpeed);
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

        /// <summary>
        /// Parameter Processor 邏輯（當前內嵌於 Runner）
        /// </summary>
        private void ProcessParameters(ref InputData input)
        {
            // 🆕（B9）MoveSpeed 平滑：鍵盤輸入強度為 0/1 二值，直送會讓 Mixer 一幀從 Idle 跳到 Sprint、
            // 中間 Walk/Run tier 踩不到。以 SmoothDamp 隨時間平順爬升/回落，經過各速度 tier；
            // 加速/減速採不同時間常數（減速略長＝放開後自然滑行收步）。SmoothDamp 內部使用 Time.deltaTime。
            float rawMagnitude = Mathf.Clamp01(input.MoveInput.magnitude);
            float smoothTime = rawMagnitude > _smoothedMoveSpeed ? moveSpeedAccelTime : moveSpeedDecelTime;
            _smoothedMoveSpeed = Mathf.SmoothDamp(_smoothedMoveSpeed, rawMagnitude, ref _moveSpeedVelocity, smoothTime);

            // 完全停止時 snap 到 0（清 SmoothDamp 殘尾）：確保 Mixer 回純 Idle、狀態機依 MoveSpeed<0.1 收斂進 Idle。
            if (rawMagnitude < 0.001f && _smoothedMoveSpeed < 0.001f)
            {
                _smoothedMoveSpeed = 0f;
                _moveSpeedVelocity = 0f;
            }

            _runtimeData.MoveSpeed = _smoothedMoveSpeed;

            // 移動方向：有輸入時直接採用並記憶；放開（rawMagnitude≈0）但仍在減速滑行（smoothed>0）時保留最後方向，
            // 讓 MotionDriver（Idle/Move 皆走 ExecuteBaseMovement）以殘速續走該方向，動畫與位移同步收步、不滑步；
            // 完全停止才歸零 → ExecuteBaseMovement 的方向閘門關閉，乾淨停步。
            if (rawMagnitude > 0.001f)
            {
                _lastMoveDir = input.MoveInput;
                _runtimeData.MoveDirection = input.MoveInput;
            }
            else
            {
                _runtimeData.MoveDirection = _smoothedMoveSpeed > 0.001f ? _lastMoveDir : Vector2.zero;
            }

            _runtimeData.UpperBodyWeight = _smoothedMoveSpeed > 0.001f ? 0.5f : 0.0f;

            // ✨（既有）身體轉向不在此硬編碼：完全收斂至 LateUpdate 內 CurrentState.OnUpdateMotion，
            // 實現「單一決策、單一物理出口」的架構潔淨。
        }
    }
}