using Project.Core.Blackboard;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class JumpState : BaseState
    {
        public override StateType Type => StateType.Jump;
        private float _airTimer;
        public bool IsLanded { get; private set; }

        public override bool CanEnter(PlayerRuntimeData data) => data.Intent.JumpRequested;

        public override void OnEnter(PlayerRuntimeData data)
        {
            Debug.Log("<color=yellow>[State] 進入 JUMP 狀態！起跳！</color>");
            _airTimer = 1.2f; // 模擬滯空 1.2 秒
            IsLanded = false;
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (IsLanded) return;

            _airTimer -= deltaTime;
            if (_airTimer <= 0)
            {
                IsLanded = true;
                Debug.Log("<color=orange>[State] JUMP 模擬落地點達成</color>");
            }
        }

        public override void OnExit(PlayerRuntimeData data) => IsLanded = false;
    }
}