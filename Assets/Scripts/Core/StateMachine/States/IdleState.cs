using Project.Core.Blackboard;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class IdleState : BaseState
    {
        public override StateType Type => StateType.Idle;

        public override bool CanEnter(PlayerRuntimeData data) => data.MoveSpeed < 0.1f;
        public override void OnEnter(PlayerRuntimeData data) => Debug.Log("<color=white>[State] 進入 IDLE 狀態</color>");
        public override void OnTick(PlayerRuntimeData data, float deltaTime) { }
        public override void OnExit(PlayerRuntimeData data) { }
    }
}