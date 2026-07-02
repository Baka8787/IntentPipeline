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

        public void Initialize(StateMachineConfigSO config)
        {
            _config = config;
            _config.Initialize();

            // 註冊所有實體狀態
            RegisterState(new IdleState());
            RegisterState(new MoveState());
            RegisterState(new JumpState());
            RegisterState(new RollState());

            // 預設進入 Idle
            _currentState = _stateRegistry[StateType.Idle];
            _currentState.OnEnter(null);
        }

        private void RegisterState(BaseState state)
        {
            state.Initialize(_config);
            _stateRegistry[state.Type] = state;
        }

        public void Tick(PlayerRuntimeData data, float deltaTime)
        {
            if (_currentState == null) return;

            // 1. 執行當前狀態逻辑
            _currentState.OnTick(data, deltaTime);

            // 2. 主動意圖打斷評估 (按鍵觸發時優先檢查)
            if (EvaluateInterrupts(data)) return;

            // 3. 自然狀態過渡評估 (例如 Jump/Roll 結束了，或者從 Move 停下變 Idle)
            EvaluateTransitions(data);
        }

        private bool EvaluateInterrupts(PlayerRuntimeData data)
        {
            // 依優先序遍歷所有狀態，看有沒有人想憑藉「玩家當當幀意圖」強行打斷當前狀態
            foreach (var pair in _stateRegistry)
            {
                BaseState targetState = pair.Value;
                if (targetState.Type == _currentState.Type) continue;

                // 條件：對方符合當幀意圖(CanEnter) && 當前狀態允許被對方打斷
                if (targetState.CanEnter(data) && _currentState.CanBeInterruptedBy(targetState))
                {
                    TransitionTo(targetState, data);
                    return true;
                }
            }
            return false;
        }

        private void EvaluateTransitions(PlayerRuntimeData data)
        {
            // 特殊處理：若當前是計時制動作，未結束前不允許自然回歸
            if (_currentState is JumpState jump && !jump.IsLanded) return;
            if (_currentState is RollState roll && !roll.IsRollFinished) return;

            // 讀取 SO 配置的自然過渡優先順序
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