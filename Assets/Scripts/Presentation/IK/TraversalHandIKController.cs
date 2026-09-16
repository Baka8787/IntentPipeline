using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Presentation.IK
{
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
#endif


        private TraversalHandIKTargetData _targetData;
        private TraversalHandIKPoseData _poseData;
        private Transform _leftHandBone;
        private Transform _rightHandBone;

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
            }
        }

        private void Start()
        {
            AnimationFacadeBase facade = GetComponent<AnimationFacadeBase>();
            if (facade != null) facade.SetApplyAnimatorIK(0, true);
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

            _targetData.LeftHandPosition = contacts.LeftHandWorldTarget;
            _targetData.RightHandPosition = contacts.RightHandWorldTarget;
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
            float exitTime = plan.ExitKnot.NormalizedTime;
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

            if (leftWeight <= 0f && rightWeight <= 0f)
            {
                Status = TraversalHandIKStatus.OutsideContactWindow;
                UpdateDebug();
                return;
            }

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
            UpdateDebug();
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
                    $"  solved {LeftSolvedResidualError * 100f:F1} cm\n" +
                    $"R w {_targetData.RightHandPositionWeight:F2}" +
                    $"  goal {RightResidualError * 100f:F1} cm" +
                    $"  solved {RightSolvedResidualError * 100f:F1} cm";
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
