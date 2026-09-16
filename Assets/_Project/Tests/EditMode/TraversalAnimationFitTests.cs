using NUnit.Framework;
using Project.Core.Environment;
using Project.Core.StateMachine;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// **Animation Fitting layer** 的回歸（`docs/24` §8／§10）。
    ///
    /// 兩組不變量：
    /// <list type="number">
    /// <item><b>F 系列</b>：fitting 本身是純函式，rate 一律由「動畫位移 vs 需求位移」推導，
    ///   不可用時要據實回報理由而不是硬算一個數字。</item>
    /// <item><b>R 系列</b>：**感測範圍必須涵蓋 entry 合法帶。**
    ///   這條就是 2026-09-13 那個 bug 的守門員——`ForwardScanDistance` 是 1.25 m 的 authored 常數，
    ///   而 Vault1m 由 bake 推導的理想進場距離是 1.402 m，理想站位落在感測範圍外，
    ///   可執行區間只剩約 20 cm。任何人再把兩者調成互相矛盾，這裡就會紅。</item>
    /// </list>
    /// </summary>
    public class TraversalAnimationFitTests
    {
        private const string VaultPath = "Assets/ScriptableObjects/Motion/Bake_Vault1m.asset";
        private const string Climb2mPath = "Assets/ScriptableObjects/Motion/Bake_Climb2m.asset";
        private const string ParamsPath =
            "Assets/ScriptableObjects/StateMachine/TraversalStateParams.asset";

        private static MotionBakeData Load(string path)
        {
            var bake = AssetDatabase.LoadAssetAtPath<MotionBakeData>(path);
            Assert.IsNotNull(bake, $"找不到正式 bake：{path}");
            return bake;
        }

        private static TraversalStateParamsSO LoadParams()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TraversalStateParamsSO>(ParamsPath);
            Assert.IsNotNull(asset, $"找不到 TraversalStateParams：{ParamsPath}");
            return asset;
        }

        // =====================================================================
        // F 系列 —— Fitting 本身
        // =====================================================================

        [Test]
        public void F1_Disabled_ReportsNotFittedAndNeutralRate()
        {
            MotionBakeData vault = Load(VaultPath);
            var settings = new TraversalAnimationFitSettings(false, 0.6f, 1.6f);
            TraversalCandidate candidate = VaultCandidate(1.4f, 1.2f);
            TraversalAnimationFit fit = FitAt(vault, in candidate, 1.4f, in settings);

            Assert.AreEqual(TraversalFitReason.NotFitted, fit.Reason);
            Assert.AreEqual(1f, fit.PlaybackRate, 1e-5f, "關閉時必須完全中性，行為與導入本層前相同");
        }

        [Test]
        public void F2_MissingBake_ReportsMissingBakeDataAndNeutralRate()
        {
            var settings = TraversalAnimationFitSettings.Default;
            TraversalCandidate candidate = VaultCandidate(1.4f, 1.2f);
            var entry = default(TraversalEntryEvaluation);
            TraversalAnimationFit fit =
                TraversalAnimationFitter.Fit(null, in candidate, in entry, in settings);

            Assert.AreEqual(TraversalFitReason.MissingBakeData, fit.Reason);
            Assert.AreEqual(1f, fit.PlaybackRate, 1e-5f);
        }

        [Test]
        public void F3_IdealStance_NeedsNoRateChange()
        {
            MotionBakeData vault = Load(VaultPath);
            Assert.IsTrue(vault.TryGetDesiredEntryDistance(out float ideal));
            var settings = TraversalAnimationFitSettings.Default;
            TraversalCandidate candidate = VaultCandidate(ideal, 1.2f);
            TraversalAnimationFit fit = FitAt(vault, in candidate, ideal, in settings);

            Assert.AreEqual(TraversalFitReason.Fitted, fit.Reason);
            Assert.AreEqual(1f, fit.PlaybackRate, 0.02f,
                "站在推導出的理想距離上，動畫的助跑就是需求助跑 ⇒ 不該改速率");
            Assert.AreEqual(fit.ApproachAnimatedDistance, fit.ApproachRequiredDistance, 0.01f);
        }

        [Test]
        public void F4_StandingCloser_PlaysFaster_StandingFurther_PlaysSlower()
        {
            // rate = 動畫位移 / 需求位移。站得近 ⇒ 需要走的更短 ⇒ 同樣的世界速度要用更短的時間走完。
            MotionBakeData vault = Load(VaultPath);
            Assert.IsTrue(vault.TryGetDesiredEntryDistance(out float ideal));
            var settings = new TraversalAnimationFitSettings(true, 0.1f, 10f);

            TraversalCandidate near = VaultCandidate(ideal - 0.25f, 1.2f);
            TraversalAnimationFit nearFit = FitAt(vault, in near, ideal - 0.25f, in settings);
            TraversalCandidate far = VaultCandidate(ideal + 0.25f, 1.2f);
            TraversalAnimationFit farFit = FitAt(vault, in far, ideal + 0.25f, in settings);

            Assert.Greater(nearFit.PlaybackRate, 1f, "站得比理想近應該播快");
            Assert.Less(farFit.PlaybackRate, 1f, "站得比理想遠應該播慢");

            // 數值關係也要對：required = animated + longitudinalError。
            float animated = nearFit.ApproachAnimatedDistance;
            Assert.AreEqual(animated / (animated - 0.25f), nearFit.PlaybackRate, 0.02f);
            Assert.AreEqual(animated / (animated + 0.25f), farFit.PlaybackRate, 0.02f);
        }

        [Test]
        public void F5_RateClamp_IsReportedNotSilentlyApplied()
        {
            MotionBakeData vault = Load(VaultPath);
            Assert.IsTrue(vault.TryGetDesiredEntryDistance(out float ideal));
            var settings = new TraversalAnimationFitSettings(true, 0.95f, 1.05f);

            TraversalCandidate near = VaultCandidate(ideal - 0.3f, 1.2f);
            TraversalAnimationFit fit = FitAt(vault, in near, ideal - 0.3f, in settings);

            Assert.AreEqual(TraversalFitReason.RateClampedFast, fit.Reason,
                "被夾住就要說出來——靜默夾限是 docs/23 §A10 點名的除錯黑洞");
            Assert.AreEqual(1.05f, fit.PlaybackRate, 1e-4f);
            Assert.Greater(fit.RawPlaybackRate, fit.PlaybackRate, "原始值必須保留給 debug");
        }

        [Test]
        public void F6_ClipWithoutApproachTravel_DoesNotInventARate()
        {
            // Climb2m 在接觸前 root 完全不移動（H(contact) = 0）⇒ 沒有速度可以配。
            // 這種情況必須回 NotFitted，而不是用一個退化的除法生出離譜的 rate。
            MotionBakeData climb = Load(Climb2mPath);
            Assert.AreEqual(
                0f,
                TraversalAnimationFitter.GetApproachDistance(climb),
                0.01f,
                "前提變了：Climb2m 接觸前開始有位移，本測試的假設要重寫");

            Assert.IsTrue(climb.TryGetDesiredEntryDistance(out float ideal));
            var settings = TraversalAnimationFitSettings.Default;
            TraversalCandidate candidate = ClimbCandidate(ideal);
            TraversalAnimationFit fit = FitAt(climb, in candidate, ideal, in settings);

            Assert.AreEqual(TraversalFitReason.NotFitted, fit.Reason);
            Assert.AreEqual(1f, fit.PlaybackRate, 1e-5f);
        }

        [Test]
        public void F7_CommittedLandingDepth_TakesTheSmallerOfAnimatedAndVerified()
        {
            // 比動畫遠 ⇒ 放手後還要被往前推；比驗證過的面遠 ⇒ 踩空。兩個都不能要。
            Assert.AreEqual(0.5f,
                TraversalAnimationFitter.ResolveCommittedLandingDepth(0.5f, 1.2f), 1e-5f);
            Assert.AreEqual(0.8f,
                TraversalAnimationFitter.ResolveCommittedLandingDepth(1.5f, 0.8f), 1e-5f);
            Assert.AreEqual(0.7f,
                TraversalAnimationFitter.ResolveCommittedLandingDepth(0.7f, float.NaN), 1e-5f);
            Assert.AreEqual(0.9f,
                TraversalAnimationFitter.ResolveCommittedLandingDepth(float.NaN, 0.9f), 1e-5f);
            Assert.IsTrue(float.IsNaN(
                TraversalAnimationFitter.ResolveCommittedLandingDepth(float.NaN, float.NaN)));
        }

        // =====================================================================
        // R 系列 —— 感測範圍必須由動畫需求推導
        // =====================================================================

        [Test]
        public void R1_SensingReach_CoversEveryEnabledKindsLegalEntryBand()
        {
            // ⭐ 2026-09-13 的 bug：ForwardScanDistance 1.25 m < Vault1m 理想進場距離 1.402 m，
            //    理想站位落在感測範圍外，可執行區間只剩約 20 cm（docs/24 §9.3）。
            TraversalStateParamsSO prms = LoadParams();
            float reach = prms.GetRequiredSensingReach();
            Assert.Greater(reach, 0f, "至少要有一個啟用中的 kind 推得出進場距離");

            TraversalEntryPolicySettings entry = prms.EntryPolicy;
            foreach (TraversalKind kind in new[]
                     { TraversalKind.Vault1m, TraversalKind.Climb1m, TraversalKind.Climb2m })
            {
                if (!prms.IsKindEnabled(kind)) continue;
                MotionBakeData bake = prms.GetBinding(kind).Bake;
                float desired = bake != null && bake.TryGetDesiredEntryDistance(out float derived)
                    ? derived
                    : entry.DesiredWallDistance;
                float bandFar = desired + entry.MaximumFarError;
                Assert.GreaterOrEqual(reach, bandFar - 1e-4f,
                    $"{kind}：合法進場帶到 {bandFar:F3} m，但感測只到 {reach:F3} m ⇒ " +
                    "玩家站在合法位置卻偵測不到障礙物");
            }
        }

        [Test]
        public void R2_LandingScanDepth_CoversEveryEnabledKindsNaturalExitDepth()
        {
            TraversalStateParamsSO prms = LoadParams();
            float landing = prms.GetRequiredLandingDepth();

            foreach (TraversalKind kind in new[]
                     { TraversalKind.Vault1m, TraversalKind.Climb1m, TraversalKind.Climb2m })
            {
                if (!prms.IsKindEnabled(kind)) continue;
                MotionBakeData bake = prms.GetBinding(kind).Bake;
                if (bake == null || !bake.TryGetNaturalExitDepth(out float depth)) continue;
                Assert.GreaterOrEqual(landing, depth - 1e-4f,
                    $"{kind}：動畫自然落點 {depth:F3} m 超出落腳面掃描上限 {landing:F3} m ⇒ " +
                    "終點會停在探測常數上，落差只能靠 correction 往回拉");
            }
        }

        [Test]
        public void R3_Probe_TakesTheLargerOfAuthoredAndDerivedRange()
        {
            var go = new GameObject("probe-range-test");
            try
            {
                go.AddComponent<CharacterController>();
                var probe = go.AddComponent<TraversalProbe>();

                float authoredScan = probe.EffectiveForwardScanDistance;
                Assert.Greater(authoredScan, 0f);

                probe.SetDerivedRangeRequirements(authoredScan + 1f, 5f);
                Assert.AreEqual(authoredScan + 1f, probe.EffectiveForwardScanDistance, 1e-4f,
                    "動畫需求比 authored 遠時要放大");
                Assert.AreEqual(5f, probe.EffectiveLandingScanDepth, 1e-4f);

                probe.SetDerivedRangeRequirements(0.01f, 0.01f);
                Assert.AreEqual(authoredScan, probe.EffectiveForwardScanDistance, 1e-4f,
                    "動畫需求比 authored 近時不得縮小——authored 是下限，不是上限");

                probe.SetDerivedRangeRequirements(float.NaN, float.NaN);
                Assert.AreEqual(authoredScan, probe.EffectiveForwardScanDistance, 1e-4f,
                    "沒有推導值時必須安全退回 authored");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // =====================================================================
        // 測試用 candidate
        // =====================================================================

        private static TraversalAnimationFit FitAt(
            MotionBakeData bake,
            in TraversalCandidate candidate,
            float wallDistance,
            in TraversalAnimationFitSettings settings)
        {
            bake.TryGetDesiredEntryDistance(out float ideal);
            var entrySettings = TraversalEntryPolicySettings.Default;
            TraversalEntryEvaluation entry = TraversalEntryPolicy.Evaluate(
                in candidate, in entrySettings, ideal);
            Assert.AreEqual(wallDistance, entry.LongitudinalDistance, 0.01f,
                "測試候選的站位與參數不一致——量錯基準就會量出假結論");
            return TraversalAnimationFitter.Fit(bake, in candidate, in entry, in settings);
        }

        /// <summary>薄牆 vault：落點在牆的另一側地面。</summary>
        private static TraversalCandidate VaultCandidate(float wallDistance, float landingDepth)
        {
            Vector3 forward = Vector3.forward;
            Vector3 edge = new Vector3(0f, 1f, 0f);
            Vector3 destination = new Vector3(0f, 0f, landingDepth);
            var ledge = new TraversalLedgeFrame(edge, Vector3.right, -forward, Vector3.up, -1f, 1f);
            return new TraversalCandidate(
                TraversalKind.Vault1m, TraversalRejectReason.None,
                new Vector3(0f, 0.5f, 0f), -forward, edge, Vector3.up,
                1f, 0.5f, destination, forward, ledge,
                TraversalCorridorEvidence.Clear(
                    edge - forward * 0.2f, edge + Vector3.up * 0.05f, destination),
                new Vector3(0f, 0f, -wallDistance), forward,
                TraversalDetectionDirectionSource.Facing,
                wallDistance - 0.12f + 0.03f,
                landingDepth);
        }

        /// <summary>2 m 平台 climb：落點在平台頂面。</summary>
        private static TraversalCandidate ClimbCandidate(float wallDistance)
        {
            Vector3 forward = Vector3.forward;
            Vector3 edge = new Vector3(0f, 2.003f, 0f);
            Vector3 destination = new Vector3(0f, 2.003f, 0.6f);
            var ledge = new TraversalLedgeFrame(edge, Vector3.right, -forward, Vector3.up, -1f, 1f);
            return new TraversalCandidate(
                TraversalKind.Climb2m, TraversalRejectReason.None,
                new Vector3(0f, 1f, 0f), -forward, edge, Vector3.up,
                2.003f, 0.6f, destination, forward, ledge,
                TraversalCorridorEvidence.Clear(
                    edge - forward * 0.2f, edge + Vector3.up * 0.05f, destination),
                new Vector3(0f, 0f, -wallDistance), forward,
                TraversalDetectionDirectionSource.Facing,
                wallDistance - 0.12f + 0.03f,
                0.6f);
        }
    }
}
