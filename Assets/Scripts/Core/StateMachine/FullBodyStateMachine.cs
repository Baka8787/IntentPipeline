using System.Collections.Generic;
using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Core.StateMachine
{
    public class FullBodyStateMachine
    {
        private readonly Dictionary<StateType, BaseState> _stateRegistry = new();
        private BaseState _currentState;
        private StateMachineConfigSO _config;

        public BaseState CurrentState => _currentState;

        /// <summary>
        /// 💡 修正安全性合約：要求傳入初始化完成的 data，杜絕 OnEnter(null) 隱性風險
        /// </summary>
        public void Initialize(StateMachineConfigSO config, PlayerRuntimeData data)
        {
            _config = config;
            _config.Initialize();

            RegisterState(new IdleState());
            RegisterState(new MoveState());
            RegisterState(new JumpState());
            RegisterState(new RollState());

            _currentState = _stateRegistry[StateType.Idle];
            _currentState.OnEnter(data); // 💡 傳入實體數據
        }

        private void RegisterState(BaseState state)
        {
            state.Initialize(_config);
            _stateRegistry[state.Type] = state;
        }

        public void Tick(PlayerRuntimeData data, float deltaTime)
        {
            if (_currentState == null) return;

            _currentState.OnTick(data, deltaTime);

            if (EvaluateInterrupts(data)) return;

            EvaluateTransitions(data);
        }

        /// <summary>
        /// 💡 解決 Dictionary 遍歷不確定性：採用手動結構體迭代比大小，達成本地零 GC 的最高優先級判定
        /// </summary>
        private bool EvaluateInterrupts(PlayerRuntimeData data)
        {
            BaseState bestCandidate = null;
            int highestPriority = int.MinValue;

            // Dictionary.Enumerator 結構體迭代，零 GC Alloc
            foreach (var pair in _stateRegistry)
            {
                BaseState targetState = pair.Value;
                if (targetState.Type == _currentState.Type) continue;

                if (targetState.CanEnter(data) && _currentState.CanBeInterruptedBy(targetState))
                {
                    int priority = _config.GetPriority(targetState.Type);
                    if (priority > highestPriority)
                    {
                        highestPriority = priority;
                        bestCandidate = targetState;
                    }
                }
            }

            if (bestCandidate != null)
            {
                TransitionTo(bestCandidate, data);
                return true;
            }
            return false;
        }

        private void EvaluateTransitions(PlayerRuntimeData data)
        {
            // 唯讀抽象多型屬性，狀態機主體不知道具體動作細節
            if (!_currentState.CanTransitionAway) return;

            var allowedTargets = _config.GetValidTransitions(_currentState.Type);
            foreach (var targetType in allowedTargets)
            {
                BaseState targetState = _stateRegistry[targetType];
                if (targetState.CanEnter(data))
                {
                    TransitionTo(targetState, data);
                    break;
                }
            }
        }

        private void TransitionTo(BaseState nextState, PlayerRuntimeData data)
        {
            _currentState.OnExit(data);
            _currentState = nextState;
            _currentState.OnEnter(data);
        }
    }
}