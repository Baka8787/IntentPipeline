using System;
using NUnit.Framework;
using Project.Core.Movement;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-10-14，`docs/56` §12.3／§13）<see cref="EnemyTacticalPositioning"/> 的規則——純邏輯，NavMesh 以假的查詢提供。
    /// A1（P5a）：目標不可達 ⇒ 最近可達點待命。站位槽（P5b）：只在失效時換、到位待命、繞行、無有效槽交還現行行為。
    /// 真 NavMesh 上的行為由 <c>EnemyNavigationProgressPlayModeTests</c> 驗。
    /// </summary>
    public sealed class EnemyTacticalPositioningTests
    {
        private const float BandMin = 1.25f;
        private const float Reach = 2f;
        private static readonly Vector3 Target = new Vector3(0f, 0f, 6f);
        private static readonly Vector3 Closest = new Vector3(0f, 0f, 2.7f);   // 圍欄外最近可達點：離目標 3.3 m
        private static float Ideal => EnemyTacticalPositioning.IdealRadius(BandMin, Reach);

        /// <summary>可設定的假 NavMesh：預設是一片開闊平地。</summary>
        private sealed class FakeQuery : IEnemyPathQuery
        {
            public bool PathSucceeds = true;
            public bool PathComplete;
            public Vector3 PathEnd = Closest;
            public int PathCalls;
            public Func<Vector3, bool> Walkable = _ => true;
            public Func<Vector3, Vector3, bool> LineClear = (_, __) => true;
            public Func<Vector3, float> EdgeDistance = _ => 5f;

            public bool TryCalculatePathEnd(Vector3 from, Vector3 to, float radius, out Vector3 end, out bool complete)
            {
                PathCalls++;
                end = PathEnd;
                complete = PathComplete;
                return PathSucceeds;
            }

            public bool TrySampleWalkable(Vector3 point, float radius, out Vector3 walkable)
            {
                walkable = point;
                return Walkable(point);
            }

            public bool IsStraightLineClear(Vector3 from, Vector3 to) => LineClear(from, to);
            public float DistanceToEdge(Vector3 walkable) => EdgeDistance(walkable);

            public bool TryPathLength(Vector3 from, Vector3 to, out float length)
            {
                length = Vector3.Distance(from, to) * 1.5f;   // 假設要繞一點
                return Walkable(to);
            }
        }

        private static EnemyPathSnapshot PartialToTarget(Vector3 end) =>
            new EnemyPathSnapshot { Valid = true, Destination = Target, Partial = true, End = end };

        private static TacticalGoal Tick(EnemyTacticalPositioning p, Vector3 self, Vector3 target, float now,
                                         EnemyPathSnapshot path, IEnemyPathQuery q, bool stuck = false) =>
            p.Tick(self, target, BandMin, Reach, now, path, q, stuck);

        // =====================================================================================================
        // A1（P5a）
        // =====================================================================================================

        [Test]
        public void A1_Entry_PartialPathWhoseEndIsOutOfReach_HoldsAtTheClosestReachablePoint()
        {
            var p = new EnemyTacticalPositioning();
            TacticalGoal g = Tick(p, new Vector3(0f, 0f, -1f), Target, 0f, PartialToTarget(Closest), new FakeQuery());
            Assert.IsTrue(g.Active);
            Assert.AreEqual(TacticalSituation.Unreachable, g.Situation);
            Assert.AreEqual(Closest, g.Point, "站位點＝部分路徑終點（Unity 算出的最接近可達點）");
            Assert.IsFalse(g.Arrived);
        }

        [Test]
        public void A1_NoEntry_WhenPathIsComplete_OrEndIsWithinReach_OrPathIsToSomethingElse()
        {
            var q = new FakeQuery();
            Vector3 far = new Vector3(0f, 0f, -5f);   // 站位區外，避免站位槽介入
            Assert.IsFalse(Tick(new EnemyTacticalPositioning(), far, Target, 0f,
                new EnemyPathSnapshot { Valid = true, Destination = Target, Partial = false, End = Target }, q).Active, "完整路徑");
            Assert.IsFalse(Tick(new EnemyTacticalPositioning(), far, Target, 0f,
                PartialToTarget(Target - new Vector3(0f, 0f, 0.4f)), q).Active, "玩家貼牆（終點在攻擊距離內）不算不可達");
            Assert.IsFalse(Tick(new EnemyTacticalPositioning(), far, Target, 0f,
                new EnemyPathSnapshot { Valid = true, Destination = new Vector3(9f, 0f, 9f), Partial = true, End = Closest }, q).Active,
                "這條路徑不是通往目標的（例如還在走巡邏點）");
            Assert.IsFalse(Tick(new EnemyTacticalPositioning(), far, Target, 0f, default, q).Active, "沒有可讀的路徑");
            Assert.AreEqual(0, q.PathCalls, "進入判定只讀 agent 快照，不額外查詢——一般打鬥沒有成本");
        }

        [Test]
        public void A1_Arrival_UsesHysteresis()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Tick(p, Vector3.zero, Target, 0f, PartialToTarget(Closest), q);
            Assert.IsTrue(Tick(p, Closest + new Vector3(0.3f, 0f, 0f), Target, 0.1f, default, q).Arrived, "≤ 0.35 m ⇒ 抵達");
            Assert.IsTrue(Tick(p, Closest + new Vector3(0.6f, 0f, 0f), Target, 0.2f, default, q).Arrived, "0.35–0.7 m 仍算抵達（遲滯）");
            Assert.IsFalse(Tick(p, Closest + new Vector3(0.8f, 0f, 0f), Target, 0.3f, default, q).Arrived, "> 0.7 m 才離開抵達");
        }

        [Test]
        public void A1_Exit_WhenTheTargetBecomesReachable()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Vector3 far = new Vector3(0f, 0f, -5f);
            Tick(p, far, Target, 0f, PartialToTarget(Closest), q);
            q.PathComplete = true;
            Assert.IsTrue(Tick(p, far, Target, EnemyTacticalPositioning.ExitCheckInterval * 0.5f, default, q).Active, "檢查間隔內不查詢");
            Assert.IsFalse(Tick(p, far, Target, EnemyTacticalPositioning.ExitCheckInterval + 0.01f, default, q).Active,
                "路徑完整 ⇒ 交還（此處在站位區外 ⇒ 現行行為）");
            Assert.AreEqual(TacticalSituation.None, p.Situation);
        }

        [Test]
        public void A1_QueryFailure_KeepsHolding_NeverFallsBackToPushing()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Tick(p, Vector3.zero, Target, 0f, PartialToTarget(Closest), q);
            q.PathSucceeds = false;
            Assert.IsTrue(Tick(p, Vector3.zero, Target, 5f, default, q).Active, "無法證明目標可達 ⇒ 維持待命");
        }

        [Test]
        public void A1_TargetMovesFar_PicksTheNewClosestPoint_OnlyIfItMovedEnough()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Tick(p, Vector3.zero, Target, 0f, PartialToTarget(Closest), q);
            q.PathEnd = Closest + new Vector3(3f, 0f, 0f);
            Assert.AreEqual(Closest, Tick(p, Vector3.zero, Target + new Vector3(1f, 0f, 0f), 1f, default, q).Point, "目標小幅移動 ⇒ 不換點");
            Vector3 moved = Target + new Vector3(2f, 0f, 0f);
            q.PathEnd = Closest + new Vector3(0.5f, 0f, 0f);
            Assert.AreEqual(Closest, Tick(p, Vector3.zero, moved, 2f, default, q).Point, "新點差不多 ⇒ 不換（防抖）");
            q.PathEnd = Closest + new Vector3(3f, 0f, 0f);
            Assert.AreEqual(q.PathEnd, Tick(p, Vector3.zero, moved + new Vector3(2f, 0f, 0f), 3f, default, q).Point, "新點差很多 ⇒ 換點");
        }

        [Test]
        public void Reset_ClearsTheCommitment()
        {
            var p = new EnemyTacticalPositioning();
            Tick(p, Vector3.zero, Target, 0f, PartialToTarget(Closest), new FakeQuery());
            p.Reset();
            Assert.AreEqual(TacticalSituation.None, p.Situation, "脫戰／死亡 ⇒ 清除");
            Assert.IsFalse(Tick(p, new Vector3(0f, 0f, -5f), Target, 1f, default, new FakeQuery()).Active, "清除後需要重新滿足進入條件");
        }

        // =====================================================================================================
        // 站位槽（P5b）
        // =====================================================================================================

        private static readonly Vector3 Origin = Vector3.zero;

        [Test]
        public void Slot_Zone_EntersNearTheBand_LeavesWithHysteresis()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Assert.IsFalse(Tick(p, new Vector3(0f, 0f, -3.5f), Origin, 0f, default, q).Active, "3.5 m（> 攻擊距離 + 1）⇒ 站位區外");
            Assert.IsTrue(Tick(p, new Vector3(0f, 0f, -2.9f), Origin, 0.1f, default, q).Active, "≤ 3.0 m ⇒ 進入");
            Assert.IsTrue(Tick(p, new Vector3(0f, 0f, -3.8f), Origin, 0.2f, default, q).Active, "3.0–4.0 m 仍在區內（遲滯）");
            Assert.IsFalse(Tick(p, new Vector3(0f, 0f, -4.2f), Origin, 0.3f, default, q).Active, "> 4.0 m ⇒ 離開，交還 Chase");
        }

        [Test]
        public void Slot_OpenGround_KeepsTheCurrentBearing_AtTheIdealRadius_AndHoldsOnArrival()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            TacticalGoal g = Tick(p, new Vector3(0f, 0f, -2.5f), Origin, 0f, default, q);
            Assert.AreEqual(TacticalSituation.Slot, g.Situation);
            Assert.AreEqual(Ideal, EnemyTacticalPositioning.HorizontalDistance(g.Point, Origin), 1e-4f, "槽在交戰帶中點");
            Assert.AreEqual(-Ideal, g.Point.z, 1e-4f, "開闊平地 ⇒ 沿自己目前方位（不為動而動）");
            Assert.IsTrue(g.DirectLine);
            Assert.IsTrue(Tick(p, g.Point, Origin, 0.1f, default, q).Arrived, "到位 ⇒ 待命");
        }

        [Test]
        public void Slot_NeverHoldsOutsideTheAttackRange_EvenWithinTheArrivalTolerance()
        {
            // 2026-10-14 實測：Y Bot 的帶是 [1.25, 1.6]，理想半徑 1.425；抵達容差 0.35 m 比 1.425 → 1.6 的餘裕還寬。
            // 舊版被打斷後停在 1.75 m（離槽點 0.325 m ⇒ 算「到位」）而永遠打不到。
            const float yBotMin = 1.25f, yBotReach = 1.6f;
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Vector3 self = new Vector3(0f, 0f, -1.75f);
            TacticalGoal g = p.Tick(self, Origin, yBotMin, yBotReach, 0f, default, q);
            Assert.AreEqual(TacticalSituation.Slot, g.Situation);
            Assert.IsFalse(g.Arrived, "離槽點在容差內，但 1.75 m 在攻擊距離（1.6）外 ⇒ 不得待命");
            Vector3 attackable = new Vector3(0f, 0f, -1.5f);
            Assert.IsTrue(p.Tick(attackable, Origin, yBotMin, yBotReach, 0.1f, default, q).Arrived, "1.5 m：離槽點 0.075 m、打得到 ⇒ 待命");
            Assert.IsFalse(p.Tick(new Vector3(0f, 0f, -1.58f), Origin, yBotMin, yBotReach, 0.2f, default, q).Arrived,
                "距攻擊距離不到 HoldReachMargin ⇒ 也不算（避免在邊界上一出拳就出界）");
        }

        [Test]
        public void Slot_PausesWhileTheTargetMoves_AndResumesAfterItSettles()
        {
            // §13.4：MovingSlow 實測——世界方位的槽在目標移動時讓敵人一路跟著平移、落在攻擊距離外 ⇒ 移動中交還距離帶行為。
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Vector3 self = new Vector3(0f, 0f, -2f);
            float t = 0f;
            Vector3 target = Origin;
            Assert.IsTrue(Tick(p, self, target, t, default, q).Active, "靜止目標 ⇒ 站位槽");
            for (int i = 0; i < 30; i++)   // 目標以 1 m/s 移動 0.5 s
            {
                t += 1f / 60f;
                target += new Vector3(1f / 60f, 0f, 0f);
                Tick(p, self, target, t, default, q);
            }
            Assert.IsTrue(p.TargetMoving, "1 m/s ⇒ 判定移動中");
            Assert.IsFalse(Tick(p, self, target, t + 1f / 60f, default, q).Active, "移動中 ⇒ 交還現行行為");
            for (int i = 0; i < 20; i++) Tick(p, self, target, t += 1f / 60f, default, q);   // 停下 1/3 s：還沒滿 0.5 s
            Assert.IsTrue(p.TargetMoving, "剛停下不立刻恢復（遲滯）");
            for (int i = 0; i < 60; i++) Tick(p, self, target, t += 1f / 60f, default, q);
            Assert.IsFalse(p.TargetMoving, "靜止超過 0.5 s ⇒ 恢復");
            Assert.IsTrue(Tick(p, self, target, t += 1f / 60f, default, q).Active, "恢復後重新選槽");
        }

        [Test]
        public void Slot_Disabled_LeavesOnlyA1()
        {
            var p = new EnemyTacticalPositioning { SlotPositioningEnabled = false };
            Assert.IsFalse(Tick(p, new Vector3(0f, 0f, -2f), Origin, 0f, default, new FakeQuery()).Active, "關閉站位槽 ⇒ 交戰帶內交還現行行為（P5a）");
            Assert.IsTrue(Tick(p, new Vector3(0f, 0f, -1f), Target, 0.1f, PartialToTarget(Closest), new FakeQuery()).Active, "A1 不受影響");
        }

        [Test]
        public void Slot_StaysCommitted_WhileValid_NoPeriodicReselection()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Vector3 self = new Vector3(0f, 0f, -Ideal);
            Tick(p, self, Origin, 0f, default, q);
            for (float t = 0.1f; t < 20f; t += 0.1f)
            {
                Vector3 jitter = new Vector3(Mathf.Sin(t * 3f) * 0.3f, 0f, Mathf.Cos(t * 2f) * 0.3f);   // 目標小幅晃動
                Tick(p, self, Origin + jitter, t, default, q);
            }
            Assert.AreEqual(1, p.SlotChanges, "槽一直有效 ⇒ 20 秒內不換（D6：沒有定期重新挑槽）");
        }

        [Test]
        public void Slot_InvalidSlot_IsReplaced_AndTheAbandonedBearingIsPenalised()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Vector3 self = new Vector3(0f, 0f, -Ideal);
            Tick(p, self, Origin, 0f, default, q);
            q.Walkable = v => v.z > -1.0f;   // 南側變成不可走（例如目標退到牆前）
            TacticalGoal g = Tick(p, self, Origin, 0.25f, default, q);
            Assert.AreEqual(2, p.SlotChanges, "槽失效 ⇒ 重選");
            Assert.Greater(g.Point.z, -1.0f, "新槽必須可走");
            q.Walkable = _ => true;           // 原方位恢復可走：3 s 內不因為「比較近」立刻換回去（H5）
            for (float t = 0.5f; t < 2.5f; t += 0.1f) Tick(p, self, Origin, t, default, q);
            Assert.AreEqual(2, p.SlotChanges, "新槽仍有效 ⇒ 不換回（只在失效時換）");
        }

        [Test]
        public void Slot_ReachLineAcrossAWall_IsNotAValidSlot()
        {
            // 一道沿 x 軸的牆在 z＝-1（|x| ≤ 3）：南側的點到目標的直線會跨過它。
            bool CrossesWall(Vector3 a, Vector3 b)
            {
                if ((a.z + 1f) * (b.z + 1f) >= 0f) return false;
                float x = a.x + (b.x - a.x) * ((-1f - a.z) / (b.z - a.z));
                return Mathf.Abs(x) <= 3f;
            }
            var q = new FakeQuery { LineClear = (a, b) => !CrossesWall(a, b) };
            var p = new EnemyTacticalPositioning();
            TacticalGoal g = Tick(p, new Vector3(0f, 0f, -2f), Origin, 0f, default, q);   // 隔牆、2 m（在攻擊距離上）
            Assert.AreEqual(TacticalSituation.Slot, g.Situation);
            Assert.IsTrue(p.TryGetSlotPoint(Origin, out Vector3 slot));
            Assert.IsFalse(CrossesWall(slot, Origin), "選中的槽必須有觸及線（A2：不站在牆後出拳）");
            Assert.IsFalse(g.DirectLine, "從牆後走過去要沿路徑");
        }

        [Test]
        public void Slot_Corner_PicksASlotWithClearance_NotIntoTheWalls()
        {
            // 牆角：x、z 都只到 7.5（NavMesh 邊界）。目標壓近牆角內的敵人。
            var q = new FakeQuery
            {
                Walkable = v => v.x <= 7.5f && v.z <= 7.5f,
                EdgeDistance = v => Mathf.Min(7.5f - v.x, 7.5f - v.z),
            };
            var p = new EnemyTacticalPositioning();
            Vector3 target = new Vector3(6.95f, 0f, 6.95f), self = new Vector3(7.3f, 0f, 7.3f);
            TacticalGoal g = Tick(p, self, target, 0f, default, q);
            Assert.AreEqual(TacticalSituation.Slot, g.Situation, "必須找得到一個脫離牆角的槽");
            float bearing = EnemyTacticalPositioning.BearingFromTarget(target, g.Point);
            Vector3 slot = EnemyTacticalPositioning.SlotPoint(target, bearing, Ideal);
            Assert.GreaterOrEqual(q.EdgeDistance(slot), EnemyTacticalPositioning.MinEdgeClearance, "不選貼牆的槽（T1）");
        }

        [Test]
        public void Slot_FarSideOfTheTarget_IsReachedByOrbiting_NotThroughTheTarget()
        {
            // 一段矮牆在 z＝-0.5、|x| < 1：自己這側（目前方位、±30°、±60°）的槽到目標的觸及線都會跨過它，
            // 只剩 ±90° 以外的槽有效 ⇒ 方位差 90° > 60° ⇒ 必須先繞。繞行中繼點本身可走。
            bool Crosses(Vector3 a, Vector3 b)
            {
                if ((a.z + 0.5f) * (b.z + 0.5f) >= 0f) return false;
                float x = a.x + (b.x - a.x) * ((-0.5f - a.z) / (b.z - a.z));
                return Mathf.Abs(x) < 1f;
            }
            var q = new FakeQuery { LineClear = (a, b) => !Crosses(a, b) };
            var p = new EnemyTacticalPositioning();
            Vector3 self = new Vector3(0f, 0f, -Ideal);
            TacticalGoal g = Tick(p, self, Origin, 0f, default, q);
            Assert.AreEqual(TacticalSituation.Slot, g.Situation);
            Assert.Greater(EnemyTacticalPositioning.HorizontalDistance(g.Point, Origin), Ideal - 1e-3f, "中繼點仍在圓周上");
            float selfBearing = EnemyTacticalPositioning.BearingFromTarget(Origin, self);
            float stepBearing = EnemyTacticalPositioning.BearingFromTarget(Origin, g.Point);
            Assert.AreEqual(45f, Mathf.Abs(Mathf.DeltaAngle(selfBearing * Mathf.Rad2Deg, stepBearing * Mathf.Rad2Deg)), 1f,
                "方位差 > 60° ⇒ 先繞 45°，不從目標中間穿過");
        }

        [Test]
        public void Slot_NoValidSlot_HandsBackToTheCurrentBehaviour()
        {
            var q = new FakeQuery { LineClear = (_, __) => false };   // 到處都隔牆
            Assert.IsFalse(Tick(new EnemyTacticalPositioning(), new Vector3(0f, 0f, -2f), Origin, 0f, default, q).Active,
                "沒有任何有效槽 ⇒ 交還現行行為（不引入新的靜止終態）");
        }

        [Test]
        public void Slot_Stuck_CountsAsInvalid_OnlyAfterTheGrace()
        {
            var p = new EnemyTacticalPositioning();
            var q = new FakeQuery();
            Vector3 self = new Vector3(0f, 0f, -2.5f);
            Tick(p, self, Origin, 0f, default, q);
            Tick(p, self, Origin, 0.5f, default, q, stuck: true);
            Assert.AreEqual(1, p.SlotChanges, "選槽後 1 s 內的卡住是上一個判定窗的結論 ⇒ 不算");
            Tick(p, self, Origin, EnemyTacticalPositioning.StuckGraceAfterSelect + 0.1f, default, q, stuck: true);
            Assert.AreEqual(2, p.SlotChanges, "之後仍卡住 ⇒ 槽失效、重選（E5）");
        }
    }
}
