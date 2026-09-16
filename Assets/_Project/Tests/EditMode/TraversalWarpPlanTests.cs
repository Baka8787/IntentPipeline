using NUnit.Framework;
using Project.Presentation.Motion;
using Project.Core.Environment;
using Project.Core.StateMachine;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public class TraversalWarpPlanTests
    {
        private MotionBakeData _bake;

        [SetUp]
        public void SetUp()
        {
            _bake = ScriptableObject.CreateInstance<MotionBakeData>();
            _bake.BakedDuration = 1f;
            _bake.SpeedCurve = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 1f));
            _bake.VerticalCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 2f), new Keyframe(1f, 1f));
            _bake.RotationCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(1f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_bake);
        }

        [Test]
        public void WARP1_ExactFit_PreservesBakedTrajectory()
        {
            TraversalWarpPlan plan = CreatePlan(new Vector3(0f, 1f, 1f), Vector3.forward, 0f, 1f);

            Assert.IsTrue(plan.TryEvaluate(0.5f, out Vector3 warped, out _));
            Assert.AreEqual(0f, warped.x, 0.0001f);
            Assert.AreEqual(_bake.GetVerticalAt(0.5f), warped.y, 0.0001f);
            Assert.AreEqual(_bake.GetHorizontalDisplacementAt(0.5f), warped.z, 0.0001f);
        }

        [Test]
        public void WARP2_LowerTarget_PreservesArcAndEndsAtTarget()
        {
            Vector3 target = new Vector3(0f, 0.5f, 1f);
            TraversalWarpPlan plan = CreatePlan(target, Vector3.forward, 0f, 1f);

            Assert.IsTrue(plan.TryEvaluate(0.5f, out Vector3 midpoint, out _));
            Assert.Greater(midpoint.y, target.y, "降低終點後仍須保留原本的抬升弧形");
            Assert.IsTrue(plan.TryEvaluate(1f, out Vector3 end, out _));
            Assert.Less(Vector3.Distance(end, target), 0.0001f);
        }

        [Test]
        public void WARP3_HigherTargetWithinTolerance_EndsAtTarget()
        {
            Vector3 target = new Vector3(0f, 1.4f, 1f);
            TraversalWarpPlan plan = CreatePlan(target, Vector3.forward, 0f, 1f);

            Assert.IsTrue(plan.TryEvaluate(1f, out Vector3 end, out _));
            Assert.Less(Vector3.Distance(end, target), 0.0001f);
        }

        [Test]
        public void WARP4_DifferentHorizontalDistance_EndsAtDestination()
        {
            // 🔄（docs/23 §R1／Phase 1）語義更新：warp window 只控制 **correction 融入的快慢**，
            //    不再控制 baked shape 的播放速度。舊版在 warpEnd 就把整條 trajectory 播完
            //    （trajectory = Lerp(start, 1, t)），那正是 root 比 pose 快 25% 的來源。
            //    現在：warpEnd 時 correction 已 100% 融入；destination 在動畫結束時抵達。
            Vector3 target = new Vector3(0.35f, 1f, 1.4f);
            TraversalWarpPlan plan = CreatePlan(target, Vector3.forward, 0.1f, 0.8f);

            Assert.AreEqual(1f, plan.CorrectionWeightAt(0.8f), 0.0001f,
                "correction 必須在 warpEnd 完全融入");
            Assert.IsTrue(plan.TryEvaluate(1f, out Vector3 end, out _));
            Assert.Less(Vector3.Distance(end, target), 0.0001f,
                "動畫結束時必須落在 committed destination");
        }

        [Test]
        public void WARP4B_RootSamplesBakedCurveAtAnimationTime()
        {
            // ⭐ docs/23 §A2 的回歸金絲雀：root 取樣 baked 曲線的時間必須恆等於動畫進度。
            TraversalWarpPlan plan = CreatePlan(new Vector3(0.35f, 1f, 1.4f), Vector3.forward, 0.1f, 0.8f);

            for (int i = 0; i <= 50; i++)
            {
                float n = i / 50f;
                Assert.AreEqual(n, plan.TrajectoryNormalizedAt(n), 0.0001f,
                    $"n={n:F2} 的 root 時間被重新映射 —— root/pose 去同步已回歸");
            }
        }

        [Test]
        public void WARP5_EntryFacingOffset_EndsAtCommittedForward()
        {
            Vector3 startForward = Quaternion.Euler(0f, 20f, 0f) * Vector3.forward;
            Assert.IsTrue(TraversalWarpPlan.TryCreate(
                _bake, Vector3.zero, startForward, new Vector3(0f, 1f, 1f), Vector3.forward,
                0f, 1f, 2f, 1f, out TraversalWarpPlan plan));

            Assert.IsTrue(plan.TryEvaluate(1f, out _, out float yaw));
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, yaw), 0.001f);
        }

        [Test]
        public void WARP7_DegenerateOrInvalidData_FailsWithoutNonFinitePose()
        {
            _bake.SpeedCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(1f, 0f));
            Assert.IsFalse(TraversalWarpPlan.TryCreate(
                _bake, Vector3.zero, Vector3.forward, Vector3.forward, Vector3.forward,
                0f, 1f, 2f, 1f, out TraversalWarpPlan zeroPlan));
            Assert.IsFalse(zeroPlan.TryEvaluate(1f, out Vector3 position, out float yaw));
            Assert.IsFalse(float.IsNaN(position.x));
            Assert.IsFalse(float.IsInfinity(yaw));

            _bake.SpeedCurve = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 1f));
            _bake.VerticalCurve = null;
            Assert.IsFalse(TraversalWarpPlan.TryCreate(
                _bake, Vector3.zero, Vector3.forward, Vector3.forward, Vector3.forward,
                0f, 1f, 2f, 1f, out _));
            Assert.IsFalse(TraversalWarpPlan.TryCreate(
                _bake, Vector3.zero, Vector3.forward,
                new Vector3(float.NaN, 0f, 1f), Vector3.forward,
                0f, 1f, 2f, 1f, out _));
        }

        [Test]
        public void M1_ExactFitContact_HasNearZeroRootCorrection()
        {
            ConfigureTraversalBlock();
            float exactContactHeight = _bake.GetVerticalAt(0.2f) + 1f;
            TraversalCandidate candidate = V3Candidate(
                ledgeHeight: exactContactHeight, halfWidth: 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);

            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));
            Assert.IsTrue(plan.IsPiecewise);
            Assert.Less(plan.LeftContactKnot.PositionCorrection.magnitude, 0.0001f,
                $"left correction={plan.LeftContactKnot.PositionCorrection}");
            Assert.Less(plan.RightContactKnot.PositionCorrection.magnitude, 0.0001f,
                $"right correction={plan.RightContactKnot.PositionCorrection}");
        }

        [Test]
        public void M2_M4_DifferentLedgeHeightAndYaw_ContactsAndExitMatch()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(ledgeHeight: 2.1f, halfWidth: 0.8f, yaw: 20f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);

            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));
            Assert.IsTrue(plan.TryEvaluate(
                plan.RightContactKnot.NormalizedTime, out Vector3 root, out float yaw));
            Vector3 rightHand = root + Quaternion.Euler(0f, yaw, 0f) *
                                _bake.Traversal.RightContactAnchor.RightHandInRoot;
            Assert.Less(Vector3.Distance(rightHand, plan.ContactTargets.RightHandWorldTarget), 0.001f);
            Assert.IsTrue(plan.TryEvaluate(plan.ExitKnot.NormalizedTime, out Vector3 exit, out float exitYaw));
            Assert.Less(Vector3.Distance(exit, candidate.DestinationPoint), 0.001f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(
                Mathf.Atan2(candidate.Forward.x, candidate.Forward.z) * Mathf.Rad2Deg,
                exitYaw)), 0.01f);
        }

        [Test]
        public void M3_PiecewiseCorrection_IsContinuousAcrossContactAndExit()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(ledgeHeight: 2.1f, halfWidth: 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            float knot = plan.TransferKnot.NormalizedTime;
            Assert.IsTrue(plan.TryEvaluate(knot - 0.0001f, out Vector3 before, out _));
            Assert.IsTrue(plan.TryEvaluate(knot + 0.0001f, out Vector3 after, out _));
            Assert.Less(Vector3.Distance(before, after), 0.01f);
            Assert.AreNotEqual(plan.LeftContactKnot.PositionCorrection, plan.ExitKnot.PositionCorrection);
        }

        [Test]
        public void M5_BothHandTargetsRemainOrderedAndResidualIsBounded()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(ledgeHeight: 2f, halfWidth: 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            Vector3 separation = plan.ContactTargets.RightHandWorldTarget -
                                 plan.ContactTargets.LeftHandWorldTarget;
            Assert.GreaterOrEqual(Vector3.Dot(separation, candidate.LedgeFrame.Tangent), 0f);
            Assert.Less(plan.ContactTargets.LeftHandResidualError, 0.001f);
            Assert.Less(plan.ContactTargets.RightHandResidualError, 0.001f);
        }

        [Test]
        public void M6_NarrowLedge_KeepsPrimaryAndRejectsUnsupportedSecondaryContact()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(ledgeHeight: 2f, halfWidth: 0.12f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            Assert.AreEqual(TraversalPrimaryHand.Left, plan.ContactTargets.PrimaryHand);
            Assert.IsTrue(plan.ContactTargets.HasLeftHand);
            Assert.IsFalse(plan.ContactTargets.HasRightHand,
                "窄 ledge 沒有 secondary surface 時，不得壓縮 authored 雙手姿勢來偽造接觸");
            float left = Vector3.Dot(
                plan.ContactTargets.LeftHandWorldTarget - candidate.LedgeFrame.EdgeOrigin,
                candidate.LedgeFrame.Tangent);
            Assert.GreaterOrEqual(left, candidate.LedgeFrame.TangentMin + 0.05f - 0.0001f);
            Assert.LessOrEqual(left, candidate.LedgeFrame.TangentMax - 0.05f + 0.0001f);
        }

        [Test]
        public void M8_InvalidContactMarkers_FailSafely()
        {
            _bake.Traversal = new TraversalMotionBakeBlock(
                true, TraversalHandContactMode.BothHands,
                HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                0.5f, 0.4f, 0.3f, 0.8f, 0.9f, 0.2f);
            TraversalCandidate candidate = V3Candidate(2f, 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsFalse(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));
            Assert.IsFalse(plan.IsValid);
            Assert.IsFalse(plan.TryEvaluate(0.5f, out Vector3 position, out float yaw));
            Assert.IsFalse(float.IsNaN(position.x));
            Assert.IsFalse(float.IsInfinity(yaw));
        }

        [Test]
        public void HC2_ContactConstraint_ReducesAnimatedHandError()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(2.1f, 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            TraversalHandContactMeasurement left = plan.ContactTargets.LeftHandMeasurement;
            float unwarpedError = Vector3.Distance(
                left.OriginalAnimatedHandWorld, left.WorldTarget);
            Assert.IsTrue(left.IsValid);
            Assert.Less(left.ErrorDistance, unwarpedError);
            Assert.AreEqual(left.WorldTarget - left.AnimatedHandWorld, left.ErrorVector);
        }

        [Test]
        public void HC3_ContactCorrection_IsAlreadyActiveBeforeContact()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(2.1f, 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            float beforeContact = plan.LeftContactKnot.NormalizedTime * 0.5f;
            Assert.IsTrue(TraversalWarpPlan.TryEvaluateOriginal(
                _bake, plan.StartPosition, plan.StartForward, beforeContact,
                out Vector3 original, out _));
            Assert.IsTrue(plan.TryEvaluate(beforeContact, out Vector3 warped, out _));
            Assert.Greater(Vector3.Distance(original, warped), 0.001f);
        }

        [Test]
        public void HC4_PairedContacts_UsePrimaryCorrectionWithoutShortWindowOscillation()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(2.1f, 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            Assert.Less(Vector3.Distance(
                plan.LeftContactKnot.PositionCorrection,
                plan.RightContactKnot.PositionCorrection), 0.0001f);
            Assert.AreEqual(TraversalPrimaryHand.Left, plan.ContactTargets.PrimaryHand);
            Assert.IsTrue(plan.ContactTargets.HasRightHand,
                "寬 ledge 必須為 secondary hand 找到 committed surface contact");
            Assert.IsTrue(TraversalWarpPlan.TryEvaluateOriginal(
                _bake, plan.StartPosition, plan.StartForward,
                plan.LeftContactKnot.NormalizedTime, out Vector3 originalPrimaryRoot, out _));
            Vector3 primarySurface = candidate.LedgeFrame.PointAt(-0.2f);
            TraversalRootConstraintResult primarySolve = TraversalRootConstraintSolver.SolveSingle(
                _bake.Traversal.LeftContactAnchor.LeftHandInRoot,
                primarySurface,
                0f);
            Assert.IsTrue(primarySolve.IsValid);
            Assert.Less(Vector3.Distance(
                    plan.LeftContactKnot.PositionCorrection,
                    primarySolve.RootPosition - originalPrimaryRoot),
                0.0001f,
                "root correction 必須完全由 primary contact 決定，不能再混入 secondary 平均");
            Assert.Less(plan.ContactTargets.LeftHandResidualError, 0.2f);
            Assert.Less(plan.ContactTargets.RightHandResidualError, 0.2f);
        }

        [Test]
        public void HC6_SecondaryHand_QueriesCommittedLedgeSurfaceAfterPrimaryRootSolve()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(2.1f, 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            Assert.AreEqual(TraversalPrimaryHand.Left, plan.ContactTargets.PrimaryHand);
            Assert.IsTrue(plan.ContactTargets.HasRightHand);
            Vector3 secondaryTargetFromEdge =
                plan.ContactTargets.RightHandWorldTarget - candidate.LedgeFrame.EdgeOrigin;
            Assert.AreEqual(0f, Vector3.Dot(secondaryTargetFromEdge, candidate.LedgeFrame.WallNormal),
                0.0001f, "secondary target 必須落在 Probe commit 的 wall/edge surface 上");
            Assert.AreEqual(0f, Vector3.Dot(secondaryTargetFromEdge, candidate.LedgeFrame.TopNormal),
                0.0001f, "secondary target 不得沿 animation 高度漂離 committed edge surface");
        }

        [Test]
        public void HC5_ContactSoftConstraint_DoesNotPullCapsuleSupportThroughWall()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(
                2.1f, 0.8f, capsuleWallClearance: 0.05f);
            TraversalEntryPolicySettings settings = TraversalEntryPolicySettings.Default;
            TraversalEntryEvaluation entry = TraversalEntryPolicy.Evaluate(in candidate, in settings);
            Assert.IsTrue(entry.Executable);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            Assert.IsTrue(plan.TryEvaluate(
                plan.LeftContactKnot.NormalizedTime, out Vector3 contactRoot, out _));
            float rootWallDistance = Vector3.Dot(
                contactRoot - candidate.LedgeFrame.EdgeOrigin,
                candidate.LedgeFrame.WallNormal);
            float capsuleSupportDistance = entry.LongitudinalDistance - entry.CapsuleWallClearance;
            Assert.GreaterOrEqual(rootWallDistance, capsuleSupportDistance - 0.0001f);
        }

        [TestCase(2.2f)]
        [TestCase(0.7f)]
        public void V1_V2_VerticalCorrection_StartsBeforeContactForHigherOrLowerLedge(
            float ledgeHeight)
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(ledgeHeight, 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            float beforeContact = plan.LeftContactKnot.NormalizedTime * 0.75f;
            Assert.IsTrue(TraversalWarpPlan.TryEvaluateOriginal(
                _bake, plan.StartPosition, plan.StartForward, beforeContact,
                out Vector3 original, out _));
            Assert.IsTrue(plan.TryEvaluate(beforeContact, out Vector3 warped, out _));
            Assert.Greater(Mathf.Abs(warped.y - original.y), 0.001f);
        }

        [Test]
        public void V3_V4_ExitAndRecoveryHoldDestinationHeight()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(2.1f, 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            Assert.IsTrue(plan.TryEvaluate(plan.ExitKnot.NormalizedTime, out Vector3 exit, out _));
            Assert.IsTrue(plan.TryEvaluate(plan.RecoveryKnot.NormalizedTime, out Vector3 recovery, out _));
            Assert.AreEqual(candidate.DestinationPoint.y, exit.y, 0.0001f);
            Assert.AreEqual(candidate.DestinationPoint.y, recovery.y, 0.0001f);
            Assert.Less(Vector3.Distance(exit, recovery), 0.0001f);
        }

        [Test]
        public void V5_WarpedVertical_IsFiniteAndContinuous()
        {
            ConfigureTraversalBlock();
            TraversalCandidate candidate = V3Candidate(2.1f, 0.8f);
            TraversalEntryEvaluation entry = ExecutableEntry(candidate);
            Assert.IsTrue(TraversalPlanBuilder.TryBuild(
                _bake, in candidate, in entry, 0.05f, 5f, 5f, out TraversalWarpPlan plan));

            Assert.IsTrue(plan.TryEvaluate(0f, out Vector3 previous, out _));
            for (int i = 1; i <= 100; i++)
            {
                Assert.IsTrue(plan.TryEvaluate(i / 100f, out Vector3 current, out _));
                Assert.IsFalse(float.IsNaN(current.y));
                Assert.IsFalse(float.IsInfinity(current.y));
                Assert.Less(Mathf.Abs(current.y - previous.y), 0.25f);
                previous = current;
            }
        }

        [Test]
        public void IK_Window_FadesInHoldsAndReleasesBeforeExit()
        {
            var window = new TraversalHandIKWindow(true, 0.1f, 0.2f, 0.5f);
            Assert.AreEqual(0f, window.Evaluate(0.1f, 0.8f), 0.0001f);
            Assert.Greater(window.Evaluate(0.15f, 0.8f), 0f);
            Assert.AreEqual(1f, window.Evaluate(0.3f, 0.8f), 0.0001f);
            Assert.Less(window.Evaluate(0.7f, 0.8f), 1f);
            Assert.AreEqual(0f, window.Evaluate(0.8f, 0.8f), 0.0001f);
        }
        [Test]
        public void IK_InvalidWindow_DisablesSafely()
        {
            var invalid = new TraversalHandIKWindow(true, 0.4f, 0.2f, 0.5f);
            Assert.IsFalse(invalid.IsValid);
            Assert.AreEqual(0f, invalid.Evaluate(0.3f, 0.8f));
        }

        private void ConfigureTraversalBlock(bool withHandIK = false)
        {
            TraversalMotionBakeBlock authored = withHandIK
                ? new TraversalMotionBakeBlock(
                    true,
                    TraversalHandContactMode.BothHands,
                    HumanBodyBones.LeftHand,
                    HumanBodyBones.RightHand,
                    0.2f,
                    0.3f,
                    0.6f,
                    0.8f,
                    0.9f,
                    0.2f,
                    true,
                    new TraversalHandIKWindow(true, 0.12f, 0.2f, 0.55f),
                    new TraversalHandIKWindow(true, 0.2f, 0.3f, 0.55f),
                    0f)
                : new TraversalMotionBakeBlock(
                    true,
                    TraversalHandContactMode.BothHands,
                    HumanBodyBones.LeftHand,
                    HumanBodyBones.RightHand,
                    0.2f,
                    0.3f,
                    0.6f,
                    0.8f,
                    0.9f,
                    0.2f);
            var entry = Anchor(0f);
            var left = Anchor(0.2f);
            float rightHandHeight = _bake.GetVerticalAt(0.2f) + 1f -
                                    _bake.GetVerticalAt(0.3f);
            // At the later right contact the baked root has advanced 0.1m, so the hand's
            // forward reach is 0.1m shorter while both hands still meet the same ledge plane.
            var right = Anchor(0.3f, rightHandHeight, 0.2f);
            var transfer = Anchor(0.6f);
            var exit = Anchor(0.8f);
            var recovery = Anchor(0.9f);
            _bake.Traversal = authored.WithBakedAnchors(
                entry, left, right, transfer, exit, recovery);
        }

        private TraversalBakedAnchor Anchor(float time, float handY = 1f, float handZ = 0.3f)
        {
            _bake.GetHorizontalDisplacementAt(time);
            return new TraversalBakedAnchor(
                time,
                new Vector3(0f, _bake.GetVerticalAt(time), _bake.GetHorizontalDisplacementAt(time)),
                0f,
                new Vector3(-0.2f, handY, handZ),
                new Vector3(0.2f, handY, handZ),
                true,
                true);
        }

        private TraversalCandidate V3Candidate(
            float ledgeHeight,
            float halfWidth,
            float yaw = 0f,
            float capsuleWallClearance = 1f)
        {
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 wallNormal = -forward;
            Vector3 tangent = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 edge = forward * 0.5f + Vector3.up * ledgeHeight;
            Vector3 destination = forward * 0.8f + Vector3.up;
            var ledge = new TraversalLedgeFrame(
                edge, tangent, wallNormal, Vector3.up, -halfWidth, halfWidth);
            var corridor = TraversalCorridorEvidence.Clear(
                wallNormal * 0.2f,
                wallNormal * 0.2f + Vector3.up * 2f,
                destination + Vector3.up,
                destination);
            return new TraversalCandidate(
                TraversalKind.Climb1m,
                TraversalRejectReason.None,
                forward * 0.5f,
                wallNormal,
                edge,
                Vector3.up,
                ledgeHeight,
                1f,
                destination,
                forward,
                ledge,
                corridor,
                Vector3.zero,
                forward,
                TraversalDetectionDirectionSource.Facing,
                capsuleWallClearance);
        }

        private static TraversalEntryEvaluation ExecutableEntry(TraversalCandidate candidate)
        {
            return new TraversalEntryEvaluation(
                candidate.SensedRootPosition,
                candidate.SensedFacingForward,
                candidate.SensedRootPosition,
                Quaternion.LookRotation(candidate.Forward),
                0.45f,
                0f,
                0f,
                0f,
                0.2f,
                0.8f,
                true,
                TraversalEntryRejectReason.None);
        }

        private TraversalWarpPlan CreatePlan(
            Vector3 target,
            Vector3 targetForward,
            float warpStart,
            float warpEnd)
        {
            Assert.IsTrue(TraversalWarpPlan.TryCreate(
                _bake, Vector3.zero, Vector3.forward, target, targetForward,
                warpStart, warpEnd, 2f, 1f, out TraversalWarpPlan plan));
            return plan;
        }
    }
}
