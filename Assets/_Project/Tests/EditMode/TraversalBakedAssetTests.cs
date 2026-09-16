using NUnit.Framework;
using Project.Core.Environment;
using Project.Core.StateMachine;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（docs/23 §R1／Phase 1）**對出貨資產本身的回歸測試。**
    ///
    /// 既有的 traversal 測試全部以 <c>ScriptableObject.CreateInstance&lt;MotionBakeData&gt;()</c>
    /// ＋手工填的曲線驗演算法——那是 docs/23 §A9 指認的「測試綠但出貨資料完全走不到」模式。
    /// 本檔載入 <b>三支正式 Bake</b>，驗的是玩家實際會遇到的數值。
    ///
    /// ⚠️ 本檔**不**驗手感，只驗可證偽的數值性質：
    /// 時間基準一致、correction 不後段集中、尾段不 snap、失敗有理由。
    /// </summary>
    public class TraversalBakedAssetTests
    {
        private const string VaultPath = "Assets/ScriptableObjects/Motion/Bake_Vault1m.asset";
        private const string Climb1mPath = "Assets/ScriptableObjects/Motion/Bake_Climb1m.asset";
        private const string Climb2mPath = "Assets/ScriptableObjects/Motion/Bake_Climb2m.asset";

        private static string[] ShippedBakePaths => new[] { VaultPath, Climb1mPath, Climb2mPath };

        private static MotionBakeData Load(string path)
        {
            var bake = AssetDatabase.LoadAssetAtPath<MotionBakeData>(path);
            Assert.IsNotNull(bake, $"找不到正式 bake：{path}");
            Assert.Greater(bake.Duration, 0f, $"{path} 的 BakedDuration 無效");
            Assert.IsNotNull(bake.VerticalCurve, $"{path} 缺 VerticalCurve");
            Assert.IsNotNull(bake.SpeedCurve, $"{path} 缺 SpeedCurve");
            return bake;
        }

        /// <summary>Correction 為零的 exact-fit target：把 baked 終點原樣當成目的地。</summary>
        private static Vector3 ExactFitTarget(MotionBakeData bake, Vector3 start, Vector3 forward) =>
            start +
            forward * bake.GetHorizontalDisplacementAt(bake.Duration) +
            Vector3.up * bake.GetVerticalAt(bake.Duration);

        // =====================================================================
        // A2／E-2 缺陷① —— root 與 animation pose 的時間基準
        // =====================================================================

        [Test]
        public void B1_ShippedBakes_RootTimeEqualsAnimationTime()
        {
            foreach (string path in ShippedBakePaths)
            {
                MotionBakeData bake = Load(path);
                Vector3 target = ExactFitTarget(bake, Vector3.zero, Vector3.forward);
                Assert.IsTrue(
                    TraversalWarpPlan.TryCreate(
                        bake, Vector3.zero, Vector3.forward, target, Vector3.forward,
                        0f, bake.GetVerticalSettleNormalizedTime(), 2f, 1f,
                        out TraversalWarpPlan plan),
                    $"{path}: exact-fit target 仍建不出 endpoint plan");

                for (int i = 0; i <= 100; i++)
                {
                    float n = i / 100f;
                    Assert.AreEqual(n, plan.TrajectoryNormalizedAt(n), 1e-4f,
                        $"{path}: n={n:F2} 的 root 時間被重新映射");
                }
            }
        }

        [Test]
        public void B2_ShippedBakes_ExactFitTarget_ProducesNoRootPoseDesync()
        {
            // ⭐ 這是 docs/23 §E-2 的核心回歸：**correction 為零時，warped 必須逐格等於 original。**
            //    修正前實測峰值 Climb1m +0.82m／Climb2m +0.53m／Vault1m −1.02m。
            foreach (string path in ShippedBakePaths)
            {
                MotionBakeData bake = Load(path);
                Vector3 target = ExactFitTarget(bake, Vector3.zero, Vector3.forward);
                Assert.IsTrue(TraversalWarpPlan.TryCreate(
                    bake, Vector3.zero, Vector3.forward, target, Vector3.forward,
                    0f, bake.GetVerticalSettleNormalizedTime(), 2f, 1f,
                    out TraversalWarpPlan plan));

                float worstVertical = 0f;
                float worstTotal = 0f;
                for (int i = 0; i <= 200; i++)
                {
                    float n = i / 200f;
                    Assert.IsTrue(TraversalWarpPlan.TryEvaluateOriginal(
                        bake, Vector3.zero, Vector3.forward, n,
                        out Vector3 original, out _));
                    Assert.IsTrue(plan.TryEvaluate(n, out Vector3 warped, out _));
                    worstVertical = Mathf.Max(worstVertical, Mathf.Abs(warped.y - original.y));
                    worstTotal = Mathf.Max(worstTotal, Vector3.Distance(warped, original));
                }

                Assert.Less(worstVertical, 0.005f,
                    $"{path}: correction=0 時 root 仍與 animation pose 垂直分離 {worstVertical:F3} m");
                Assert.Less(worstTotal, 0.005f,
                    $"{path}: correction=0 時 root 仍與 animation pose 分離 {worstTotal:F3} m");
            }
        }

        // =====================================================================
        // E-2 缺陷②③ —— correction 不得結構性集中到尾段
        // =====================================================================

        [Test]
        public void B3_VerticalSettleTime_IsDerivedFromShippedCurves()
        {
            foreach (string path in ShippedBakePaths)
            {
                MotionBakeData bake = Load(path);
                float settle = bake.GetVerticalSettleNormalizedTime();

                Assert.Greater(settle, 0f, $"{path}: 推導出的垂直動作窗為空");
                Assert.LessOrEqual(settle, 1f, $"{path}: settle 超出 0–1");
                Assert.Less(settle, 1f,
                    $"{path}: 三支正式 clip 的尾段都是站定／跑出，settle 不應等於 1");

                // settle 之後 VerticalCurve 必須真的是平的——否則這個推導沒有意義。
                float finalValue = bake.GetVerticalAt(bake.Duration);
                for (float n = settle; n <= 1f; n += 0.01f)
                {
                    Assert.AreEqual(finalValue, bake.GetVerticalAt(n * bake.Duration), 1e-3f,
                        $"{path}: settle={settle:F3} 之後 VerticalCurve 仍在變化");
                }
            }
        }

        [Test]
        public void B4_VerticalCorrection_IsFullyAppliedByActionEnd_NotAtClipEnd()
        {
            // docs/23 §E-2 缺陷③：舊預設 warp window [0, 0.8] 比實際動作窗寬 2–5 倍，
            // 於是角色已經站上平台之後還在飄。correction 必須在動畫「爬完了」的時刻就收斂。
            foreach (string path in ShippedBakePaths)
            {
                MotionBakeData bake = Load(path);
                float settle = bake.GetVerticalSettleNormalizedTime();
                Vector3 exact = ExactFitTarget(bake, Vector3.zero, Vector3.forward);
                Vector3 raised = exact + Vector3.up * 0.2f;

                Assert.IsTrue(TraversalWarpPlan.TryCreate(
                    bake, Vector3.zero, Vector3.forward, raised, Vector3.forward,
                    0f, settle, 2f, 1f, out TraversalWarpPlan plan));

                Assert.AreEqual(1f, plan.CorrectionWeightAt(settle), 1e-3f,
                    $"{path}: 動作結束時 correction 尚未收斂");

                Assert.IsTrue(TraversalWarpPlan.TryEvaluateOriginal(
                    bake, Vector3.zero, Vector3.forward, settle,
                    out Vector3 originalAtSettle, out _));
                Assert.IsTrue(plan.TryEvaluate(settle, out Vector3 warpedAtSettle, out _));
                Assert.AreEqual(0.2f, warpedAtSettle.y - originalAtSettle.y, 0.01f,
                    $"{path}: 動作結束時只套用了部分垂直 correction");
            }
        }

        [Test]
        public void B5_ShippedBakes_NoTerminalSnap_AfterActionEnd()
        {
            // 「結尾瞬間修正」的數值定義：動作結束後 **warp 施加的 correction** 仍在變化。
            //
            // ⚠️ 必須量 correction（warped − original），不能量絕對 Y：
            //    Vault1m 的 VerticalCurve 本身就在 settle 邊界從 1.02m 掉回 0（角色落到對面地面），
            //    那是動畫內容，不是 warp 的 snap。量絕對 Y 會把兩者混為一談。
            foreach (string path in ShippedBakePaths)
            {
                MotionBakeData bake = Load(path);
                float settle = bake.GetVerticalSettleNormalizedTime();
                Vector3 raised = ExactFitTarget(bake, Vector3.zero, Vector3.forward) + Vector3.up * 0.2f;
                Assert.IsTrue(TraversalWarpPlan.TryCreate(
                    bake, Vector3.zero, Vector3.forward, raised, Vector3.forward,
                    0f, settle, 2f, 1f, out TraversalWarpPlan plan));

                float previousCorrection = float.NaN;
                float worstTailStep = 0f;
                float worstAnyStep = 0f;
                for (int i = 0; i <= 200; i++)
                {
                    float n = i / 200f;
                    Assert.IsTrue(TraversalWarpPlan.TryEvaluateOriginal(
                        bake, Vector3.zero, Vector3.forward, n, out Vector3 original, out _));
                    Assert.IsTrue(plan.TryEvaluate(n, out Vector3 warped, out _));
                    float correction = warped.y - original.y;
                    if (!float.IsNaN(previousCorrection))
                    {
                        float step = Mathf.Abs(correction - previousCorrection);
                        worstAnyStep = Mathf.Max(worstAnyStep, step);
                        if (n > settle) worstTailStep = Mathf.Max(worstTailStep, step);
                    }
                    previousCorrection = correction;
                }

                Assert.Less(worstTailStep, 0.001f,
                    $"{path}: 動作結束後 correction 仍在變化 {worstTailStep:F4} m（結尾 snap）");
                Assert.Greater(worstAnyStep, 0f,
                    $"{path}: correction 從未被施加，本測試失去意義");
            }
        }

        // =====================================================================
        // A4 —— Vault1m 的 3.26m 水平總位移必須「說得出口」，不得靜默退化
        // =====================================================================

        [Test]
        public void B6_Vault1mHorizontalOverrun_IsReportedNotSilent()
        {
            MotionBakeData bake = Load(VaultPath);
            float bakedHorizontal = bake.GetHorizontalDisplacementAt(bake.Duration);
            Assert.Greater(bakedHorizontal, 2f,
                "前提改變：Bake_Vault1m 的 baked 水平位移不再遠大於實際 entry 距離，" +
                "docs/23 §A4 需重新評估");

            // 以實際 entry band（root 距牆 ~0.45m、destination 在 edge 前 ~0.6m）為目標。
            Vector3 target = Vector3.forward * 1.05f;
            Assert.IsFalse(
                TraversalWarpPlan.TryCreate(
                    bake, Vector3.zero, Vector3.forward, target, Vector3.forward,
                    0f, bake.GetVerticalSettleNormalizedTime(), 1.5f, 0.75f,
                    out TraversalWarpPlan plan,
                    out TraversalWarpPlanRejection rejection,
                    out float requiredHorizontal,
                    out float requiredVertical),
                "前提改變：Vault1m 在現實 entry 距離下已不再超限");

            Assert.IsFalse(plan.IsValid);
            Assert.AreEqual(TraversalWarpPlanRejection.HorizontalCorrectionExceeded, rejection,
                "Vault1m 的水平超限必須有理由，不能只回 false");
            Assert.Greater(requiredHorizontal, 1.5f,
                "回報的需求量必須真的超過上限，否則面板會說謊");
            Assert.IsFalse(float.IsNaN(requiredVertical));
        }

        // =====================================================================
        // Phase 2 —— 接觸標記由 bake 自動測出，正式資產必須真的走得到 piecewise
        // =====================================================================

        [Test]
        public void B7_ActiveShippedBakes_HaveDerivedTraversalMarkers()
        {
            // 🔄 本測試原本斷言「三支 bake 都沒有 Traversal block」（Phase 1 的事實）。
            //    Phase 2 由 TraversalContactDerivation 自動測出並寫入，預期因此反轉。
            //    Climb1m 不在此列：它的手全程滑動、測不到接觸，且已由內容開關停用。
            foreach (string path in new[] { VaultPath, Climb2mPath })
            {
                MotionBakeData bake = Load(path);
                TraversalMotionBakeBlock block = bake.Traversal;

                Assert.IsTrue(block.HasValidBakedData,
                    $"{path}: 缺少自動測出的 Traversal block —— 請重跑 TraversalContactDerivation.Apply");
                Assert.AreNotEqual(TraversalHandContactMode.None, block.ContactMode);

                float latestContact = 0f;
                if (block.UsesLeftHand) latestContact = block.LeftHandContactNormalizedTime;
                if (block.UsesRightHand)
                    latestContact = Mathf.Max(latestContact, block.RightHandContactNormalizedTime);

                Assert.Greater(latestContact, 0f,
                    $"{path}: 接觸落在第 0 格 —— 偵測到的是動畫開頭的靜止，不是接觸");
                Assert.Less(latestContact, block.TransferNormalizedTime,
                    $"{path}: transfer 必須嚴格晚於最後一次接觸");
                Assert.Less(block.TransferNormalizedTime, block.ExitNormalizedTime,
                    $"{path}: exit 必須嚴格晚於 transfer");
                Assert.LessOrEqual(block.ExitNormalizedTime, block.RecoveryNormalizedTime);

                if (block.UsesLeftHand)
                    Assert.Greater(block.LeftContactAnchor.LeftHandInRoot.magnitude, 0.05f,
                        $"{path}: 左手 hand-in-root 幾乎為零，空間採樣沒有生效");
                if (block.UsesRightHand)
                    Assert.Greater(block.RightContactAnchor.RightHandInRoot.magnitude, 0.05f,
                        $"{path}: 右手 hand-in-root 幾乎為零，空間採樣沒有生效");
            }
        }

        [Test]
        public void B9_Climb2m_BuildsPiecewisePlanWithSmallHandResidual()
        {
            // Phase 2 的實際驗收：正式 bake ＋ 真實幾何 ⇒ 走得到 piecewise，
            // 而且手的殘差小到 Hand IK 收得掉（controller 的放棄門檻是 0.35 m）。
            MotionBakeData bake = Load(Climb2mPath);
            const float ledgeHeight = 2.003f;

            foreach (float wallDistance in new[] { 0.45f, 0.61f })
            {
                TraversalCandidate candidate = ShippedCandidate(
                    TraversalKind.Climb2m, ledgeHeight, wallDistance, 0.6f);
                TraversalEntryEvaluation entry = TraversalEntryPolicy.Evaluate(
                    in candidate, TraversalEntryPolicySettings.Default);
                Assert.IsTrue(entry.Executable,
                    $"wall={wallDistance:F2}: 測試前提失效，entry 被 {entry.RejectReason} 擋下");

                Assert.IsTrue(
                    TraversalPlanBuilder.TryBuild(
                        bake, in candidate, in entry, 0.08f, 1.5f, 0.75f,
                        out TraversalWarpPlan plan,
                        out TraversalWarpPlanRejection rejection),
                    $"wall={wallDistance:F2}: piecewise 建不起來（{rejection}）");
                Assert.IsTrue(plan.IsPiecewise);

                TraversalContactTargets contacts = plan.ContactTargets;
                Assert.IsTrue(contacts.HasLeftHand && contacts.HasRightHand, "Climb2m 是雙手抓握");
                Assert.Less(contacts.LeftHandResidualError, 0.05f,
                    $"wall={wallDistance:F2}: 左手殘差 {contacts.LeftHandResidualError * 100f:F1} cm 過大");
                Assert.Less(contacts.RightHandResidualError, 0.05f,
                    $"wall={wallDistance:F2}: 右手殘差 {contacts.RightHandResidualError * 100f:F1} cm 過大");
            }
        }

        [Test]
        public void B11_RootCorrection_IsFrozenWhileAHandIsGripping()
        {
            // ⭐ 2026-09-13 Playtest：「手應該抓牢固之後卻還在位移」。
            //
            // 手一旦植在牆／邊緣上，它的世界位置就是硬約束。手的世界位置等於
            //     clip 自己的手位置 + root correction
            // 所以只要 correction 在抓握期間有任何變化，手就會被拖著在牆面上滑
            // （沒有 IK 釘住它時尤其明顯）。
            // 舊版的 Transfer knot 指向另一個空間目標，於是整段抓握期都在內插 ⇒ 手一路滑。
            //
            // 本測試要求：**Contact → Transfer（＝最後一隻手放開）之間 correction 必須完全不動。**
            MotionBakeData bake = Load(Climb2mPath);
            TraversalMotionBakeBlock block = bake.Traversal;
            Assert.IsTrue(block.HasValidBakedData);

            Assert.IsTrue(bake.TryGetDesiredEntryDistance(out float desiredEntry));
            TraversalCandidate candidate = ShippedCandidate(
                TraversalKind.Climb2m, 2.003f, desiredEntry, 0.6f);
            var settings = TraversalEntryPolicySettings.Default;
            TraversalEntryEvaluation entry = TraversalEntryPolicy.Evaluate(
                in candidate, in settings, desiredEntry);
            Assert.IsTrue(
                TraversalPlanBuilder.TryBuild(
                    bake, in candidate, in entry, settings.LedgeEndMargin, 1.5f, 0.75f,
                    out TraversalWarpPlan plan,
                    out TraversalWarpPlanRejection rejection),
                $"piecewise 建不起來（{rejection}）");

            float gripStart = Mathf.Min(
                block.LeftHandContactNormalizedTime, block.RightHandContactNormalizedTime);
            float gripEnd = block.TransferNormalizedTime;
            Assert.Greater(gripEnd, gripStart, "抓握窗為空，測試失去意義");

            // knot 層級：接觸與放開的 correction 必須相同。
            Assert.AreEqual(
                0f,
                Vector3.Distance(
                    plan.RightContactKnot.PositionCorrection,
                    plan.TransferKnot.PositionCorrection),
                1e-4f,
                "Transfer 的 correction 與最後一次接觸不同 ⇒ 抓握期間 root 仍在被推");

            // 取樣層級：整段抓握窗內，correction 相對抓握起點的變化必須為零。
            Assert.IsTrue(plan.TryEvaluate(gripStart, out Vector3 warpedAtGrip, out _));
            Assert.IsTrue(TraversalWarpPlan.TryEvaluateOriginal(
                bake, plan.StartPosition, plan.StartForward, gripStart,
                out Vector3 originalAtGrip, out _));
            Vector3 gripCorrection = warpedAtGrip - originalAtGrip;

            float worstDrift = 0f;
            for (int i = 0; i <= 40; i++)
            {
                float n = Mathf.Lerp(gripStart, gripEnd, i / 40f);
                Assert.IsTrue(plan.TryEvaluate(n, out Vector3 warped, out _));
                Assert.IsTrue(TraversalWarpPlan.TryEvaluateOriginal(
                    bake, plan.StartPosition, plan.StartForward, n,
                    out Vector3 original, out _));
                worstDrift = Mathf.Max(
                    worstDrift, Vector3.Distance(warped - original, gripCorrection));
            }

            Assert.Less(worstDrift, 0.002f,
                $"抓握期間 warp 額外推了手 {worstDrift * 100f:F1} cm —— 手會在牆面上滑");
        }

        [Test]
        public void B10_EntryDistance_IsDerivedPerClip_AndKeepsCorrectionWithinBudget()
        {
            // docs/22 §14.5：單一全域 0.45 m 服務不了兩支需求差 3.5 倍的動畫。
            // 推導值必須有意義地不同，而且在自己的合法範圍內，warp 的修正量要進得了預算。
            MotionBakeData vault = Load(VaultPath);
            MotionBakeData climb = Load(Climb2mPath);

            Assert.IsTrue(vault.TryGetDesiredEntryDistance(out float vaultEntry));
            Assert.IsTrue(climb.TryGetDesiredEntryDistance(out float climbEntry));

            Assert.Greater(vaultEntry, climbEntry * 2f,
                "跑動翻越自帶助跑，進場距離必須明顯大於原地起攀——兩者若接近代表推導沒生效");
            Assert.Greater(vaultEntry, 0.9f, "Vault 的進場距離應落在助跑可行的範圍");
            Assert.Less(climbEntry, 0.6f, "Climb2m 是原地起攀，進場距離應該很短");

            // 在推導出的合法範圍內，contact 解出來的修正量必須進得了 binding 的預算（1.5 m）。
            var settings = TraversalEntryPolicySettings.Default;
            foreach (float wallDistance in new[]
                     {
                         Mathf.Max(0.05f, vaultEntry - settings.MaximumCloseError + 0.05f),
                         vaultEntry,
                         vaultEntry + settings.MaximumFarError - 0.05f,
                     })
            {
                TraversalCandidate candidate = ShippedVaultCandidate(wallDistance);
                TraversalEntryEvaluation entry = TraversalEntryPolicy.Evaluate(
                    in candidate, in settings, vaultEntry);
                Assert.IsTrue(entry.Executable,
                    $"wall={wallDistance:F2} 在推導出的範圍內卻被 {entry.RejectReason} 擋下");
                Assert.IsTrue(
                    TraversalPlanBuilder.TryBuild(
                        bake: vault, candidate: in candidate, entry: in entry,
                        ledgeSafetyMargin: settings.LedgeEndMargin,
                        maxHorizontalCorrection: 1.5f, maxVerticalCorrection: 0.75f,
                        plan: out TraversalWarpPlan plan,
                        rejection: out TraversalWarpPlanRejection rejection),
                    $"wall={wallDistance:F2}: Vault 在自己的合法範圍內仍建不起 plan（{rejection}）");
                Assert.IsTrue(plan.IsPiecewise);
            }
        }

        /// <summary>薄牆 vault：落點在牆的另一側地面，不是牆頂。</summary>
        private static TraversalCandidate ShippedVaultCandidate(float wallDistance) =>
            ShippedVaultCandidate(wallDistance, 0.45f, 1f);

        /// <summary>
        /// 薄牆 vault，可指定落腳面深度——用來驗證「終點由動畫自己的落點決定」。
        /// </summary>
        private static TraversalCandidate ShippedVaultCandidate(
            float wallDistance, float landingDepth, float ledgeHeight)
        {
            Vector3 forward = Vector3.forward;
            Vector3 wallNormal = -forward;
            Vector3 edge = new Vector3(0f, ledgeHeight, 0f);
            Vector3 destination = new Vector3(0f, 0f, landingDepth);
            var ledge = new TraversalLedgeFrame(
                edge, Vector3.right, wallNormal, Vector3.up, -1f, 1f);
            TraversalCorridorEvidence corridor = TraversalCorridorEvidence.Clear(
                edge - forward * 0.2f, edge + Vector3.up * 0.05f, destination);
            return new TraversalCandidate(
                TraversalKind.Vault1m,
                TraversalRejectReason.None,
                new Vector3(0f, ledgeHeight * 0.5f, 0f),
                wallNormal,
                edge,
                Vector3.up,
                ledgeHeight,
                0.5f,
                destination,
                forward,
                ledge,
                corridor,
                new Vector3(0f, 0f, -wallDistance),
                forward,
                TraversalDetectionDirectionSource.Facing,
                wallDistance - 0.12f + 0.03f,
                landingDepth);
        }

        /// <summary>
        /// ⭐ 2026-09-13 Playtest：「上去的瞬間手指插進牆面」。
        ///
        /// grip edge 對齊真實邊緣之後，**四根壓在頂面上的手指指尖必須在頂面之上**。
        /// 這條同時守住兩個曾經出錯的東西：
        /// <list type="number">
        /// <item>grip edge 的 Y 基準（曾取「動畫自己的障礙物頂面」，但指尖其實騎在它上下各 1 cm）。</item>
        /// <item><b>推導用的 rig 必須是執行期的 rig</b>——`X Bot` 與 `Y Bot` 的指尖差 3.7 cm，
        ///   用錯 rig 烘出來的 grip edge 在遊戲裡一定偏。</item>
        /// </list>
        /// ⚠️ 拇指不列入：它按的是牆面，實測比頂面低約 9 cm，本來就該在下面。
        /// </summary>
        [Test]
        public void B14_GripEdgeAlignment_KeepsFingertipsAboveTheTopFace()
        {
            // ⚠️ 必須是**玩家**的 rig。場景裡有兩個角色：`X Bot`（PlayerInputSource ＋ TraversalProbe）
            //    與 `Y Bot`（AIInputSource，敵人）。兩者 Avatar 不同，同一格指尖差 3.7 cm ——
            //    用錯 rig 烘出來的 grip edge 在遊戲裡一定偏。下面用「有沒有 TraversalProbe」
            //    把這件事釘死，而不是只靠一個檔名字串。
            const string RuntimeRigPath = "Assets/Prefabs/X Bot.prefab";
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeRigPath);
            Assert.IsNotNull(rig, $"找不到執行期 rig：{RuntimeRigPath}");
            Assert.IsNotNull(rig.GetComponentInChildren<Project.Core.Environment.TraversalProbe>(true),
                $"{RuntimeRigPath} 沒有 TraversalProbe ⇒ 它不是會做 traversal 的角色，" +
                "grip edge 不該用它推導");

            foreach (string path in new[] { Climb2mPath, VaultPath })
            {
                MotionBakeData bake = Load(path);
                TraversalMotionBakeBlock block = bake.Traversal;
                Assert.IsTrue(block.HasValidBakedData, $"{path}: 缺 Traversal block");
                Assert.IsTrue(block.HasGripEdges, $"{path}: 缺 grip edge");

                AnimationClip clip = bake.SourceClip;
                Assert.IsNotNull(clip, $"{path}: 缺 SourceClip");

                bool usesLeft = block.UsesLeftHand;
                Vector3 grip = usesLeft ? block.LeftGripEdgeInRoot : block.RightGripEdgeInRoot;
                float contact = usesLeft
                    ? block.LeftHandContactNormalizedTime
                    : block.RightHandContactNormalizedTime;

                UnityEngine.SceneManagement.Scene preview =
                    UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                try
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(rig, preview);
                    Animator animator = instance.GetComponentInChildren<Animator>();
                    Assert.IsNotNull(animator, $"{RuntimeRigPath}: 沒有 Animator");
                    animator.applyRootMotion = true;
                    Transform root = animator.transform;
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    clip.SampleAnimation(animator.gameObject, Mathf.Clamp01(contact) * clip.length);

                    Quaternion inverseYaw =
                        Quaternion.Inverse(Quaternion.Euler(0f, root.rotation.eulerAngles.y, 0f));
                    Vector3 rootPosition = root.position;

                    HumanBodyBones[] fingers = usesLeft
                        ? new[]
                        {
                            HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftMiddleDistal,
                            HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftLittleDistal,
                        }
                        : new[]
                        {
                            HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleDistal,
                            HumanBodyBones.RightRingDistal, HumanBodyBones.RightLittleDistal,
                        };

                    int checkedTips = 0;
                    foreach (HumanBodyBones bone in fingers)
                    {
                        Transform distal = animator.GetBoneTransform(bone);
                        if (distal == null) continue;
                        for (int c = 0; c < distal.childCount; c++)
                        {
                            float tipY = (inverseYaw * (distal.GetChild(c).position - rootPosition)).y;
                            // grip edge 會被對齊到真實頂面 ⇒ 指尖相對頂面的高度就是 tipY − grip.y。
                            float aboveTop = tipY - grip.y;
                            checkedTips++;
                            Assert.Greater(aboveTop, 0f,
                                $"{path} / {bone}: 對齊後指尖在頂面下方 {aboveTop * 100f:F1} cm " +
                                "⇒ 抓握瞬間手指會插進牆／平台轉角");
                        }
                    }

                    Assert.Greater(checkedTips, 0, $"{path}: 沒有量到任何指尖，測試失去意義");
                    Object.DestroyImmediate(instance);
                }
                finally
                {
                    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
                }
            }
        }

        /// <summary>
        /// Playtest（2026-09-14）：「手看起來不像把角色固定在 ledge 上」。
        ///
        /// 量到的事實：IK 權重在 n=0.24 就歸零，而那時 root 距平台頂面還有 **0.485 m**——
        /// 「手撐起身體」的整個後半段沒有任何錨定。
        ///
        /// 根因是**錨點被當成一個點**：固定腕點在推起階段會超過手臂長度（reach ratio 實測到 1.26），
        /// 所以只能提前放手。但真實的 mantle 是手掌**沿頂面滾動**，接觸點留在邊緣、手腕會動。
        ///
        /// 現在超伸改由執行期的 anchor blend 處理（`TraversalHandIKController` 依 reach ratio
        /// 把固定錨點平滑讓位給「貼在已驗證邊緣面上的動畫手腕」），因此**窗必須覆蓋整個抓握期**。
        /// 本測試守的就是這一點；貼面約束本身由 `TraversalWarpPlanTests` 的 `SURF*` 守。
        /// </summary>
        [Test]
        public void B15_Climb2m_HandIK_CoversTheWholeMeasuredGrip()
        {
            const string RuntimeRigPath = "Assets/Prefabs/X Bot.prefab";
            MotionBakeData bake = Load(Climb2mPath);
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeRigPath);
            Assert.IsNotNull(rig, $"找不到執行期 rig：{RuntimeRigPath}");

            Project.Editor.Stages.TraversalContactDerivation.Measurement measurement =
                Project.Editor.Stages.TraversalContactDerivation.Measure(bake, rig);
            Assert.IsTrue(measurement.IsValid, measurement.Report);
            Assert.AreEqual(TraversalHandContactMode.BothHands, measurement.ContactMode,
                "本測試只守雙手支撐的 Climb2m，不得把已驗收的單手 Vault window 一起改掉");

            AssertHandWindowCoversMeasuredGrip(
                bake, rig, true, in measurement.Left, bake.Traversal.LeftHandIKWindow);
            AssertHandWindowCoversMeasuredGrip(
                bake, rig, false, in measurement.Right, bake.Traversal.RightHandIKWindow);
        }

        private static void AssertHandWindowCoversMeasuredGrip(
            MotionBakeData bake,
            GameObject rig,
            bool left,
            in Project.Editor.Stages.TraversalContactDerivation.HandPlant plant,
            TraversalHandIKWindow window)
        {
            string side = left ? "Left" : "Right";
            Assert.IsTrue(plant.HasFixedGoalExtensionStart,
                $"{side}: Climb2m 前提改變——plant 期間固定腕點不再拉長肩—腕鏈，請重檢本測試");
            Assert.Less(plant.FixedGoalExtensionStartNormalizedTime, plant.EndNormalizedTime,
                $"{side}: extension 事件已不在 plant 內");

            float fadeSpan = window.FullNormalizedTime - window.StartNormalizedTime;
            float expectedRelease = Mathf.Max(
                window.FullNormalizedTime,
                plant.FixedGoalExtensionStartNormalizedTime - fadeSpan);
            Assert.AreEqual(expectedRelease, window.ReleaseNormalizedTime, 1e-4f,
                $"{side}: production bake 沒有使用 X Bot 的 kinematic release 重推");
            Assert.AreEqual(
                0f,
                window.Evaluate(
                    plant.FixedGoalExtensionStartNormalizedTime,
                    bake.Traversal.ExitNormalizedTime),
                1e-4f,
                $"{side}: 固定目標開始拉長手臂時 IK 仍有權重");

            UnityEngine.SceneManagement.Scene preview =
                UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(rig, preview);
                Animator animator = instance.GetComponentInChildren<Animator>();
                Assert.IsNotNull(animator);
                Assert.IsTrue(animator.isHuman);
                animator.applyRootMotion = true;

                HumanBodyBones handBone =
                    left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                HumanBodyBones upperArmBone =
                    left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                Transform hand = animator.GetBoneTransform(handBone);
                Transform upperArm = animator.GetBoneTransform(upperArmBone);
                Assert.IsNotNull(hand);
                Assert.IsNotNull(upperArm);

                Transform root = animator.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                bake.SourceClip.SampleAnimation(
                    animator.gameObject,
                    plant.StartNormalizedTime * bake.SourceClip.length);
                Vector3 fixedContactWrist = hand.position;

                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                bake.SourceClip.SampleAnimation(
                    animator.gameObject,
                    plant.FixedGoalExtensionStartNormalizedTime * bake.SourceClip.length);
                float fixedGoalDistance = Vector3.Distance(upperArm.position, fixedContactWrist);
                float authoredDistance = Vector3.Distance(upperArm.position, hand.position);
                Assert.Greater(fixedGoalDistance, authoredDistance,
                    $"{side}: extension marker 並沒有要求更長的肩—腕距離");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [Test]
        public void B12_NaturalExitDepth_IsMeasuredAtTheExitMarker_NotAtClipEnd()
        {
            // ⭐ 2026-09-13（docs/24 §11）：這個值唯一的消費者是 warp plan 的 **Exit knot**，
            //    而那個 knot 在 Traversal.ExitNormalizedTime 觸發。量在 clip 結尾等於拿
            //    「動畫最後會走到哪」去要求「動畫在 exit 當下就要到那裡」。
            //    Vault1m：結尾 3.259 m ⇒ 舊值 1.857 m，但 exit(0.460) 當下只到 2.519 m ⇒ 實際 1.116 m。
            //    差的 0.74 m 全部變成 exit correction 把角色往前扯。
            MotionBakeData vault = Load(VaultPath);
            Assert.IsTrue(vault.TryGetDesiredEntryDistance(out float entryDistance));
            Assert.IsTrue(vault.TryGetNaturalExitDepth(out float depth));

            float exitNormalized = vault.Traversal.ExitNormalizedTime;
            float atExit = vault.GetHorizontalDisplacementAt(exitNormalized * vault.Duration) - entryDistance;
            float atEnd = vault.GetHorizontalDisplacementAt(vault.Duration) - entryDistance;

            Assert.AreEqual(atExit, depth, 1e-3f, "自然落點深度必須量在 Exit marker 當下");
            Assert.Less(depth, atEnd - 0.3f,
                "Vault1m 的 exit 之後還有一大段 run-out —— 兩種量法必須明顯不同，" +
                "否則代表這個回歸失去鑑別力（clip 或 marker 被換過，請重新檢查）");

            // Climb2m 的 exit 之後是靜止站姿，兩種量法同值：修正不得改變它的行為。
            MotionBakeData climb = Load(Climb2mPath);
            Assert.IsTrue(climb.TryGetDesiredEntryDistance(out float climbEntry));
            Assert.IsTrue(climb.TryGetNaturalExitDepth(out float climbDepth));
            Assert.AreEqual(
                climb.GetHorizontalDisplacementAt(climb.Duration) - climbEntry,
                climbDepth, 1e-3f,
                "Climb2m 在 exit 之後不再位移，修正前後應同值");
        }

        [Test]
        public void B13_IdealStance_NeedsAlmostNoHorizontalCorrection()
        {
            // docs/24 §10 的驗收條件：**尺寸差異不再主要靠加大 horizontal correction 解決。**
            // 站在推導出的理想距離、落腳面也容得下動畫的自然落點時，
            // contact 與 exit 的**水平** correction 都必須是公分級。
            // （垂直 correction 不在此列——那是障礙物高度與動畫高度的差，屬 clip selection 的範圍。）
            MotionBakeData vault = Load(VaultPath);
            Assert.IsTrue(vault.TryGetDesiredEntryDistance(out float ideal));
            Assert.IsTrue(vault.TryGetNaturalExitDepth(out float naturalDepth));

            TraversalCandidate candidate = ShippedVaultCandidate(ideal, naturalDepth, ledgeHeight: 1f);
            var settings = TraversalEntryPolicySettings.Default;
            TraversalEntryEvaluation entry = TraversalEntryPolicy.Evaluate(
                in candidate, in settings, ideal);
            Assert.IsTrue(entry.Executable, $"理想站位被 {entry.RejectReason} 擋下");

            Assert.IsTrue(
                TraversalPlanBuilder.TryBuild(
                    vault, in candidate, in entry, settings.LedgeEndMargin, 1.5f, 0.75f,
                    out TraversalWarpPlan plan, out TraversalWarpPlanRejection rejection),
                $"理想站位仍建不起 piecewise（{rejection}）");

            float contactHorizontal = Vector3.ProjectOnPlane(
                plan.LeftContactKnot.PositionCorrection, Vector3.up).magnitude;
            float exitHorizontal = Vector3.ProjectOnPlane(
                plan.ExitKnot.PositionCorrection, Vector3.up).magnitude;

            Assert.Less(contactHorizontal, 0.05f,
                $"理想站位的接觸水平修正 {contactHorizontal * 100f:F1} cm —— 進場距離的基準又不一致了");
            Assert.Less(exitHorizontal, 0.05f,
                $"理想站位的離開水平修正 {exitHorizontal * 100f:F1} cm —— " +
                "終點又回到探測常數而不是動畫自己的落點");
        }

        [Test]
        public void B8_DefaultBinding_DoesNotClaimAnAuthoredWarpEnd()
        {
            // ⭐ 2026-09-13 live-Editor 實測抓到的回歸：struct 的欄位初始式本來就把
            //    warpEndNormalizedTime 設成 0.8，而正式 asset 沒有序列化該欄位 ⇒ 值仍是 0.8。
            //    任何「值 > 0 就算 author 過」的判斷都會恆為 true，
            //    讓 bake 推導出的 correction 窗完全不會被使用（畫面顯示 window=0.000-0.800 derived=False）。
            var defaultBinding = new TraversalMotionBinding("Climb2m", null);
            Assert.IsFalse(defaultBinding.HasAuthoredWarpEnd,
                "沒有顯式指定 warp end 的 binding 不得被當成 authored，否則 bake 推導路徑是死的");
            Assert.AreEqual(0.8f, defaultBinding.WarpEndNormalizedTime, 0.0001f,
                "退化值本身仍然保留，只是不再冒充 authored");

            var authoredBinding = new TraversalMotionBinding(
                "Climb2m", null, 0.1f, 0.7f, 0.9f, 2f, 2f);
            Assert.IsTrue(authoredBinding.HasAuthoredWarpEnd,
                "顯式指定窗的 binding 必須維持 authored（既有 PlayMode rig 依賴此行為）");
            Assert.AreEqual(0.7f, authoredBinding.WarpEndNormalizedTime, 0.0001f);
        }

        /// <summary>
        /// 依實際場景幾何組出候選：牆面在原點、玩家在其後 <paramref name="wallDistance"/> 公尺。
        /// ⚠️ capsule clearance 必須與真實 CharacterController 一致（radius 0.12／skin 0.03）——
        /// 隨手填一個值會讓 <c>ClampOutsideWall</c> 用不成立的限制把 contact solve 夾掉，
        /// 測出來的殘差會憑空變大（2026-09-13 踩過）。
        /// </summary>
        private static TraversalCandidate ShippedCandidate(
            TraversalKind kind, float ledgeHeight, float wallDistance, float depth)
        {
            Vector3 forward = Vector3.forward;
            Vector3 wallNormal = -forward;
            Vector3 root = new Vector3(0f, 0f, -wallDistance);
            Vector3 edge = new Vector3(0f, ledgeHeight, 0f);
            Vector3 destination = new Vector3(0f, ledgeHeight, depth);
            var ledge = new TraversalLedgeFrame(
                edge, Vector3.right, wallNormal, Vector3.up, -1f, 1f);
            TraversalCorridorEvidence corridor = TraversalCorridorEvidence.Clear(
                edge - forward * 0.2f, edge + Vector3.up * 0.05f, destination);
            return new TraversalCandidate(
                kind,
                TraversalRejectReason.None,
                new Vector3(0f, ledgeHeight * 0.5f, 0f),
                wallNormal,
                edge,
                Vector3.up,
                ledgeHeight,
                depth,
                destination,
                forward,
                ledge,
                corridor,
                root,
                forward,
                TraversalDetectionDirectionSource.Facing,
                wallDistance - 0.12f + 0.03f);
        }
    }
}
