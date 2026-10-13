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

        [Test]
        public void P04_Stuck_OnlyWhenWantingToMoveWithoutProgress()
        {
            bool hasAnchor = false;
            Vector3 anchor = default;
            float anchorTime = 0f;
            Vector3 position = Vector3.zero;

            Assert.IsFalse(Step(position, true, 0f), "第一帧只建立錨點");
            Assert.IsFalse(Step(position, true, Settings.StuckWindowSeconds * 0.5f), "時間窗內不判定");
            Assert.IsTrue(Step(position, true, Settings.StuckWindowSeconds + 0.01f), "想移動卻整個時間窗沒前進 ⇒ 卡住");

            Assert.IsFalse(Step(position, false, 2f), "不想移動 ⇒ 不是卡住（停下來不算）");
            Assert.IsFalse(Step(position, true, 2.1f), "重新想移動 ⇒ 重新計時");

            float t = 2.1f;
            for (int i = 0; i < 30; i++)
            {
                t += 0.1f;
                position += new Vector3(Settings.StuckMinProgress * 1.5f, 0f, 0f);
                Assert.IsFalse(Step(position, true, t), "持續前進 ⇒ 永遠不卡住");
            }

            bool Step(Vector3 p, bool wants, float now) =>
                EnemyNavigator.UpdateStuck(ref hasAnchor, ref anchor, ref anchorTime, p, wants, now, Settings);
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
