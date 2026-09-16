using UnityEngine;

namespace Project.Presentation.Motion
{
    /// <summary>
    /// 🆕（docs/23 §R1）Plan 建立失敗的理由碼。**靜默 <c>false</c> 是 review 指認的可觀測性缺口**：
    /// 例如 Vault1m 的 baked 水平總位移 3.26m 會讓 endpoint correction 超限，
    /// 舊版只回 <c>false</c>，畫面上看不出 traversal 為何整段退回無 warp 的 baked 曲線。
    /// </summary>
    public enum TraversalWarpPlanRejection
    {
        None = 0,
        MissingBake,
        InvalidBakeCurves,
        InvalidInputPose,
        InvalidWarpWindow,
        DegenerateStartForward,
        ZeroBakedHorizontal,
        HorizontalCorrectionExceeded,
        VerticalCorrectionExceeded,
        NonFiniteResult,

        // ── piecewise 專屬 ──
        InvalidCandidate,
        MissingTraversalBlock,
        InvalidLedgeFrame,
        CorridorBlocked,
        EntryNotExecutable,
        ContactSolveFailed,
        KnotRejected,
        KnotOrderInvalid,
    }

    /// <summary>執行期實際採用的 plan 型態。</summary>
    public enum TraversalPlanMode
    {
        /// <summary>尚未 commit（state 剛進入、或沒有 MotionDriver）。</summary>
        NotCommitted = 0,
        /// <summary>V3 六 knot contact plan。</summary>
        Piecewise,
        /// <summary>V2 endpoint plan（piecewise 不可用時的退化路徑）。</summary>
        EndpointFallback,
        /// <summary>兩條路徑都建不起來 ⇒ 執行純 baked 曲線，無任何 warp。</summary>
        Failed,
    }

    /// <summary>
    /// Commit 當下的 plan 診斷快照。由 <c>TraversalState</c> 填入、<c>MotionDriver</c> 保存供 debug 讀取。
    /// **只描述決策，不參與執行。**
    /// </summary>
    public readonly struct TraversalWarpPlanDiagnostics
    {
        public readonly bool HasResult;
        public readonly TraversalPlanMode Mode;
        public readonly TraversalWarpPlanRejection PiecewiseRejection;
        public readonly TraversalWarpPlanRejection EndpointRejection;
        /// <summary>endpoint 路徑實際需要的水平修正量（m）與其上限，用來解釋 `HorizontalCorrectionExceeded`。</summary>
        public readonly float RequiredHorizontalCorrection;
        public readonly float MaxHorizontalCorrection;
        public readonly float RequiredVerticalCorrection;
        public readonly float MaxVerticalCorrection;
        /// <summary>實際生效的 correction 窗（可能由 bake 推導而非 author）。</summary>
        public readonly float WarpStartNormalizedTime;
        public readonly float WarpEndNormalizedTime;
        public readonly bool WarpEndIsDataDerived;

        public TraversalWarpPlanDiagnostics(
            TraversalPlanMode mode,
            TraversalWarpPlanRejection piecewiseRejection,
            TraversalWarpPlanRejection endpointRejection,
            float requiredHorizontalCorrection,
            float maxHorizontalCorrection,
            float requiredVerticalCorrection,
            float maxVerticalCorrection,
            float warpStartNormalizedTime,
            float warpEndNormalizedTime,
            bool warpEndIsDataDerived)
        {
            HasResult = true;
            Mode = mode;
            PiecewiseRejection = piecewiseRejection;
            EndpointRejection = endpointRejection;
            RequiredHorizontalCorrection = requiredHorizontalCorrection;
            MaxHorizontalCorrection = maxHorizontalCorrection;
            RequiredVerticalCorrection = requiredVerticalCorrection;
            MaxVerticalCorrection = maxVerticalCorrection;
            WarpStartNormalizedTime = warpStartNormalizedTime;
            WarpEndNormalizedTime = warpEndNormalizedTime;
            WarpEndIsDataDerived = warpEndIsDataDerived;
        }
    }

    public enum TraversalWarpKnotRole
    {
        Entry = 0,
        LeftContact,
        RightContact,
        Transfer,
        Exit,
        Recovery,
    }

