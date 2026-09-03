using System;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// 所有同構 Action 共用的一顆 FSM state。Definition 決定 phase、動畫鍵與 release 點；
    /// 本類別不播放動畫、不建立 GameObject，位移只經 MotionDriver。
    ///
    /// 🆕（ADR-005 D1）**一顆 state 承載多份 Definition**，以 <see cref="ActionSlot"/> 為身分索引。
    /// Definition 不再於 Initialize 綁死，而是每次進入時依 request 的 slot 現查——
    /// 這是 FU-2 的解，且**沒有新增第二個 gate 權威**：能不能出手仍然只由本類別的
    /// <see cref="CanEnter"/> 回答（ADR-004 D2）。
    /// </summary>
    public class ActionState : BaseState
    {
        /// <summary>
        /// per-slot 冷卻的陣列長度＝<see cref="ActionSlot"/> 最大成員 +1，查表 O(1) 且執行期零配置。
        ///
        /// ⚠️ **刻意不寫死 <c>(int)ActionSlot.Reaction + 1</c>**：保留段（100 起）將來若再加成員，
        /// 寫死會讓陣列悄悄長度不足並在 <c>OnExit</c> 越界。改為由 enum 自己算，加成員不必記得改這裡。
        ///
        /// ⚖️ **稀疏是設計，不是浪費**：<see cref="ActionSlot"/> 的數值就是身分，且**刻意不要求連續**
        /// （玩家段 1–3、保留段 100 起，中間的空隙讓兩段各自獨立成長）。以身分當索引 ⇒ 表必然稀疏。
        /// 目前 101 格只用到 5 格 ＝ 404 B，**每個角色一次**的配置、不在熱路徑。
        /// ⛔ 不要為了「填滿號碼」去改 enum 數值——那是在改身分，會讓既有資產指向別的 slot。
        /// ⛔ 也不要為了省這 404 B 加一層 slot→密集索引的對照（2026-09-02 已裁決不做）——
        /// 那等於多一把內部的鍵，與 ADR-005 D1「不得有第二把鍵」的精神相悖，而省下的是零頭。
        /// </summary>
        internal static readonly int SlotCount = ComputeSlotCount();

        private static int ComputeSlotCount()
        {
            var values = (ActionSlot[])System.Enum.GetValues(typeof(ActionSlot));
            int max = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int value = (int)values[i];
                if (value > max) max = value;
            }
            return max + 1;
        }

        private readonly ActionRequestTarget _externalRequestTarget;
        private readonly IActionLifecycleSink[] _lifecycleSinks;

        // 🆕（ADR-005）冷卻改為 per-slot。**仍住在 ActionState 內部**（ADR-004 D2）——
        // 搬到 Runner／Config／HUD 都會讓「能不能出手」有第二個回答者。
        private readonly float[] _cooldownEndTime = new float[SlotCount];
#if UNITY_EDITOR
        // 缺 Definition 是資產接線錯誤，不改變 gate 的嚴格行為；每個 slot 只警告一次，避免 CanEnter
        // 每幀輪詢時持續配置訊息字串並淹沒 Console。
        private readonly bool[] _missingDefinitionWarnings = new bool[SlotCount];
