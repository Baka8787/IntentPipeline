using Project.Core.Blackboard;

namespace Project.Presentation
{
    /// <summary>
    /// 🆕（M2）表現層 Controller 統一契約：讀黑板 → 驅動表現（Audio / IK / 表情 / VFX）。
    /// 由 PresentationPipeline 於管線順序 6.5（LateUpdate，MotionDriver 之後、統一復位之前）集中呼叫，
    /// 這是單幀事件（JustLanded / JustLeftGround）唯一保證可讀到的時間窗。
    /// 契約：實作者對 PlayerRuntimeData 只讀不寫（含仲裁旗標 BlockXxx），Controller 彼此不得互相引用。
    /// </summary>
    public interface IPresentationController
    {
        void Tick(PlayerRuntimeData data);
    }
}
