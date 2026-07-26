using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class MoveState : BaseState
    {
        public override StateType Type => StateType.Move;

        // 🆕（ADR-003 Stage 2）與 IdleState 互補的同一個門檻信號（見該檔註解）。
        public override bool CanEnter(PlayerRuntimeData data)
            => MovementModel != null && MovementModel.IsProducingMotion;

        // 🆕（ADR-003 D3）ambient 狀態：位移結算 delegate 給 active model。
        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            if (MovementModel != null) MovementModel.UpdateMotion(motionDriver, data);
            else base.OnUpdateMotion(motionDriver, animationFacade, data);
        }

        public override void OnEnter(PlayerRuntimeData data)
        {
            // 富文本字串會產生 GC Alloc，比照 JumpState / CharacterPipelineRunner 慣例（ADR-002 §3）
            // 包進 UNITY_EDITOR，Release 建置由編譯器直接移除。
#if UNITY_EDITOR
            Debug.Log("<color=lime>[State] 進入 MOVE 狀態</color>");
#endif
        }
        public override void OnTick(PlayerRuntimeData data, float deltaTime) { }
        public override void OnExit(PlayerRuntimeData data) { }
    }
}