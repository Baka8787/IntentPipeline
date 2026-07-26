using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class JumpState : BaseState
    {
        public override StateType Type => StateType.Jump;

        // 🆕（ADR-002）程式碼內建安全退化值：查無 Stages / 該段無可信烘焙資料時使用。
        private const float FallbackInitialVelocity = 7.5f; // m/s
        private const float FallbackGravity = 9.81f;        // m/s²（正值大小）

        // 落地判定保留一段最短滯空保護時間，避免離地瞬間 isGrounded 尚未切換成 false 就被誤判為已落地。
        private const float MinAirborneTimeBeforeLandingCheck = 0.15f;

        // 🆕（ADR-002）逐段預算的發射資料與起跳前搖，於 Initialize 依各段 MotionBakeData + 三個倍率算好。
        // 皆為一次性配置（非每幀），執行期只讀取，符合 Zero-GC。
        private JumpLaunchData[] _stageLaunch;
        private float[] _stageTakeoffDelay;
        private int _stageCount;

        // 執行期狀態（皆為狀態內部，不進黑板）
        private int _jumpIndex;          // 本次離地後的當前段（0 = 地面跳）
        private float _stageElapsedTime;  // 當前段自「開始」以來的經過時間，用來對齊該段起跳前搖
        private float _airborneTimer;     // 真正離地後的滯空計時，用於落地保護
        private bool _isVelocityInjected; // 當前段是否已注入發射衝量

        public bool IsLanded { get; private set; }
        public override bool CanTransitionAway => IsLanded;

        /// <summary>
        /// 🆕（ADR-002）從 Config 以泛型安全查表拿取 JumpStateParams，逐段預算 JumpLaunchData 與起跳前搖。
        /// 物理量全部來自各段 MotionBakeData（單一真相）；查無綁定或該段無可信資料時安全退化。
        /// </summary>
        public override void Initialize(StateMachineConfigSO config, IMovementModel movementModel)
        {
            base.Initialize(config, movementModel);

            var jumpParams = config != null ? config.GetStateParams<JumpStateParams>(Type) : null;

#if UNITY_EDITOR
            // 🆕（M1 收案輪）設計問題提示（非限制），比照 RollState 斷鏈防線＋M2 Warning 治理原則：
            // 查無 JumpStateParams 時跳躍仍可運作（BuildStages 合成 fallback 段），但物理量與動畫烘焙
            // 數據完全脫鉤——高度/重力用硬編碼、前搖 0s，可能重現「先蹲下再往上」的時間軸不同步。
            // GetStateParams<T> 在「未綁定／引用失效／資產型別不符」三種情境都靜默回 null（v0.10 已知
            // 防呆缺口），本警告即該防呆的輕量落地（專用 Editor 驗證工具依雙 Gate 評估不建）。
            // Application.isPlaying：EditMode 測試以最小拓撲 config 組裝屬合法輸入，不誤鳴；
            // Player build 整段由 UNITY_EDITOR 排除，Play 偵測力零損失。
            if (jumpParams == null && Application.isPlaying)
            {
                Debug.LogWarning(
                    $"[JumpState] 查無 {Type} 的 JumpStateParams（StateMachineConfig 的 paramsMappings 未綁定、引用已失效、或資產型別不符）。" +
                    $"跳躍將退化為硬編碼預設（初速 {FallbackInitialVelocity} m/s、重力 {FallbackGravity} m/s²、前搖 0s），" +
                    "與動畫烘焙數據脫鉤（apex 高度不符、起跳時機可能與預備動作不同步）。" +
                    "請檢查 Config.paramsMappings 是否綁定有效的 JumpStateParams 資產。");
            }
#endif

            BuildStages(jumpParams);
        }

        /// <summary>
        /// 依 Stages + 三個 Designer Tuning 倍率，逐段逆推發射初速（v = √(2gh)）並快取。
        /// </summary>
        private void BuildStages(JumpStateParams jumpParams)
        {
            var stages = jumpParams != null ? jumpParams.Stages : null;
            int count = stages != null ? stages.Count : 0;

            float heightMul = jumpParams != null ? jumpParams.HeightMultiplier : 1f;
            float gravityMul = jumpParams != null ? jumpParams.GravityMultiplier : 1f;
            float velMul = jumpParams != null ? jumpParams.LaunchVelocityMultiplier : 1f;

            // 安全退化：沒有配置 Stages 時，合成一段 fallback，讓跳躍仍可運作而不是完全跳不起來。
            if (count <= 0)
            {
                _stageCount = 1;
                _stageLaunch = new JumpLaunchData[1];
                _stageTakeoffDelay = new float[1];
                _stageLaunch[0] = new JumpLaunchData(FallbackInitialVelocity * velMul, FallbackGravity * gravityMul);
                _stageTakeoffDelay[0] = 0f;
                return;
            }

            _stageCount = count;
            _stageLaunch = new JumpLaunchData[count];
            _stageTakeoffDelay = new float[count];

            for (int i = 0; i < count; i++)
            {
                MotionBakeData bake = stages[i].Bake;

                float delay = 0f;
                float launchVelocity;
                float gravity;

                if (bake != null && bake.AutoApexHeight > 0f && bake.AutoCalculatedGravity > 0f)
                {
                    delay = Mathf.Max(0f, bake.AutoTakeoffDelay);
                    gravity = bake.AutoCalculatedGravity * gravityMul;
                    float height = bake.AutoApexHeight * heightMul;
                    // v = √(2gh)：以套用倍率後的 g、h 逆推，再乘上初速倍率。
                    // 三個倍率皆為 1 時，apex 精準命中 AutoApexHeight（ADR-002 §2.3 自洽性）。
                    launchVelocity = Mathf.Sqrt(2f * gravity * height) * velMul;
                }
                else
                {
                    // 該段沒有可信烘焙資料（非跳躍/未烘焙/Bake Into Pose 採不到上升量）→ 安全退化。
                    gravity = FallbackGravity * gravityMul;
                    launchVelocity = FallbackInitialVelocity * velMul;
                }

                _stageLaunch[i] = new JumpLaunchData(launchVelocity, gravity);
                _stageTakeoffDelay[i] = delay;
            }
        }

        // 🆕 起跳資格閘門：跳躍意圖 + 必須著地才能起跳，從根本杜絕無限空中跳（空中段改在 OnTick 內部消化）。
        public override bool CanEnter(PlayerRuntimeData data)
            => data.Intent.JumpRequested && data.IsGrounded;

        public override void OnEnter(PlayerRuntimeData data)
        {
#if UNITY_EDITOR
            Debug.Log("<color=yellow>[State] 進入 JUMP 狀態！等待該段起跳前搖後注入垂直初速度！</color>");
#endif
            IsLanded = false;
            _jumpIndex = 0;              // 地面跳 = 第 0 段
            _stageElapsedTime = 0f;
            _airborneTimer = 0f;
            _isVelocityInjected = false; // 離地時刻由 OnUpdateMotion 依當前段前搖決定
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            _stageElapsedTime += deltaTime;

            if (IsLanded) return;

            // 🆕（ADR-002）空中再按跳：在 JumpState 內部消化（interrupt 系統不自我重入，無法靠狀態轉移）。
            // JumpButtonDown = WasPressedThisFrame（邊沿），Intent 每幀復位 → 一次按壓只推進一段。
            if (_isVelocityInjected && data.Intent.JumpRequested && _jumpIndex + 1 < _stageCount)
            {
                _jumpIndex++;
                _isVelocityInjected = false; // 下一段的注入交給 OnUpdateMotion 依該段前搖點火
                _stageElapsedTime = 0f;       // 重新計時，對齊下一段的起跳前搖
                return;                       // 本幀已推進，暫不做落地判定
            }

            // 尚未真正離地（仍在當前段的前搖階段）時不判定落地，避免貼地站著被 IsGrounded 誤判成已落地。
            if (!_isVelocityInjected) return;

            _airborneTimer += deltaTime;
            if (_airborneTimer >= MinAirborneTimeBeforeLandingCheck && data.IsGrounded)
            {
                IsLanded = true;
#if UNITY_EDITOR
                Debug.Log("<color=orange>[State] JUMP 偵測到真實落地（IsGrounded == true）</color>");
#endif
            }
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            IsLanded = false;
            _isVelocityInjected = false;
            _jumpIndex = 0;
        }

        // 🆕（ADR-002）當前段前搖結束才點火，注入該段逆推的初速 + 重力；其餘時間走一般貼地移動。
        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            if (!_isVelocityInjected && _stageElapsedTime >= _stageTakeoffDelay[_jumpIndex])
            {
                motionDriver.ApplyJumpLaunch(_stageLaunch[_jumpIndex]);
                _isVelocityInjected = true;
                _airborneTimer = 0f; // 從真正離地那一刻開始重新計時滯空保護
            }

            // 無論是否已注入衝量，都走同一個常規物理移動出口：
            // 前搖期間靠重力系統的貼地力（ReboundForce）維持站立，離地後則是 baked 重力驅動的拋物線。
            motionDriver.ExecuteBaseMovement(data);
        }
    }
}