#endif

        private ActionDefinitionSO _definition;
        private ActionSlot _activeSlot;
        private ActionPhase _phase;
        private ActionPhaseEntry _currentEntry;
        private float _phaseElapsed;
        private string _currentAnimationKey;
        private bool _releaseEmittedThisExecution;

        public ActionState(
            ActionRequestTarget externalRequestTarget = null,
            IActionLifecycleSink[] lifecycleSinks = null)
        {
            _externalRequestTarget = externalRequestTarget;
            _lifecycleSinks = lifecycleSinks ?? Array.Empty<IActionLifecycleSink>();
        }

        public override StateType Type => StateType.Action;
        public override string AnimationKey => _currentAnimationKey;
        public override bool CanTransitionAway => _phase == ActionPhase.None;
        public ActionPhase CurrentPhase => _phase;

        /// <summary>當前執行中的 Action 身分；閒置時為 <see cref="ActionSlot.None"/>。</summary>
        public ActionSlot ActiveSlot => _activeSlot;

        public override void Initialize(StateMachineConfigSO config, IMovementModel movementModel)
        {
            base.Initialize(config, movementModel);

#if UNITY_EDITOR
            if (Application.isPlaying && (config == null || config.ActionDefinitionCount == 0))
            {
                Debug.LogWarning("[ActionState] StateMachineConfig 未綁定任何 ActionDefinitionSO；Action 將拒絕進入。");
            }
#endif
        }

        public override bool CanEnter(PlayerRuntimeData data)
        {
            if (data == null || Config == null) return false;
            return TryResolveRequest(data, out _, out _);
        }

        public override void OnEnter(PlayerRuntimeData data)
        {
            _releaseEmittedThisExecution = false;

            if (!TryResolveRequest(data, out ActionSlot slot, out ActionDefinitionSO definition))
            {
                // CanEnter 與 OnEnter 之間 request 消失。不進入任何 phase，讓 CanTransitionAway 立刻放行。
                Complete();
                return;
            }

            _activeSlot = slot;
            _definition = definition;
            ActiveLifecycleSink()?.Begin();
            if (!EnterPhase(ActionPhase.Start)) ActiveLifecycleSink()?.Cleanup();
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (_phase == ActionPhase.None || deltaTime <= 0f) return;
            _phaseElapsed += deltaTime;
            TryEmitRelease();

            switch (_phase)
            {
                case ActionPhase.Start:
                    if (_phaseElapsed >= CurrentDuration()) EnterAfterStart();
                    break;

                case ActionPhase.Loop:
                    if (_definition.CancelMoveIntentThreshold > 0f &&
                        data.MovementIntent.DesiredSpeedNormalized >= _definition.CancelMoveIntentThreshold)
                    {
                        if (!EnterPhase(ActionPhase.Cancel)) Complete();
                    }
                    else if (_currentEntry.WaitForTrigger && IsRetriggeredThisFrame(data))
                    {
                        if (!EnterPhase(ActionPhase.End)) Complete();
                    }
                    else if (!_currentEntry.WaitForTrigger && _phaseElapsed >= CurrentDuration())
                    {
                        if (!EnterPhase(ActionPhase.End)) Complete();
                    }
                    break;

                case ActionPhase.End:
                case ActionPhase.Cancel:
                    if (_phaseElapsed >= CurrentDuration()) Complete();
                    break;
            }
        }

        public override bool CanBeInterruptedBy(BaseState other)
        {
            if (!base.CanBeInterruptedBy(other)) return false;
            return _phase == ActionPhase.None || _currentEntry.Interruptible;
        }

        /// <summary>
        /// 🆕（ADR-005；FU-1 的解）**Action → Action 中斷**。
        ///
        /// `FullBodyStateMachine.EvaluateInterrupts` 依型別排除自己，因此同為 <see cref="StateType.Action"/>
        /// 的兩個技能原本永遠無法互相打斷。本方法讓 FSM 能就「同一顆 state 的重入」單獨提問。
        ///
        /// ⚠️ **這不是第二個 interrupt 權威**：判準仍是既有的兩項——authored 的
        /// <c>Interruptible</c>（資產）＋ 目標 slot 自己的冷卻（本 state 唯一持有）。
        /// 新增的只有「身分不同」這個條件，沒有引入任何新的決策來源。
        /// </summary>
        public override bool CanReenter(PlayerRuntimeData data)
        {
            if (_phase == ActionPhase.None) return false;
            if (!_currentEntry.Interruptible) return false;
            if (!TryResolveRequest(data, out ActionSlot slot, out _)) return false;
            return slot != _activeSlot;
        }

        public override void OnUpdateMotion(
            MotionDriver motionDriver,
            AnimationFacadeBase animationFacade,
            PlayerRuntimeData data)
        {
            bool hasBake = _phase != ActionPhase.None &&
                           _currentEntry.Bake != null &&
                           _currentEntry.Bake.Duration > 0f;
            bool isActuallyPlaying = animationFacade != null &&
                                     !string.IsNullOrEmpty(AnimationKey) &&
                                     animationFacade.IsPlaying(AnimationKey);

            if (!hasBake || !isActuallyPlaying)
            {
                motionDriver.ExecuteBaseMovement(data);
                return;
            }

            motionDriver.ExecuteBakedCurveMovement(
                _currentEntry.Bake, animationFacade.GetNormalizedTime(), data);
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            ActiveLifecycleSink()?.Cleanup();
            CommitCooldown();
            ResetExecutionState();
        }

        /// <summary>指定 slot 的冷卻剩餘秒數；0 ＝ 可用。供測試與（未來）HUD 讀取。</summary>
        public float GetCooldownRemaining(ActionSlot slot)
        {
            if (slot == ActionSlot.None) return 0f;
            return Mathf.Max(0f, _cooldownEndTime[(int)slot] - Time.time);
        }

        /// <summary>
        /// 解析「這一幀該進入哪一個 Action」。**唯一的准入判斷點**（ADR-004 D2）。
        /// 玩家意圖優先於 external request——同幀兩者都有時，自己的操作不該被別人的受擊請求蓋掉。
        /// </summary>
        private bool TryResolveRequest(
            PlayerRuntimeData data, out ActionSlot slot, out ActionDefinitionSO definition)
        {
            slot = data.Intent.RequestedActionSlot;
            if (slot == ActionSlot.None && _externalRequestTarget != null)
            {
                slot = _externalRequestTarget.PendingSlot;
            }

            definition = null;
            if (slot == ActionSlot.None) return false;

            definition = Config.GetActionDefinition(slot);
            if (definition == null)
            {
#if UNITY_EDITOR
                WarnMissingDefinitionOnce(slot);
#endif
                return false;
            }
            if (!HasPhase(definition, ActionPhase.Start)) return false;
            if (definition.RequiresGrounded && !data.IsGrounded) return false;
            return Time.time >= _cooldownEndTime[(int)slot];
        }

