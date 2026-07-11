using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class JumpState : BaseState
    {
        public override StateType Type => StateType.Jump;

        // 🆕 SRP 重構：物理參數改由 JumpStateParams 資產驅動。
        // 這裡僅保留執行期快取與程式碼內建預設值（查無綁定資產時的 fallback）。
        private float _impulseForce = 7.5f; // 起跳發射初速度 (m/s)
        private float _takeoffDelay = 1.0f; // 起跳寬限：離地翻轉前不判定落地的最小滯空時間 (秒)

        // 落地判定改為讀取黑板真實 isGrounded（見 MotionDriver 每帧回寫），不再用死碼計時器假裝落地。
        private float _airTime;
        private bool _hasLeftGround;
        private bool _isVelocityInjected;

        public bool IsLanded { get; private set; }
        public override bool CanTransitionAway => IsLanded;

        // 🆕 移除硬編碼，改在 Initialize 裡查表（對齊 RollState 的資料驅動模式）
        public override void Initialize(StateMachineConfigSO config)
        {
            base.Initialize(config);

            var jumpParams = config.GetStateParams<JumpStateParams>(Type);
            if (jumpParams != null)
            {
                _impulseForce = jumpParams.ImpulseForce;
                _takeoffDelay = jumpParams.TakeoffDelay;
            }
            // 查無綁定資產時，_impulseForce / _takeoffDelay 沿用欄位宣告的程式碼內建預設值
        }

        // 🆕 起跳資格閘門：除了跳躍意圖，必須「著地中」才能起跳，從根本杜絕無限空中跳。
        public override bool CanEnter(PlayerRuntimeData data)
            => data.Intent.JumpRequested && data.IsGrounded;

        public override void OnEnter(PlayerRuntimeData data)
        {
            Debug.Log("<color=yellow>[State] 進入 JUMP 狀態！物理初速度發射點火！</color>");
            _airTime = 0f;
            _hasLeftGround = false;
            IsLanded = false;
            _isVelocityInjected = false;
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (IsLanded) return;

            _airTime += deltaTime;

            // 起跳離地偵測：ApplyJumpImpulse 生效後角色會離開地面，isGrounded 轉為 false。
            if (!data.IsGrounded)
            {
                _hasLeftGround = true;
                return;
            }

            // 讀到黑板 IsGrounded == true：必須「確實離地過」且已過起跳寬限，才視為真實落地。
            // （寬限杜絕起跳首帧 isGrounded 尚未翻轉造成的假落地）
            if (_hasLeftGround && _airTime >= _takeoffDelay)
            {
                IsLanded = true;
                Debug.Log("<color=orange>[State] JUMP 偵測到真實著地訊號，落地！</color>");
            }
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            IsLanded = false;
            _hasLeftGround = false;
            _isVelocityInjected = false;
        }

        // 🆕 覆寫運動結算：傳入黑板資料完成全新的 Procedural 移動與起跳點火！
        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            // 第一帧安全注入起跳推力
            if (!_isVelocityInjected)
            {
                motionDriver.ApplyJumpImpulse(_impulseForce);
                _isVelocityInjected = true;
            }

            // 執行統一接收 data 的新版常規物理移動出口（內部會回寫 data.IsGrounded）
            motionDriver.ExecuteBaseMovement(data);
        }
    }
}
