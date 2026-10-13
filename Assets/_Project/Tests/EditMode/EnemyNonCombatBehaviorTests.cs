using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Combat;
using Project.Core.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.4，P1–P3）非戰鬥行為：Return／Dwell／Roam／Patrol。
    /// 決策用假的導航查詢驅動（EditMode 沒有 NavMesh）；真 NavMesh 上的行為由 PlayMode 驗。
    /// </summary>
    public sealed class EnemyNonCombatBehaviorTests
    {
        /// <summary>假的導航查詢：預設處處可走、處處可達；可指定「走不到」的點。</summary>
        private sealed class FakeQuery : IEnemyNavigationQuery
        {
            public bool SampleSucceeds = true;
            public readonly List<Vector3> Unreachable = new List<Vector3>();

            public bool TrySampleWalkablePoint(Vector3 point, float maxDistance, out Vector3 walkable)
            {
                walkable = point;
                return SampleSucceeds;
            }

            public bool IsPathComplete(Vector3 from, Vector3 to)
            {
                foreach (Vector3 blocked in Unreachable)
                    if ((blocked - to).sqrMagnitude < 0.01f) return false;
                return true;
            }
        }

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private EnemyTerritory Territory(float roam = 6f, Transform[] waypoints = null,
            WaypointTraversal traversal = WaypointTraversal.Loop, float dwellMin = 1f, float dwellMax = 2f)
        {
            var go = new GameObject("Territory");
            _created.Add(go);
            var territory = go.AddComponent<EnemyTerritory>();
            territory.ConfigureForTests(Vector3.zero, roam, soft: roam * 2f, hard: roam * 3f, cooldown: 2f,
                dwellMin: dwellMin, dwellMax: dwellMax, points: waypoints, traversal: traversal);
            return territory;
        }

        private Transform[] Waypoints(params Vector3[] positions)
        {
            var result = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var go = new GameObject($"Waypoint-{i}");
                _created.Add(go);
                go.transform.position = positions[i];
                result[i] = go.transform;
            }
            return result;
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            Vector3 d = a - b;
            d.y = 0f;
            return d.magnitude;
        }

        // ── P2 Roam ────────────────────────────────────────────────────

        [Test]
        public void P2_Roam_PicksReachablePointsInsideRadius_DwellsOnArrival()
        {
            EnemyTerritory territory = Territory(roam: 6f, dwellMin: 1f, dwellMax: 2f);
            var behavior = new EnemyNonCombatBehavior(seed: 7);
            var query = new FakeQuery();
            Vector3 self = Vector3.zero;
            float now = 0f;

            for (int round = 0; round < 20; round++)
            {
                NonCombatDecision decision = behavior.Tick(self, now, false, territory, query);
                Assert.AreEqual(EnemyNonCombatMode.Roam, decision.Mode, $"第 {round} 輪：應選出遊走點");
                Assert.IsTrue(decision.HasDestination);
                Assert.LessOrEqual(Horizontal(decision.Destination, territory.Home), territory.RoamRadius + 1e-4f,
                    "遊走點必須在 Roam 半徑內");
                Assert.GreaterOrEqual(Horizontal(decision.Destination, self), EnemyNonCombatBehavior.RoamMinimumDistance - 1e-4f,
                    "遊走點離目前位置至少最小距離（避免原地小抖）");

                self = decision.Destination;   // 抵達
                decision = behavior.Tick(self, now, false, territory, query);
                Assert.AreEqual(EnemyNonCombatMode.Dwell, decision.Mode, "抵達 ⇒ 停留");
                Assert.IsFalse(decision.HasDestination, "停留時不給目的地");

                float dwellStart = now;
                while (behavior.Tick(self, now, false, territory, query).Mode == EnemyNonCombatMode.Dwell) now += 0.05f;
                float dwell = now - dwellStart;
                Assert.GreaterOrEqual(dwell, territory.DwellMinSeconds - 0.06f, "停留不短於 min");
                Assert.LessOrEqual(dwell, territory.DwellMaxSeconds + 0.06f, "停留不長於 max");
            }
        }

        [Test]
        public void P2_Roam_NoReachablePoint_DwellsAndRetries_InsteadOfPushing()
        {
            EnemyTerritory territory = Territory();
            var behavior = new EnemyNonCombatBehavior(seed: 3);
            var query = new FakeQuery { SampleSucceeds = false };

            NonCombatDecision decision = behavior.Tick(Vector3.zero, 0f, false, territory, query);
            Assert.AreEqual(EnemyNonCombatMode.Dwell, decision.Mode);
            Assert.IsFalse(decision.HasDestination, "找不到可達點 ⇒ 停下來，不朝任何方向推");

            query.SampleSucceeds = true;
            decision = behavior.Tick(Vector3.zero, EnemyNonCombatBehavior.RetryDwellSeconds + 0.01f, false, territory, query);
            Assert.AreEqual(EnemyNonCombatMode.Roam, decision.Mode, "重試時間到 ⇒ 再選");
        }

        [Test]
        public void P2_Roam_Stuck_GivesUpThatPoint()
        {
            EnemyTerritory territory = Territory();
            var behavior = new EnemyNonCombatBehavior(seed: 5);
            var query = new FakeQuery();
            Assert.AreEqual(EnemyNonCombatMode.Roam, behavior.Tick(Vector3.zero, 0f, false, territory, query).Mode);
            Assert.AreEqual(EnemyNonCombatMode.Dwell, behavior.Tick(Vector3.zero, 0.1f, true, territory, query).Mode,
                "卡住 ⇒ 放棄這個點，停一下再選");
        }

        // ── P1 Return ──────────────────────────────────────────────────

        [Test]
        public void P1_Return_OutsideRoam_GoesHome_WithInnerOuterHysteresis()
        {
            EnemyTerritory territory = Territory(roam: 6f);
            var behavior = new EnemyNonCombatBehavior(seed: 1);
            var query = new FakeQuery();

            NonCombatDecision decision = behavior.Tick(new Vector3(0f, 0f, 9f), 0f, false, territory, query);
            Assert.AreEqual(EnemyNonCombatMode.Return, decision.Mode, "Roam 外 ⇒ 回家");
            Assert.AreEqual(territory.Home, decision.Destination);

            decision = behavior.Tick(new Vector3(0f, 0f, 4.5f), 0.5f, false, territory, query);
            Assert.AreEqual(EnemyNonCombatMode.Return, decision.Mode, "已回到 Roam 內但還沒到一半半徑 ⇒ 繼續回家（遲滯）");

            decision = behavior.Tick(new Vector3(0f, 0f, 2.9f), 1f, false, territory, query);
            Assert.AreEqual(EnemyNonCombatMode.Dwell, decision.Mode, "回到 Roam × 0.5 內 ⇒ 結束 Return");
        }

        [Test]
        public void P1_Return_RepeatedlyStuck_RestsThenResumes_NoTeleport()
        {
            EnemyTerritory territory = Territory(roam: 6f);
            var behavior = new EnemyNonCombatBehavior(seed: 1);
            var query = new FakeQuery();
            Vector3 far = new Vector3(0f, 0f, 9f);
            float t = 0f;
            Assert.AreEqual(EnemyNonCombatMode.Return, behavior.Tick(far, t, false, territory, query).Mode);

            for (int i = 0; i < EnemyNonCombatBehavior.MaxReturnStucksBeforeRest; i++)
            {
                behavior.Tick(far, t += 0.1f, false, territory, query);
                behavior.Tick(far, t += 0.1f, true, territory, query);   // 每次「剛變成卡住」算一次
            }
            Assert.AreEqual(EnemyNonCombatMode.Dwell, behavior.Mode, "連續卡住 ⇒ 先停一下");

            Assert.AreEqual(EnemyNonCombatMode.Return,
                behavior.Tick(far, t + EnemyNonCombatBehavior.RetryDwellSeconds + 0.01f, false, territory, query).Mode,
                "停完仍在 Roam 外 ⇒ 繼續回家");
        }

        [Test]
        public void P1_Return_DoesNotOscillateAtTheBoundary()
        {
            EnemyTerritory territory = Territory(roam: 6f);
            var behavior = new EnemyNonCombatBehavior(seed: 11);
            var query = new FakeQuery();
            int switches = 0;
            EnemyNonCombatMode last = EnemyNonCombatMode.None;
            for (int i = 0; i < 600; i++)
            {
                float jitter = (i % 2 == 0) ? 5.95f : 6.05f;   // 在 Roam 邊界上逐帧左右跳
                EnemyNonCombatMode mode = behavior.Tick(new Vector3(0f, 0f, jitter), i * 0.016f, false, territory, query).Mode;
                if (mode != last) switches++;
                last = mode;
            }
            Assert.LessOrEqual(switches, 3, "邊界抖動不得造成逐帧模式切換（內外圈遲滯）");
        }

        // ── P3 Patrol ──────────────────────────────────────────────────

        [Test]
        public void P3_Patrol_LoopVisitsInOrder_AndResumesFromNearestAfterCombat()
        {
            Transform[] points = Waypoints(new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 2f), new Vector3(-2f, 0f, 0f));
            EnemyTerritory territory = Territory(waypoints: points, dwellMin: 0f, dwellMax: 0f);
            var behavior = new EnemyNonCombatBehavior(seed: 2);
            var query = new FakeQuery();
            Vector3 self = new Vector3(1.8f, 0f, 0f);   // 最近的是 #0
            float t = 0f;

            var visited = new List<int>();
            for (int step = 0; step < 7; step++)
            {
                NonCombatDecision d = behavior.Tick(self, t += 0.1f, false, territory, query);
                while (d.Mode == EnemyNonCombatMode.Dwell) d = behavior.Tick(self, t += 0.1f, false, territory, query);
                Assert.AreEqual(EnemyNonCombatMode.Patrol, d.Mode);
                visited.Add(behavior.WaypointIndex);
                self = d.Destination;   // 抵達
                behavior.Tick(self, t += 0.1f, false, territory, query);
            }
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 1, 2, 0 }, visited, "Loop：依序循環");

            behavior.OnEngaged();
            NonCombatDecision resumed = behavior.Tick(new Vector3(-1.9f, 0f, 0.1f), t + 0.1f, false, territory, query);
            Assert.AreEqual(EnemyNonCombatMode.Patrol, resumed.Mode);
            Assert.AreEqual(2, behavior.WaypointIndex, "交戰回來 ⇒ 從最近的點接續");
        }

        [Test]
        public void P3_Patrol_PingPongOrder()
        {
            int direction = 1;
            var order = new List<int> { 0 };
            int index = 0;
            for (int i = 0; i < 6; i++)
            {
                index = WaypointCursor.Advance(index, direction, 3, WaypointTraversal.PingPong, out direction);
                order.Add(index);
            }
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 1, 0, 1, 2 }, order);
        }

        [Test]
        public void P3_Patrol_UnreachableWaypointIsSkipped_AllUnreachableRests()
        {
            Transform[] points = Waypoints(new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 2f), new Vector3(-2f, 0f, 0f));
            EnemyTerritory territory = Territory(waypoints: points, dwellMin: 0f, dwellMax: 0f);
            var query = new FakeQuery();
            query.Unreachable.Add(points[0].position);

            var behavior = new EnemyNonCombatBehavior(seed: 2);
            NonCombatDecision d = behavior.Tick(new Vector3(1.8f, 0f, 0f), 0f, false, territory, query);
            Assert.AreEqual(EnemyNonCombatMode.Patrol, d.Mode);
            Assert.AreEqual(1, behavior.WaypointIndex, "最近的 #0 走不到 ⇒ 跳到 #1，不朝 #0 推牆");

            query.Unreachable.Add(points[1].position);
            query.Unreachable.Add(points[2].position);
            var stuckBehavior = new EnemyNonCombatBehavior(seed: 2);
            d = stuckBehavior.Tick(new Vector3(1.8f, 0f, 0f), 0f, false, territory, query);
            Assert.AreEqual(EnemyNonCombatMode.Dwell, d.Mode, "全部走不到 ⇒ 停下來，稍後再試");
        }

        // ── 整合：AIMovementSource（真元件，沒有 NavMesh ⇒ 直接朝目的地） ───────

        [Test]
        public void P1_Integration_NoTerritory_StaysIdle_AsBefore()
        {
            AIMovementSource movement = CreateEnemy(withTerritory: false, out PlayerRuntimeData data, out _);
            InputData input = default;
            movement.ProduceIntent(ref input, data);
            Assert.AreEqual(default(MovementIntentData), data.MovementIntent, "沒有 EnemyTerritory ⇒ 未交戰時站著（P1.1）");
        }

        [Test]
        public void P1_Integration_OutOfCombat_WalksAtBakeDerivedWalkSpeed()
        {
            AIMovementSource movement = CreateEnemy(withTerritory: true, out PlayerRuntimeData data, out CombatDirectionalSpeedProfileSO profile);
            movement.transform.position = new Vector3(0f, 0f, 9f);   // Roam（6 m）外 ⇒ Return
            InputData input = default;
            movement.ProduceIntent(ref input, data);

            Assert.AreEqual(EnemyNonCombatMode.Return, movement.NonCombatMode);
            Assert.Greater(Vector3.Dot(data.MovementIntent.DesiredDirection, Vector3.back), 0.99f, "朝家走");
            Assert.AreEqual(profile.ResolveForwardNormalizedSpeed(), data.MovementIntent.DesiredSpeedNormalized, 1e-6f,
                "非戰鬥速度＝Bake 推導的 walk normalized（N5）");
            Assert.Less(data.MovementIntent.DesiredSpeedNormalized, 0.3f, "前提：Y Bot walk 約 0.263，不是 1");

            data.CombatContext = new CombatContextData { InCombat = true, HasTarget = true, TargetPosition = new Vector3(0f, 0f, 20f) };
            movement.ProduceIntent(ref input, data);
            Assert.AreEqual(EnemyNonCombatMode.None, movement.NonCombatMode, "交戰中 ⇒ 非戰鬥行為重設，脫戰後重新選");
        }

        private AIMovementSource CreateEnemy(bool withTerritory, out PlayerRuntimeData data, out CombatDirectionalSpeedProfileSO profile)
        {
            var go = new GameObject("NonCombat-Enemy");
            _created.Add(go);
            go.AddComponent<NavMeshAgent>();
            AIMovementSource movement = go.AddComponent<AIMovementSource>();
            typeof(AIMovementSource).GetField("_agent", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(movement, null);
            profile = AssetDatabase.LoadAssetAtPath<CombatDirectionalSpeedProfileSO>(
                "Assets/ScriptableObjects/Motion/YBotCombatDirectionalSpeedProfile.asset");
            Assert.IsNotNull(profile);
            typeof(AIMovementSource).GetField("nonCombatSpeedSource", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(movement, profile);
            if (withTerritory)
            {
                var territory = go.AddComponent<EnemyTerritory>();
                territory.ConfigureForTests(Vector3.zero, roam: 6f, soft: 14f, hard: 22f);
            }
            data = new PlayerRuntimeData();
            return movement;
        }
    }
}
