using NUnit.Framework;
using Project.Core.Environment;
using Project.Core.StateMachine;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public class TraversalSelectionPolicyTests
    {
        [Test]
        public void J1_ClimbBelowSafeNormalJumpReach_PrefersJump()
        {
            bool preferTraversal = TraversalSelectionPolicy.PreferTraversal(
                TraversalKind.Climb1m, 0.8f, 5f, 10f, 0.15f);

            Assert.IsFalse(preferTraversal);
        }

        [Test]
        public void J2_ClimbAboveSafeNormalJumpReach_PrefersTraversal()
        {
            bool preferTraversal = TraversalSelectionPolicy.PreferTraversal(
                TraversalKind.Climb1m, 1.2f, 5f, 10f, 0.15f);

            Assert.IsTrue(preferTraversal);
        }

        [Test]
        public void J3_VaultIsNotFilteredByNormalJumpCapability()
        {
            bool preferTraversal = TraversalSelectionPolicy.PreferTraversal(
                TraversalKind.Vault1m, 0.2f, 20f, 10f, 0.15f);

            Assert.IsTrue(preferTraversal);
        }

        [TestCase(float.NaN, 10f)]
        [TestCase(5f, float.NaN)]
        [TestCase(5f, 0f)]
        [TestCase(float.PositiveInfinity, 10f)]
        public void J4_InvalidJumpParameters_ConservativelyKeepClimb(float velocity, float gravity)
        {
            bool first = TraversalSelectionPolicy.PreferTraversal(
                TraversalKind.Climb1m, 0.5f, velocity, gravity, 0.15f);
            bool second = TraversalSelectionPolicy.PreferTraversal(
                TraversalKind.Climb1m, 0.5f, velocity, gravity, 0.15f);

            Assert.IsTrue(first);
            Assert.AreEqual(first, second);
        }

        [Test]
        public void NoneCandidate_PreservesJumpFallback()
        {
            Assert.IsFalse(TraversalSelectionPolicy.PreferTraversal(
                TraversalKind.None, 0f, 5f, 10f, 0.15f));
        }

        // =====================================================================
        // 🆕（docs/23 §A10／Phase 1）Selection 的否決必須留下理由與判準數字。
        // 舊版只回 bool：Climb 被讓給普通 Jump 時畫面顯示 `Climb1m` ＋ `Entry: Accept`
        // 卻什麼都不發生，是本系統最誤導的狀態。
        // =====================================================================

        [Test]
        public void S1_ClimbWithinJumpReach_ReportsSilentVetoReasonAndNumbers()
        {
            TraversalSelectionEvaluation evaluation = TraversalSelectionPolicy.Evaluate(
                TraversalKind.Climb1m, 0.8f, 5f, 10f, 0.15f);

            Assert.IsTrue(evaluation.HasResult);
            Assert.IsFalse(evaluation.PreferTraversal);
            Assert.AreEqual(TraversalSelectionReason.ClimbWithinJumpReach, evaluation.Reason);
            Assert.IsTrue(evaluation.HasReachEvidence,
                "否決時必須帶出判準數字，否則畫面仍然無法解釋");
            Assert.AreEqual(1.25f, evaluation.TheoreticalApex, 0.001f);
            Assert.AreEqual(1.10f, evaluation.SafeReach, 0.001f);
            Assert.AreEqual(0.8f, evaluation.CandidateHeight, 0.001f);
        }

        [Test]
        public void S2_ClimbAboveJumpReach_ReportsAcceptanceWithSameEvidence()
        {
            TraversalSelectionEvaluation evaluation = TraversalSelectionPolicy.Evaluate(
                TraversalKind.Climb1m, 1.2f, 5f, 10f, 0.15f);

            Assert.IsTrue(evaluation.PreferTraversal);
            Assert.AreEqual(TraversalSelectionReason.ClimbAboveJumpReach, evaluation.Reason);
            Assert.AreEqual(1.10f, evaluation.SafeReach, 0.001f);
        }

        [Test]
        public void S3_Vault_IsContextualAndSaysSo()
        {
            TraversalSelectionEvaluation evaluation = TraversalSelectionPolicy.Evaluate(
                TraversalKind.Vault1m, 0.2f, 20f, 10f, 0.15f);

            Assert.IsTrue(evaluation.PreferTraversal);
            Assert.AreEqual(TraversalSelectionReason.ContextualVault, evaluation.Reason);
            Assert.IsFalse(evaluation.HasReachEvidence, "Vault 不比 jump reach，不該假造判準數字");
        }

        [TestCase(float.NaN, 10f)]
        [TestCase(5f, 0f)]
        public void S4_UnprovableJumpReach_KeepsClimbAndSaysWhy(float velocity, float gravity)
        {
            TraversalSelectionEvaluation evaluation = TraversalSelectionPolicy.Evaluate(
                TraversalKind.Climb1m, 0.5f, velocity, gravity, 0.15f);

            Assert.IsTrue(evaluation.PreferTraversal);
            Assert.AreEqual(TraversalSelectionReason.JumpReachUnknown, evaluation.Reason);
        }

        [TestCase(TraversalKind.Vault1m)]
        [TestCase(TraversalKind.Climb1m)]
        [TestCase(TraversalKind.Climb2m)]
        public void S6_DisabledKind_YieldsToNormalJumpAndSaysSo(TraversalKind kind)
        {
            // 內容開關停用某個 kind 時，必須**明講**是被停用，不能混進其他 reject 理由裡。
            TraversalCandidate candidate = DisabledKindCandidate(kind);

            TraversalSelectionEvaluation disabled = TraversalSelectionPolicy.Evaluate(
                in candidate, null, 0.15f, false);
            Assert.IsFalse(disabled.PreferTraversal);
            Assert.AreEqual(TraversalSelectionReason.KindDisabled, disabled.Reason);
            Assert.AreEqual(kind, disabled.Kind);

            TraversalSelectionEvaluation enabled = TraversalSelectionPolicy.Evaluate(
                in candidate, null, 0.15f, true);
            Assert.IsTrue(enabled.PreferTraversal,
                "開關勾回來就必須恢復——停用不得留下任何殘留狀態");
            Assert.AreNotEqual(TraversalSelectionReason.KindDisabled, enabled.Reason);
        }

        private static TraversalCandidate DisabledKindCandidate(TraversalKind kind) =>
            new TraversalCandidate(
                kind,
                TraversalRejectReason.None,
                Vector3.forward * 0.5f,
                -Vector3.forward,
                new Vector3(0f, 1f, 1f),
                Vector3.up,
                1f,
                1f,
                new Vector3(0f, 1f, 1.6f),
                Vector3.forward);

        [Test]
        public void S5_NoneCandidate_ReportsInvalidCandidate()
        {
            TraversalSelectionEvaluation evaluation = TraversalSelectionPolicy.Evaluate(
                TraversalKind.None, 0f, 5f, 10f, 0.15f);

            Assert.IsFalse(evaluation.PreferTraversal);
            Assert.AreEqual(TraversalSelectionReason.InvalidCandidate, evaluation.Reason);
        }

        [Test]
        public void E1_StationaryFacingCandidate_IsExecutable()
        {
            TraversalCandidate candidate = EntryCandidate(new Vector3(0f, 0f, 0.55f), Vector3.forward);
            TraversalEntryPolicySettings settings = TraversalEntryPolicySettings.Default;
            TraversalEntryEvaluation result = TraversalEntryPolicy.Evaluate(in candidate, in settings);
            Assert.IsTrue(result.Executable);
            Assert.AreEqual(TraversalEntryRejectReason.None, result.RejectReason);
        }

        [Test]
        public void E2_TooFar_CandidateExistsButEntryRejects()
            => AssertEntryRejected(
                EntryCandidate(Vector3.zero, Vector3.forward),
                TraversalEntryRejectReason.TooFar);

        [Test]
        public void EN3_AlreadyCloseButCapsuleSafe_IsExecutable()
        {
            TraversalCandidate candidate = EntryCandidate(
                new Vector3(0f, 0f, 0.9f), Vector3.forward, 0.02f);
            TraversalEntryPolicySettings settings = TraversalEntryPolicySettings.Default;
            TraversalEntryEvaluation result = TraversalEntryPolicy.Evaluate(in candidate, in settings);
            Assert.IsTrue(result.Executable);
            Assert.AreEqual(TraversalEntryDistanceBand.AlreadyClose, result.DistanceBand);
            Assert.AreEqual(TraversalEntryRejectReason.None, result.RejectReason);
        }

        [Test]
        public void EN4_PenetratingCapsule_RejectsTooCloseUnsafe()
            => AssertEntryRejected(
                EntryCandidate(new Vector3(0f, 0f, 0.9f), Vector3.forward, -0.05f),
                TraversalEntryRejectReason.TooCloseUnsafe);

        [Test]
        public void EN4_RootWallRelationBelowSafetyBand_RejectsTooCloseUnsafe()
            => AssertEntryRejected(
                EntryCandidate(new Vector3(0f, 0f, 0.99f), Vector3.forward, 0.02f),
                TraversalEntryRejectReason.TooCloseUnsafe);

        [Test]
        public void E4_LateralError_Rejects()
            => AssertEntryRejected(
                EntryCandidate(new Vector3(1f, 0f, 0.55f), Vector3.forward),
                TraversalEntryRejectReason.LateralOutOfRange);

        [Test]
        public void E5_FacingAngle_Rejects()
            => AssertEntryRejected(
                EntryCandidate(new Vector3(0f, 0f, 0.55f), Vector3.right),
                TraversalEntryRejectReason.FacingOutOfRange);

        [Test]
        public void E6_EntryPolicy_IsDeterministicValueOnly()
        {
            TraversalCandidate candidate = EntryCandidate(new Vector3(0f, 0f, 0.55f), Vector3.forward);
            TraversalEntryPolicySettings settings = TraversalEntryPolicySettings.Default;
            TraversalEntryEvaluation first = TraversalEntryPolicy.Evaluate(in candidate, in settings);
            TraversalEntryEvaluation second = TraversalEntryPolicy.Evaluate(in candidate, in settings);
            Assert.AreEqual(first.Executable, second.Executable);
            Assert.AreEqual(first.DesiredRootPosition, second.DesiredRootPosition);
            Assert.AreEqual(first.FacingAngleError, second.FacingAngleError);
        }

        private static void AssertEntryRejected(
            TraversalCandidate candidate,
            TraversalEntryRejectReason expected)
        {
            Assert.IsTrue(candidate.IsValid, "Entry legality must be separate from candidate existence.");
            TraversalEntryPolicySettings settings = TraversalEntryPolicySettings.Default;
            TraversalEntryEvaluation result = TraversalEntryPolicy.Evaluate(in candidate, in settings);
            Assert.IsFalse(result.Executable);
            Assert.AreEqual(expected, result.RejectReason);
        }

        private static TraversalCandidate EntryCandidate(
            Vector3 root,
            Vector3 facing,
            float capsuleWallClearance = 1f)
        {
            var ledge = new TraversalLedgeFrame(
                new Vector3(0f, 1f, 1f),
                Vector3.right,
                Vector3.back,
                Vector3.up,
                -0.5f,
                0.5f);
            var corridor = TraversalCorridorEvidence.Clear(
                new Vector3(0f, 0f, 0.55f),
                new Vector3(0f, 1.5f, 0.55f),
                new Vector3(0f, 1.5f, 1.5f),
                new Vector3(0f, 1f, 1.5f));
            return new TraversalCandidate(
                TraversalKind.Vault1m,
                TraversalRejectReason.None,
                new Vector3(0f, 0.5f, 1f),
                Vector3.back,
                ledge.EdgeOrigin,
                Vector3.up,
                1f,
                0.3f,
                corridor.ExitRoot,
                Vector3.forward,
                ledge,
                corridor,
                root,
                facing,
                TraversalDetectionDirectionSource.Facing,
                capsuleWallClearance);
        }
    }
}
