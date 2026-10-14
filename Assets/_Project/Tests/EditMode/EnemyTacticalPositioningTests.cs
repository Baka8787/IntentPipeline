using NUnit.Framework;
using Project.Core.Movement;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-10-14，`docs/56` §12.3，P5a）<see cref="EnemyTacticalPositioning"/> 的 A1（目標不可達）規則——
    /// 純邏輯，路徑以假的查詢提供。真 NavMesh 上的行為由 <c>EnemyUnreachableTargetPlayModeTests</c> 驗。
    /// </summary>
    public sealed class EnemyTacticalPositioningTests
    {
        private const float Reach = 2f;
        private static readonly Vector3 Target = new Vector3(0f, 0f, 6f);
        private static readonly Vector3 Closest = new Vector3(0f, 0f, 2.7f);   // 圍欄外最近可達點：離目標 3.3 m

        private sealed class FakePathQuery : IEnemyPathQuery
        {
            public bool Succeeds = true;
            public bool Complete;
            public Vector3 End = Closest;
            public int Calls;

            public bool TryCalculatePathEnd(Vector3 from, Vector3 to, float radius, out Vector3 end, out bool complete)
            {
                Calls++;
                end = End;
                complete = Complete;
                return Succeeds;
            }
        }

        private static EnemyPathSnapshot PartialToTarget(Vector3 end) =>
            new EnemyPathSnapshot { Valid = true, Destination = Target, Partial = true, End = end };

        [Test]
        public void Entry_PartialPathWhoseEndIsOutOfReach_HoldsAtTheClosestReachablePoint()
        {
            var p = new EnemyTacticalPositioning();
            TacticalGoal g = p.Tick(new Vector3(0f, 0f, -1f), Target, Reach, 0f, PartialToTarget(Closest), new FakePathQuery());
            Assert.IsTrue(g.Active);
            Assert.AreEqual(TacticalSituation.Unreachable, g.Situation);
            Assert.AreEqual(Closest, g.Point, "站位點＝部分路徑終點（Unity 算出的最接近可達點）");
            Assert.IsFalse(g.Arrived);
        }

        [Test]
        public void NoEntry_WhenPathIsComplete_OrEndIsWithinReach_OrPathIsToSomethingElse()
        {
            var q = new FakePathQuery();
            Assert.IsFalse(new EnemyTacticalPositioning().Tick(Vector3.zero, Target, Reach, 0f,
                new EnemyPathSnapshot { Valid = true, Destination = Target, Partial = false, End = Target }, q).Active, "完整路徑");
            Assert.IsFalse(new EnemyTacticalPositioning().Tick(Vector3.zero, Target, Reach, 0f,
                PartialToTarget(Target - new Vector3(0f, 0f, 0.4f)), q).Active, "玩家貼牆（終點在攻擊距離內）不算不可達");
            Assert.IsFalse(new EnemyTacticalPositioning().Tick(Vector3.zero, Target, Reach, 0f,
                new EnemyPathSnapshot { Valid = true, Destination = new Vector3(9f, 0f, 9f), Partial = true, End = Closest }, q).Active,
                "這條路徑不是通往目標的（例如還在走巡邏點）");
            Assert.IsFalse(new EnemyTacticalPositioning().Tick(Vector3.zero, Target, Reach, 0f, default, q).Active, "沒有可讀的路徑");
            Assert.AreEqual(0, q.Calls, "進入判定只讀 agent 快照，不額外查詢——一般打鬥沒有成本");
        }

        [Test]
        public void Arrival_UsesHysteresis_AndHoldsWithoutMoving()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakePathQuery();
            p.Tick(Vector3.zero, Target, Reach, 0f, PartialToTarget(Closest), q);
            Assert.IsTrue(p.Tick(Closest + new Vector3(0.3f, 0f, 0f), Target, Reach, 0.1f, default, q).Arrived, "≤ 0.35 m ⇒ 抵達");
            Assert.IsTrue(p.Tick(Closest + new Vector3(0.6f, 0f, 0f), Target, Reach, 0.2f, default, q).Arrived, "0.35–0.7 m 仍算抵達（遲滯）");
            Assert.IsFalse(p.Tick(Closest + new Vector3(0.8f, 0f, 0f), Target, Reach, 0.3f, default, q).Arrived, "> 0.7 m 才離開抵達");
        }

        [Test]
        public void Exit_WhenTheTargetBecomesReachable()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakePathQuery();
            p.Tick(Vector3.zero, Target, Reach, 0f, PartialToTarget(Closest), q);
            q.Complete = true;
            Assert.IsTrue(p.Tick(Vector3.zero, Target, Reach, EnemyTacticalPositioning.ExitCheckInterval * 0.5f, default, q).Active,
                "檢查間隔內不查詢");
            Assert.IsFalse(p.Tick(Vector3.zero, Target, Reach, EnemyTacticalPositioning.ExitCheckInterval + 0.01f, default, q).Active,
                "路徑完整 ⇒ 交還現行行為");
            Assert.AreEqual(TacticalSituation.None, p.Situation);
        }

        [Test]
        public void QueryFailure_KeepsHolding_NeverFallsBackToPushing()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakePathQuery();
            p.Tick(Vector3.zero, Target, Reach, 0f, PartialToTarget(Closest), q);
            q.Succeeds = false;
            Assert.IsTrue(p.Tick(Vector3.zero, Target, Reach, 5f, default, q).Active, "無法證明目標可達 ⇒ 維持待命");
        }

        [Test]
        public void TargetMovesFar_PicksTheNewClosestPoint_OnlyIfItMovedEnough()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakePathQuery();
            p.Tick(Vector3.zero, Target, Reach, 0f, PartialToTarget(Closest), q);

            Vector3 jitter = Target + new Vector3(1.0f, 0f, 0f);   // < 1.5 m：不重算
            q.End = Closest + new Vector3(3f, 0f, 0f);
            Assert.AreEqual(Closest, p.Tick(Vector3.zero, jitter, Reach, 1f, default, q).Point, "目標小幅移動 ⇒ 不換點");

            Vector3 moved = Target + new Vector3(2f, 0f, 0f);       // > 1.5 m：重算
            q.End = Closest + new Vector3(0.5f, 0f, 0f);            // 新點只差 0.5 m（< 1.0）⇒ 不換
            Assert.AreEqual(Closest, p.Tick(Vector3.zero, moved, Reach, 2f, default, q).Point, "新點差不多 ⇒ 不換（防抖）");

            Vector3 movedMore = moved + new Vector3(2f, 0f, 0f);
            q.End = Closest + new Vector3(3f, 0f, 0f);
            Assert.AreEqual(q.End, p.Tick(Vector3.zero, movedMore, Reach, 3f, default, q).Point, "新點差很多 ⇒ 換點");
        }

        [Test]
        public void Reset_ClearsTheCommitment()
        {
            var p = new EnemyTacticalPositioning();
            p.Tick(Vector3.zero, Target, Reach, 0f, PartialToTarget(Closest), new FakePathQuery());
            p.Reset();
            Assert.AreEqual(TacticalSituation.None, p.Situation, "脫戰／死亡 ⇒ 清除");
            Assert.IsFalse(p.Tick(Vector3.zero, Target, Reach, 1f, default, new FakePathQuery()).Active, "清除後需要重新滿足進入條件");
        }
    }
}
