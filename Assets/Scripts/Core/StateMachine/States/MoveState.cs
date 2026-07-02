using Project.Core.Blackboard;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class MoveState : BaseState
    {
        public override StateType Type => StateType.Move;

        public override bool CanEnter(PlayerRuntimeData data) => data.MoveSpeed >= 0.1f;
        public override void OnEnter(PlayerRuntimeData data) => Debug.Log("<color=lime>[State] 進入 MOVE 狀態</color>");
        public override void OnTick(PlayerRuntimeData data, float deltaTime) { }
        public override void OnExit(PlayerRuntimeData data) { }
    }
}