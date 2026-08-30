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
    /// </summary>
    public class ActionState : BaseState
    {
        private readonly ActionRequestTarget _externalRequestTarget;
        private readonly IActionLifecycleSink _lifecycleSink;

        private ActionDefinitionSO _definition;
        private ActionPhase _phase;
        private ActionPhaseEntry _currentEntry;
        private float _phaseElapsed;
        private float _cooldownEndTime;
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

        public override void Initialize(StateMachineConfigSO config, IMovementModel movementModel)
        {
            base.Initialize(config, movementModel);
            _definition = config != null ? config.GetStateParams<ActionDefinitionSO>(Type) : null;

#if UNITY_EDITOR
            if (_definition == null && Application.isPlaying)
            {
                Debug.LogWarning("[ActionState] StateMachineConfig 未為 Action 綁定 ActionDefinitionSO；Action 將拒絕進入。");
            }
#endif
        }

        public override bool CanEnter(PlayerRuntimeData data)
        {
            if (data == null || _definition == null || !TryFindPhase(ActionPhase.Start, out _)) return false;

            bool requested = data.Intent.FireRequested ||
                             (_externalRequestTarget != null && _externalRequestTarget.HasPendingRequest);
            if (!requested) return false;
            if (_definition.RequiresGrounded && !data.IsGrounded) return false;
            return Time.time >= _cooldownEndTime;
        }

        public override void OnEnter(PlayerRuntimeData data)
        {
            _releaseEmittedThisExecution = false;
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
                    else if (_currentEntry.WaitForTrigger && data.Intent.FireRequested)
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
            _phase = ActionPhase.None;
            _currentEntry = default;
            _phaseElapsed = 0f;
            _currentAnimationKey = null;
            _releaseEmittedThisExecution = false;
            if (_definition != null) _cooldownEndTime = Time.time + Mathf.Max(0f, _definition.Cooldown);
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
        {
            ActionPhaseEntry[] phases = _definition != null ? _definition.Phases : null;
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

        private void Complete()
        {
            _lifecycleSink?.Cleanup();
            _phase = ActionPhase.None;
            _currentEntry = default;
            _phaseElapsed = 0f;
            _currentAnimationKey = null;
        }
    }
}
