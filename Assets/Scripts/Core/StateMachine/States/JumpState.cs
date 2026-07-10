using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class JumpState : BaseState
    {
        public override StateType Type => StateType.Jump;
        [SerializeField] private float jumpImpulseForce = 7.5f; // 起跳發射初速度 (m/s)

        private float _airTimer;
        public bool IsLanded { get; private set; }
        public override bool CanTransitionAway => IsLanded;

        public override bool CanEnter(PlayerRuntimeData data) => data.Intent.JumpRequested;

        public override void OnEnter(PlayerRuntimeData data)
        {
            Debug.Log("<color=yellow>[State] 進入 JUMP 狀態！物理初速度發射點火！</color>");
            _airTimer = 1.0f; // 模擬滯空快取
            IsLanded = false;

            // 🆕 核心修正：配合 OnAnimatorMove 移除，進入跳躍第一幀，主動通知 Driver 給予向上衝量！
            // 由於系統架構優化，我們稍後會在 OnUpdateMotion 內直接獲取 Driver 引用並完成點火
            _isVelocityInjected = false;
        }

        private bool _isVelocityInjected;

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (IsLanded) return;

            _airTimer -= deltaTime;
            if (_airTimer <= 0)
            {
                IsLanded = true;
                Debug.Log("<color=orange>[State] JUMP 落地計時結束</color>");
            }
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            IsLanded = false;
            _isVelocityInjected = false;
        }

        // 🆕 覆寫運動結算：傳入黑板資料完成全新的 Procedural 移動與起跳點火！
        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            // 第一幀安全注入起跳推力
            if (!_isVelocityInjected)
            {
                motionDriver.ApplyJumpImpulse(jumpImpulseForce);
                _isVelocityInjected = true;
            }

            // 執行統一接收 data 的新版常規物理移動出口
            motionDriver.ExecuteBaseMovement(data);
        }
    }
}