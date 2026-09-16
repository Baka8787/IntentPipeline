using UnityEngine;

namespace Project.Core.Environment
{
    public enum TraversalDetectionDirectionSource
    {
        Facing = 0,
        Move = 1,
    }

    public enum TraversalCorridorRejectReason
    {
        None = 0,
        EntryToClearanceBlocked,
        ClearanceToExitBlocked,
        ExitDescentBlocked,
    }

    /// <summary>Probe-owned ledge geometry. TangentMin/Max are signed metres from EdgeOrigin.</summary>
    public readonly struct TraversalLedgeFrame
    {
        private const float Epsilon = 0.000001f;

        public readonly Vector3 EdgeOrigin;
        public readonly Vector3 Tangent;
        public readonly Vector3 WallNormal;
        public readonly Vector3 TopNormal;
        public readonly float TangentMin;
        public readonly float TangentMax;

        public bool IsValid =>
            IsFinite(EdgeOrigin) && IsFinite(Tangent) && IsFinite(WallNormal) && IsFinite(TopNormal) &&
            Tangent.sqrMagnitude > Epsilon && WallNormal.sqrMagnitude > Epsilon &&
            TopNormal.sqrMagnitude > Epsilon && IsFinite(TangentMin) && IsFinite(TangentMax) &&
            TangentMax >= TangentMin;

        public TraversalLedgeFrame(
            Vector3 edgeOrigin,
            Vector3 tangent,
            Vector3 wallNormal,
            Vector3 topNormal,
            float tangentMin,
            float tangentMax)
        {
            EdgeOrigin = edgeOrigin;
            Tangent = tangent.sqrMagnitude > Epsilon ? tangent.normalized : Vector3.zero;
            WallNormal = wallNormal.sqrMagnitude > Epsilon ? wallNormal.normalized : Vector3.zero;
            TopNormal = topNormal.sqrMagnitude > Epsilon ? topNormal.normalized : Vector3.zero;
            TangentMin = tangentMin;
            TangentMax = tangentMax;
        }

        public Vector3 PointAt(float tangentDistance) =>
            EdgeOrigin + Tangent * Mathf.Clamp(tangentDistance, TangentMin, TangentMax);

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Fixed standing-capsule evidence sampled by TraversalProbe; consumers never re-query.</summary>
    public readonly struct TraversalCorridorEvidence
    {
        public readonly bool IsClear;
        public readonly TraversalCorridorRejectReason RejectReason;
        public readonly Vector3 EntryRoot;
        public readonly Vector3 ClearanceRoot;
        public readonly Vector3 TransferRoot;
        public readonly Vector3 ExitRoot;

        public TraversalCorridorEvidence(
            bool isClear,
            TraversalCorridorRejectReason rejectReason,
            Vector3 entryRoot,
            Vector3 clearanceRoot,
            Vector3 transferRoot,
            Vector3 exitRoot)
        {
            IsClear = isClear;
            RejectReason = rejectReason;
            EntryRoot = entryRoot;
            ClearanceRoot = clearanceRoot;
            TransferRoot = transferRoot;
            ExitRoot = exitRoot;
        }

        public static TraversalCorridorEvidence Clear(
            Vector3 entryRoot,
            Vector3 clearanceRoot,
            Vector3 exitRoot) =>
            new TraversalCorridorEvidence(true, TraversalCorridorRejectReason.None,
                entryRoot, clearanceRoot, clearanceRoot, exitRoot);

        public static TraversalCorridorEvidence Clear(
            Vector3 entryRoot,
            Vector3 clearanceRoot,
            Vector3 transferRoot,
            Vector3 exitRoot) =>
            new TraversalCorridorEvidence(true, TraversalCorridorRejectReason.None,
                entryRoot, clearanceRoot, transferRoot, exitRoot);
    }

    public readonly struct TraversalCandidate
    {
        public readonly TraversalKind Kind;
        public readonly TraversalRejectReason RejectReason;
        public readonly Vector3 ObstacleHitPoint;
        public readonly Vector3 ObstacleNormal;
        public readonly Vector3 TopPoint;
        public readonly Vector3 TopNormal;
        public readonly float Height;
        public readonly float Depth;
        public readonly Vector3 DestinationPoint;
        public readonly Vector3 Forward;
        public readonly TraversalLedgeFrame LedgeFrame;
        public readonly TraversalCorridorEvidence Corridor;
        public readonly Vector3 SensedRootPosition;
        public readonly Vector3 SensedFacingForward;
        /// <summary>
        /// Probe measured signed clearance from the current standing capsule front to the
        /// committed wall plane. Negative means penetration. EntryPolicy only consumes this
        /// snapshot; it never performs its own query.
        /// </summary>
        public readonly float EntryCapsuleWallClearance;

        /// <summary>
        /// 🆕（docs/24 §10）Probe 實際驗證過、**落腳面從邊緣往內連續延伸的最遠深度**（m）。
        ///
        /// ⚠️ 與 <see cref="Depth"/> 是兩件事：<c>Depth</c> 是**分類證據**
        /// （超過 <c>Vault1mMaxDepth</c> ⇒ 視為實心平台 ⇒ Climb），是個二元旗標；
        /// 本欄位是**落地預算**——動畫自己的自然落點可能遠在 1.8 m 外，
        /// 用分類常數 0.6 m 當終點會逼 warp 事後把角色往回拉（Playtest 的「抓牢後還在位移」）。
        ///
        /// 掃描上限由各支動畫推導的自然落點決定（<c>TraversalStateParamsSO.GetRequiredLandingDepth()</c>），
        /// 不是另一個 magic number。薄牆（surface 不連續）時為 <c>NaN</c>，呼叫端退回既有行為。
        /// </summary>
        public readonly float LandingSurfaceDepth;

        public readonly TraversalDetectionDirectionSource DirectionSource;

        public bool IsValid => Kind != TraversalKind.None;

        public TraversalCandidate(
            TraversalKind kind,
            TraversalRejectReason rejectReason,
            Vector3 obstacleHitPoint,
            Vector3 obstacleNormal,
            Vector3 topPoint,
            Vector3 topNormal,
            float height,
            float depth,
            Vector3 destinationPoint,
            Vector3 forward)
            : this(
                kind,
                rejectReason,
                obstacleHitPoint,
                obstacleNormal,
                topPoint,
                topNormal,
                height,
                depth,
                destinationPoint,
                forward,
                CreateCompatibilityLedgeFrame(topPoint, topNormal, obstacleNormal, forward),
                TraversalCorridorEvidence.Clear(
                    topPoint - NormalizedOrForward(forward) * 0.45f,
                    topPoint + Vector3.up * 0.5f,
                    destinationPoint),
                new Vector3(topPoint.x, 0f, topPoint.z) - NormalizedOrForward(forward) * 0.45f,
                NormalizedOrForward(forward),
                TraversalDetectionDirectionSource.Move,
                1f)
        {
        }

        public TraversalCandidate(
            TraversalKind kind,
            TraversalRejectReason rejectReason,
            Vector3 obstacleHitPoint,
            Vector3 obstacleNormal,
            Vector3 topPoint,
            Vector3 topNormal,
            float height,
            float depth,
            Vector3 destinationPoint,
            Vector3 forward,
            TraversalLedgeFrame ledgeFrame,
            TraversalCorridorEvidence corridor,
            Vector3 sensedRootPosition,
            Vector3 sensedFacingForward,
            TraversalDetectionDirectionSource directionSource,
            float entryCapsuleWallClearance = 1f,
            float landingSurfaceDepth = float.NaN)
        {
            Kind = kind;
            RejectReason = rejectReason;
            ObstacleHitPoint = obstacleHitPoint;
            ObstacleNormal = obstacleNormal;
            TopPoint = topPoint;
            TopNormal = topNormal;
            Height = height;
            Depth = depth;
            DestinationPoint = destinationPoint;
            Forward = forward;
            LedgeFrame = ledgeFrame;
            Corridor = corridor;
            SensedRootPosition = sensedRootPosition;
            SensedFacingForward = sensedFacingForward;
            EntryCapsuleWallClearance = entryCapsuleWallClearance;
            LandingSurfaceDepth = landingSurfaceDepth;
            DirectionSource = directionSource;
        }

        private static TraversalLedgeFrame CreateCompatibilityLedgeFrame(
            Vector3 topPoint,
            Vector3 topNormal,
            Vector3 obstacleNormal,
            Vector3 forward)
        {
            Vector3 wallNormal = obstacleNormal.sqrMagnitude > 0.000001f
                ? obstacleNormal.normalized
                : -NormalizedOrForward(forward);
            Vector3 tangent = Vector3.Cross(topNormal, wallNormal);
            if (tangent.sqrMagnitude <= 0.000001f) tangent = Vector3.right;
            return new TraversalLedgeFrame(topPoint, tangent, wallNormal, topNormal, -1f, 1f);
        }

        private static Vector3 NormalizedOrForward(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.000001f ? value.normalized : Vector3.forward;
        }
    }
}
