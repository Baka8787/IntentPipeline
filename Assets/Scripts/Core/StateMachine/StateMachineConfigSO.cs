using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Presentation.Motion; 

namespace Project.Core.StateMachine
{
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

    [Serializable]
    public struct StateBakeMapping
    {
        public StateType State;
        [Tooltip("該狀態使用的烘焙運動資料，若該狀態不需要則留空")]
        public MotionBakeData BakeData;
    }

    [CreateAssetMenu(fileName = "StateMachineConfig", menuName = "Project/Core/StateMachineConfig")]
    public class StateMachineConfigSO : ScriptableObject
    {
        [SerializeField] private List<StateRule> rules = new List<StateRule>();

        [Header("動作烘焙資料配置")]
        [SerializeField] private List<StateBakeMapping> bakeMappings = new List<StateBakeMapping>(); 

        private readonly Dictionary<StateType, List<StateType>> _interruptMap = new();
        private readonly Dictionary<StateType, List<StateType>> _transitionMap = new();
        private readonly Dictionary<StateType, int> _priorityMap = new();
        private readonly Dictionary<StateType, MotionBakeData> _bakeMap = new();

        public void Initialize()
        {
            _interruptMap.Clear();
            _transitionMap.Clear();
            _priorityMap.Clear();
            _bakeMap.Clear(); 

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
    }
}