using UnityEngine;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// Jump 狀態的物理參數資產。
    /// 透過 <see cref="StateMachineConfigSO"/> 的 paramsMappings 綁定至 <see cref="StateType.Jump"/>，
    /// 由 <see cref="JumpState"/> 在 Initialize 時查表快取。
    /// </summary>
    [CreateAssetMenu(fileName = "JumpStateParams", menuName = "Project/Core/StateParams/JumpStateParams")]
    public class JumpStateParams : StateParamsSO
    {
        [Tooltip("起跳瞬間注入的向上發射初速度 (m/s)")]
        public float ImpulseForce = 7.5f;

        [Tooltip("起跳後鎖定滯空、開始判定落地的延遲時間 (秒)")]
        public float TakeoffDelay = 1.0f;
    }
}
