using UnityEngine;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// 狀態專屬參數資產的抽象基底（SRP 職責分離）。
    /// 將各狀態的物理／數值參數，從狀態拓撲（<see cref="StateRule"/>）與狀態邏輯（<see cref="BaseState"/> 子類別）中抽離。
    /// 由 <see cref="StateMachineConfigSO"/> 以 StateType 綁定對應資產，運行期透過
    /// <see cref="StateMachineConfigSO.GetStateParams{TParams}"/> 泛型安全查表取得。
    /// </summary>
    public abstract class StateParamsSO : ScriptableObject
    {
    }
}
