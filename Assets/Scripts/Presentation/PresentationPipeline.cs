using Project.Core.Blackboard;

namespace Project.Presentation
{
    /// <summary>
    /// 🆕（M2）表現層驅動骨架：集中持有角色身上所有 IPresentationController，
    /// Runner 只呼叫本類別的 Tick、不認識任何具體 Controller——新增表現模組（IK / Facial / VFX）
    /// 只需實作介面掛上角色階層，Runner 零改動。
    /// 零 GC：Controller 陣列於建構時一次配置（來源為 Runner Start 的 GetComponentsInChildren），
    /// 執行期 Tick 為純 for 迴圈，無任何堆配置。
    /// 邊界：本類別只做「依序驅動」，不做輸出仲裁；未來若多個 Controller 競爭同一輸出，
    /// 升級為 ArbiterPipeline 並補 ADR（見 docs/01-design-doc.md §4.6）。
    /// </summary>
    public class PresentationPipeline
    {
        private readonly IPresentationController[] _controllers;

        public PresentationPipeline(IPresentationController[] controllers)
        {
            _controllers = controllers ?? System.Array.Empty<IPresentationController>();
        }

        public void Tick(PlayerRuntimeData data)
        {
            for (int i = 0; i < _controllers.Length; i++)
            {
                _controllers[i].Tick(data);
            }
        }
    }
}
