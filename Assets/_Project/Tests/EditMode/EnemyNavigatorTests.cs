using NUnit.Framework;
using Project.Core.Movement;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-10-13，`docs/55` P0）<see cref="EnemyNavigator"/> 的規則——全部是純函數，不需要 NavMesh。
    /// 真 NavMesh 上的行為由 <c>EnemyChasePlayModeTests</c> 驗。
    /// </summary>
    public sealed class EnemyNavigatorTests
    {
        private static readonly EnemyNavigationSettings Settings = EnemyNavigationSettings.Default;

        // ── P0.2 路徑請求節流 ───────────────────────────────────────────

        [Test]
        public void P02_FirstRequest_IsAlwaysSent()
        {
            Assert.IsTrue(EnemyNavigator.ShouldRequestPath(false, default, Vector3.one, 0f, 0f, false, Settings));
        }

        [Test]
        public void P02_StaticDestination_IsNeverRequestedAgain()
        {
            Vector3 destination = new Vector3(3f, 0f, 4f);
            for (float t = 0.1f; t < 10f; t += 0.1f)
            {
                Assert.IsFalse(EnemyNavigator.ShouldRequestPath(true, destination, destination, 0f, t, false, Settings),
                    $"目的地不動、路徑有效 ⇒ t={t:F1} 不得重新請求（舊版每帧一次）");
            }
        }

        [Test]
        public void P02_MovingDestination_RespectsDistanceAndInterval()
        {
            Vector3 last = Vector3.zero;
            Vector3 small = new Vector3(Settings.RepathDistance * 0.9f, 0f, 0f);
            Vector3 large = new Vector3(Settings.RepathDistance * 1.1f, 0f, 0f);
            float afterInterval = Settings.RepathInterval + 0.01f;
            float beforeInterval = Settings.RepathInterval * 0.5f;

            Assert.IsFalse(EnemyNavigator.ShouldRequestPath(true, last, small, 0f, afterInterval, false, Settings),
                "移動小於 RepathDistance ⇒ 不請求");
            Assert.IsFalse(EnemyNavigator.ShouldRequestPath(true, last, large, 0f, beforeInterval, false, Settings),
                "移動夠多但未過 RepathInterval ⇒ 不請求（避免每帧請求）");
            Assert.IsTrue(EnemyNavigator.ShouldRequestPath(true, last, large, 0f, afterInterval, false, Settings),
                "移動夠多且過了間隔 ⇒ 請求");
        }

        [Test]
        public void P02_InvalidPath_IsRetried_ButThrottled()
        {
            Vector3 destination = Vector3.one;
            Assert.IsFalse(EnemyNavigator.ShouldRequestPath(true, destination, destination, 0f, Settings.RepathInterval * 0.5f, true, Settings),
                "路徑失效的重試也受間隔限制");
            Assert.IsTrue(EnemyNavigator.ShouldRequestPath(true, destination, destination, 0f, Settings.RepathInterval + 0.01f, true, Settings),
                "路徑失效且過了間隔 ⇒ 重試（即使目的地沒動）");
        }

        // ── P0.3 路徑狀態判讀 ───────────────────────────────────────────

        [Test]
        public void P03_PathFollow_MatchesLegacyAndHandlesPartialPathEnd()
        {
            Assert.AreEqual(PathFollow.Wait,
                EnemyNavigator.ResolvePathFollow(true, false, NavMeshPathStatus.PathComplete, 5f, Settings),
                "還在算 ⇒ 等（與舊版相同）");
            Assert.AreEqual(PathFollow.DirectToDestination,
                EnemyNavigator.ResolvePathFollow(false, false, NavMeshPathStatus.PathComplete, 5f, Settings),
                "沒有路徑 ⇒ 直接朝目的地（與舊版相同）");
            Assert.AreEqual(PathFollow.DirectToDestination,
                EnemyNavigator.ResolvePathFollow(false, true, NavMeshPathStatus.PathInvalid, 5f, Settings),
                "路徑無效 ⇒ 直接朝目的地（與舊版相同）");
            Assert.AreEqual(PathFollow.FollowCorner,
                EnemyNavigator.ResolvePathFollow(false, true, NavMeshPathStatus.PathComplete, 0.1f, Settings),
                "完整路徑 ⇒ 沿轉角（與舊版相同）");
            Assert.AreEqual(PathFollow.FollowCorner,
                EnemyNavigator.ResolvePathFollow(false, true, NavMeshPathStatus.PathPartial, Settings.PartialPathArriveDistance + 0.1f, Settings),
                "部分路徑、還沒走到盡頭 ⇒ 先沿路徑走");
            Assert.AreEqual(PathFollow.DirectToDestination,
                EnemyNavigator.ResolvePathFollow(false, true, NavMeshPathStatus.PathPartial, Settings.PartialPathArriveDistance - 0.1f, Settings),
                "🆕 部分路徑走到盡頭 ⇒ 改直接朝目的地，不在部分終點停住（G4）");
        }

        // ── P0.4 卡住偵測（只回報） ───────────────────────────────────────

        // 🔄（2026-10-14，ADR-012 AC2）卡住＝「有移動請求、實際被允許移動、但沒有有效進展」。
        //    舊版只看「上一帧有沒有意圖」，出拳／受擊期間（意圖照發、但根本不執行）會被誤判卡住——P5-0 量到 27–156 帧。

        private const float Dt = 1f / 60f;
        private const float WalkStep = 1.64f * Dt;   // walk 一帧要求的水平位移

        private static int FramesForWindow => Mathf.CeilToInt(Settings.StuckWindowSeconds / Dt) + 1;

        [Test]
        public void P04_AllowedToMove_NoProgress_IsStuck()
        {
            var w = new StuckWindow();
            bool stuck = false;
            for (int i = 0; i < FramesForWindow; i++)
                stuck = EnemyNavigator.UpdateStuck(ref w, true, WalkStep, 0f, Dt, Settings);
            Assert.IsTrue(stuck, "被允許照意圖移動一整個窗、實際沒有前進 ⇒ 卡住");
        }

        [Test]
        public void P04_NotAllowedToMove_NeitherAccumulatesNorReports()
        {
            // 出拳 0.8 s、受擊 0.72 s：上一帧沒有執行基礎移動（報告過期）。
            var w = new StuckWindow();
            for (int i = 0; i < FramesForWindow * 3; i++)
                Assert.IsFalse(EnemyNavigator.UpdateStuck(ref w, false, WalkStep, 0f, Dt, Settings),
                    "攻擊／受擊／Traversal 期間不得回報卡住");
            Assert.AreEqual(0f, w.ExecutedSeconds, "不被允許移動的帧不得累積");
        }

        [Test]
        public void P04_WindowSurvivesAnInterruption_AndContinuesAfterIt()
        {
            var w = new StuckWindow();
            int half = FramesForWindow / 2;
            for (int i = 0; i < half; i++) EnemyNavigator.UpdateStuck(ref w, true, WalkStep, 0f, Dt, Settings);
            for (int i = 0; i < 48; i++) Assert.IsFalse(EnemyNavigator.UpdateStuck(ref w, false, WalkStep, 0f, Dt, Settings));
            bool stuck = false;
            for (int i = 0; i < FramesForWindow - half; i++) stuck = EnemyNavigator.UpdateStuck(ref w, true, WalkStep, 0f, Dt, Settings);
            Assert.IsTrue(stuck, "推牆—出拳—繼續推牆：被允許移動的時間合計滿一個窗就判定（中斷不清空）");
        }

        [Test]
        public void P04_Progress_IsNeverStuck()
        {
            var w = new StuckWindow();
            for (int i = 0; i < FramesForWindow * 4; i++)
                Assert.IsFalse(EnemyNavigator.UpdateStuck(ref w, true, WalkStep, WalkStep * 0.9f, Dt, Settings), "有進展 ⇒ 永遠不卡住");
        }

        [Test]
        public void P04_NegligibleRequest_ResetsTheWindow_StandingStillIsNotStuck()
        {
            var w = new StuckWindow();
            for (int i = 0; i < FramesForWindow - 2; i++) EnemyNavigator.UpdateStuck(ref w, true, WalkStep, 0f, Dt, Settings);
            Assert.IsFalse(EnemyNavigator.UpdateStuck(ref w, true, 0f, 0f, Dt, Settings), "被允許移動但沒有請求（停下來）");
            Assert.AreEqual(0f, w.ExecutedSeconds, "停下來重置窗");
            for (int i = 0; i < FramesForWindow * 2; i++)
                Assert.IsFalse(EnemyNavigator.UpdateStuck(ref w, true, 0f, 0f, Dt, Settings), "一直站著不是卡住");
        }

        [Test]
        public void P04_PartialProgress_UsesTheRatio()
        {
            var slow = new StuckWindow();
            var fine = new StuckWindow();
            bool slowStuck = false, fineStuck = false;
            for (int i = 0; i < FramesForWindow; i++)
            {
                slowStuck = EnemyNavigator.UpdateStuck(ref slow, true, WalkStep, WalkStep * (EnemyNavigator.StuckProgressRatio * 0.5f), Dt, Settings);
                fineStuck = EnemyNavigator.UpdateStuck(ref fine, true, WalkStep, WalkStep * (EnemyNavigator.StuckProgressRatio * 2f), Dt, Settings);
            }
            Assert.IsTrue(slowStuck, "實際位移遠低於要求（被擋住只剩滑行）⇒ 卡住");
            Assert.IsFalse(fineStuck, "實際位移達到要求的一定比例 ⇒ 不卡住");
        }

        [Test]
        public void P04_StaleReport_MeansNotExecuted()
        {
            var report = new Project.Core.Blackboard.MotionExecutionData
                { Frame = 100, RequestedHorizontalDistance = WalkStep, AchievedHorizontalDistance = 0f };
            Assert.IsTrue(report.DescribesFrame(100));
            Assert.IsFalse(report.DescribesFrame(101), "沒有呼叫 ExecuteBaseMovement 的帧留下過期報告 ⇒ 視為未執行（ADR-012 D1）");
        }

        // ── 沒有 agent（EditMode／離網）時的安全退化 ─────────────────────

        [Test]
        public void NoAgent_FallsBackToDestination_AndNeverCountsRequests()
        {
            var navigator = new EnemyNavigator(Settings);
            navigator.Bind(null, Settings);
            navigator.Sync(Vector3.zero);
            navigator.RequestDestination(Vector3.forward * 5f, 0f);

            Assert.IsFalse(navigator.IsOnNavMesh);
            Assert.AreEqual(0, navigator.PathRequestCount);
            Assert.IsTrue(navigator.TryResolveSteeringPoint(Vector3.zero, Vector3.forward * 5f, out Vector3 point));
            Assert.AreEqual(Vector3.forward * 5f, point);
        }

        [Test]
        public void Steering_ReturnsHorizontalUnitDirection_OrFalseWhenDegenerate()
        {
            Assert.IsTrue(EnemySteering.TryResolveDirection(Vector3.zero, new Vector3(3f, 7f, 4f), out Vector3 direction));
            Assert.AreEqual(0f, direction.y);
            Assert.AreEqual(1f, direction.magnitude, 1e-5f);
            Assert.IsFalse(EnemySteering.TryResolveDirection(Vector3.zero, new Vector3(0f, 3f, 0f), out _),
                "正上方（水平零向量）沒有方向");
        }
    }
}
