using UnityEngine;

namespace Project.Core.Environment
{
    /// <summary>
    /// 將一次已完成的環境量測分類為 traversal candidate。純函式：不發 physics query、
    /// 不讀時間或黑板，previousKind 只用於高度與深度遲滯。
    /// </summary>
    public static class TraversalClassifier
    {
        private const float DirectionSqrEpsilon = 0.000001f;

        public static TraversalCandidate Classify(
            in TraversalProbeMeasurement measurement,
            in TraversalProbeSettings settings,
            TraversalKind previousKind)
        {
            if (!IsFinite(in measurement, in settings) ||
                measurement.Forward.sqrMagnitude <= DirectionSqrEpsilon)
            {
                return default;
            }

            if (!measurement.IsGrounded)
                return Reject(in measurement, TraversalRejectReason.NotGrounded);

            // Query 結果的拒絕順序是規格的一部分；不要把較晚的理由提前。
            if (!measurement.HasForwardHit)
                return Reject(in measurement, TraversalRejectReason.NoForwardHit);
            if (!measurement.HasTop)
                return Reject(in measurement, TraversalRejectReason.NoValidTop);
            if (Vector3.Angle(measurement.TopNormal, Vector3.up) > measurement.SlopeLimit)
                return Reject(in measurement, TraversalRejectReason.TopTooSteep);

            float maximumHeight = settings.Climb2mMaxHeight;
            if (previousKind == TraversalKind.Climb2m)
                maximumHeight += settings.HeightHysteresis;
            if (measurement.Height > maximumHeight)
                return Reject(in measurement, TraversalRejectReason.TooHigh);
            if (measurement.DestinationBlocked)
                return Reject(in measurement, TraversalRejectReason.DestinationBlocked);
            if (!measurement.Corridor.IsClear)
                return Reject(in measurement, TraversalRejectReason.CorridorBlocked);

            bool isOneMetreLayer = SelectOneMetreLayer(
                measurement.Height,
                settings.Climb1mMaxHeight,
                settings.HeightHysteresis,
                previousKind);
            if (!isOneMetreLayer)
                return Accept(in measurement, TraversalKind.Climb2m);

            TraversalKind oneMetreKind = SelectOneMetreKind(
                measurement.Depth,
                settings.Vault1mMaxDepth,
                settings.DepthHysteresis,
                previousKind);
            return Accept(in measurement, oneMetreKind);
        }

        private static bool SelectOneMetreLayer(
            float height,
            float boundary,
            float hysteresis,
            TraversalKind previousKind)
        {
            if (previousKind == TraversalKind.Climb2m)
                return height < boundary - hysteresis;
            if (previousKind == TraversalKind.Vault1m || previousKind == TraversalKind.Climb1m)
                return height <= boundary + hysteresis;
            return height <= boundary;
        }

        private static TraversalKind SelectOneMetreKind(
            float depth,
            float boundary,
            float hysteresis,
            TraversalKind previousKind)
        {
            if (previousKind == TraversalKind.Vault1m && depth <= boundary + hysteresis)
                return TraversalKind.Vault1m;
            if (previousKind == TraversalKind.Climb1m && depth >= boundary - hysteresis)
                return TraversalKind.Climb1m;
            return depth <= boundary ? TraversalKind.Vault1m : TraversalKind.Climb1m;
        }

        private static TraversalCandidate Accept(
            in TraversalProbeMeasurement measurement,
            TraversalKind kind)
        {
            return Build(in measurement, kind, TraversalRejectReason.None);
        }

        private static TraversalCandidate Reject(
            in TraversalProbeMeasurement measurement,
            TraversalRejectReason reason)
        {
            return Build(in measurement, TraversalKind.None, reason);
        }

        private static TraversalCandidate Build(
            in TraversalProbeMeasurement measurement,
            TraversalKind kind,
            TraversalRejectReason reason)
        {
            return new TraversalCandidate(
                kind,
                reason,
                measurement.ForwardHitPoint,
                measurement.ForwardHitNormal,
                measurement.TopPoint,
                measurement.TopNormal,
                measurement.Height,
                measurement.Depth,
                measurement.DestinationPoint,
                measurement.Forward,
                measurement.LedgeFrame,
                measurement.Corridor,
                measurement.RootPosition,
                measurement.FacingForward,
                measurement.DirectionSource,
                measurement.EntryCapsuleWallClearance,
                measurement.LandingSurfaceDepth);
        }

        private static bool IsFinite(
            in TraversalProbeMeasurement measurement,
            in TraversalProbeSettings settings)
        {
            return IsFinite(measurement.Forward) &&
                   IsFinite(measurement.RootPosition) &&
                   IsFinite(measurement.FacingForward) &&
                   IsFinite(measurement.EntryCapsuleWallClearance) &&
                   IsFinite(measurement.ForwardHitPoint) &&
                   IsFinite(measurement.ForwardHitNormal) &&
                   IsFinite(measurement.TopPoint) &&
                   IsFinite(measurement.TopNormal) &&
                   IsFinite(measurement.DestinationPoint) &&
                   IsFinite(measurement.FirstClearHeight) &&
                   IsFinite(measurement.Height) &&
                   IsFinite(measurement.Depth) &&
                   IsFinite(measurement.SlopeLimit) &&
                   IsFinite(measurement.HorizontalSpeed) &&
                   IsFinite(settings.ScanStep) &&
                   IsFinite(settings.ScanCeiling) &&
                   IsFinite(settings.ForwardScanDistance) &&
                   IsFinite(settings.Vault1mMaxDepth) &&
                   IsFinite(settings.Climb1mMaxHeight) &&
                   IsFinite(settings.Climb2mMaxHeight) &&
                   IsFinite(settings.HeightHysteresis) &&
                   IsFinite(settings.DepthHysteresis) &&
                   IsFinite(settings.ProbeMinSpeed) &&
                   IsFinite(settings.DestinationClearanceMargin);
        }

        private static bool IsFinite(Vector3 value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
