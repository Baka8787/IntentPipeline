using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// 狀態拓撲規則（SRP 職責分離）：只描述狀態機的結構關係——
    /// 誰能打斷誰、可自然過渡到哪、同帧多意圖的優先級。
    /// 不承載任何狀態專屬的物理／數值參數（那些交由 <see cref="StateParamsSO"/> 系列資產負責）。
    /// </summary>
    [Serializable]
    public struct StateRule
    {
        public StateType State;
        [Tooltip("數值越高，同帧複數意圖觸發時越優先執行")]
        public int Priority;
        [Tooltip("哪些狀態可以主動打斷當前狀態（意圖觸發時檢查）")]
        public List<StateType> CanBeInterruptedBy;
        [Tooltip("當前狀態結束或無意圖時，允許自然過渡到的狀態優先級")]
        public List<StateType> ValidTransitions;
    }
}
