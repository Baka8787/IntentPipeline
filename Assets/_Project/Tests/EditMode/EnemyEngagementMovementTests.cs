using NUnit.Framework;
using Project.Core.Movement;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-09-15）敵人交戰**移動決策**的確定性測試。
    ///
    /// <para><b>這個檔案存在的理由是一個具體的缺陷</b></para>
    /// 2026-09-15 錄影：敵人在兩片牆的縫旁纏鬥後，**連續 11 秒零位移**，
    /// 期間玩家跑遠也不追。根因是兩個形狀相同的洞：
    /// <list type="number">
    /// <item>側移方向是**純幾何切線**、完全不認識 NavMesh，而側移是**穩態**模式
    ///       （交戰帶很窄，打鬥期間幾乎全程待在裡面）⇒ 敵人會把自己側移出網。</item>
    /// <item>離網之後 <c>Hold; return;</c>，而全 repo **沒有任何一行**會把 agent 放回去
    ///       ⇒ <c>Hold</c> 是沒有出口的終態。</item>
    /// </list>
    ///
    /// <para><b>為什麼測得到</b></para>
    /// NavMesh 查詢在 EditMode 不存在，所以決策邏輯被刻意切成**純函數**、由呼叫端餵入查詢結果
    /// （<see cref="AIMovementSource.TryResolveStrafeDirection"/>／
    /// <see cref="AIMovementSource.TryResolveRecoveryDirection"/>）。
    /// 這裡測的是「拿到這些環境事實之後該怎麼決定」，**不需要場景、不需要 Play**。
    /// </summary>
    public sealed class EnemyEngagementMovementTests
    {
        private const float Epsilon = 1e-4f;

        private static readonly Vector3 ToTarget = new(0f, 0f, 4f); // 目標在正前方

        // ── 側移可走性 ────────────────────────────────────────────────────────────

        [Test]
        public void EM1_PreferredSideWalkable_KeepsPreferredSign()
        {
            bool resolved = AIMovementSource.TryResolveStrafeDirection(
                ToTarget, preferredSign: 1, preferredWalkable: true, oppositeWalkable: false,
                out Vector3 direction, out int sign);

            Assert.IsTrue(resolved);
            Assert.AreEqual(1, sign, "偏好側可走時不得無故換邊——換邊會讓側移看起來在抽搐");
            Assert.AreEqual(AIMovementSource.ResolveStrafeDirection(ToTarget, 1), direction);
        }

        [Test]
        public void EM2_PreferredSideBlocked_FlipsToOppositeSide()
        {
            bool resolved = AIMovementSource.TryResolveStrafeDirection(
                ToTarget, preferredSign: 1, preferredWalkable: false, oppositeWalkable: true,
                out Vector3 direction, out int sign);

            Assert.IsTrue(resolved, "單側被擋不構成停步——另一側還能走就該走另一側");
            Assert.AreEqual(-1, sign);
            Assert.AreEqual(AIMovementSource.ResolveStrafeDirection(ToTarget, -1), direction);
        }

        /// <summary>
        /// ⚠️ 這條守的是**修正本身不得引入新的卡死**：兩側皆不可走時回 false，
        /// 呼叫端停步但**不得改變交戰模式**——下一幀仍由距離推導出 Strafe 並重試，
        /// 玩家一動或牆一讓開就恢復。停步是暫時的，不是終態。
        /// </summary>
        [Test]
        public void EM3_BothSidesBlocked_ReportsNoDirection()
        {
            bool resolved = AIMovementSource.TryResolveStrafeDirection(
                ToTarget, preferredSign: -1, preferredWalkable: false, oppositeWalkable: false,
                out Vector3 direction, out int sign);

            Assert.IsFalse(resolved);
            Assert.AreEqual(Vector3.zero, direction);
            Assert.AreEqual(-1, sign, "解不出方向時不得順手翻轉偏好側，否則下一幀會從被擋的那側重試");
        }

        [Test]
        public void EM4_DegenerateToTarget_ResolvesNothingInsteadOfNaN()
        {
            bool resolved = AIMovementSource.TryResolveStrafeDirection(
                Vector3.zero, preferredSign: 1, preferredWalkable: true, oppositeWalkable: true,
                out Vector3 direction, out _);

            Assert.IsFalse(resolved, "角色與目標水平重疊時沒有可靠切線，必須明確回報解不出");
            Assert.IsFalse(float.IsNaN(direction.x) || float.IsNaN(direction.z),
                "退化輸入不得產生 NaN——NaN 會從 MovementIntent 一路污染到 Transform");
        }

        [Test]
        public void EM5_StrafeDirectionIsPerpendicularAndUnitLength()
        {
            Vector3 cw = AIMovementSource.ResolveStrafeDirection(ToTarget, 1);
            Vector3 ccw = AIMovementSource.ResolveStrafeDirection(ToTarget, -1);

            Assert.AreEqual(0f, Vector3.Dot(cw, ToTarget.normalized), Epsilon, "側移必須垂直於視線");
            Assert.AreEqual(1f, cw.magnitude, Epsilon);
            Assert.AreEqual(-cw, ccw, "兩個正負號必須互為反向，否則『換邊』不是真的換邊");
        }

        // ── 離網自救 ──────────────────────────────────────────────────────────────

        [Test]
        public void EM6_RecoveryPrefersNearestNavMeshSample()
        {
            var self = new Vector3(5f, 0f, 5f);
            var sample = new Vector3(5f, 0f, 3f); // 最近可導航點在正後方

            bool resolved = AIMovementSource.TryResolveRecoveryDirection(
                self, hasNavMeshSample: true, sampledPosition: sample, toTarget: ToTarget,
                out Vector3 direction);

            Assert.IsTrue(resolved);
            Assert.AreEqual(0f, direction.y, "自救方向必須留在水平面");
            Assert.Greater(Vector3.Dot(direction.normalized, Vector3.back), 0.99f,
                "有取樣點時必須朝取樣點，而不是朝目標");
        }

        /// <summary>
        /// 🔴 **這是整組測試裡最重要的一條。**
        /// 取樣失敗時**不得**回退成「不動」——那正是 2026-09-15 那個 11 秒靜止的缺陷本體。
        /// 目標是玩家，依定義站在可走的地方，朝它走至少方向是對的。
        /// </summary>
        [Test]
        public void EM7_RecoveryFallsBackToTarget_WhenNoSampleFound()
        {
            bool resolved = AIMovementSource.TryResolveRecoveryDirection(
                Vector3.zero, hasNavMeshSample: false, sampledPosition: Vector3.zero, toTarget: ToTarget,
                out Vector3 direction);

            Assert.IsTrue(resolved,
                "離網且取樣失敗時仍必須解出方向——回 false 等於恢復那個沒有出口的終態");
            Assert.Greater(Vector3.Dot(direction.normalized, Vector3.forward), 0.99f);
        }

        [Test]
        public void EM8_RecoveryFallsBackToTarget_WhenSampleIsUnderfoot()
        {
            var self = new Vector3(2f, 0f, 2f);

            bool resolved = AIMovementSource.TryResolveRecoveryDirection(
                self, hasNavMeshSample: true, sampledPosition: self, toTarget: ToTarget,
                out Vector3 direction);

            Assert.IsTrue(resolved,
                "取樣點與自身重合時方向退化為零；此時必須改用目標方向，不得當成『已經到了』而停住");
            Assert.Greater(Vector3.Dot(direction.normalized, Vector3.forward), 0.99f);
        }

        [Test]
        public void EM9_RecoveryReportsNothing_WhenEveryCandidateIsDegenerate()
        {
            bool resolved = AIMovementSource.TryResolveRecoveryDirection(
                Vector3.zero, hasNavMeshSample: false, sampledPosition: Vector3.zero,
                toTarget: Vector3.zero, out Vector3 direction);

            Assert.IsFalse(resolved, "取樣與目標都退化時確實無方向可解，此時才允許不寫意圖");
            Assert.AreEqual(Vector3.zero, direction);
        }

        // ── 交戰帶遲滯（既有行為的回歸守門） ──────────────────────────────────────

        [Test]
        public void EM10_EngagementBand_ResolvesStrafeBetweenThresholds()
        {
            Assert.AreEqual(AIMovementSource.EngagementMovement.Retreat,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Hold, 1.0f, 1.25f, 1.6f, 0.15f));
            Assert.AreEqual(AIMovementSource.EngagementMovement.Approach,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Hold, 3.0f, 1.25f, 1.6f, 0.15f));
            Assert.AreEqual(AIMovementSource.EngagementMovement.Strafe,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Hold, 1.4f, 1.25f, 1.6f, 0.15f));
        }

        /// <summary>
        /// Y Bot 實際數值下，側移帶只有 <c>[1.25, 1.6]</c> ＝ **0.35 m 寬**。
        /// 這條把「側移是穩態、Approach 只是過渡」釘成可執行的事實——
        /// 它正是「側移必須諮詢環境」的理由，數值若被調寬到讓側移變成罕見狀態，
        /// 這條會失敗並提醒重新檢視上面那個結論。
        /// </summary>
        [Test]
        public void EM11_StrafeIsTheSteadyState_NotATransient()
        {
            const float minimum = 1.25f;
            const float maximum = 1.6f;
            int strafeSamples = 0;

            // 掃過整個近戰交戰範圍，統計有多少距離會解出側移。
            for (int i = 0; i <= 100; i++)
            {
                float distance = Mathf.Lerp(minimum - 0.3f, maximum + 0.3f, i / 100f);
                if (AIMovementSource.ResolveEngagementMovement(
                        AIMovementSource.EngagementMovement.Strafe,
                        distance, minimum, maximum, 0.15f) == AIMovementSource.EngagementMovement.Strafe)
                {
                    strafeSamples++;
                }
            }

            Assert.Greater(strafeSamples, 0,
                "側移必須是近戰距離內真實存在的穩態；若它從不出現，環境感知的修正就失去對象");
        }
    }
}
