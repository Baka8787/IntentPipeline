using Project.Core.Blackboard;
using Project.Core.Environment;
using Project.Core.Movement;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// Traversal V2 的單一 committed state。Probe 擁有即時 candidate；本 state 在准入時鎖存 candidate，
    /// OnEnter 建立固定 warp plan，執行期間不重新 query、分類或回讀 Probe。
    /// </summary>
    public sealed class TraversalState : BaseState
    {
        private const float FallbackDuration = 0.5f;

        private readonly TraversalProbe _probe;
        private readonly MotionDriver _entryMotionDriver;
        private TraversalStateParamsSO _params;
        private JumpStateParams _jumpParams;
        private TraversalCandidate _committedCandidate;
        private TraversalEntryEvaluation _entryEvaluation;
        private TraversalAnimationFit _animationFit = TraversalAnimationFit.Unfitted(TraversalFitReason.NotFitted);
        private bool _playbackSpeedApplied;
        private TraversalMotionBinding _currentBinding;
        private TraversalWarpPlan _warpPlan;
        private MotionBakeData _currentBake;
        private string _currentAnimationKey;
        private float _remainingDuration;
        private float _previousNormalizedTime;
        private float _normalizedTime;
        private bool _isFinished;
        private bool _canRecover;
        private bool _hasStartedPlayback;
        private bool _warpPlanCommitted;
        private MotionDriver _executionMotionDriver;
        private AnimationFacadeBase _executionAnimationFacade;

        public TraversalState(TraversalProbe probe = null, MotionDriver entryMotionDriver = null)
        {
            _probe = probe;
            _entryMotionDriver = entryMotionDriver;
        }

        public override StateType Type => StateType.Traversal;
        public override string AnimationKey => _currentAnimationKey ?? base.AnimationKey;
        public override bool CanTransitionAway => _canRecover || _isFinished;

        public TraversalKind CommittedKind => _committedCandidate.Kind;
        public TraversalCandidate CommittedCandidate => _committedCandidate;
        public MotionBakeData CurrentBake => _currentBake;
        public float NormalizedTime => _normalizedTime;
        public bool IsFinished => _isFinished;
        public bool CanRecover => _canRecover;
        public bool HasStartedPlayback => _hasStartedPlayback;
        public TraversalWarpPlan WarpPlan => _warpPlan;
        public TraversalEntryEvaluation EntryEvaluation => _entryEvaluation;

        /// <summary>
        /// 🆕（docs/24 §8）本次 traversal 的 Animation Fitting 結果。
        /// 在 <see cref="CanEnter"/> 與 candidate 同時鎖存，執行期只讀。
        /// </summary>
        public TraversalAnimationFit AnimationFit => _animationFit;

        public override void Initialize(StateMachineConfigSO config, IMovementModel movementModel)
        {
            base.Initialize(config, movementModel);
            _params = config.GetStateParams<TraversalStateParamsSO>(Type);
            _jumpParams = config.GetStateParams<JumpStateParams>(StateType.Jump);
            PushDerivedSensingRequirements();

#if UNITY_EDITOR
            if (_params == null && Application.isPlaying)
            {
                Debug.LogWarning(
                    "[TraversalState] StateMachineConfig 沒有綁定 TraversalStateParamsSO。" +
                    "Traversal 仍會安全結束，但不會有動畫或烘焙位移。請在 Integration Gate 完成三種 binding。",
                    config);
            }
#endif
        }

        /// <summary>
        /// 🆕（docs/24 §10）把「動畫需要多遠的感測範圍」推給 Probe。
        ///
        /// ⚠️ 方向是 **StateMachine → Environment 的單向推送**：Probe 只收兩個 float，
        /// 不反向認識 bake／binding（LayerRule：`Core/Environment` 禁止依賴 StateMachine／Presentation）。
        /// 每次 <see cref="CanEnter"/> 都重推一次，讓 Inspector 上臨時勾掉某個 kind 之後
        /// 感測範圍會自己跟上——避免又出現「推導值從來沒生效」那類靜默失效（`docs/22` §13.8）。
        /// </summary>
        private void PushDerivedSensingRequirements()
        {
            if (_probe == null || _params == null) return;
            _probe.SetDerivedRangeRequirements(
                _params.GetRequiredSensingReach(),
                _params.GetRequiredLandingDepth());
        }

        public override bool CanEnter(PlayerRuntimeData data)
        {
            if (!data.Intent.JumpRequested || !data.IsGrounded || _probe == null)
                return false;

            PushDerivedSensingRequirements();
            TraversalCandidate candidate = _probe.Candidate;
            TraversalEntryPolicySettings entrySettings = _params != null
                ? _params.EntryPolicy
                : TraversalEntryPolicySettings.Default;
            // 🆕（docs/22 §14.5）進場距離改由該 traversal 自己的 bake 推導：
            //    Climb2m 想要 ≈0.36 m、Vault1m 想要 ≈1.29 m（跑動翻越自帶助跑）。
            //    推不出來時（缺 Traversal block）退回全域設定，行為與先前一致。
            TraversalMotionBinding candidateBinding = _params != null
                ? _params.GetBinding(candidate.Kind)
                : default;
            float desiredEntryDistance =
                candidateBinding.Bake != null &&
                candidateBinding.Bake.TryGetDesiredEntryDistance(out float derivedEntryDistance)
                    ? derivedEntryDistance
                    : float.NaN;
            TraversalEntryEvaluation evaluation = TraversalEntryPolicy.Evaluate(
                in candidate, in entrySettings, desiredEntryDistance);
            _probe.RecordEntryEvaluation(in evaluation);
            if (!evaluation.Executable)
            {
                // Entry 先擋下就不會評估 selection；清掉上一次的結果，避免面板顯示過期的 Select 行。
                _probe.RecordSelectionEvaluation(default);
                return false;
            }

            float safetyMargin = _params != null ? _params.NormalJumpReachSafetyMargin : 0.15f;
            // 🆕（docs/23 §A10／R1）Selection 的否決過去完全靜默——畫面顯示 `Climb1m` ＋ `Entry: Accept`
            //    卻什麼都不發生。現在無論接受或否決都把理由推給 Probe 顯示。
            bool kindEnabled = _params == null || _params.IsKindEnabled(candidate.Kind);
            TraversalSelectionEvaluation selection = TraversalSelectionPolicy.Evaluate(
                in candidate, _jumpParams, safetyMargin, kindEnabled);
            _probe.RecordSelectionEvaluation(in selection);
            if (!selection.PreferTraversal) return false;

            // 與 JumpState._enterAsFall 同型：CanEnter 是決策邊界，OnEnter 只消費已提交結果。
            _committedCandidate = candidate;
            _entryEvaluation = evaluation;
            // 🆕（docs/24 §8）Animation Fitting 與 candidate 在同一個決策邊界鎖存：
            //    先算「這支動畫要怎麼播才貼近現場尺寸」，`TraversalPlanBuilder` 才去算「剩下的落差怎麼修」。
            TraversalAnimationFitSettings fitSettings =
                _params != null ? _params.AnimationFit : default;
            _animationFit = TraversalAnimationFitter.Fit(
                candidateBinding.Bake, in candidate, in evaluation, in fitSettings);
            _probe.RecordAnimationFit(in _animationFit);
            return true;
        }

        public override void OnEnter(PlayerRuntimeData data)
        {
            _currentBinding = _params != null
                ? _params.GetBinding(_committedCandidate.Kind)
                : default;

            _currentBake = _currentBinding.Bake;
            _currentAnimationKey = string.IsNullOrEmpty(_currentBinding.AnimationKey)
                ? GetFallbackAnimationKey(_committedCandidate.Kind)
                : _currentBinding.AnimationKey;

            float bakedDuration = _currentBake != null ? _currentBake.Duration : 0f;
            // Fitting 改了播放速率 ⇒ 實際秒數同比例縮放。這個計時器只是 presentation 尚未開始前的
            // 安全退化（播放一開始就改由 normalized time 當完成權威），但仍不該用錯的長度。
            float fittedRate = _animationFit.PlaybackRate > 0f ? _animationFit.PlaybackRate : 1f;
            _remainingDuration = bakedDuration > 0f ? bakedDuration / fittedRate : FallbackDuration;
            _previousNormalizedTime = 0f;
            _normalizedTime = 0f;
            _isFinished = false;
            _canRecover = false;
            _hasStartedPlayback = false;
            _warpPlan = default;
            _warpPlanCommitted = false;
            _executionMotionDriver = null;
            _executionAnimationFacade = null;
            _playbackSpeedApplied = false;
            CommitWarpPlan(_entryMotionDriver);

#if UNITY_EDITOR
            Debug.Log($"<color=cyan>[State] 進入 TRAVERSAL：{_committedCandidate.Kind}</color>");
#endif
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (_isFinished || _hasStartedPlayback) return;

            // Presentation 尚未真正開始時才用 Bake duration／固定秒數作安全退化。
            // 一旦播放開始，完成權威改由 normalized time，才能尊重 TransitionAsset 的 playback speed。
            _remainingDuration -= deltaTime;
            if (_remainingDuration <= 0f)
                _isFinished = true;
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            RestoreCollisionProfile();
            // 🆕（docs/24 §8）**速率一定要還原。** Animancer 依 transition key 重用同一個 state 物件，
            //    這次 fit 出來的速率會原封不動留到下一次 traversal（甚至別的狀態共用同一資產時）。
            if (_executionAnimationFacade != null && !string.IsNullOrEmpty(_currentAnimationKey))
                _executionAnimationFacade.SetPlaybackSpeed(_currentAnimationKey, 1f);
            if (_executionMotionDriver != null)
                _executionMotionDriver.EndTraversalPresentation();
            _committedCandidate = default;
            _entryEvaluation = default;
            _currentBake = null;
            _currentBinding = default;
            _warpPlan = default;
            _currentAnimationKey = null;
            _remainingDuration = 0f;
            _previousNormalizedTime = 0f;
            _normalizedTime = 0f;
            _isFinished = false;
            _canRecover = false;
            _hasStartedPlayback = false;
            _warpPlanCommitted = false;
            _executionMotionDriver = null;
            _executionAnimationFacade = null;
            _playbackSpeedApplied = false;
            _animationFit = TraversalAnimationFit.Unfitted(TraversalFitReason.NotFitted);
        }

        public override void OnUpdateMotion(
            MotionDriver motionDriver,
            AnimationFacadeBase animationFacade,
            PlayerRuntimeData data)
        {
            CommitWarpPlan(motionDriver);
            _executionMotionDriver = motionDriver;
            _executionAnimationFacade = animationFacade;

            // Animancer 查表失敗時 GetNormalizedTime 可能仍回傳上一支動畫的進度；先確認本鍵真的在播。
            bool isActuallyPlaying = animationFacade != null &&
                                     !string.IsNullOrEmpty(AnimationKey) &&
                                     animationFacade.IsPlaying(AnimationKey);

            if (!isActuallyPlaying)
            {
                motionDriver.ApplyTraversalCollisionProfile(0f);
                // committed 期間即使 presentation 尚未開始或 binding 缺失，也不可退回普通重力路徑。
                ExecuteCommittedMotion(motionDriver, 0f, 0f, data);
                if (_hasStartedPlayback && Time.deltaTime > 0f)
                    _isFinished = true;
                return;
            }

            // 🆕（docs/24 §8）Animation Fitting 的播放速率**只能在動畫真的開始播之後**套用——
            //    Animancer 的 state 物件要播過一次才進快取。套一次就好，之後不再逐幀寫。
            if (!_playbackSpeedApplied)
            {
                _playbackSpeedApplied = true;
                animationFacade.SetPlaybackSpeed(AnimationKey, _animationFit.PlaybackRate);
            }

            _hasStartedPlayback = true;
            float currentNormalizedTime = Mathf.Clamp01(animationFacade.GetNormalizedTime());
            if (float.IsNaN(currentNormalizedTime) || float.IsInfinity(currentNormalizedTime))
                currentNormalizedTime = _previousNormalizedTime;

            _normalizedTime = currentNormalizedTime;
            motionDriver.ApplyTraversalCollisionProfile(currentNormalizedTime);
            ExecuteCommittedMotion(
                motionDriver, currentNormalizedTime, _previousNormalizedTime, data);
            _previousNormalizedTime = currentNormalizedTime;

            if (_warpPlan.IsValid &&
                currentNormalizedTime >= GetRecoveryNormalizedTime())
            {
                RestoreCollisionProfile();
                _canRecover = true;
            }

            if (currentNormalizedTime >= 1f)
                _isFinished = true;
        }

        private void CommitWarpPlan(MotionDriver motionDriver)
        {
            if (_warpPlanCommitted || motionDriver == null) return;
            _warpPlanCommitted = true;
            TraversalEntryPolicySettings entrySettings = _params != null
                ? _params.EntryPolicy
                : TraversalEntryPolicySettings.Default;
            bool builtPiecewise = TraversalPlanBuilder.TryBuild(
                _currentBake,
                in _committedCandidate,
                in _entryEvaluation,
                entrySettings.LedgeEndMargin,
                _currentBinding.MaxHorizontalCorrection,
                _currentBinding.MaxVerticalCorrection,
                out _warpPlan,
                out TraversalWarpPlanRejection piecewiseRejection);

            float warpStart = _currentBinding.WarpStartNormalizedTime;
            bool warpEndIsDataDerived = !_currentBinding.HasAuthoredWarpEnd;
            float warpEnd = ResolveEndpointWarpEnd(_currentBake, in _currentBinding, warpStart);
            var endpointRejection = TraversalWarpPlanRejection.None;
            float requiredHorizontal = float.NaN;
            float requiredVertical = float.NaN;
            if (!builtPiecewise)
            {
                motionDriver.TryCreateTraversalWarpPlan(
                    _currentBake,
                    _committedCandidate.DestinationPoint,
                    _committedCandidate.Forward,
                    warpStart,
                    warpEnd,
                    _currentBinding.MaxHorizontalCorrection,
                    _currentBinding.MaxVerticalCorrection,
                    out _warpPlan,
                    out endpointRejection,
                    out requiredHorizontal,
                    out requiredVertical);
            }

            // 🆕（docs/23 §R1）三種結局（Piecewise／EndpointFallback／Failed）與各自的理由一律發布，
            //    讓「traversal 開始了但完全沒有 warp」不再只能從畫面上猜。
            TraversalPlanMode mode = builtPiecewise
                ? TraversalPlanMode.Piecewise
                : _warpPlan.IsValid
                    ? TraversalPlanMode.EndpointFallback
                    : TraversalPlanMode.Failed;
            var diagnostics = new TraversalWarpPlanDiagnostics(
                mode,
                piecewiseRejection,
                endpointRejection,
                requiredHorizontal,
                _currentBinding.MaxHorizontalCorrection,
                requiredVertical,
                _currentBinding.MaxVerticalCorrection,
                warpStart,
                warpEnd,
                warpEndIsDataDerived);

            _executionMotionDriver = motionDriver;
            motionDriver.BeginTraversalPresentation(in _warpPlan, in diagnostics);
            if (_warpPlan.IsValid)
            {
                TraversalCollisionProfile collisionProfile = _currentBinding.CollisionProfile;
                motionDriver.BeginTraversalCollisionProfile(in collisionProfile);
            }
        }

        /// <summary>
        /// 🆕（docs/23 §R1／§E-2 缺陷③）endpoint correction 窗的收斂點。
        ///
        /// 未 author 時**不使用涵蓋整支 clip 的常數預設**，改由 bake 自己的
        /// <c>VerticalCurve</c> 平台起點推導——那是動畫「爬完了」的時刻。
        /// 實測：Vault1m ≈ 0.48、Climb1m ≈ 0.585、Climb2m ≈ 0.45，其後是站定或跑出，
        /// 舊預設 0.8 會讓 correction 在角色已經站上平台之後才補完。
        /// </summary>
        private static float ResolveEndpointWarpEnd(
            MotionBakeData bake,
            in TraversalMotionBinding binding,
            float warpStart)
        {
            if (binding.HasAuthoredWarpEnd || bake == null)
                return binding.WarpEndNormalizedTime;

            float settle = bake.GetVerticalSettleNormalizedTime();
            if (float.IsNaN(settle) || float.IsInfinity(settle) || settle <= warpStart)
                return binding.WarpEndNormalizedTime;
            return Mathf.Clamp01(settle);
        }

        private float GetRecoveryNormalizedTime()
        {
            float recovery = _currentBinding.RecoverNormalizedTime;
            if (_warpPlan.IsPiecewise)
                recovery = Mathf.Max(recovery, _warpPlan.RecoveryKnot.NormalizedTime);
            return recovery;
        }

        private void RestoreCollisionProfile()
        {
            if (_executionMotionDriver != null)
                _executionMotionDriver.RestoreTraversalCollisionProfile();
        }

        private void ExecuteCommittedMotion(
            MotionDriver motionDriver,
            float normalizedTime,
            float previousNormalizedTime,
            PlayerRuntimeData data)
        {
            if (_warpPlan.IsValid)
            {
                motionDriver.ExecuteTraversalWarpedMovement(
                    in _warpPlan, normalizedTime, previousNormalizedTime, data);
                return;
            }

            motionDriver.ExecuteCommittedCurveMovement(
                _currentBake, normalizedTime, previousNormalizedTime, data);
        }

        private static string GetFallbackAnimationKey(TraversalKind kind)
        {
            switch (kind)
            {
                case TraversalKind.Vault1m:
                    return "Vault1m";
                case TraversalKind.Climb1m:
                    return "Climb1m";
                case TraversalKind.Climb2m:
                    return "Climb2m";
                default:
                    return "Traversal";
            }
        }
    }
}