    public readonly struct TraversalWarpKnot
    {
        public readonly TraversalWarpKnotRole Role;
        public readonly float NormalizedTime;
        public readonly Vector3 PositionCorrection;
        public readonly float YawCorrection;

        public TraversalWarpKnot(
            TraversalWarpKnotRole role,
            float normalizedTime,
            Vector3 positionCorrection,
            float yawCorrection)
        {
            Role = role;
            NormalizedTime = normalizedTime;
            PositionCorrection = positionCorrection;
            YawCorrection = yawCorrection;
        }
    }

    public readonly struct TraversalHandContactMeasurement
    {
        public readonly bool IsValid;
        public readonly float NormalizedTime;
        public readonly Vector3 OriginalAnimatedHandWorld;
        public readonly Vector3 AnimatedHandWorld;
        public readonly Vector3 WorldTarget;
        public readonly Vector3 ErrorVector;
        public readonly float ErrorDistance;

        public TraversalHandContactMeasurement(
            float normalizedTime,
            Vector3 originalAnimatedHandWorld,
            Vector3 animatedHandWorld,
            Vector3 worldTarget)
        {
            IsValid = IsFinite(normalizedTime) && IsFinite(originalAnimatedHandWorld) &&
                      IsFinite(animatedHandWorld) && IsFinite(worldTarget);
            NormalizedTime = normalizedTime;
            OriginalAnimatedHandWorld = originalAnimatedHandWorld;
            AnimatedHandWorld = animatedHandWorld;
            WorldTarget = worldTarget;
            ErrorVector = worldTarget - animatedHandWorld;
            ErrorDistance = ErrorVector.magnitude;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public enum TraversalPrimaryHand
    {
        None = 0,
        Left,
        Right,
    }

    public readonly struct TraversalContactTargets
    {
        public readonly TraversalPrimaryHand PrimaryHand;
        public readonly bool HasLeftHand;
        public readonly bool HasRightHand;
        public readonly Vector3 LeftHandWorldTarget;
        public readonly Vector3 RightHandWorldTarget;
        public readonly Quaternion LeftHandWorldRotation;
        public readonly Quaternion RightHandWorldRotation;
        public readonly TraversalHandContactMeasurement LeftHandMeasurement;
        public readonly TraversalHandContactMeasurement RightHandMeasurement;
        public readonly bool HandIKConfigured;
        public readonly TraversalHandIKWindow LeftHandIKWindow;
        public readonly TraversalHandIKWindow RightHandIKWindow;
        public readonly float HandIKRotationWeight;

        public float LeftHandResidualError => LeftHandMeasurement.ErrorDistance;
        public float RightHandResidualError => RightHandMeasurement.ErrorDistance;

        public TraversalContactTargets(
            TraversalPrimaryHand primaryHand,
            bool hasLeftHand,
            bool hasRightHand,
            Vector3 leftHandWorldTarget,
            Vector3 rightHandWorldTarget,
            Quaternion leftHandWorldRotation,
            Quaternion rightHandWorldRotation,
            in TraversalHandContactMeasurement leftHandMeasurement,
            in TraversalHandContactMeasurement rightHandMeasurement,
            bool handIKConfigured,
            TraversalHandIKWindow leftHandIKWindow,
            TraversalHandIKWindow rightHandIKWindow,
            float handIKRotationWeight)
        {
            PrimaryHand = primaryHand;
            HasLeftHand = hasLeftHand;
            HasRightHand = hasRightHand;
            LeftHandWorldTarget = leftHandWorldTarget;
            RightHandWorldTarget = rightHandWorldTarget;
            LeftHandWorldRotation = leftHandWorldRotation;
            RightHandWorldRotation = rightHandWorldRotation;
            LeftHandMeasurement = leftHandMeasurement;
            RightHandMeasurement = rightHandMeasurement;
            HandIKConfigured = handIKConfigured;
            LeftHandIKWindow = leftHandIKWindow;
            RightHandIKWindow = rightHandIKWindow;
            HandIKRotationWeight = Mathf.Clamp01(handIKRotationWeight);
        }
    }

    /// <summary>
    /// Traversal V2 單次進場鎖定的最小 warp plan。只重映射既有 MotionBakeData 到一個固定世界目標；
    /// 不追蹤環境，也不提供通用 action／interaction warping framework。
    /// </summary>
    public readonly struct TraversalWarpPlan
    {
        private const float Epsilon = 0.0001f;

        private readonly MotionBakeData _bake;
        private readonly Vector3 _startPosition;
        private readonly Vector3 _startForward;
        private readonly Vector3 _horizontalCorrection;
        private readonly float _verticalCorrection;
        private readonly float _verticalScale;
        private readonly float _startYaw;
        private readonly float _yawCorrection;
        private readonly float _warpStartNormalizedTime;
        private readonly float _warpEndNormalizedTime;
        private readonly bool _isPiecewise;
        private readonly TraversalWarpKnot _entryKnot;
        private readonly TraversalWarpKnot _leftContactKnot;
        private readonly TraversalWarpKnot _rightContactKnot;
        private readonly TraversalWarpKnot _transferKnot;
        private readonly TraversalWarpKnot _exitKnot;
        private readonly TraversalWarpKnot _recoveryKnot;

        public bool IsValid { get; }
        public MotionBakeData Bake => _bake;
        public Vector3 StartPosition => _startPosition;
        public Vector3 StartForward => _startForward;
        public Vector3 BakedEndDisplacement { get; }
        public Vector3 TargetPosition { get; }
        public Vector3 TargetForward { get; }
        public float HorizontalScale { get; }
        public float VerticalScale => _verticalScale;
        public float YawCorrection => _yawCorrection;
        public float WarpStartNormalizedTime => _warpStartNormalizedTime;
        public float WarpEndNormalizedTime => _warpEndNormalizedTime;
        public bool IsPiecewise => _isPiecewise;
        public TraversalWarpKnot EntryKnot => _entryKnot;
        public TraversalWarpKnot LeftContactKnot => _leftContactKnot;
        public TraversalWarpKnot RightContactKnot => _rightContactKnot;
        public TraversalWarpKnot TransferKnot => _transferKnot;
        public TraversalWarpKnot ExitKnot => _exitKnot;
        public TraversalWarpKnot RecoveryKnot => _recoveryKnot;
        public TraversalContactTargets ContactTargets { get; }

        private TraversalWarpPlan(
            MotionBakeData bake,
            Vector3 startPosition,
            Vector3 startForward,
            Vector3 bakedEndDisplacement,
            Vector3 targetPosition,
            Vector3 targetForward,
            Vector3 horizontalCorrection,
            float verticalCorrection,
            float verticalScale,
            float startYaw,
            float yawCorrection,
            float horizontalScale,
            float warpStartNormalizedTime,
            float warpEndNormalizedTime)
        {
            _bake = bake;
            _startPosition = startPosition;
            _startForward = startForward;
            _horizontalCorrection = horizontalCorrection;
            _verticalCorrection = verticalCorrection;
            _verticalScale = verticalScale;
            _startYaw = startYaw;
            _yawCorrection = yawCorrection;
            _warpStartNormalizedTime = warpStartNormalizedTime;
            _warpEndNormalizedTime = warpEndNormalizedTime;
            _isPiecewise = false;
            _entryKnot = default;
            _leftContactKnot = default;
            _rightContactKnot = default;
            _transferKnot = default;
            _exitKnot = default;
            _recoveryKnot = default;
            BakedEndDisplacement = bakedEndDisplacement;
            TargetPosition = targetPosition;
            TargetForward = targetForward;
            HorizontalScale = horizontalScale;
            ContactTargets = default;
            IsValid = true;
        }

        private TraversalWarpPlan(
            MotionBakeData bake,
            Vector3 startPosition,
            Vector3 startForward,
            Vector3 targetPosition,
            Vector3 targetForward,
            TraversalWarpKnot entryKnot,
            TraversalWarpKnot leftContactKnot,
            TraversalWarpKnot rightContactKnot,
            TraversalWarpKnot transferKnot,
            TraversalWarpKnot exitKnot,
            TraversalWarpKnot recoveryKnot,
            TraversalContactTargets contactTargets)
        {
            _bake = bake;
            _startPosition = startPosition;
            _startForward = startForward;
            _horizontalCorrection = Vector3.zero;
            _verticalCorrection = 0f;
            _verticalScale = 1f;
            _startYaw = Mathf.Atan2(startForward.x, startForward.z) * Mathf.Rad2Deg;
            _yawCorrection = recoveryKnot.YawCorrection;
            _warpStartNormalizedTime = entryKnot.NormalizedTime;
            _warpEndNormalizedTime = exitKnot.NormalizedTime;
            _isPiecewise = true;
            _entryKnot = entryKnot;
            _leftContactKnot = leftContactKnot;
            _rightContactKnot = rightContactKnot;
            _transferKnot = transferKnot;
            _exitKnot = exitKnot;
            _recoveryKnot = recoveryKnot;
            BakedEndDisplacement = new Vector3(
                0f,
                bake.GetVerticalAt(bake.Duration),
                bake.GetHorizontalDisplacementAt(bake.Duration));
            TargetPosition = targetPosition;
            TargetForward = targetForward;
            HorizontalScale = 1f;
            ContactTargets = contactTargets;
            IsValid = true;
        }

        public static bool TryCreate(
            MotionBakeData bake,
            Vector3 startPosition,
            Vector3 startForward,
            Vector3 targetPosition,
            Vector3 targetForward,
            float warpStartNormalizedTime,
            float warpEndNormalizedTime,
            float maxHorizontalCorrection,
            float maxVerticalCorrection,
            out TraversalWarpPlan plan) =>
            TryCreate(
                bake, startPosition, startForward, targetPosition, targetForward,
                warpStartNormalizedTime, warpEndNormalizedTime,
                maxHorizontalCorrection, maxVerticalCorrection,
                out plan, out _, out _, out _);

        /// <summary>
        /// 🆕（docs/23 §R1）帶理由的 endpoint plan 建立。失敗時 <paramref name="rejection"/> 說明原因，
        /// 並回報實際需要的 correction 量，讓「超限」在畫面上可解釋而非靜默退化。
        /// </summary>
        public static bool TryCreate(
            MotionBakeData bake,
            Vector3 startPosition,
            Vector3 startForward,
            Vector3 targetPosition,
            Vector3 targetForward,
            float warpStartNormalizedTime,
            float warpEndNormalizedTime,
            float maxHorizontalCorrection,
            float maxVerticalCorrection,
            out TraversalWarpPlan plan,
            out TraversalWarpPlanRejection rejection,
            out float requiredHorizontalCorrection,
            out float requiredVerticalCorrection)
        {
            plan = default;
            rejection = TraversalWarpPlanRejection.None;
            requiredHorizontalCorrection = float.NaN;
            requiredVerticalCorrection = float.NaN;

            if (bake == null)
            {
                rejection = TraversalWarpPlanRejection.MissingBake;
                return false;
            }
            if (!IsFinite(bake.Duration) || bake.Duration <= Epsilon ||
                bake.SpeedCurve == null || bake.SpeedCurve.length == 0 ||
                bake.VerticalCurve == null || bake.VerticalCurve.length == 0 ||
                !HasFiniteKeys(bake.SpeedCurve) || !HasFiniteKeys(bake.VerticalCurve) ||
                (bake.RotationCurve != null && !HasFiniteKeys(bake.RotationCurve)))
            {
                rejection = TraversalWarpPlanRejection.InvalidBakeCurves;
                return false;
            }
            if (!IsFinite(startPosition) || !IsFinite(targetPosition) ||
                !IsFinite(startForward) || !IsFinite(targetForward))
            {
                rejection = TraversalWarpPlanRejection.InvalidInputPose;
                return false;
            }
            if (!IsFinite(warpStartNormalizedTime) || !IsFinite(warpEndNormalizedTime) ||
                !IsFinite(maxHorizontalCorrection) || !IsFinite(maxVerticalCorrection) ||
                maxHorizontalCorrection < 0f || maxVerticalCorrection < 0f)
            {
                rejection = TraversalWarpPlanRejection.InvalidWarpWindow;
                return false;
            }

            float warpStart = Mathf.Clamp01(warpStartNormalizedTime);
            float warpEnd = Mathf.Clamp01(warpEndNormalizedTime);
            if (warpEnd <= warpStart + Epsilon)
            {
                rejection = TraversalWarpPlanRejection.InvalidWarpWindow;
                return false;
            }

            startForward.y = 0f;
            if (startForward.sqrMagnitude <= Epsilon)
            {
                rejection = TraversalWarpPlanRejection.DegenerateStartForward;
                return false;
            }
            startForward.Normalize();

            Vector3 targetDelta = targetPosition - startPosition;
            Vector3 targetHorizontal = Vector3.ProjectOnPlane(targetDelta, Vector3.up);
            targetForward.y = 0f;
            if (targetForward.sqrMagnitude <= Epsilon)
                targetForward = targetHorizontal.sqrMagnitude > Epsilon ? targetHorizontal : startForward;
            targetForward.Normalize();

            float bakedHorizontal = bake.GetHorizontalDisplacementAt(bake.Duration);
            float bakedVertical = bake.GetVerticalAt(bake.Duration);
            float bakedYaw = bake.GetRotationAt(bake.Duration);
            if (!IsFinite(bakedHorizontal) || !IsFinite(bakedVertical) || !IsFinite(bakedYaw))
            {
                rejection = TraversalWarpPlanRejection.InvalidBakeCurves;
                return false;
            }
            if (Mathf.Abs(bakedHorizontal) <= Epsilon)
            {
                rejection = TraversalWarpPlanRejection.ZeroBakedHorizontal;
                return false;
            }

            Vector3 bakedHorizontalVector = startForward * bakedHorizontal;
            Vector3 horizontalCorrection = targetHorizontal - bakedHorizontalVector;
            float verticalCorrection = targetDelta.y - bakedVertical;
            requiredHorizontalCorrection = horizontalCorrection.magnitude;
            requiredVerticalCorrection = verticalCorrection;
            if (requiredHorizontalCorrection > maxHorizontalCorrection + Epsilon)
            {
                rejection = TraversalWarpPlanRejection.HorizontalCorrectionExceeded;
                return false;
            }
            if (Mathf.Abs(verticalCorrection) > maxVerticalCorrection + Epsilon)
            {
                rejection = TraversalWarpPlanRejection.VerticalCorrectionExceeded;
                return false;
            }

            Vector3 bakedEndForward = Quaternion.AngleAxis(bakedYaw, Vector3.up) * startForward;
            float yawCorrection = Vector3.SignedAngle(bakedEndForward, targetForward, Vector3.up);
            if (!IsFinite(yawCorrection))
            {
                rejection = TraversalWarpPlanRejection.NonFiniteResult;
                return false;
            }

            // 📌 保留為純資訊性指標（不再參與 evaluation，見 TryEvaluate 的模型註解）。
            float verticalScale = Mathf.Abs(bakedVertical) > Epsilon
                ? targetDelta.y / bakedVertical
                : 1f;
            float horizontalScale = targetHorizontal.magnitude / Mathf.Abs(bakedHorizontal);
            if (!IsFinite(verticalScale) || !IsFinite(horizontalScale))
            {
                rejection = TraversalWarpPlanRejection.NonFiniteResult;
                return false;
            }

            float startYaw = Mathf.Atan2(startForward.x, startForward.z) * Mathf.Rad2Deg;
            var bakedEnd = new Vector3(0f, bakedVertical, bakedHorizontal);
            plan = new TraversalWarpPlan(
                bake,
                startPosition,
                startForward,
                bakedEnd,
                targetPosition,
                targetForward,
                horizontalCorrection,
                verticalCorrection,
                verticalScale,
                startYaw,
                yawCorrection,
                horizontalScale,
                warpStart,
                warpEnd);
            return true;
        }

        public static bool TryCreatePiecewise(
            MotionBakeData bake,
            Vector3 startPosition,
            Vector3 startForward,
            Vector3 targetPosition,
            Vector3 targetForward,
            in TraversalWarpKnot entryKnot,
            in TraversalWarpKnot leftContactKnot,
            in TraversalWarpKnot rightContactKnot,
            in TraversalWarpKnot transferKnot,
            in TraversalWarpKnot exitKnot,
            in TraversalWarpKnot recoveryKnot,
            in TraversalContactTargets contactTargets,
            out TraversalWarpPlan plan) =>
            TryCreatePiecewise(
                bake, startPosition, startForward, targetPosition, targetForward,
                in entryKnot, in leftContactKnot, in rightContactKnot,
                in transferKnot, in exitKnot, in recoveryKnot, in contactTargets,
                out plan, out _);

        /// <summary>🆕（docs/23 §R1）帶理由的 piecewise plan 建立。</summary>
        public static bool TryCreatePiecewise(
            MotionBakeData bake,
            Vector3 startPosition,
            Vector3 startForward,
            Vector3 targetPosition,
            Vector3 targetForward,
            in TraversalWarpKnot entryKnot,
            in TraversalWarpKnot leftContactKnot,
            in TraversalWarpKnot rightContactKnot,
            in TraversalWarpKnot transferKnot,
            in TraversalWarpKnot exitKnot,
            in TraversalWarpKnot recoveryKnot,
            in TraversalContactTargets contactTargets,
            out TraversalWarpPlan plan,
            out TraversalWarpPlanRejection rejection)
        {
            plan = default;
            rejection = TraversalWarpPlanRejection.None;
            if (bake == null)
            {
                rejection = TraversalWarpPlanRejection.MissingBake;
                return false;
            }
            if (!IsFinite(bake.Duration) || bake.Duration <= Epsilon ||
                bake.SpeedCurve == null || bake.SpeedCurve.length == 0 ||
                bake.VerticalCurve == null || bake.VerticalCurve.length == 0 ||
                !HasFiniteKeys(bake.SpeedCurve) || !HasFiniteKeys(bake.VerticalCurve))
            {
                rejection = TraversalWarpPlanRejection.InvalidBakeCurves;
                return false;
            }
            if (!IsFinite(startPosition) || !IsFinite(startForward) ||
                !IsFinite(targetPosition) || !IsFinite(targetForward) ||
                startForward.sqrMagnitude <= Epsilon || targetForward.sqrMagnitude <= Epsilon)
            {
                rejection = TraversalWarpPlanRejection.InvalidInputPose;
                return false;
            }
            if (!IsValidKnot(in entryKnot) || !IsValidKnot(in leftContactKnot) ||
                !IsValidKnot(in rightContactKnot) || !IsValidKnot(in transferKnot) ||
                !IsValidKnot(in exitKnot) || !IsValidKnot(in recoveryKnot))
            {
                rejection = TraversalWarpPlanRejection.KnotRejected;
                return false;
            }
            if (entryKnot.NormalizedTime > leftContactKnot.NormalizedTime ||
                leftContactKnot.NormalizedTime > rightContactKnot.NormalizedTime ||
                rightContactKnot.NormalizedTime > transferKnot.NormalizedTime ||
                transferKnot.NormalizedTime > exitKnot.NormalizedTime ||
                exitKnot.NormalizedTime > recoveryKnot.NormalizedTime)
            {
                rejection = TraversalWarpPlanRejection.KnotOrderInvalid;
                return false;
            }

            startForward.y = 0f;
            targetForward.y = 0f;
            if (startForward.sqrMagnitude <= Epsilon || targetForward.sqrMagnitude <= Epsilon)
                return false;
            startForward.Normalize();
            targetForward.Normalize();
            plan = new TraversalWarpPlan(
                bake,
                startPosition,
                startForward,
                targetPosition,
                targetForward,
                entryKnot,
                leftContactKnot,
                rightContactKnot,
                transferKnot,
                exitKnot,
                recoveryKnot,
                contactTargets);
            return true;
        }

        public bool TryEvaluate(float normalizedTime, out Vector3 position, out float yaw)
        {
            position = _startPosition;
            yaw = _startYaw;
            if (!IsValid || !IsFinite(normalizedTime)) return false;

            if (_isPiecewise)
                return TryEvaluatePiecewise(normalizedTime, out position, out yaw);

            float normalized = Mathf.Clamp01(normalizedTime);
            float warpWeight = CorrectionWeightAt(normalized);

            // ⭐ docs/23 §A2／§E-2：**root 的時間基準必須等於動畫的時間基準。**
            //    舊版把 baked trajectory 線性壓進 warp window（trajectory = Lerp(start, 1, t)），
            //    但**沒有同步改動畫播放速度** ⇒ root 比 pose 快，實測垂直去同步達
            //    Climb1m +0.82m／Climb2m +0.53m／Vault1m −1.02m，**且在 correction=0 時依然發生**。
            //    這裡改為直接以動畫進度取樣 baked 曲線；warp window 只控制「correction 融入的快慢」，
            //    不再控制「baked shape 的播放速度」。要壓縮位移屬 playback speed 的職責（Horizon GDC 2017）。
            float clipTime = normalized * _bake.Duration;
            float horizontal = _bake.GetHorizontalDisplacementAt(clipTime);
            float bakedVertical = _bake.GetVerticalAt(clipTime);
            float bakedYaw = _bake.GetRotationAt(clipTime);
            if (!IsFinite(horizontal) || !IsFinite(bakedVertical) || !IsFinite(bakedYaw)) return false;

            // ⭐ docs/23 §E-2 缺陷②：垂直改為與水平同型的**加法** correction。
            //    舊版乘法（Lerp(V, V×scale, w)）讓絕對修正量正比於「動畫自己已經爬了多高」，
            //    結構性地把修正集中到尾段。兩軸現在共用同一個 warpWeight 與同一個時間基準。
            float vertical = bakedVertical + _verticalCorrection * warpWeight;
            position = _startPosition + _startForward * horizontal +
                       _horizontalCorrection * warpWeight + Vector3.up * vertical;
            yaw = _startYaw + bakedYaw + _yawCorrection * warpWeight;
            return IsFinite(position) && IsFinite(yaw);
        }

        /// <summary>
        /// Correction 的融入權重（0＝完全依 baked shape，1＝correction 完全套用）。
        /// piecewise plan 由 knot 自行內插，本函式只描述 endpoint plan 的窗。
        /// </summary>
        public float CorrectionWeightAt(float normalizedTime)
        {
            if (!IsFinite(normalizedTime)) return 0f;
            float normalized = Mathf.Clamp01(normalizedTime);
            if (normalized <= _warpStartNormalizedTime) return 0f;
            if (normalized >= _warpEndNormalizedTime) return 1f;
            float t = (normalized - _warpStartNormalizedTime) /
                      (_warpEndNormalizedTime - _warpStartNormalizedTime);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 🆕（docs/23 §R1）**Root／Pose 同步的回歸金絲雀。**
        /// 回傳「root 取樣 baked 曲線時使用的 normalized 時間」。修正後它必須恆等於傳入的動畫進度；
        /// 任何再次引入時間重映的改動都會讓 <c>TraversalBakedAssetTests</c> 立刻變紅。
        /// </summary>
        public float TrajectoryNormalizedAt(float normalizedTime)
        {
            if (!IsValid || !IsFinite(normalizedTime)) return 0f;
            return Mathf.Clamp01(normalizedTime);
        }

        public static bool TryEvaluateOriginal(
            MotionBakeData bake,
            Vector3 startPosition,
            Vector3 startForward,
            float normalizedTime,
            out Vector3 position,
            out float yaw)
        {
            position = startPosition;
            yaw = 0f;
            if (bake == null || !IsFinite(bake.Duration) || bake.Duration <= Epsilon ||
                !IsFinite(startPosition) || !IsFinite(startForward) ||
                !IsFinite(normalizedTime))
            {
                return false;
            }

            startForward.y = 0f;
            if (startForward.sqrMagnitude <= Epsilon) return false;
            startForward.Normalize();
            float time = Mathf.Clamp01(normalizedTime) * bake.Duration;
            float horizontal = bake.GetHorizontalDisplacementAt(time);
            float vertical = bake.GetVerticalAt(time);
            float bakedYaw = bake.GetRotationAt(time);
            if (!IsFinite(horizontal) || !IsFinite(vertical) || !IsFinite(bakedYaw)) return false;
            position = startPosition + startForward * horizontal + Vector3.up * vertical;
            yaw = Mathf.Atan2(startForward.x, startForward.z) * Mathf.Rad2Deg + bakedYaw;
            return IsFinite(position) && IsFinite(yaw);
        }

        private bool TryEvaluatePiecewise(float normalizedTime, out Vector3 position, out float yaw)
        {
            float normalized = Mathf.Clamp01(normalizedTime);
            // Exit is a positional contract, not merely another correction sample. Once Exit is
            // reached, cancel the animation tail root motion and hold the committed destination
            // until Recovery. This prevents the visible late Y drift/snap reported in Playtest.
            if (normalized >= _exitKnot.NormalizedTime)
            {
                position = TargetPosition;
                yaw = Mathf.Atan2(TargetForward.x, TargetForward.z) * Mathf.Rad2Deg;
                return IsFinite(position) && IsFinite(yaw);
            }
            if (!TryEvaluateOriginal(
                    _bake, _startPosition, _startForward, normalized,
                    out Vector3 originalPosition, out float originalYaw))
            {
                position = _startPosition;
                yaw = _startYaw;
                return false;
            }

            TraversalWarpKnot from;
            TraversalWarpKnot to;
            SelectSegment(normalized, out from, out to);
            float span = to.NormalizedTime - from.NormalizedTime;
            float t = span > Epsilon
                ? Mathf.Clamp01((normalized - from.NormalizedTime) / span)
                : 1f;
            float smooth = t * t * (3f - 2f * t);
            Vector3 correction = Vector3.Lerp(from.PositionCorrection, to.PositionCorrection, smooth);
            float yawCorrection = Mathf.LerpAngle(from.YawCorrection, to.YawCorrection, smooth);
            position = originalPosition + correction;
            yaw = originalYaw + yawCorrection;

            // Hand anchoring and base contact alignment are separate terms. The knot correction
            // remains frozen through Contact→Transfer; this bake-derived, root-local term is the
            // minimum body adjustment needed to keep the primary shoulder→wrist chain reachable.
            // It is already zero before Exit, so the committed destination contract is unchanged.
            TraversalRootReachConstraint reachConstraint = _bake.Traversal.RootReachConstraint;
            if (reachConstraint.IsValid)
            {
                Vector3 localReachCorrection = reachConstraint.Evaluate(normalized);
                position += Quaternion.AngleAxis(yaw, Vector3.up) * localReachCorrection;
            }
            return IsFinite(position) && IsFinite(yaw);
        }

        private void SelectSegment(
            float normalized,
            out TraversalWarpKnot from,
            out TraversalWarpKnot to)
        {
            if (normalized <= _leftContactKnot.NormalizedTime)
            {
                from = _entryKnot;
                to = _leftContactKnot;
            }
            else if (normalized <= _rightContactKnot.NormalizedTime)
            {
                from = _leftContactKnot;
                to = _rightContactKnot;
            }
            else if (normalized <= _transferKnot.NormalizedTime)
            {
                from = _rightContactKnot;
                to = _transferKnot;
            }
            else if (normalized <= _exitKnot.NormalizedTime)
            {
                from = _transferKnot;
                to = _exitKnot;
            }
            else
            {
                from = _exitKnot;
                to = _recoveryKnot;
            }
        }

        private static bool IsValidKnot(in TraversalWarpKnot knot) =>
            IsFinite(knot.NormalizedTime) && knot.NormalizedTime >= 0f && knot.NormalizedTime <= 1f &&
            IsFinite(knot.PositionCorrection) && IsFinite(knot.YawCorrection);

        private static bool HasFiniteKeys(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                Keyframe key = curve[i];
                if (!IsFinite(key.time) || !IsFinite(key.value) ||
                    !IsFinite(key.inTangent) || !IsFinite(key.outTangent))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
