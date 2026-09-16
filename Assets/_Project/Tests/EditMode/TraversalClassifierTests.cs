using NUnit.Framework;
using Project.Core.Environment;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public class TraversalClassifierTests
    {
        private static readonly TraversalProbeSettings Settings = new TraversalProbeSettings(
            obstacleMask: ~0,
            scanStep: 0.1f,
            scanCeiling: 2.2f,
            forwardScanDistance: 1.25f,
            vault1mMaxDepth: 0.6f,
            climb1mMaxHeight: 1.25f,
            climb2mMaxHeight: 2.1f,
            heightHysteresis: 0.1f,
            depthHysteresis: 0.08f,
            probeMinSpeed: 0.1f,
            destinationClearanceMargin: 0.05f);

        [TestCase(1.0f, 0.3f, TraversalKind.Vault1m)]
        [TestCase(1.0f, 0.9f, TraversalKind.Climb1m)]
        [TestCase(1.8f, 0.9f, TraversalKind.Climb2m)]
        public void ValidMeasurements_ClassifyEachTraversalKind(
            float height,
            float depth,
            TraversalKind expected)
        {
            TraversalProbeMeasurement measurement = Valid(height: height, depth: depth);

            TraversalCandidate result = TraversalClassifier.Classify(
                in measurement, in Settings, TraversalKind.None);

            Assert.AreEqual(expected, result.Kind);
            Assert.AreEqual(TraversalRejectReason.None, result.RejectReason);
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void NotGrounded_ReturnsDedicatedRejectReason()
            => AssertRejected(Valid(isGrounded: false), TraversalRejectReason.NotGrounded);

        [Test]
        public void StationaryGrounded_RemainsClassifiable()
        {
            TraversalProbeMeasurement measurement = Valid(horizontalSpeed: 0f);
            TraversalCandidate result = TraversalClassifier.Classify(
                in measurement, in Settings, TraversalKind.None);
            Assert.AreEqual(TraversalKind.Vault1m, result.Kind);
        }

        [Test]
        public void NoForwardHit_ReturnsDedicatedRejectReason()
            => AssertRejected(Valid(hasForwardHit: false), TraversalRejectReason.NoForwardHit);

        [Test]
        public void NoValidTop_ReturnsDedicatedRejectReason()
            => AssertRejected(Valid(hasTop: false), TraversalRejectReason.NoValidTop);

        [Test]
        public void TopTooSteep_ReturnsDedicatedRejectReason()
            => AssertRejected(
                Valid(topNormal: Quaternion.AngleAxis(50f, Vector3.right) * Vector3.up),
                TraversalRejectReason.TopTooSteep);

        [Test]
        public void TooHigh_ReturnsDedicatedRejectReason()
            => AssertRejected(
                Valid(height: Settings.Climb2mMaxHeight + 0.01f),
                TraversalRejectReason.TooHigh);

        [Test]
        public void DestinationBlocked_ReturnsDedicatedRejectReason()
            => AssertRejected(
                Valid(destinationBlocked: true),
                TraversalRejectReason.DestinationBlocked);

        [Test]
        public void CorridorBlocked_ReturnsDedicatedRejectReason()
        {
            TraversalProbeMeasurement baseline = Valid();
            var blocked = new TraversalCorridorEvidence(
                false,
                TraversalCorridorRejectReason.ClearanceToExitBlocked,
                baseline.Corridor.EntryRoot,
                baseline.Corridor.ClearanceRoot,
                baseline.Corridor.TransferRoot,
                baseline.Corridor.ExitRoot);
            var measurement = new TraversalProbeMeasurement(
                baseline.IsGrounded, baseline.HorizontalSpeed, baseline.HasForwardHit,
                baseline.ForwardHitPoint, baseline.ForwardHitNormal, baseline.FirstClearHeight,
                baseline.HasTop, baseline.TopPoint, baseline.TopNormal, baseline.Height,
                baseline.Depth, baseline.DestinationPoint, baseline.DestinationBlocked,
                baseline.SlopeLimit, baseline.Forward, baseline.LedgeFrame, blocked,
                baseline.RootPosition, baseline.FacingForward, baseline.DirectionSource);
            AssertRejected(measurement, TraversalRejectReason.CorridorBlocked);
        }

        [Test]
        public void RejectOrder_NoTopWinsBeforeSteepHighAndBlocked()
        {
            TraversalProbeMeasurement measurement = Valid(
                hasTop: false,
                topNormal: Vector3.right,
                height: 99f,
                destinationBlocked: true);

            AssertRejected(measurement, TraversalRejectReason.NoValidTop);
        }

        [Test]
        public void HeightBoundary_OneMetreHistoryAbsorbsSmallUpwardJitter()
        {
            TraversalProbeMeasurement first = Valid(height: 1.24f, depth: 0.3f);
            TraversalCandidate current = TraversalClassifier.Classify(
                in first, in Settings, TraversalKind.None);
            Assert.AreEqual(TraversalKind.Vault1m, current.Kind);

            float[] jitter = { 1.27f, 1.23f, 1.29f, 1.24f };
            for (int i = 0; i < jitter.Length; i++)
            {
                TraversalProbeMeasurement measurement = Valid(height: jitter[i], depth: 0.3f);
                current = TraversalClassifier.Classify(in measurement, in Settings, current.Kind);
                Assert.AreEqual(TraversalKind.Vault1m, current.Kind,
                    "1m history 在 +heightHysteresis 內不得逐幀翻到 Climb2m");
            }
        }

        [Test]
        public void HeightBoundary_TwoMetreHistoryAbsorbsSmallDownwardJitter()
        {
            TraversalProbeMeasurement first = Valid(height: 1.26f, depth: 0.3f);
            TraversalCandidate current = TraversalClassifier.Classify(
                in first, in Settings, TraversalKind.None);
            Assert.AreEqual(TraversalKind.Climb2m, current.Kind);

            float[] jitter = { 1.22f, 1.18f, 1.24f, 1.16f };
            for (int i = 0; i < jitter.Length; i++)
            {
                TraversalProbeMeasurement measurement = Valid(height: jitter[i], depth: 0.3f);
                current = TraversalClassifier.Classify(in measurement, in Settings, current.Kind);
                Assert.AreEqual(TraversalKind.Climb2m, current.Kind,
                    "2m history 在 -heightHysteresis 內不得逐幀翻回 1m 類別");
            }
        }

        [Test]
        public void DepthBoundary_VaultHistoryAbsorbsSmallThickSideJitter()
        {
            TraversalProbeMeasurement first = Valid(depth: 0.59f);
            TraversalCandidate current = TraversalClassifier.Classify(
                in first, in Settings, TraversalKind.None);
            Assert.AreEqual(TraversalKind.Vault1m, current.Kind);

            float[] jitter = { 0.62f, 0.58f, 0.66f, 0.61f };
            for (int i = 0; i < jitter.Length; i++)
            {
                TraversalProbeMeasurement measurement = Valid(depth: jitter[i]);
                current = TraversalClassifier.Classify(in measurement, in Settings, current.Kind);
                Assert.AreEqual(TraversalKind.Vault1m, current.Kind,
                    "Vault history 在 +depthHysteresis 內不得逐幀翻到 Climb1m");
            }
        }

        [Test]
        public void DepthBoundary_ClimbHistoryAbsorbsSmallThinSideJitter()
        {
            TraversalProbeMeasurement first = Valid(depth: 0.61f);
            TraversalCandidate current = TraversalClassifier.Classify(
                in first, in Settings, TraversalKind.None);
            Assert.AreEqual(TraversalKind.Climb1m, current.Kind);

            float[] jitter = { 0.58f, 0.63f, 0.54f, 0.59f };
            for (int i = 0; i < jitter.Length; i++)
            {
                TraversalProbeMeasurement measurement = Valid(depth: jitter[i]);
                current = TraversalClassifier.Classify(in measurement, in Settings, current.Kind);
                Assert.AreEqual(TraversalKind.Climb1m, current.Kind,
                    "Climb history 在 -depthHysteresis 內不得逐幀翻到 Vault1m");
            }
        }

        [Test]
        public void ZeroLengthForward_DegradesToNoneEvenWithPreviousKind()
            => AssertDegenerate(Valid(forward: Vector3.zero));

        [Test]
        public void NaNHeight_DegradesToNoneEvenWithPreviousKind()
            => AssertDegenerate(Valid(height: float.NaN));

        [Test]
        public void NaNForward_DegradesToNoneEvenWithPreviousKind()
            => AssertDegenerate(Valid(forward: new Vector3(float.NaN, 0f, 1f)));

        [Test]
        public void NoHit_NeverFallsBackToPreviousKind()
        {
            TraversalProbeMeasurement measurement = Valid(hasForwardHit: false);
            TraversalCandidate result = TraversalClassifier.Classify(
                in measurement, in Settings, TraversalKind.Climb2m);

            Assert.AreEqual(TraversalKind.None, result.Kind);
            Assert.AreEqual(TraversalRejectReason.NoForwardHit, result.RejectReason);
        }

        private static void AssertRejected(
            TraversalProbeMeasurement measurement,
            TraversalRejectReason expected)
        {
            TraversalCandidate result = TraversalClassifier.Classify(
                in measurement, in Settings, TraversalKind.None);
            Assert.AreEqual(TraversalKind.None, result.Kind);
            Assert.AreEqual(expected, result.RejectReason);
            Assert.IsFalse(result.IsValid);
        }

        private static void AssertDegenerate(TraversalProbeMeasurement measurement)
        {
            TraversalCandidate result = TraversalClassifier.Classify(
                in measurement, in Settings, TraversalKind.Vault1m);
            Assert.AreEqual(TraversalKind.None, result.Kind);
            Assert.AreEqual(TraversalRejectReason.None, result.RejectReason,
                "退化值不應假裝成某一個有效 query reject branch");
        }

        private static TraversalProbeMeasurement Valid(
            bool isGrounded = true,
            float horizontalSpeed = 1f,
            bool hasForwardHit = true,
            bool hasTop = true,
            float height = 1f,
            float depth = 0.3f,
            bool destinationBlocked = false,
            Vector3? topNormal = null,
            Vector3? forward = null)
        {
            return new TraversalProbeMeasurement(
                isGrounded,
                horizontalSpeed,
                hasForwardHit,
                new Vector3(0f, 0.5f, 0.5f),
                Vector3.back,
                height + 0.1f,
                hasTop,
                new Vector3(0f, height, 0.52f),
                topNormal ?? Vector3.up,
                height,
                depth,
                new Vector3(0f, height, 1.1f),
                destinationBlocked,
                45f,
                forward ?? Vector3.forward);
        }
    }
}
