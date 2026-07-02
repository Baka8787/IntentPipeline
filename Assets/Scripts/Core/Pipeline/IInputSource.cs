using Project.Core.Blackboard;

namespace Project.Core.Pipeline
{
    /// <summary>
    /// v0.3 變更：配合 ref struct，簽名改為傳址寫入參數 (ref pass)
    /// </summary>
    public interface IInputSource
    {
        void FetchRawInput(ref InputData data);
    }
}