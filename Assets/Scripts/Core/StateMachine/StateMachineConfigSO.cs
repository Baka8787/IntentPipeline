using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Core.StateMachine
{
    [Serializable]
    public struct StateRule
    {
        public StateType State;
        [Tooltip("哪些狀態可以主動打斷當前狀態（意圖觸發時檢查）")]
        public List<StateType> CanBeInterruptedBy;
        [Tooltip("當前狀態結束或無意圖時，允許自然過渡到的狀態優先級")]
        public List<StateType> ValidTransitions;
    }

    [CreateAssetMenu(fileName = "StateMachineConfig", menuName = "Project/Core/StateMachineConfig")]
    public class StateMachineConfigSO : ScriptableObject
    {
        [SerializeField] private List<StateRule> rules = new List<StateRule>();

        private readonly Dictionary<StateType, List<StateType>> _interruptMap = new();
        private readonly Dictionary<StateType, List<StateType>> _transitionMap = new();

        // 運行時快速查表優化
        public void Initialize()
        {
            _interruptMap.Clear();
            _transitionMap.Clear();
            foreach (var rule in rules)
            {
                _interruptMap[rule.State] = rule.CanBeInterruptedBy ?? new List<StateType>();
                _transitionMap[rule.State] = rule.ValidTransitions ?? new List<StateType>();
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
    }
}