using NUnit.Framework;
using Project.Core.Environment;
using Project.Core.StateMachine;
using Project.Presentation.Motion;
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

        /// <summary>
        /// 🆕 2026-09-20：**動畫覆蓋範圍是內容決策，必須明講，且不得偽裝成幾何否決。**
        ///
        /// 由來（`docs/39` §2 Q7 ＋ 2026-09-20 實測）：Climb2m 的 classifier band 是 (1.25, 2.1]，
        /// 但該支動畫實際只在約 1.62–2.05 m 成立。低於 1.62 m 時 contact 殘差超過 IK guard，
        /// 手會在接觸當幀失控再瞬移。舊行為是照爬不誤；現在必須讓給普通 Jump 並說明理由。
        /// </summary>
        [Test]
        public void S7_HeightOutsideAnimationCoverage_YieldsToNormalJumpAndSaysSo()
        {
            // 高度 1.0 m：在 Climb2m 的 classifier band 內，但在覆蓋範圍 [1.62, 2.05] 之外。
            TraversalCandidate candidate = CoverageCandidate(TraversalKind.Climb2m, 1.0f);

            TraversalSelectionEvaluation outside = TraversalSelectionPolicy.Evaluate(
                in candidate, null, 0.15f, true, 1.62f, 2.05f);
            Assert.IsFalse(outside.PreferTraversal);
            Assert.AreEqual(
                TraversalSelectionReason.OutsideAnimationCoverage, outside.Reason,
                "覆蓋範圍外必須是自己的 reason——混進 ClimbWithinJumpReach 會讓「缺動畫」" +
                "看起來像「跳得到」，那是兩個完全不同的修法");

            // 同一個 candidate，不設覆蓋範圍（0 ＝ 不限制）⇒ 必須恢復舊行為。
            TraversalSelectionEvaluation unbounded = TraversalSelectionPolicy.Evaluate(
                in candidate, null, 0.15f, true, 0f, 0f);
            Assert.AreNotEqual(
                TraversalSelectionReason.OutsideAnimationCoverage, unbounded.Reason,
                "0 必須代表『不限制』，否則舊資產會在升級當下全部靜默失效");
        }

        [TestCase(1.61f, false)]
        [TestCase(1.62f, true)]   // 邊界是閉區間
        [TestCase(1.90f, true)]
        [TestCase(2.05f, true)]   // 邊界是閉區間
        [TestCase(2.06f, false)]
        public void S8_CoverageBoundsAreInclusive(float height, bool expectedInside)
        {
            TraversalCandidate candidate = CoverageCandidate(TraversalKind.Climb2m, height);

            TraversalSelectionEvaluation evaluation = TraversalSelectionPolicy.Evaluate(
                in candidate, null, 0.15f, true, 1.62f, 2.05f);

            Assert.AreEqual(
                expectedInside,
                evaluation.Reason != TraversalSelectionReason.OutsideAnimationCoverage,
                $"高度 {height} 的覆蓋判定不符預期");
        }

        /// <summary>
        /// 🆕（docs/52 T-03／docs/47 F-05）**「跳得過嗎」必須用 JumpState 實際會用的 launch。**
        /// 過去 policy 自己寫一份 v = √(2gh)，入口條件已與 JumpState 漂移（一邊取 |g|、一邊要求 g&gt;0）。
        /// 這裡以 JumpState 自己建出的 stage launch 為正本對拍。
        /// </summary>
        [TestCase(1f, 1f, 1f)]
        [TestCase(1.3f, 0.8f, 1.1f)]
        public void J6_PolicyApex_EqualsJumpStatesOwnLaunch(
            float heightMultiplier, float gravityMultiplier, float velocityMultiplier)
        {
            MotionBakeData bake = ScriptableObject.CreateInstance<MotionBakeData>();
            JumpStateParams jumpParams = ScriptableObject.CreateInstance<JumpStateParams>();
            try
            {
                bake.AutoApexHeight = 0.9535f;
                bake.AutoCalculatedGravity = 16.777f;
                jumpParams.Stages.Add(new JumpStage { Bake = bake });
                jumpParams.HeightMultiplier = heightMultiplier;
                jumpParams.GravityMultiplier = gravityMultiplier;
                jumpParams.LaunchVelocityMultiplier = velocityMultiplier;

                JumpLaunchData jumpLaunch = BuildJumpStateStageZero(jumpParams);
                float jumpApex = jumpLaunch.InitialVerticalVelocity * jumpLaunch.InitialVerticalVelocity /
                                 (2f * jumpLaunch.Gravity);

                TraversalCandidate candidate = CoverageCandidate(TraversalKind.Climb2m, 1.9f);
                TraversalSelectionEvaluation evaluation =
                    TraversalSelectionPolicy.Evaluate(in candidate, jumpParams, 0.15f);

                Assert.IsTrue(evaluation.HasReachEvidence, $"應有 reach 證據，實際 {evaluation.Reason}");
                Assert.AreEqual(jumpApex, evaluation.TheoreticalApex, 1e-4f,
                    "policy 的 apex 必須等於 JumpState 實際起跳的 apex");
            }
            finally
            {
                Object.DestroyImmediate(jumpParams);
                Object.DestroyImmediate(bake);
            }
        }

        /// <summary>
        /// bake 重力 ≤0 時 JumpState 不採用烘焙值（改用 fallback）；policy 必須同樣不採用，
        /// 並依 ADR-008 D7 判「跳躍能力無法推導」而保守保留 traversal——不得自己取 |g| 算出一個 JumpState 不會用的 apex。
        /// </summary>
        [Test]
        public void J7_NonPositiveBakedGravity_IsUnknownForPolicyAsForJumpState()
        {
            MotionBakeData bake = ScriptableObject.CreateInstance<MotionBakeData>();
            JumpStateParams jumpParams = ScriptableObject.CreateInstance<JumpStateParams>();
            try
            {
                bake.AutoApexHeight = 0.9535f;
                bake.AutoCalculatedGravity = -16.777f;
                jumpParams.Stages.Add(new JumpStage { Bake = bake });

                Assert.IsFalse(jumpParams.TryGetStageBakedLaunch(0, out _),
                    "負重力 bake 不是可信的跳躍資料");
                TraversalCandidate candidate = CoverageCandidate(TraversalKind.Climb1m, 0.5f);
                TraversalSelectionEvaluation evaluation =
                    TraversalSelectionPolicy.Evaluate(in candidate, jumpParams, 0.15f);
                Assert.IsTrue(evaluation.PreferTraversal);
                Assert.AreEqual(TraversalSelectionReason.JumpReachUnknown, evaluation.Reason);
            }
            finally
            {
                Object.DestroyImmediate(jumpParams);
                Object.DestroyImmediate(bake);
            }
        }

        private static JumpLaunchData BuildJumpStateStageZero(JumpStateParams jumpParams)
        {
            var jump = new JumpState();
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(JumpState).GetMethod("BuildStages", flags).Invoke(jump, new object[] { jumpParams });
            var stages = (JumpLaunchData[])typeof(JumpState).GetField("_stageLaunch", flags).GetValue(jump);
            return stages[0];
        }

        private static TraversalCandidate CoverageCandidate(TraversalKind kind, float height) =>
            new TraversalCandidate(
                kind,
                TraversalRejectReason.None,
                Vector3.forward * 0.5f,
                -Vector3.forward,
                new Vector3(0f, height, 1f),
                Vector3.up,
                height,
                1f,
                new Vector3(0f, height, 1.6f),
                Vector3.forward);

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
