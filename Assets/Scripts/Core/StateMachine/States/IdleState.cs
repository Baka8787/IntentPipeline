using Project.Core.Blackboard;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class IdleState : BaseState
    {
        public override StateType Type => StateType.Idle;

        public override bool CanEnter(PlayerRuntimeData data) => data.MoveSpeed < 0.1f;

        public override void OnEnter(PlayerRuntimeData data)
        {
            // 富文本字串會產生 GC Alloc，比照 JumpState / CharacterPipelineRunner 慣例（ADR-002 §3）
            // 包進 UNITY_EDITOR，Release 建置由編譯器直接移除。
#if UNITY_EDITOR
            Debug.Log("<color=white>[State] 進入 IDLE 狀態</color>");
#endif
        }
        public override void OnTick(PlayerRuntimeData data, float deltaTime) { }
        public override void OnExit(PlayerRuntimeData data) { }
    }
}