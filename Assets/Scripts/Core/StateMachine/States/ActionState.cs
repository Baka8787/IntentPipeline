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
        /// <summary>per-slot 冷卻的陣列長度。以 enum 最大成員 +1 直接定尺寸，查表 O(1) 且零配置。</summary>
        private static readonly int SlotCount = (int)ActionSlot.Reaction + 1;

        private readonly ActionRequestTarget _externalRequestTarget;
        private readonly IActionLifecycleSink _lifecycleSink;

        // 🆕（ADR-005）冷卻改為 per-slot。**仍住在 ActionState 內部**（ADR-004 D2）——
        // 搬到 Runner／Config／HUD 都會讓「能不能出手」有第二個回答者。
        private readonly float[] _cooldownEndTime = new float[SlotCount];

        private ActionDefinitionSO _definition;
        private ActionSlot _activeSlot;
        private ActionPhase _phase;
        private ActionPhaseEntry _currentEntry;
        private float _phaseElapsed;
        private string _currentAnimationKey;
        private bool _releaseEmittedThisExecution;

        public ActionState(ActionRequestTarget externalRequestTarget = null, IActionLifecycleSink lifecycleSink = null)
        {
            _externalRequestTarget = externalRequestTarget;
            _lifecycleSink = lifecycleSink;
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
            _lifecycleSink?.Begin();
            if (!EnterPhase(ActionPhase.Start)) _lifecycleSink?.Cleanup();
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
            _lifecycleSink?.Cleanup();

            // 冷卻記在**剛結束的那個 slot** 上，不是全域。Definition 為空（未成功進入）時不寫。
            if (_definition != null && _activeSlot != ActionSlot.None)
            {
                _cooldownEndTime[(int)_activeSlot] =
                    Time.time + Mathf.Max(0f, _definition.Cooldown);
            }

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
            if (definition == null) return false;
            if (!HasPhase(definition, ActionPhase.Start)) return false;
            if (definition.RequiresGrounded && !data.IsGrounded) return false;
            return Time.time >= _cooldownEndTime[(int)slot];
        }

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
                _lifecycleSink?.Cleanup();
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
            _lifecycleSink?.Release();
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
            _lifecycleSink?.Cleanup();
            ResetExecutionState();
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
