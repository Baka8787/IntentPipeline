using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Presentation.Motion;

namespace Project.Core.StateMachine
{
    [Serializable]
    public struct StateBakeMapping
    {
        public StateType State;
        [Tooltip("該狀態使用的烘焙運動資料，若該狀態不需要則留空")]
        public MotionBakeData BakeData;
    }

    [Serializable]
    public struct StateParamsMapping
    {
        public StateType State;
        [Tooltip("該狀態使用的參數資產（如 JumpStateParams），若該狀態不需要則留空")]
        public StateParamsSO Params;
    }

    [CreateAssetMenu(fileName = "StateMachineConfig", menuName = "Project/Core/StateMachineConfig")]
    public class StateMachineConfigSO : ScriptableObject
    {
        [SerializeField] private List<StateRule> rules = new List<StateRule>();

        [Header("動作烘焙資料配置")]
        [SerializeField] private List<StateBakeMapping> bakeMappings = new List<StateBakeMapping>();

        [Header("狀態專屬參數配置")]
        [SerializeField] private List<StateParamsMapping> paramsMappings = new List<StateParamsMapping>();

        private readonly Dictionary<StateType, List<StateType>> _interruptMap = new();
        private readonly Dictionary<StateType, List<StateType>> _transitionMap = new();
        private readonly Dictionary<StateType, int> _priorityMap = new();
        private readonly Dictionary<StateType, MotionBakeData> _bakeMap = new();
        private readonly Dictionary<StateType, StateParamsSO> _paramsMap = new();

        public void Initialize()
        {
            _interruptMap.Clear();
            _transitionMap.Clear();
            _priorityMap.Clear();
            _bakeMap.Clear();
            _paramsMap.Clear();

            foreach (var rule in rules)
            {
                _interruptMap[rule.State] = rule.CanBeInterruptedBy ?? new List<StateType>();
                _transitionMap[rule.State] = rule.ValidTransitions ?? new List<StateType>();
                _priorityMap[rule.State] = rule.Priority;
            }

            foreach (var mapping in bakeMappings)
            {
                if (mapping.BakeData != null)
                {
                    _bakeMap[mapping.State] = mapping.BakeData;
                }
            }

            foreach (var mapping in paramsMappings)
            {
                if (mapping.Params != null)
                {
                    _paramsMap[mapping.State] = mapping.Params;
                }
            }
        }

        public bool CheckCanInterrupt(StateType currentState, StateType nextState)
        {
            return _interruptMap.TryGetValue(currentState, out var list) && list.Contains(nextState);
        }

        public IReadOnlyList<StateType> GetValidTransitions(StateType state)
        {
            return _transitionMap.TryGetValue(state, out var list) ? list : Array.Empty<StateType>();
        }

        public int GetPriority(StateType state)
        {
            return _priorityMap.TryGetValue(state, out var priority) ? priority : 0;
        }

        public MotionBakeData GetBakeData(StateType state)
        {
            return _bakeMap.TryGetValue(state, out var data) ? data : null;
        }

        /// <summary>
        /// 泛型安全查表：依 StateType 取得綁定的狀態參數資產並轉型為 <typeparamref name="TParams"/>。
        /// 查無綁定或型別不符時回傳 null，呼叫端應自行 fallback 到程式碼內建預設值。
        /// 取代先前散落在 StateRule 內的 Jump 物理欄位與具體 float getter，統一由參數資產（SRP）承載。
        /// </summary>
        public TParams GetStateParams<TParams>(StateType state) where TParams : StateParamsSO
        {
            return _paramsMap.TryGetValue(state, out var stateParams) ? stateParams as TParams : null;
        }
    }
}
