using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class IdleState : BaseState
    {
        public override StateType Type => StateType.Idle;

        // 🆕（ADR-003 Stage 2）門檻信號改問 model，不再讀衍生的 MoveSpeed（dev-spec §7.3 第三列結案）。
        // 「速度多少算在動」是 locomotion 的內部知識，狀態機只問語意化的「這顆 model 在不在產生運動」。
        // model 未注入時退化為恆可進 Idle（Runner 會在 Awake LogError，屬設定錯誤而非執行期分支）。
        public override bool CanEnter(PlayerRuntimeData data)
            => MovementModel == null || !MovementModel.IsProducingMotion;

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
            Debug.Log("<color=white>[State] 進入 IDLE 狀態</color>");
#endif
        }
        public override void OnTick(PlayerRuntimeData data, float deltaTime) { }
        public override void OnExit(PlayerRuntimeData data) { }
    }
}