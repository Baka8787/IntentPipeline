using System.Collections.Generic;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Movement;

namespace Project.Core.StateMachine
{
    public class FullBodyStateMachine
    {
        private readonly Dictionary<StateType, BaseState> _stateRegistry = new();
        private BaseState _currentState;
        private StateMachineConfigSO _config;

        // 🆕（ADR-003 Stage 2）active Movement Model 的**唯一持有點**。狀態機不使用它，
        // 只負責把同一個實例發給每一顆 state——「一份平滑狀態」因此是結構保證而非紀律。
        private IMovementModel _movementModel;
        private ActionRequestTarget _actionRequestTarget;

        public BaseState CurrentState => _currentState;

        /// <summary>
        /// 💡 修正安全性合約：要求傳入初始化完成的 data，杜絕 OnEnter(null) 隱性風險
        ///
        /// 🆕（ADR-003 Stage 2）新增 <paramref name="movementModel"/>：當下 active 的 Movement Model，
        /// 由 <c>CharacterPipelineRunner</c> 解析後注入（DIP——狀態機只認識 <see cref="IMovementModel"/> 介面）。
        /// </summary>
        public void Initialize(
            StateMachineConfigSO config,
            PlayerRuntimeData data,
            IMovementModel movementModel,
            ActionRequestTarget actionRequestTarget = null,
            IActionLifecycleSink actionLifecycleSink = null)
        {
            _config = config;
            _movementModel = movementModel;
            _actionRequestTarget = actionRequestTarget;
            _config.Initialize();

            RegisterState(new IdleState());
            RegisterState(new MoveState());
            RegisterState(new JumpState());
            RegisterState(new RollState());
            RegisterState(new ActionState(actionRequestTarget, actionLifecycleSink));

            _currentState = _stateRegistry[StateType.Idle];
            _currentState.OnEnter(data); // 💡 傳入實體數據
        }

        private void RegisterState(BaseState state)
        {
            state.Initialize(_config, _movementModel);
            _stateRegistry[state.Type] = state;
        }

        public void Tick(PlayerRuntimeData data, float deltaTime)
        {
            try
            {
                if (_currentState == null) return;

                _currentState.OnTick(data, deltaTime);
                if (!EvaluateInterrupts(data)) EvaluateTransitions(data);
            }
            finally
            {
                // External request 只取得本次 Tick 的一次仲裁機會；接受或拒絕都不排隊。
                _actionRequestTarget?.ClearAfterEvaluation();
            }
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

                // 🆕（ADR-005；FU-1）同型別原本一律排除（`continue`），使兩個共用 StateType.Action
                // 的技能永遠無法互相打斷。改為：同型別時交由該狀態自己回答「能不能被自己重入」。
                // 預設 false ⇒ Idle／Move／Jump／Roll 行為逐字不變。
                //
                // ⚠️ 重入候選**必須與其他候選走同一套 priority 比較**，不得就地 TransitionTo——
                // 那會讓字典迭代順序決定結果，並讓重入繞過比它更高優先的狀態（例：Roll 閃避）。
                bool isSelf = targetState.Type == _currentState.Type;
                if (isSelf)
                {
                    if (!_currentState.CanReenter(data)) continue;
                }
                else if (!targetState.CanEnter(data) || !_currentState.CanBeInterruptedBy(targetState))
                {
                    continue;
                }

                int priority = _config.GetPriority(targetState.Type);
                if (priority > highestPriority)
                {
                    highestPriority = priority;
                    bestCandidate = targetState;
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

            // ⚠️（2026-07-26 Profiler 實測修正）**必須用索引迴圈，不能用 foreach**。
            // GetValidTransitions 的回傳型別是介面 IReadOnlyList<StateType>；對介面 foreach 時
            // 編譯器只能走 IEnumerable<T>.GetEnumerator()，於是 List<T> 的 **struct** enumerator
            // 被裝箱到堆上——每帧 40 B（實測值：物件標頭 16 ＋ List 參照 8 ＋ index 4 ＋ version 4
            // ＋ current 4 → 對齊 40）。索引迴圈根本不建立 enumerator，因此零配置。
            // 為何不把回傳型別改成具體 List<StateType>：那會讓呼叫端拿到可變集合，
            // 為了效能犧牲唯讀封裝並不划算——改迭代方式即可，簽名不動。
            // 對照組：EvaluateInterrupts 迭代的是**具體** Dictionary，走 struct enumerator，本來就零配置。
            IReadOnlyList<StateType> allowedTargets = _config.GetValidTransitions(_currentState.Type);
            for (int i = 0; i < allowedTargets.Count; i++)
            {
                BaseState targetState = _stateRegistry[allowedTargets[i]];
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
