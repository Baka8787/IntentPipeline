using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class JumpState : BaseState
    {
        public override StateType Type => StateType.Jump;

        // 🆕（SRP）物理參數改由 JumpStateParams 資產驅動（見 Initialize 查表）。
        // 這裡僅保留執行期快取與程式碼內建 fallback 預設值（查無綁定資產時使用）。
        private float _impulseForce = 7.5f; // 起跳發射初速度 (m/s) 的內建 fallback 預設

        // 🆕（延遲注入，沿用 main 的做法）除錯「先蹲下再往上」發現：
        // 物理衝量若在進入 Jump 當幀就無條件注入，會與帶下蹲預備姿勢（純骨骼 Pose）的動畫時間軸對不上，
        // 表現為「角色已在往上飛、身體卻還在演蹲下」。解法：把注入延後到 _takeoffDelay 秒後，
        // 這段期間維持一般貼地移動（ExecuteBaseMovement），讓預備動畫在角色仍貼地時演完，時間到才真正離地。
        private float _takeoffDelay; // 內建 fallback 0（= 不延遲，等同一進狀態立即注入）
        private float _stateElapsedTime;

        // 落地判定改讀黑板 IsGrounded（來源 CharacterController.isGrounded）。
        // 保留一段最短滯空保護時間，避免離地瞬間 isGrounded 尚未切換成 false 就被誤判為已落地。
        private const float MinAirborneTimeBeforeLandingCheck = 0.15f;
        private float _airborneTimer;
        private bool _isVelocityInjected;

        public bool IsLanded { get; private set; }
        public override bool CanTransitionAway => IsLanded;

        /// <summary>
        /// 🆕 從 Config 以泛型安全查表拿取 JumpStateParams（比照 RollState 從 Config 查 MotionBakeData 的既有慣例）。
        /// 查無綁定資產時，_impulseForce / _takeoffDelay 沿用欄位宣告的程式碼內建預設值。
        /// </summary>
        public override void Initialize(StateMachineConfigSO config)
        {
            base.Initialize(config);

            var jumpParams = config != null ? config.GetStateParams<JumpStateParams>(Type) : null;
            if (jumpParams != null)
            {
                if (jumpParams.ImpulseForce > 0f) _impulseForce = jumpParams.ImpulseForce;
                _takeoffDelay = Mathf.Max(0f, jumpParams.TakeoffDelay);
            }
        }

        // 🆕 起跳資格閘門：跳躍意圖 + 必須著地才能起跳，從根本杜絕無限空中跳。
        public override bool CanEnter(PlayerRuntimeData data)
            => data.Intent.JumpRequested && data.IsGrounded;

        public override void OnEnter(PlayerRuntimeData data)
        {
            Debug.Log("<color=yellow>[State] 進入 JUMP 狀態！等待起跳延遲後注入垂直初速度！</color>");
            IsLanded = false;
            _stateElapsedTime = 0f;
            _airborneTimer = 0f;

            // 離地時刻由 OnUpdateMotion 依 _takeoffDelay 決定，不再是「一進狀態立刻發射」。
            _isVelocityInjected = false;
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            _stateElapsedTime += deltaTime;

            if (IsLanded) return;

            // 尚未真正離地（仍在起跳延遲/預備動畫階段）時不判定落地，
            // 避免延遲期間角色仍貼地站著卻被 IsGrounded == true 誤判為「已經跳完落地」。
            if (!_isVelocityInjected) return;

            _airborneTimer += deltaTime;
            if (_airborneTimer >= MinAirborneTimeBeforeLandingCheck && data.IsGrounded)
            {
                IsLanded = true;
                Debug.Log("<color=orange>[State] JUMP 偵測到真實落地（IsGrounded == true）</color>");
            }
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            IsLanded = false;
            _isVelocityInjected = false;
        }

        // 🆕 覆寫運動結算：延遲期間維持一般貼地 Procedural 移動，delay 結束才點火注入起跳衝量。
        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            if (!_isVelocityInjected && _stateElapsedTime >= _takeoffDelay)
            {
                motionDriver.ApplyJumpImpulse(_impulseForce);
                _isVelocityInjected = true;
                _airborneTimer = 0f; // 從真正離地那一刻開始重新計時滯空保護
            }

            // 無論是否已注入衝量，都走同一個常規物理移動出口：
            // 延遲期間角色靠重力系統的貼地力（ReboundForce）維持站立，離地後則是自然拋物線。
            motionDriver.ExecuteBaseMovement(data);
        }
    }
}