#if UNITY_EDITOR
        private void WarnMissingDefinitionOnce(ActionSlot slot)
        {
            int index = (int)slot;
            if (index >= 0 && index < _missingDefinitionWarnings.Length)
            {
                if (_missingDefinitionWarnings[index]) return;
                _missingDefinitionWarnings[index] = true;
            }

            Debug.LogWarning(
                $"[ActionState] ActionSlot.{slot} 沒有對應的 ActionDefinitionSO；" +
                "請檢查 StateMachineConfig 的 actionDefinitions 與該 Definition 的 Slot 欄位。");
        }
#endif

        /// <summary>Loop 期的 <c>WaitForTrigger</c>：只有**同一個 slot** 再次被請求才算 re-trigger。</summary>
        private bool IsRetriggeredThisFrame(PlayerRuntimeData data)
        {
            if (data.Intent.RequestedActionSlot == _activeSlot) return true;
            return _externalRequestTarget != null && _externalRequestTarget.PendingSlot == _activeSlot;
        }

        private void EnterAfterStart()
        {
            if (EnterPhase(ActionPhase.Loop)) return;
            if (EnterPhase(ActionPhase.End)) return;
            Complete();
        }

        private bool EnterPhase(ActionPhase phase)
        {
            if (!TryFindPhase(phase, out ActionPhaseEntry entry)) return false;

            _phase = phase;
            _currentEntry = entry;
            _phaseElapsed = 0f;
            _currentAnimationKey = entry.AnimationKey;

            if (phase == ActionPhase.Cancel)
            {
                ActiveLifecycleSink()?.Cleanup();
            }
            else
            {
                TryEmitRelease();
            }

            return true;
        }

        private void TryEmitRelease()
        {
            if (_releaseEmittedThisExecution || !_currentEntry.EmitsRelease) return;

            float normalizedTime = Mathf.Clamp01(_currentEntry.ReleaseNormalizedTime);
            if (_phaseElapsed < CurrentDuration() * normalizedTime) return;

            _releaseEmittedThisExecution = true;
            ActiveLifecycleSink()?.Release();
        }

        private float CurrentDuration()
        {
            float bakedDuration = _currentEntry.Bake != null ? _currentEntry.Bake.Duration : 0f;
            return bakedDuration > 0f ? bakedDuration : Mathf.Max(0f, _currentEntry.FallbackDuration);
        }

        private bool TryFindPhase(ActionPhase phase, out ActionPhaseEntry entry)
            => TryFindPhase(_definition, phase, out entry);

        private static bool TryFindPhase(
            ActionDefinitionSO definition, ActionPhase phase, out ActionPhaseEntry entry)
        {
            ActionPhaseEntry[] phases = definition != null ? definition.Phases : null;
            int count = phases != null ? phases.Length : 0;
            for (int i = 0; i < count; i++)
            {
                if (phases[i].Phase != phase) continue;
                entry = phases[i];
                return true;
            }

            entry = default;
            return false;
        }

        private static bool HasPhase(ActionDefinitionSO definition, ActionPhase phase)
            => TryFindPhase(definition, phase, out _);

        private void Complete()
        {
            // 必須在 CommitCooldown／ResetExecutionState 清掉 _activeSlot 前查表；
            // 否則自然完成時會把 Cleanup 送到 None，重演冷卻曾踩過的同一個順序 bug。
            ActiveLifecycleSink()?.Cleanup();
            CommitCooldown();
            ResetExecutionState();
        }

        private IActionLifecycleSink ActiveLifecycleSink()
        {
            int index = (int)_activeSlot;
            return index > 0 && index < _lifecycleSinks.Length ? _lifecycleSinks[index] : null;
        }

        /// <summary>
        /// 把冷卻記在**剛結束的那個 slot** 上，不是全域。Definition 為空（未成功進入）時不寫。
        ///
        /// 🐛 **2026-09-02 修**：本段原本只寫在 <see cref="OnExit"/>，但 <see cref="Complete"/> 會先
        /// <see cref="ResetExecutionState"/> 清掉 <c>_activeSlot</c>／<c>_definition</c> ——
        /// 於是**自然播完的 Action 永遠不會進冷卻**，只有被中斷的才會。方向剛好相反。
        /// 兩個結束路徑（自然完成／被中斷）都要記，所以抽成同一個方法在兩處呼叫；
        /// 由下方的 null 判定保證冪等（`Complete()` 記完即清，後續 `OnExit` 是 no-op），
        /// 因此**不會重複延長冷卻**。
        /// </summary>
        private void CommitCooldown()
        {
            if (_definition == null || _activeSlot == ActionSlot.None) return;
            _cooldownEndTime[(int)_activeSlot] = Time.time + Mathf.Max(0f, _definition.Cooldown);
        }

        /// <summary>
        /// 清掉單次執行的瞬態。⚠️ <c>_cooldownEndTime</c> **刻意不在此清除**——
        /// 冷卻是跨執行的狀態，清了就等於每次出手都重置冷卻。
        /// </summary>
        private void ResetExecutionState()
        {
            _phase = ActionPhase.None;
            _currentEntry = default;
            _phaseElapsed = 0f;
            _currentAnimationKey = null;
            _releaseEmittedThisExecution = false;
            _activeSlot = ActionSlot.None;
            _definition = null;
        }
    }
}
