using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Presentation.IK
{
    public readonly struct TraversalExecutionMeasurementSample
    {
        public readonly bool HasSample;
        public readonly float NormalizedTime;
        public readonly float PreIKHandResidual;
        public readonly float PostIKHandResidual;
        public readonly float RawWristReach;
        public readonly float ArmReachRatio;
        public readonly float RootDivergence;
        public readonly float WarpBlockedDisplacement;
        public readonly float SegmentBoundaryVelocityDelta;
        public readonly float LeftIKWeight;
        public readonly float RightIKWeight;

        public TraversalExecutionMeasurementSample(
            float normalizedTime,
            float preIKHandResidual,
            float postIKHandResidual,
            float rawWristReach,
            float armReachRatio,
            float rootDivergence,
            float warpBlockedDisplacement,
            float segmentBoundaryVelocityDelta,
            float leftIKWeight,
            float rightIKWeight)
        {
            HasSample = true;
            NormalizedTime = normalizedTime;
            PreIKHandResidual = preIKHandResidual;
            PostIKHandResidual = postIKHandResidual;
            RawWristReach = rawWristReach;
            ArmReachRatio = armReachRatio;
            RootDivergence = rootDivergence;
            WarpBlockedDisplacement = warpBlockedDisplacement;
            SegmentBoundaryVelocityDelta = segmentBoundaryVelocityDelta;
            LeftIKWeight = leftIKWeight;
            RightIKWeight = rightIKWeight;
        }
    }

    public enum TraversalHandIKStatus
    {
        NotBound = 0,
        Inactive,
        RootMappingUnavailable,
        NotConfigured,
        Disabled,
        AwaitingPose,
        OutsideContactWindow,
        ResidualTooLarge,
        Active,
    }

    /// <summary>
    /// Minimal traversal Hand IK quality pass. It consumes MotionDriver's committed plan only;
    /// it never reads TraversalProbe and never changes root motion or collision.
    /// </summary>
    public sealed class TraversalHandIKController : MonoBehaviour, IPresentationController
    {
        private const float DefaultMaximumResidualDistance = 0.35f;

        [SerializeField] private MotionDriver motionDriver;
        [SerializeField] private bool enableHandIK = true;
        [SerializeField, Min(0f)] private float maximumResidualDistance =
            DefaultMaximumResidualDistance;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private bool drawTraversalHandIKDebug = false;
        /// <summary>Game View 文字面板，預設關閉（見 MotionDriver 的同名欄位）。</summary>
        [SerializeField] private bool drawTraversalPanelText = false;
        private TextMesh _debugRuntimeLabel;

        private const int TargetRingSegments = 18;
        private const float TargetMarkerRadius = 0.055f;
        private const float TargetDirectionLength = 0.14f;
        private const float TargetLineWidth = 0.012f;

        private readonly LineRenderer[] _debugHandVectors = new LineRenderer[2];
        private readonly LineRenderer[] _debugTargetRings = new LineRenderer[2];
        private readonly TextMesh[] _debugTargetLabels = new TextMesh[2];
        private readonly Vector3[] _debugTargetRingBuffer = new Vector3[TargetRingSegments];

        // ⭐ 2026-09-20：**解算後手腕的結構球。**
        // 原本只有「手骨→target 連線」與 target 環，看不出**解完之後手實際落在哪**——
        // 而 1.6 m 的 39.47 cm contact 殘差正是要靠這個才看得出來。
        // 用三個正交圓（世界 XY／XZ／YZ）而不是單一環：單環從側面看會塌成一條線，
        // 正交球從任何視角都讀得出球心。三段圓首尾相接，只有一條 (r,0,0)→(0,r,0) 的弦。
        private const int SolvedBallSegments = 14;
        private const float SolvedBallRadius = 0.035f;
        private readonly LineRenderer[] _debugSolvedBalls = new LineRenderer[2];
        private readonly Vector3[] _debugSolvedBallBuffer =
            new Vector3[(SolvedBallSegments + 1) * 3];
        private Transform _debugHandRoot;
        private Material _debugHandMaterial;
        private Camera _debugCamera;
#endif


        private TraversalHandIKTargetData _targetData;
        private TraversalHandIKPoseData _poseData;
        private Transform _leftHandBone;
        private Transform _rightHandBone;
        private Transform _leftUpperArmBone;
        private Transform _leftLowerArmBone;
        private Transform _rightUpperArmBone;
        private Transform _rightLowerArmBone;
        private bool _measurementWasActive;
        private float _lastMeasurementNormalizedTime;

        public TraversalHandIKTargetData TargetData => _targetData;
        public TraversalHandIKPoseData PoseData => _poseData;
        public TraversalHandIKStatus Status { get; private set; } = TraversalHandIKStatus.NotBound;
        public bool IsEnabled => enableHandIK;
        public Vector3 LeftSolvedPosition { get; private set; }
        public Vector3 RightSolvedPosition { get; private set; }
        public float LeftResidualError { get; private set; }
        public float RightResidualError { get; private set; }
        public float LeftSolvedResidualError { get; private set; }
        public float RightSolvedResidualError { get; private set; }
        public float LeftRawWristReach { get; private set; }
        public float RightRawWristReach { get; private set; }
        public float LeftArmReachRatio { get; private set; }
        public float RightArmReachRatio { get; private set; }
        public float MaxArmReachRatio { get; private set; }
        public float MaxPreIKResidual { get; private set; }
        public float MaxPostIKResidual { get; private set; }
        public TraversalExecutionMeasurementSample ContactSample { get; private set; }
        public TraversalExecutionMeasurementSample ContactMidpointSample { get; private set; }
        public TraversalExecutionMeasurementSample TransferPreviousFrameSample { get; private set; }
        public TraversalExecutionMeasurementSample TransferSample { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Game View target visualization switch. It only controls runtime renderers; IK weights,
        /// committed targets, root motion, and every gameplay value remain untouched.
        /// </summary>
        public bool DebugDrawRuntimeTargets
        {
            get => drawTraversalHandIKDebug;
            set
            {
                drawTraversalHandIKDebug = value;
                if (!value) DisableRuntimeHandTargets();
            }
        }
#endif

        private void Awake()
        {
            _targetData = new TraversalHandIKTargetData();
            if (motionDriver == null) motionDriver = GetComponent<MotionDriver>();

            TraversalHandIKRig rig = GetComponentInChildren<TraversalHandIKRig>();
            if (rig == null)
            {
                Status = TraversalHandIKStatus.NotBound;
                return;
            }

            rig.Bind(_targetData);
            _poseData = rig.PoseData;
            Animator animator = rig.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                _leftHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                _rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
                _leftUpperArmBone = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                _leftLowerArmBone = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                _rightUpperArmBone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                _rightLowerArmBone = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            }
        }

        private void Start()
        {
            AnimationFacadeBase facade = GetComponent<AnimationFacadeBase>();
            if (facade != null) facade.SetApplyAnimatorIK(0, true);
        }

        private void OnDisable()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DisableRuntimeHandTargets();
            if (_debugRuntimeLabel != null) _debugRuntimeLabel.gameObject.SetActive(false);
#endif
        }

        public void Tick(PlayerRuntimeData data)
        {
            _targetData.ResetWeights();
            LeftResidualError = 0f;
            RightResidualError = 0f;
            LeftSolvedResidualError = 0f;
            RightSolvedResidualError = 0f;

            if (motionDriver == null || _poseData == null)
            {
                Status = TraversalHandIKStatus.NotBound;
                UpdateDebug();
                return;
            }
            if (!motionDriver.HasActiveTraversalPlan)
            {
                _measurementWasActive = false;
                Status = TraversalHandIKStatus.Inactive;
                UpdateDebug();
                return;
            }

            TraversalWarpPlan plan = motionDriver.ActiveTraversalPlan;
            TraversalContactTargets contacts = plan.ContactTargets;
            if (!plan.IsPiecewise || (!contacts.HasLeftHand && !contacts.HasRightHand))
            {
                Status = TraversalHandIKStatus.RootMappingUnavailable;
                UpdateDebug();
                return;
            }

            Vector3 rootDisplacementCompensation = GetPostAnimatorRootDisplacementCompensation();
            _targetData.LeftHandPosition =
                contacts.LeftHandWorldTarget - rootDisplacementCompensation;
            _targetData.RightHandPosition =
                contacts.RightHandWorldTarget - rootDisplacementCompensation;
            _targetData.LeftHandRotation = contacts.LeftHandWorldRotation;
            _targetData.RightHandRotation = contacts.RightHandWorldRotation;

            if (!contacts.HandIKConfigured)
            {
                Status = TraversalHandIKStatus.NotConfigured;
                UpdateDebug();
                return;
            }
            if (!enableHandIK)
            {
                Status = TraversalHandIKStatus.Disabled;
                UpdateDebug();
                return;
            }
            if (!_poseData.IsWarm)
            {
                Status = TraversalHandIKStatus.AwaitingPose;
                UpdateDebug();
                return;
            }

            float normalizedTime = motionDriver.ActiveTraversalNormalizedTime;
            if (!_measurementWasActive || normalizedTime + 0.0001f < _lastMeasurementNormalizedTime)
                ResetExecutionMeasurements();
            _measurementWasActive = true;
            _lastMeasurementNormalizedTime = normalizedTime;
            float exitTime = plan.ExitKnot.NormalizedTime;

            // The authored window is authoritative. TraversalContactDerivation.BuildHandIKWindow
            // derives release from FixedGoalExtensionStart - fade, so pinning the wrist past it
            // would demand a longer shoulder-to-wrist chain than the animation's own push-off.
            float leftWeight = contacts.HasLeftHand
                ? contacts.LeftHandIKWindow.Evaluate(normalizedTime, exitTime)
                : 0f;
            float rightWeight = contacts.HasRightHand
                ? contacts.RightHandIKWindow.Evaluate(normalizedTime, exitTime)
                : 0f;
            float maxResidual = IsFinite(maximumResidualDistance) && maximumResidualDistance > 0f
                ? maximumResidualDistance
                : DefaultMaximumResidualDistance;
            LeftResidualError = contacts.HasLeftHand
                ? Vector3.Distance(_poseData.LeftHandPosition, contacts.LeftHandWorldTarget)
                : 0f;
            RightResidualError = contacts.HasRightHand
                ? Vector3.Distance(_poseData.RightHandPosition, contacts.RightHandWorldTarget)
                : 0f;

            LeftSolvedPosition = _leftHandBone != null
                ? _leftHandBone.position
                : _poseData.LeftHandPosition;
            RightSolvedPosition = _rightHandBone != null
                ? _rightHandBone.position
                : _poseData.RightHandPosition;
            LeftSolvedResidualError = contacts.HasLeftHand
                ? Vector3.Distance(LeftSolvedPosition, contacts.LeftHandWorldTarget)
                : 0f;
            RightSolvedResidualError = contacts.HasRightHand
                ? Vector3.Distance(RightSolvedPosition, contacts.RightHandWorldTarget)
                : 0f;
            UpdateArmReachMetrics(in contacts, leftWeight, rightWeight);

            if (leftWeight <= 0f && rightWeight <= 0f)
            {
                Status = TraversalHandIKStatus.OutsideContactWindow;
                UpdateDebug();
                return;
            }

            // ⭐ 2026-09-20：**max 累加器只在 contact window 內折算。**
            //
            // 舊版把這三個 fold 放在上面那個 early return **之前**，於是只要 plan 還 active
            // 就一直累加——包含 Transfer 之後、角色已經爬上平台走遠、而 committed hand target
            // 還釘在下方邊緣的所有幀。實測後果：三個牆高全都回報
            // `pre/post IK max ≈ 122 cm`、`arm ratio max ≈ 2.8`、`raw wrist reach ≈ 1.57 m`，
            // 而同一次 traversal 的四個取樣點是 3–39 cm ／ 0.70–1.07。
            // 那不是超額，那是「手離 0.5 個 normalized time 前放開的那個點有多遠」。
            //
            // 這與 `docs/39` F1（blocked displacement 被 post-exit 貼地力污染）是**同一類缺陷**：
            // 未被窗口限制的 max 累加器。閘門刻意放在 residual 否決**之前**——
            // 被 guard 擋下來的那一幀（1.6 m 的 39.47 cm）正是最需要被記錄的值。
            // Gate each value by its own authored weight: a zero-weight hand is not being asked
            // to reach, so its distance to a released target is not a reach measurement.
            MaxPreIKResidual = FoldActiveHandMaximum(
                MaxPreIKResidual,
                LeftResidualError, leftWeight,
                RightResidualError, rightWeight);
            MaxPostIKResidual = FoldActiveHandMaximum(
                MaxPostIKResidual,
                LeftSolvedResidualError, leftWeight,
                RightSolvedResidualError, rightWeight);
            MaxArmReachRatio = FoldActiveHandMaximum(
                MaxArmReachRatio,
                LeftArmReachRatio, leftWeight,
                RightArmReachRatio, rightWeight);

            bool residualRejected = false;
            if (leftWeight > 0f &&
                (!IsFinite(LeftResidualError) || LeftResidualError > maxResidual))
            {
                leftWeight = 0f;
                residualRejected |= contacts.HasLeftHand;
            }
            if (rightWeight > 0f &&
                (!IsFinite(RightResidualError) || RightResidualError > maxResidual))
            {
                rightWeight = 0f;
                residualRejected |= contacts.HasRightHand;
            }

            _targetData.LeftHandPositionWeight = leftWeight;
            _targetData.RightHandPositionWeight = rightWeight;
            _targetData.LeftHandRotationWeight = leftWeight * contacts.HandIKRotationWeight;
            _targetData.RightHandRotationWeight = rightWeight * contacts.HandIKRotationWeight;

            Status = residualRejected
                ? TraversalHandIKStatus.ResidualTooLarge
                : TraversalHandIKStatus.Active;
            CaptureSegmentedExecutionMeasurements(
                in plan,
                normalizedTime,
                leftWeight,
                rightWeight);
            UpdateDebug();
        }

        private void ResetExecutionMeasurements()
        {
            MaxArmReachRatio = 0f;
            MaxPreIKResidual = 0f;
            MaxPostIKResidual = 0f;
            ContactSample = default;
            ContactMidpointSample = default;
            TransferPreviousFrameSample = default;
            TransferSample = default;
        }

        private Vector3 GetPostAnimatorRootDisplacementCompensation()
        {
            TraversalExecutionResult result = motionDriver.LastTraversalExecutionResult;
            if (!result.HasResult) return Vector3.zero;

            Vector3 actualDisplacement = result.ActualDisplacement;
            float maximumDisplacement = MotionDriver.MaxCommittedFrameDisplacement;
            if (!IsFinite(actualDisplacement) ||
                actualDisplacement.sqrMagnitude > maximumDisplacement * maximumDisplacement)
                return Vector3.zero;

            // Animator IK runs before LateUpdate, while traversal root movement is applied during
            // LateUpdate after the IK pass. The last actual root delta predicts that next move, so
            // the rig receives target - delta and the later root move returns the rendered hand to
            // the committed world target. Before this compensation, midpoint residual measured
            // 6.77 cm at timeScale 1.0 and 2.53 cm at timeScale 0.25.
            return actualDisplacement;
        }

        internal static float FoldActiveHandMaximum(
            float currentMaximum,
            float leftValue,
            float leftWeight,
            float rightValue,
            float rightWeight)
        {
            if (leftWeight > 0f) currentMaximum = Mathf.Max(currentMaximum, leftValue);
            if (rightWeight > 0f) currentMaximum = Mathf.Max(currentMaximum, rightValue);
            return currentMaximum;
        }

        private void UpdateArmReachMetrics(
            in TraversalContactTargets contacts,
            float leftWeight,
            float rightWeight)
        {
            LeftRawWristReach = RawWristReach(
                _leftUpperArmBone,
                _poseData.LeftHandPosition,
                contacts.LeftHandWorldTarget,
                leftWeight,
                contacts.HasLeftHand);
            RightRawWristReach = RawWristReach(
                _rightUpperArmBone,
                _poseData.RightHandPosition,
                contacts.RightHandWorldTarget,
                rightWeight,
                contacts.HasRightHand);
            LeftArmReachRatio = ArmReachRatio(
                _leftUpperArmBone, _leftLowerArmBone, _leftHandBone,
                LeftRawWristReach, contacts.HasLeftHand);
            RightArmReachRatio = ArmReachRatio(
                _rightUpperArmBone, _rightLowerArmBone, _rightHandBone,
                RightRawWristReach, contacts.HasRightHand);
            // Reach follows the goal Animator IK is actually asked to satisfy: the animation's
            // pre-override hand goal blended toward the uncompensated committed world target by
            // that hand's weight. The old target-only formula reported a released target's
            // distance as if it were arm stretch. Max folding still belongs to the caller and is
            // gated per hand inside the contact window.
        }

        private static float RawWristReach(
            Transform shoulder,
            Vector3 animatedHandGoal,
            Vector3 committedWorldTarget,
            float weight,
            bool hasTarget)
        {
            if (!hasTarget || shoulder == null) return 0f;
            return EvaluateBlendedWristReach(
                shoulder.position,
                animatedHandGoal,
                committedWorldTarget,
                weight);
        }

        internal static float EvaluateBlendedWristReach(
            Vector3 shoulderPosition,
            Vector3 animatedHandGoal,
            Vector3 committedWorldTarget,
            float weight)
        {
            if (!IsFinite(shoulderPosition) ||
                !IsFinite(animatedHandGoal) ||
                !IsFinite(committedWorldTarget) ||
                !IsFinite(weight))
                return 0f;

            Vector3 effectiveGoal = Vector3.Lerp(
                animatedHandGoal,
                committedWorldTarget,
                weight);
            return Vector3.Distance(shoulderPosition, effectiveGoal);
        }

        private static float ArmReachRatio(
            Transform shoulder,
            Transform elbow,
            Transform wrist,
            float rawReach,
            bool hasTarget)
        {
            if (!hasTarget || shoulder == null || elbow == null || wrist == null)
                return 0f;
            float armLength = Vector3.Distance(shoulder.position, elbow.position) +
                              Vector3.Distance(elbow.position, wrist.position);
            return armLength > 0.0001f && IsFinite(rawReach)
                ? rawReach / armLength
                : 0f;
        }

        private void CaptureSegmentedExecutionMeasurements(
            in TraversalWarpPlan plan,
            float normalizedTime,
            float leftWeight,
            float rightWeight)
        {
            if (!motionDriver.IsSegmentedExecutionActive) return;

            float contact = Mathf.Max(
                plan.LeftContactKnot.NormalizedTime,
                plan.RightContactKnot.NormalizedTime);
            float transfer = plan.TransferKnot.NormalizedTime;
            float midpoint = 0.5f * (contact + transfer);
            TraversalExecutionMeasurementSample sample = CreateMeasurementSample(
                normalizedTime, leftWeight, rightWeight);

            if (!ContactSample.HasSample && normalizedTime >= contact)
                ContactSample = sample;
            if (!ContactMidpointSample.HasSample && normalizedTime >= midpoint)
                ContactMidpointSample = sample;
            if (normalizedTime < transfer)
                TransferPreviousFrameSample = sample;
            if (!TransferSample.HasSample && normalizedTime >= transfer)
                TransferSample = sample;
        }

        private TraversalExecutionMeasurementSample CreateMeasurementSample(
            float normalizedTime,
            float leftWeight,
            float rightWeight)
        {
            return new TraversalExecutionMeasurementSample(
                normalizedTime,
                Mathf.Max(LeftResidualError, RightResidualError),
                Mathf.Max(LeftSolvedResidualError, RightSolvedResidualError),
                Mathf.Max(LeftRawWristReach, RightRawWristReach),
                Mathf.Max(LeftArmReachRatio, RightArmReachRatio),
                motionDriver.ActiveSegmentRootError,
                motionDriver.LastTraversalMaxWarpBlockedDisplacement,
                motionDriver.LastTraversalMaxSegmentBoundaryVelocityDelta,
                leftWeight,
                rightWeight);
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void UpdateDebug()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UpdateRuntimeHandTargets();

            if (!drawTraversalHandIKDebug || !drawTraversalPanelText)
            {
                if (_debugRuntimeLabel != null) _debugRuntimeLabel.gameObject.SetActive(false);
                return;
            }
            EnsureRuntimeDebugLabel();
            if (_debugRuntimeLabel != null)
            {
                // docs/23 §H-1／§H-2：面板不再印 target／animated／solved 的 Vector3 全值——
                // 這三個點與它們之間的誤差向量已經由 OnDrawGizmos 畫在世界空間裡。
                // 這裡只留純量殘差與權重。
                _debugRuntimeLabel.text =
                    $"Hand IK: {Status} enabled={enableHandIK}\n" +
                    $"L w {_targetData.LeftHandPositionWeight:F2}" +
                    $"  goal {LeftResidualError * 100f:F1} cm" +
                    $"  solved {LeftSolvedResidualError * 100f:F1} cm" +
                    $"  reach {LeftRawWristReach:F3} m / {LeftArmReachRatio:F3}\n" +
                    $"R w {_targetData.RightHandPositionWeight:F2}" +
                    $"  goal {RightResidualError * 100f:F1} cm" +
                    $"  solved {RightSolvedResidualError * 100f:F1} cm" +
                    $"  reach {RightRawWristReach:F3} m / {RightArmReachRatio:F3}";
                _debugRuntimeLabel.transform.position = transform.position + Vector3.up * 2.25f;
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    Vector3 look = _debugRuntimeLabel.transform.position - mainCamera.transform.position;
                    if (look.sqrMagnitude > 0.000001f)
                        _debugRuntimeLabel.transform.rotation = Quaternion.LookRotation(look);
                }
            }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Game View main channel: current hand bone → committed hand target, target marker, and
        /// target orientation. Every value comes from the active production plan or actual rig;
        /// this method performs no query and no IK solve.
        /// </summary>
        private void UpdateRuntimeHandTargets()
        {
            if (!drawTraversalHandIKDebug || motionDriver == null ||
                !motionDriver.HasActiveTraversalPlan || _targetData == null)
            {
                DisableRuntimeHandTargets();
                return;
            }

            TraversalContactTargets contacts = motionDriver.ActiveTraversalPlan.ContactTargets;
            UpdateRuntimeHandTarget(
                0,
                contacts.HasLeftHand,
                _leftHandBone != null
                    ? _leftHandBone.position
                    : (_poseData != null ? _poseData.LeftHandPosition : Vector3.zero),
                contacts.LeftHandWorldTarget,
                contacts.LeftHandWorldRotation,
                Color.yellow,
                "L Hand Target");
            UpdateRuntimeHandTarget(
                1,
                contacts.HasRightHand,
                _rightHandBone != null
                    ? _rightHandBone.position
                    : (_poseData != null ? _poseData.RightHandPosition : Vector3.zero),
                contacts.RightHandWorldTarget,
                contacts.RightHandWorldRotation,
                new Color(1f, 0.55f, 0.1f, 1f),
                "R Hand Target");
        }

        private void UpdateRuntimeHandTarget(
            int index,
            bool hasTarget,
            Vector3 currentHand,
            Vector3 target,
            Quaternion targetRotation,
            Color color,
            string label)
        {
            if (!hasTarget)
            {
                SetRuntimeHandTargetEnabled(index, false);
                return;
            }

            EnsureRuntimeHandTarget(index, color, label);
            LineRenderer vector = _debugHandVectors[index];
            LineRenderer ring = _debugTargetRings[index];
            TextMesh targetLabel = _debugTargetLabels[index];
            if (vector == null || ring == null || targetLabel == null) return;

            vector.enabled = true;
            vector.SetPosition(0, currentHand);
            vector.SetPosition(1, target);
            vector.SetPosition(2, target);
            vector.SetPosition(3, target + targetRotation * Vector3.forward * TargetDirectionLength);

            Vector3 right = targetRotation * Vector3.right * TargetMarkerRadius;
            Vector3 up = targetRotation * Vector3.up * TargetMarkerRadius;
            for (int i = 0; i < TargetRingSegments; i++)
            {
                float angle = i * (Mathf.PI * 2f / TargetRingSegments);
                _debugTargetRingBuffer[i] = target + right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            }
            ring.enabled = true;
            ring.SetPositions(_debugTargetRingBuffer);

            // 解算後手腕的結構球。`currentHand` 已經是 IK 解完的骨骼位置
            // （與 LeftSolvedPosition／RightSolvedPosition 同源），所以
            // 「球心 ↔ 環心」的距離就是畫面上的 post-IK residual。
            LineRenderer solvedBall = _debugSolvedBalls[index];
            if (solvedBall != null)
            {
                FillSolvedBall(currentHand);
                solvedBall.enabled = true;
                solvedBall.SetPositions(_debugSolvedBallBuffer);
            }

            targetLabel.gameObject.SetActive(true);
            targetLabel.transform.position = target + Vector3.up * 0.075f;
            if (_debugCamera == null) _debugCamera = Camera.main;
            if (_debugCamera != null)
            {
                Vector3 look = targetLabel.transform.position - _debugCamera.transform.position;
                if (look.sqrMagnitude > 0.000001f)
                    targetLabel.transform.rotation = Quaternion.LookRotation(look);
            }
        }

        /// <summary>
        /// 把三個正交圓（世界 XY／XZ／YZ）寫進共用緩衝區。逐幀呼叫但零配置：
        /// <c>Vector3</c> 是 struct，緩衝區在欄位上一次配好。
        /// </summary>
        private void FillSolvedBall(Vector3 center)
        {
            const float step = Mathf.PI * 2f / SolvedBallSegments;
            int w = 0;
            for (int i = 0; i <= SolvedBallSegments; i++)
            {
                float a = i * step;
                _debugSolvedBallBuffer[w++] = center + new Vector3(
                    Mathf.Cos(a) * SolvedBallRadius, Mathf.Sin(a) * SolvedBallRadius, 0f);
            }
            for (int i = 0; i <= SolvedBallSegments; i++)
            {
                float a = i * step;
                _debugSolvedBallBuffer[w++] = center + new Vector3(
                    Mathf.Cos(a) * SolvedBallRadius, 0f, Mathf.Sin(a) * SolvedBallRadius);
            }
            for (int i = 0; i <= SolvedBallSegments; i++)
            {
                float a = i * step;
                _debugSolvedBallBuffer[w++] = center + new Vector3(
                    0f, Mathf.Cos(a) * SolvedBallRadius, Mathf.Sin(a) * SolvedBallRadius);
            }
        }

        private void EnsureRuntimeHandTarget(int index, Color color, string label)
        {
            EnsureRuntimeHandDebugRoot();
            if (_debugHandRoot == null || _debugHandMaterial == null) return;

            if (_debugHandVectors[index] == null)
            {
                _debugHandVectors[index] = CreateRuntimeLine($"{label} Vector", 4, false, color);
            }
            if (_debugTargetRings[index] == null)
            {
                _debugTargetRings[index] = CreateRuntimeLine(
                    $"{label} Marker", TargetRingSegments, true, color);
            }
            if (_debugSolvedBalls[index] == null)
            {
                // 比 target 暗一階，讓「球＝解算結果」與「環＝committed target」一眼分得開，
                // 同時保留左右手的色相差異。
                var solvedColor = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 1f);
                _debugSolvedBalls[index] = CreateRuntimeLine(
                    $"{label} Solved", _debugSolvedBallBuffer.Length, false, solvedColor);
            }
            if (_debugTargetLabels[index] == null)
            {
                var labelObject = new GameObject(label)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = gameObject.layer,
                };
                labelObject.transform.SetParent(_debugHandRoot, false);
                TextMesh text = labelObject.AddComponent<TextMesh>();
                text.text = label;
                text.anchor = TextAnchor.LowerCenter;
                text.alignment = TextAlignment.Center;
                text.characterSize = 0.018f;
                text.fontSize = 42;
                text.color = color;
                _debugTargetLabels[index] = text;
            }
        }

        private void EnsureRuntimeHandDebugRoot()
        {
            if (_debugHandMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                _debugHandMaterial = new Material(shader)
                {
                    name = "Traversal Hand Target Debug (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            if (_debugHandRoot == null)
            {
                var root = new GameObject("Traversal Hand Target Debug")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = gameObject.layer,
                };
                _debugHandRoot = root.transform;
                _debugHandRoot.SetParent(transform, false);
            }
        }

        private LineRenderer CreateRuntimeLine(
            string lineName,
            int positionCount,
            bool loop,
            Color color)
        {
            var lineObject = new GameObject(lineName)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = gameObject.layer,
            };
            lineObject.transform.SetParent(_debugHandRoot, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = _debugHandMaterial;
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = positionCount;
            line.startWidth = TargetLineWidth;
            line.endWidth = TargetLineWidth;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return line;
        }

        private void DisableRuntimeHandTargets()
        {
            for (int i = 0; i < 2; i++) SetRuntimeHandTargetEnabled(i, false);
        }

        private void SetRuntimeHandTargetEnabled(int index, bool enabled)
        {
            if (_debugHandVectors[index] != null) _debugHandVectors[index].enabled = enabled;
            if (_debugTargetRings[index] != null) _debugTargetRings[index].enabled = enabled;
            if (_debugSolvedBalls[index] != null) _debugSolvedBalls[index].enabled = enabled;
            if (_debugTargetLabels[index] != null)
                _debugTargetLabels[index].gameObject.SetActive(enabled);
        }

        private void EnsureRuntimeDebugLabel()
        {
            if (_debugRuntimeLabel != null) return;
            var labelObject = new GameObject("Traversal Hand IK Debug")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = gameObject.layer,
            };
            labelObject.transform.SetParent(transform, false);
            _debugRuntimeLabel = labelObject.AddComponent<TextMesh>();
            _debugRuntimeLabel.anchor = TextAnchor.LowerCenter;
            _debugRuntimeLabel.alignment = TextAlignment.Center;
            _debugRuntimeLabel.characterSize = 0.025f;
            _debugRuntimeLabel.fontSize = 42;
        }
