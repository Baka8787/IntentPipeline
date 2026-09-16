using Project.Core.Environment;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// Traversal V2 的 gameplay selection policy。Probe／Classifier 只提供幾何事實；
    /// 本純函式才決定 Climb 是否應讓普通 Jump 優先。
    ///
    /// 🆕（docs/23 §A10／R1）**否決必須留下理由。** 舊版只回 <c>bool</c>，Climb 被讓給普通 Jump 時
    /// 不寫 reject reason、不進 <see cref="TraversalEntryEvaluation"/>、不上畫面 —— 畫面顯示
    /// `Climb1m` ＋ `Entry: Accept` 卻什麼都沒發生，是本系統最誤導的狀態。
    /// <see cref="Evaluate(in TraversalCandidate, JumpStateParams, float)"/> 是新的主要入口；
    /// 既有的兩個 <c>PreferTraversal</c> 多載保留為薄包裝（既有測試與呼叫端不變）。
    /// </summary>
    public static class TraversalSelectionPolicy
    {
        /// <summary>
        /// 純函式：同時回傳「選了誰」與「為什麼」。不查 Physics／Time，不寫黑板。
        /// </summary>
        public static TraversalSelectionEvaluation Evaluate(
            in TraversalCandidate candidate,
            JumpStateParams jumpParams,
            float jumpReachSafetyMargin) =>
            Evaluate(in candidate, jumpParams, jumpReachSafetyMargin, true);

        /// <param name="kindEnabled">
        /// 該 kind 是否被 authored data 啟用。<c>false</c> 時直接讓給普通 Jump，
        /// 並以 <see cref="TraversalSelectionReason.KindDisabled"/> 說明——
        /// **停用是內容決策，但不得因此變成看不見的否決。**
        /// </param>
        public static TraversalSelectionEvaluation Evaluate(
            in TraversalCandidate candidate,
            JumpStateParams jumpParams,
            float jumpReachSafetyMargin,
            bool kindEnabled)
        {
            if (!candidate.IsValid)
            {
                return TraversalSelectionEvaluation.Rejected(
                    candidate.Kind, TraversalSelectionReason.InvalidCandidate, 0f);
            }

            if (!kindEnabled)
            {
                return TraversalSelectionEvaluation.Rejected(
                    candidate.Kind, TraversalSelectionReason.KindDisabled, candidate.Height);
            }

            if (!TryGetGroundJumpLaunch(jumpParams, out float initialVelocity, out float gravity))
            {
                // Jump 能力無法推導時，保守保留已分類的 traversal（含 Vault）。
                return Evaluate(
                    candidate.Kind, candidate.Height, float.NaN, float.NaN, jumpReachSafetyMargin);
            }

            return Evaluate(
                candidate.Kind, candidate.Height, initialVelocity, gravity, jumpReachSafetyMargin);
        }

        /// <summary>
        /// 純數值多載：供測試與不持有 <see cref="JumpStateParams"/> 的呼叫端使用。
        /// </summary>
        public static TraversalSelectionEvaluation Evaluate(
            TraversalKind kind,
            float candidateHeight,
            float initialVerticalVelocity,
            float gravity,
            float jumpReachSafetyMargin)
        {
            if (kind == TraversalKind.None)
            {
                return TraversalSelectionEvaluation.Rejected(
                    kind, TraversalSelectionReason.InvalidCandidate, candidateHeight);
            }

            if (kind == TraversalKind.Vault1m)
            {
                // Vault 永遠保留 contextual traversal：普通 Jump 無法表達「越過」。
                return TraversalSelectionEvaluation.Accepted(
                    kind, TraversalSelectionReason.ContextualVault, candidateHeight);
            }

            if (kind != TraversalKind.Climb1m && kind != TraversalKind.Climb2m)
            {
                return TraversalSelectionEvaluation.Rejected(
                    kind, TraversalSelectionReason.UnsupportedKind, candidateHeight);
            }

            if (!IsFinite(candidateHeight) || candidateHeight < 0f ||
                !IsFinite(initialVerticalVelocity) || initialVerticalVelocity <= 0f ||
                !IsFinite(gravity) || gravity <= 0f ||
                !IsFinite(jumpReachSafetyMargin))
            {
                // 無法證明普通 Jump 可安全到達時，保留已分類的 Climb。
                return TraversalSelectionEvaluation.Accepted(
                    kind, TraversalSelectionReason.JumpReachUnknown, candidateHeight);
            }

            float theoreticalApex = initialVerticalVelocity * initialVerticalVelocity / (2f * gravity);
            if (!IsFinite(theoreticalApex))
            {
                return TraversalSelectionEvaluation.Accepted(
                    kind, TraversalSelectionReason.JumpReachUnknown, candidateHeight);
            }

            float safeReach = Mathf.Max(0f, theoreticalApex - Mathf.Max(0f, jumpReachSafetyMargin));
            return candidateHeight > safeReach
                ? TraversalSelectionEvaluation.AcceptedWithReach(
                    kind, TraversalSelectionReason.ClimbAboveJumpReach,
                    candidateHeight, theoreticalApex, safeReach)
                // ⚠️ docs/23 §A10：這條分支就是「畫面全綠卻什麼都沒發生」的來源。
                : TraversalSelectionEvaluation.RejectedWithReach(
                    kind, TraversalSelectionReason.ClimbWithinJumpReach,
                    candidateHeight, theoreticalApex, safeReach);
        }

        public static bool PreferTraversal(
            in TraversalCandidate candidate,
            JumpStateParams jumpParams,
            float jumpReachSafetyMargin) =>
            Evaluate(in candidate, jumpParams, jumpReachSafetyMargin).PreferTraversal;

        public static bool PreferTraversal(
            TraversalKind kind,
            float candidateHeight,
            float initialVerticalVelocity,
            float gravity,
            float jumpReachSafetyMargin) =>
            Evaluate(kind, candidateHeight, initialVerticalVelocity, gravity, jumpReachSafetyMargin)
                .PreferTraversal;

        private static bool TryGetGroundJumpLaunch(
            JumpStateParams jumpParams,
            out float initialVelocity,
            out float gravity)
        {
            initialVelocity = 0f;
            gravity = 0f;

            if (jumpParams == null || jumpParams.Stages == null || jumpParams.Stages.Count == 0)
                return false;

            MotionBakeData bake = jumpParams.Stages[0].Bake;
            if (bake == null) return false;

            float heightMultiplier = jumpParams.HeightMultiplier;
            float gravityMultiplier = jumpParams.GravityMultiplier;
            float velocityMultiplier = jumpParams.LaunchVelocityMultiplier;
            float height = bake.AutoApexHeight * heightMultiplier;
            gravity = Mathf.Abs(bake.AutoCalculatedGravity) * gravityMultiplier;

            if (!IsFinite(height) || height <= 0f ||
                !IsFinite(gravity) || gravity <= 0f ||
                !IsFinite(velocityMultiplier) || velocityMultiplier <= 0f)
            {
                initialVelocity = 0f;
                gravity = 0f;
                return false;
            }

            initialVelocity = Mathf.Sqrt(2f * gravity * height) * velocityMultiplier;
            return IsFinite(initialVelocity) && initialVelocity > 0f;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

namespace Project.Core.Environment
{
    /// <summary>
    /// 🆕（docs/23 §R1）Selection 的理由碼。`ClimbWithinJumpReach` 是 review 指認的靜默否決。
    /// </summary>
    public enum TraversalSelectionReason
    {
        None = 0,
        /// <summary>Candidate 本身不成立（Kind=None）。</summary>
        InvalidCandidate,
        /// <summary>已分類但本 policy 不處理的 kind。</summary>
        UnsupportedKind,
        /// <summary>Vault 永遠保留 contextual traversal。</summary>
        ContextualVault,
        /// <summary>無法由 JumpStateParams 推導跳躍能力 ⇒ 保守保留 Climb。</summary>
        JumpReachUnknown,
        /// <summary>Ledge 高於普通 Jump 的安全可及高度 ⇒ 走 Traversal。</summary>
        ClimbAboveJumpReach,
        /// <summary>⚠️ Ledge 在普通 Jump 可及範圍內 ⇒ 靜默讓給 Jump。這是 P3 的頭號嫌疑。</summary>
        ClimbWithinJumpReach,
        /// <summary>
        /// 該 traversal kind 已由 authored data 停用（`TraversalStateParamsSO` 的 per-kind 開關）。
        /// 這是**刻意的內容決策**，不是失敗——但仍然要說出口，否則又變成另一個靜默否決。
        /// </summary>
        KindDisabled,
    }

    /// <summary>
    /// Selection 的完整結果快照：**選了誰 ＋ 為什麼 ＋ 用什麼數字判的**。
    /// 由 <c>TraversalSelectionPolicy</c> 產生、<c>TraversalState</c> 推進 <c>TraversalProbe</c>，
    /// 純 debug／觀測用途，不進 <c>PlayerRuntimeData</c>、不影響執行。
    /// </summary>
    public readonly struct TraversalSelectionEvaluation
    {
        public readonly bool HasResult;
        public readonly bool PreferTraversal;
        public readonly TraversalSelectionReason Reason;
        public readonly TraversalKind Kind;
        public readonly float CandidateHeight;
        /// <summary>普通 Jump 的理論最高點（m）。<c>NaN</c> ＝ 未參與判斷。</summary>
        public readonly float TheoreticalApex;
        /// <summary>扣除安全餘量後的可及高度（m）。<c>NaN</c> ＝ 未參與判斷。</summary>
        public readonly float SafeReach;

        public bool HasReachEvidence => !float.IsNaN(SafeReach);

        private TraversalSelectionEvaluation(
            bool preferTraversal,
            TraversalSelectionReason reason,
            TraversalKind kind,
            float candidateHeight,
            float theoreticalApex,
            float safeReach)
        {
            HasResult = true;
            PreferTraversal = preferTraversal;
            Reason = reason;
            Kind = kind;
            CandidateHeight = candidateHeight;
            TheoreticalApex = theoreticalApex;
            SafeReach = safeReach;
        }

        public static TraversalSelectionEvaluation Accepted(
            TraversalKind kind, TraversalSelectionReason reason, float candidateHeight) =>
            new TraversalSelectionEvaluation(
                true, reason, kind, candidateHeight, float.NaN, float.NaN);

        public static TraversalSelectionEvaluation Rejected(
            TraversalKind kind, TraversalSelectionReason reason, float candidateHeight) =>
            new TraversalSelectionEvaluation(
                false, reason, kind, candidateHeight, float.NaN, float.NaN);

        public static TraversalSelectionEvaluation AcceptedWithReach(
            TraversalKind kind,
            TraversalSelectionReason reason,
            float candidateHeight,
            float theoreticalApex,
            float safeReach) =>
            new TraversalSelectionEvaluation(
                true, reason, kind, candidateHeight, theoreticalApex, safeReach);

        public static TraversalSelectionEvaluation RejectedWithReach(
            TraversalKind kind,
            TraversalSelectionReason reason,
            float candidateHeight,
            float theoreticalApex,
            float safeReach) =>
            new TraversalSelectionEvaluation(
                false, reason, kind, candidateHeight, theoreticalApex, safeReach);
    }

    public enum TraversalEntryRejectReason
    {
        None = 0,
        InvalidCandidate,
        InvalidLedgeFrame,
        CorridorBlocked,
        TooFar,
        TooCloseUnsafe,
        LateralOutOfRange,
        FacingOutOfRange,
    }

    public enum TraversalEntryDistanceBand
    {
        Invalid = 0,
        TooFar,
        ValidApproach,
        AlreadyClose,
        Unsafe,
    }

    [System.Serializable]
    public struct TraversalEntryPolicySettings
    {
        [SerializeField, Min(0f)] private float desiredWallDistance;
        [SerializeField, Min(0f)] private float maximumFarError;
        [SerializeField, Min(0f)] private float maximumCloseError;
        [SerializeField, Min(0f)] private float unsafeCapsulePenetrationTolerance;
        [SerializeField, Min(0f)] private float maximumLateralError;
        [SerializeField, Range(0f, 180f)] private float maximumFacingAngle;
        [SerializeField, Min(0f)] private float ledgeEndMargin;

        public float DesiredWallDistance => FinitePositive(desiredWallDistance, 0.45f);
        public float MaximumFarError => FinitePositive(maximumFarError, 0.35f);
        public float MaximumCloseError => FinitePositive(maximumCloseError, 0.4f);
        public float UnsafeCapsulePenetrationTolerance =>
            FiniteNonNegative(unsafeCapsulePenetrationTolerance, 0.01f);
        public float MinimumWallDistance => Mathf.Max(0f, DesiredWallDistance - MaximumCloseError);
        public float MaximumWallDistance => DesiredWallDistance + MaximumFarError;
        public float MaximumLateralError => FinitePositive(maximumLateralError, 0.2f);
        public float MaximumFacingAngle => Mathf.Clamp(
            FinitePositive(maximumFacingAngle, 35f), 0f, 180f);
        public float LedgeEndMargin => FiniteNonNegative(ledgeEndMargin, 0.08f);

        public static TraversalEntryPolicySettings Default =>
            new TraversalEntryPolicySettings(0.45f, 0.35f, 0.4f, 0.01f, 0.2f, 35f, 0.08f);

        public TraversalEntryPolicySettings(
            float desiredWallDistance,
            float maximumFarError,
            float maximumCloseError,
            float unsafeCapsulePenetrationTolerance,
            float maximumLateralError,
            float maximumFacingAngle,
            float ledgeEndMargin)
        {
            this.desiredWallDistance = desiredWallDistance;
            this.maximumFarError = maximumFarError;
            this.maximumCloseError = maximumCloseError;
            this.unsafeCapsulePenetrationTolerance = unsafeCapsulePenetrationTolerance;
            this.maximumLateralError = maximumLateralError;
            this.maximumFacingAngle = maximumFacingAngle;
            this.ledgeEndMargin = ledgeEndMargin;
        }

        private static float FiniteNonNegative(float value, float fallback) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f ? value : fallback;

        private static float FinitePositive(float value, float fallback) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f ? value : fallback;
    }

    public readonly struct TraversalEntryEvaluation
    {
        public readonly Vector3 CurrentRootPosition;
        public readonly Vector3 CurrentFacing;
        public readonly Vector3 DesiredRootPosition;
        public readonly Quaternion DesiredRootRotation;
        public readonly float LongitudinalDistance;
        public readonly float LongitudinalError;
        public readonly float LateralError;
        public readonly float FacingAngleError;
        public readonly float IdealLongitudinalDistance;
        public readonly float MaximumFarError;
        public readonly float MaximumCloseError;
        public readonly float MinimumLongitudinalDistance;
        public readonly float MaximumLongitudinalDistance;
        public readonly float CapsuleWallClearance;
        public readonly TraversalEntryDistanceBand DistanceBand;
        public readonly bool Executable;
        public readonly TraversalEntryRejectReason RejectReason;

        public TraversalEntryEvaluation(
            Vector3 currentRootPosition,
            Vector3 currentFacing,
            Vector3 desiredRootPosition,
            Quaternion desiredRootRotation,
            float longitudinalDistance,
            float longitudinalError,
            float lateralError,
            float facingAngleError,
            float idealLongitudinalDistance,
            float maximumFarError,
            float maximumCloseError,
            float minimumLongitudinalDistance,
            float maximumLongitudinalDistance,
            float capsuleWallClearance,
            TraversalEntryDistanceBand distanceBand,
            bool executable,
            TraversalEntryRejectReason rejectReason)
        {
            CurrentRootPosition = currentRootPosition;
            CurrentFacing = currentFacing;
            DesiredRootPosition = desiredRootPosition;
            DesiredRootRotation = desiredRootRotation;
            LongitudinalDistance = longitudinalDistance;
            LongitudinalError = longitudinalError;
            LateralError = lateralError;
            FacingAngleError = facingAngleError;
            IdealLongitudinalDistance = idealLongitudinalDistance;
            MaximumFarError = maximumFarError;
            MaximumCloseError = maximumCloseError;
            MinimumLongitudinalDistance = minimumLongitudinalDistance;
            MaximumLongitudinalDistance = maximumLongitudinalDistance;
            CapsuleWallClearance = capsuleWallClearance;
            DistanceBand = distanceBand;
            Executable = executable;
            RejectReason = rejectReason;
        }

        /// <summary>Compatibility constructor for existing deterministic fixtures.</summary>
        public TraversalEntryEvaluation(
            Vector3 currentRootPosition,
            Vector3 currentFacing,
            Vector3 desiredRootPosition,
            Quaternion desiredRootRotation,
            float longitudinalDistance,
            float longitudinalError,
            float lateralError,
            float facingAngleError,
            float minimumLongitudinalDistance,
            float maximumLongitudinalDistance,
            bool executable,
            TraversalEntryRejectReason rejectReason)
            : this(
                currentRootPosition,
                currentFacing,
                desiredRootPosition,
                desiredRootRotation,
                longitudinalDistance,
                longitudinalError,
                lateralError,
                facingAngleError,
                longitudinalDistance - longitudinalError,
                Mathf.Max(0f, maximumLongitudinalDistance -
                              (longitudinalDistance - longitudinalError)),
                Mathf.Max(0f, (longitudinalDistance - longitudinalError) -
                              minimumLongitudinalDistance),
                minimumLongitudinalDistance,
                maximumLongitudinalDistance,
                1f,
                executable
                    ? (longitudinalError < 0f
                        ? TraversalEntryDistanceBand.AlreadyClose
                        : TraversalEntryDistanceBand.ValidApproach)
                    : TraversalEntryDistanceBand.Invalid,
                executable,
                rejectReason)
        {
        }
    }

    /// <summary>
    /// Pure V3 entry legality. It consumes the Probe snapshot and authored tolerances only:
    /// no Physics, Time, Transform reads, or blackboard writes.
    /// </summary>
    public static class TraversalEntryPolicy
    {
        private const float Epsilon = 0.000001f;

        public static TraversalEntryEvaluation Evaluate(
            in TraversalCandidate candidate,
            in TraversalEntryPolicySettings settings) =>
            Evaluate(in candidate, in settings, float.NaN);

        /// <param name="desiredWallDistanceOverride">
        /// 🆕（docs/22 §14.5）由該 traversal 的 <c>MotionBakeData</c> 推導出的進場距離。
        /// <c>NaN</c> 時退回 <see cref="TraversalEntryPolicySettings.DesiredWallDistance"/>。
        ///
        /// 為什麼需要 per-clip：`Climb2m` 想要 ≈0.36 m、`Vault1m` 想要 ≈1.29 m（跑動翻越自帶助跑）。
        /// 用單一全域常數 0.45 m 服務兩者，等於強迫 warp 去抵銷 ~1.6 m 的位移差，那就是腳步滑動的來源。
        /// </param>
        public static TraversalEntryEvaluation Evaluate(
            in TraversalCandidate candidate,
            in TraversalEntryPolicySettings settings,
            float desiredWallDistanceOverride)
        {
            if (!candidate.IsValid)
                return Reject(in candidate, in settings, TraversalEntryRejectReason.InvalidCandidate);
            if (!candidate.LedgeFrame.IsValid)
                return Reject(in candidate, in settings, TraversalEntryRejectReason.InvalidLedgeFrame);
            if (!candidate.Corridor.IsClear)
                return Reject(in candidate, in settings, TraversalEntryRejectReason.CorridorBlocked);

            TraversalLedgeFrame ledge = candidate.LedgeFrame;
            Vector3 up = ledge.TopNormal;
            Vector3 wallNormal = Vector3.ProjectOnPlane(ledge.WallNormal, up);
            Vector3 tangent = Vector3.ProjectOnPlane(ledge.Tangent, up);
            Vector3 facing = Vector3.ProjectOnPlane(candidate.SensedFacingForward, up);
            if (wallNormal.sqrMagnitude <= Epsilon || tangent.sqrMagnitude <= Epsilon ||
                facing.sqrMagnitude <= Epsilon || !IsFinite(candidate.SensedRootPosition))
            {
                return Reject(in candidate, in settings, TraversalEntryRejectReason.InvalidLedgeFrame);
            }

            wallNormal.Normalize();
            tangent.Normalize();
            facing.Normalize();
            Vector3 traversalForward = -wallNormal;

            // per-clip 進場距離優先；沒有推導值時退回全域設定。距離帶隨之整體平移。
            float desiredWallDistance =
                !float.IsNaN(desiredWallDistanceOverride) &&
                !float.IsInfinity(desiredWallDistanceOverride) &&
                desiredWallDistanceOverride > 0f
                    ? desiredWallDistanceOverride
                    : settings.DesiredWallDistance;
            float minimumWallDistance =
                Mathf.Max(0f, desiredWallDistance - settings.MaximumCloseError);
            float maximumWallDistance = desiredWallDistance + settings.MaximumFarError;

            Vector3 rootFromEdge = candidate.SensedRootPosition - ledge.EdgeOrigin;
            float longitudinalDistance = Vector3.Dot(rootFromEdge, wallNormal);
            float tangentCoordinate = Vector3.Dot(rootFromEdge, tangent);
            float minTangent = ledge.TangentMin + settings.LedgeEndMargin;
            float maxTangent = ledge.TangentMax - settings.LedgeEndMargin;
            if (minTangent > maxTangent)
            {
                float midpoint = (ledge.TangentMin + ledge.TangentMax) * 0.5f;
                minTangent = midpoint;
                maxTangent = midpoint;
            }

            float desiredTangent = Mathf.Clamp(tangentCoordinate, minTangent, maxTangent);
            float lateralError = Mathf.Abs(tangentCoordinate - desiredTangent);
            float facingError = Vector3.Angle(facing, traversalForward);
            float longitudinalError = longitudinalDistance - desiredWallDistance;
            Vector3 desired = ledge.EdgeOrigin + tangent * desiredTangent +
                              wallNormal * desiredWallDistance;
            desired += up * Vector3.Dot(candidate.SensedRootPosition - desired, up);
            Quaternion desiredRotation = Quaternion.LookRotation(traversalForward, up);

            TraversalEntryRejectReason reason = TraversalEntryRejectReason.None;
            TraversalEntryDistanceBand distanceBand = longitudinalError < 0f
                ? TraversalEntryDistanceBand.AlreadyClose
                : TraversalEntryDistanceBand.ValidApproach;
            if (longitudinalError > settings.MaximumFarError)
            {
                reason = TraversalEntryRejectReason.TooFar;
                distanceBand = TraversalEntryDistanceBand.TooFar;
            }
            else if (-longitudinalError > settings.MaximumCloseError ||
                     candidate.EntryCapsuleWallClearance <
                     -settings.UnsafeCapsulePenetrationTolerance)
            {
                reason = TraversalEntryRejectReason.TooCloseUnsafe;
                distanceBand = TraversalEntryDistanceBand.Unsafe;
            }
            else if (lateralError > settings.MaximumLateralError)
                reason = TraversalEntryRejectReason.LateralOutOfRange;
            else if (facingError > settings.MaximumFacingAngle)
                reason = TraversalEntryRejectReason.FacingOutOfRange;

            return new TraversalEntryEvaluation(
                candidate.SensedRootPosition,
                facing,
                desired,
                desiredRotation,
                longitudinalDistance,
                longitudinalError,
                lateralError,
                facingError,
                desiredWallDistance,
                settings.MaximumFarError,
                settings.MaximumCloseError,
                minimumWallDistance,
                maximumWallDistance,
                candidate.EntryCapsuleWallClearance,
                distanceBand,
                reason == TraversalEntryRejectReason.None,
                reason);
        }

        private static TraversalEntryEvaluation Reject(
            in TraversalCandidate candidate,
            in TraversalEntryPolicySettings settings,
            TraversalEntryRejectReason reason) =>
            new TraversalEntryEvaluation(
                candidate.SensedRootPosition,
                candidate.SensedFacingForward,
                candidate.SensedRootPosition,
                Quaternion.identity,
                0f,
                0f,
                0f,
                0f,
                settings.DesiredWallDistance,
                settings.MaximumFarError,
                settings.MaximumCloseError,
                settings.MinimumWallDistance,
                settings.MaximumWallDistance,
                candidate.EntryCapsuleWallClearance,
                TraversalEntryDistanceBand.Invalid,
                false,
                reason);

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
