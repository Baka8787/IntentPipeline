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
        private float _takeoffDelay = 1.0f; // 起跳後滯空、開始判定落地的延遲 (秒)

        private float _airTimer;
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

        public override bool CanEnter(PlayerRuntimeData data) => data.Intent.JumpRequested;

        public override void OnEnter(PlayerRuntimeData data)
        {
            Debug.Log("<color=yellow>[State] 進入 JUMP 狀態！物理初速度發射點火！</color>");
            _airTimer = _takeoffDelay; // 模擬滯空快取（改由參數資產驅動）
            IsLanded = false;

            // 🆕 核心修正：配合 OnAnimatorMove 移除，進入跳躍第一幀，主動通知 Driver 給予向上衝量！
            // 由於系統架構優化，我們稍後會在 OnUpdateMotion 內直接獲取 Driver 引用並完成點火
            _isVelocityInjected = false;
        }

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
                motionDriver.ApplyJumpImpulse(_impulseForce);
                _isVelocityInjected = true;
            }

            // 執行統一接收 data 的新版常規物理移動出口
            motionDriver.ExecuteBaseMovement(data);
        }
    }
}