namespace Project.Core.Actions
{
    /// <summary>
    /// Action lifecycle 發出的窄版 semantic side-effect seam。
    /// 呼叫時點仍由 ActionState 單一持有；實作端只管理本地 Unity 物件，
    /// 不擁有 Action lifecycle、FSM transition 或 animation authority。
    /// </summary>
    public interface IActionLifecycleSink
    {
        void Begin();
        void Release();
        void Cleanup();
    }
}