#endif

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !drawTraversalHandIKDebug || _targetData == null) return;
            DrawHandDebug(
                "Left",
                _poseData != null ? _poseData.LeftHandPosition : Vector3.zero,
                LeftSolvedPosition,
                _targetData.LeftHandPosition,
                _targetData.LeftHandPositionWeight,
                LeftResidualError,
                Color.yellow);
            DrawHandDebug(
                "Right",
                _poseData != null ? _poseData.RightHandPosition : Vector3.zero,
                RightSolvedPosition,
                _targetData.RightHandPosition,
                _targetData.RightHandPositionWeight,
                RightResidualError,
                new Color(1f, 0.55f, 0.1f));
            // ⛔ docs/22 §16：Scene View 不印字。狀態與殘差在 Traversal Debug 視窗。
        }

        private static void DrawHandDebug(
            string label,
            Vector3 animated,
            Vector3 solved,
            Vector3 target,
            float weight,
            float residual,
            Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(target, 0.045f);
            if (animated.sqrMagnitude > 0.000001f) Gizmos.DrawLine(animated, target);
            // ⚠️ `solved` 在未綁定／手骨解析失敗時是 (0,0,0)。舊版無條件連線 ⇒ 畫出一條
            //    從世界原點橫貫整個畫面的洋紅線（2026-09-13 影片裡那條）。畫之前先確認它是真值。
            if (solved.sqrMagnitude > 0.000001f)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(solved, target);
            }
            _ = label; _ = weight; _ = residual; // 數值在 Traversal Debug 視窗，不在這裡印字。
        }
#endif
    }
}
