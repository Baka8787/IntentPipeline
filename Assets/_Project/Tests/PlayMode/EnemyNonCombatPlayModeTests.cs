using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.Survivability;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.6 P1–P3）**非戰鬥行為在真 NavMesh、真 Runner 上的驗收。**
    ///
    /// 出貨場景（固定 60 fps）：遊走不出領地且以 walk 速度移動、Soft Leash 放手並回家、
    /// 持續受傷也追不過 Hard Leash、指定 waypoint 時依序巡邏。
    /// 另在執行期建一個有牆的小場景：牆後的 waypoint 必須繞牆抵達（尋路，不是直線推牆）。
    /// 測試只用 <c>EnemyTerritory.ConfigureForTests</c> 改這個執行期實例的半徑／waypoint——不改資產、不改場景。
    /// </summary>
    public sealed class EnemyNonCombatPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const float FixedDelta = 1f / 60f;
        private const float WalkSpeedMetersPerSecond = 1.6443043f;   // Bake_WalkFwdLoop.AutoAverageSpeed（docs/21 §1.5）

        private Scene _scene;
        private Scene _testScene;
        private NavMeshDataInstance _navMesh;
        private readonly List<Object> _created = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (Object o in _created) if (o != null) Object.Destroy(o);
            _created.Clear();
            if (_navMesh.valid) NavMesh.RemoveNavMeshData(_navMesh);
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            if (_testScene.IsValid() && _testScene.isLoaded) yield return SceneManager.UnloadSceneAsync(_testScene);
        }

        // =====================================================================
        // P2：遊走
        // =====================================================================

        [UnityTest]
        public IEnumerator P2_YBot_RoamsInsideTerritory_AtWalkSpeed_WithoutAllocating()
        {
            yield return LoadShippedScene();
            CharacterPipelineRunner enemy = FindRunner("Y Bot");
            var territory = enemy.GetComponent<EnemyTerritory>();
            Assert.IsNotNull(territory, "Y Bot 沒有 EnemyTerritory（prefab 接線）");
            var movement = enemy.GetComponent<AIMovementSource>();
            PlayerRuntimeData data = enemy.RuntimeData;

            Vector3 previous = enemy.transform.position;
            float maxFromHome = 0f, travelled = 0f, movingSpeedSum = 0f;
            int movingFrames = 0, combatFrames = 0, modeSwitches = 0;
            EnemyNonCombatMode lastMode = movement.NonCombatMode;
            var modesSeen = new HashSet<EnemyNonCombatMode>();

            for (int frame = 0; frame < 1800; frame++)   // 30 s
            {
                yield return null;
                Vector3 position = enemy.transform.position;
                Vector3 step = position - previous;
                step.y = 0f;
                previous = position;
                travelled += step.magnitude;
                maxFromHome = Mathf.Max(maxFromHome, territory.HorizontalDistanceFromHome(position));
                if (data.CombatContext.InCombat) combatFrames++;

                float speed = step.magnitude / FixedDelta;
                if (data.MovementIntent.HasMovementRequest && speed > 0.2f && data.MoveSpeed > 0.95f * data.MovementIntent.DesiredSpeedNormalized)
                {
                    movingSpeedSum += speed;
                    movingFrames++;
                }
                EnemyNonCombatMode mode = movement.NonCombatMode;
                modesSeen.Add(mode);
                if (mode != lastMode) modeSwitches++;
                lastMode = mode;
            }

            float averageSpeed = movingFrames > 0 ? movingSpeedSum / movingFrames : 0f;
            string summary = $"[NonCombat roam] travelled={travelled:F2}m maxFromHome={maxFromHome:F2}m (roam {territory.RoamRadius}) " +
                             $"steadyWalkSpeed={averageSpeed:F3}m/s (bake {WalkSpeedMetersPerSecond:F3}) frames={movingFrames} " +
                             $"modeSwitches={modeSwitches} combatFrames={combatFrames} modes={string.Join(",", modesSeen)}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);

            Assert.AreEqual(0, combatFrames, "前提：玩家在出生點（約 18.6 m 外），不應交戰");
            Assert.Greater(travelled, 4f, "30 秒內必須真的有在遊走");
            Assert.IsTrue(modesSeen.Contains(EnemyNonCombatMode.Roam) && modesSeen.Contains(EnemyNonCombatMode.Dwell),
                "遊走與停留都要出現");
            Assert.LessOrEqual(maxFromHome, territory.RoamRadius + 0.5f, "遊走不得飄出 Roam 半徑");
            Assert.That(averageSpeed, Is.InRange(WalkSpeedMetersPerSecond * 0.85f, WalkSpeedMetersPerSecond * 1.1f),
                "穩態走路速度必須等於 walk clip 的 Bake 速度（N5；否則腳步會滑）");
            Assert.LessOrEqual(modeSwitches, 40, "模式不得頻繁切換");

            // 零 GC：同一帧內直接呼叫 producer（非戰鬥分支）1000 次。
            InputData input = default;
            for (int i = 0; i < 10; i++) movement.ProduceIntent(ref input, data);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) movement.ProduceIntent(ref input, data);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"[NonCombat roam] ProduceIntent ×1000 allocated {allocated} bytes");
            Debug.Log($"[NonCombat roam] ProduceIntent ×1000 allocated {allocated} bytes");
            Assert.AreEqual(0, allocated, "非戰鬥分支每次呼叫都不得配置記憶體");
        }

        // =====================================================================
        // P1：Leash 與回家
        // =====================================================================

        [UnityTest]
        public IEnumerator P1_SoftLeash_UndamagedChaseStops_ThenReturnsHome_WithCooldown()
        {
            yield return LoadShippedScene();
            CharacterPipelineRunner player = FindRunner("X Bot");
            CharacterPipelineRunner enemy = FindRunner("Y Bot");
            var territory = enemy.GetComponent<EnemyTerritory>();
            Vector3 home = enemy.transform.position;
            territory.ConfigureForTests(home, roam: 2f, soft: 4f, hard: 6f, cooldown: 2f, dwellMin: 0.5f, dwellMax: 1f);
            PlacePlayerOnSameFloor(player, home, 7.5f);
            PlayerRuntimeData data = enemy.RuntimeData;

            bool engaged = false;
            int disengageFrame = -1, returnedFrame = -1, reengageFrame = -1;
            float maxFromHomeInCombat = 0f, distanceAtDisengage = 0f, closestHomeAfterDisengage = float.PositiveInfinity;
            bool wasInCombat = false;
            for (int frame = 0; frame < 900 && reengageFrame < 0; frame++)
            {
                yield return null;
                bool inCombat = data.CombatContext.InCombat;
                float fromHome = territory.HorizontalDistanceFromHome(enemy.transform.position);
                if (inCombat)
                {
                    engaged = true;
                    maxFromHomeInCombat = Mathf.Max(maxFromHomeInCombat, fromHome);
                }
                if (wasInCombat && !inCombat && disengageFrame < 0)
                {
                    disengageFrame = frame;
                    distanceAtDisengage = fromHome;
                }
                if (disengageFrame >= 0 && reengageFrame < 0) closestHomeAfterDisengage = Mathf.Min(closestHomeAfterDisengage, fromHome);
                if (disengageFrame >= 0 && returnedFrame < 0 && fromHome <= territory.RoamRadius * 0.5f + 0.1f) returnedFrame = frame;
                if (disengageFrame >= 0 && !wasInCombat && inCombat) reengageFrame = frame;
                wasInCombat = inCombat;
            }

            string summary = $"[NonCombat soft] disengageFrame={disengageFrame} dHome@disengage={distanceAtDisengage:F2} " +
                             $"maxFromHomeInCombat={maxFromHomeInCombat:F2} closestHomeBeforeReengage={closestHomeAfterDisengage:F2} " +
                             $"returnedFrame={returnedFrame} reengageFrame={reengageFrame}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);

            Assert.IsTrue(engaged, "前提：玩家在 7.5 m（aggro 8 m 內）⇒ 先交戰");
            Assert.GreaterOrEqual(disengageFrame, 0, "沒被打過的追擊必須在 Soft Leash 放手");
            Assert.That(distanceAtDisengage, Is.InRange(territory.SoftLeashRadius - 0.1f, territory.SoftLeashRadius + 0.2f),
                "放手的位置必須就在 Soft Leash 上");
            Assert.LessOrEqual(maxFromHomeInCombat, territory.SoftLeashRadius + 0.2f, "交戰中不得越過 Soft Leash");
            // N3：返回途中允許重新激怒（冷卻結束、回到 Soft 內、目標在 aggro 內）——所以不要求一定走到 Roam × 0.5；
            // 要求的是「脫戰後確實往家走，而且在再交戰之前已經回到 Roam 範圍內」。沒有再交戰時則必須走完 Return。
            Assert.LessOrEqual(closestHomeAfterDisengage, territory.RoamRadius, "脫戰後必須往家走、回到 Roam 範圍內");
            if (reengageFrame < 0) Assert.GreaterOrEqual(returnedFrame, 0, "沒有再交戰時，必須回到 Roam × 0.5 內");
            if (reengageFrame >= 0)
            {
                Assert.GreaterOrEqual((reengageFrame - disengageFrame) * FixedDelta, territory.ReengageCooldownSeconds - FixedDelta,
                    "再交戰必須等冷卻結束（不得在邊界逐帧振盪）");
            }
        }

        [UnityTest]
        public IEnumerator P1_HardLeash_ContinuousDamageCannotExtendTheChase()
        {
            yield return LoadShippedScene();
            CharacterPipelineRunner player = FindRunner("X Bot");
            CharacterPipelineRunner enemy = FindRunner("Y Bot");
            var territory = enemy.GetComponent<EnemyTerritory>();
            Vector3 home = enemy.transform.position;
            territory.ConfigureForTests(home, roam: 2f, soft: 3f, hard: 5f, cooldown: 2f, dwellMin: 0.5f, dwellMax: 1f);
            PlacePlayerOnSameFloor(player, home, 7.5f);
            var health = enemy.GetComponent<CharacterHealth>();
            PlayerRuntimeData data = enemy.RuntimeData;

            float maxFromHomeInCombat = 0f;
            int disengageFrame = -1, combatFramesDuringCooldown = 0;
            bool wasInCombat = false, engaged = false;
            for (int frame = 0; frame < 1200; frame++)   // 20 s
            {
                if (frame % 90 == 0) health.ApplyDamage(1f);   // 每 1.5 s 被打一下（持續受傷）
                yield return null;
                bool inCombat = data.CombatContext.InCombat;
                float fromHome = territory.HorizontalDistanceFromHome(enemy.transform.position);
                if (inCombat)
                {
                    engaged = true;
                    maxFromHomeInCombat = Mathf.Max(maxFromHomeInCombat, fromHome);
                }
                if (wasInCombat && !inCombat && disengageFrame < 0) disengageFrame = frame;
                if (disengageFrame >= 0 && inCombat && (frame - disengageFrame) * FixedDelta < territory.ReengageCooldownSeconds - FixedDelta)
                    combatFramesDuringCooldown++;
                wasInCombat = inCombat;
            }

            string summary = $"[NonCombat hard] maxFromHomeInCombat={maxFromHomeInCombat:F2} (soft {territory.SoftLeashRadius}, hard {territory.HardLeashRadius}) " +
                             $"disengageFrame={disengageFrame} combatFramesDuringCooldown={combatFramesDuringCooldown} healthLeft={health.CurrentHealth}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);

            Assert.IsTrue(engaged, "前提：受擊 ⇒ 交戰");
            Assert.Greater(maxFromHomeInCombat, territory.SoftLeashRadius, "受過傷 ⇒ 可以追過 Soft");
            Assert.LessOrEqual(maxFromHomeInCombat, territory.HardLeashRadius + 0.2f, "持續受傷也永遠追不過 Hard Leash");
            Assert.GreaterOrEqual(disengageFrame, 0, "到 Hard Leash 必須脫戰");
            Assert.AreEqual(0, combatFramesDuringCooldown, "冷卻內持續受傷也不得再交戰");
        }

        // =====================================================================
        // P3：巡邏
        // =====================================================================

        [UnityTest]
        public IEnumerator P3_YBot_PatrolsWaypointsInOrder()
        {
            yield return LoadShippedScene();
            CharacterPipelineRunner enemy = FindRunner("Y Bot");
            var territory = enemy.GetComponent<EnemyTerritory>();
            Vector3 home = enemy.transform.position;

            var points = new List<Transform>();
            for (int i = 0; i < 12 && points.Count < 3; i++)
            {
                Vector3 candidate = home + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * 3f;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.5f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - home.y) > 0.3f) continue;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(home, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                bool farEnough = true;
                foreach (Transform p in points) if (Vector3.Distance(p.position, hit.position) < 2.5f) farEnough = false;
                if (!farEnough) continue;
                var go = new GameObject($"TestWaypoint-{points.Count}");
                _created.Add(go);
                go.transform.position = hit.position;
                points.Add(go.transform);
            }
            Assert.AreEqual(3, points.Count, "前提：在家附近找得到 3 個可達的巡邏點");

            territory.ConfigureForTests(home, roam: 6f, soft: 14f, hard: 22f, cooldown: 2f, dwellMin: 0.3f, dwellMax: 0.3f,
                points: points.ToArray(), traversal: WaypointTraversal.Loop);
            // 讓非戰鬥行為從頭選（測試在場景啟動後才指定 waypoints）。
            typeof(AIMovementSource).GetField("_nonCombat", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(enemy.GetComponent<AIMovementSource>(), null);

            var arrivals = new List<int>();
            for (int frame = 0; frame < 2400 && arrivals.Count < 5; frame++)   // 40 s
            {
                yield return null;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 d = points[i].position - enemy.transform.position;
                    d.y = 0f;
                    if (d.magnitude <= 0.8f && (arrivals.Count == 0 || arrivals[arrivals.Count - 1] != i)) arrivals.Add(i);
                }
            }

            TestContext.WriteLine($"[NonCombat patrol] arrivals={string.Join(",", arrivals)}");
            Debug.Log($"[NonCombat patrol] arrivals={string.Join(",", arrivals)}");
            Assert.GreaterOrEqual(arrivals.Count, 4, "40 秒內至少抵達 4 次");
            for (int k = 1; k < arrivals.Count; k++)
                Assert.AreEqual((arrivals[k - 1] + 1) % 3, arrivals[k], $"Loop 巡邏必須依序：{string.Join(",", arrivals)}");
        }

        // =====================================================================
        // 尋路：牆後的目的地
        // =====================================================================

        [UnityTest]
        public IEnumerator Navigation_WaypointBehindWall_IsReachedByGoingAround()
        {
            Time.captureDeltaTime = FixedDelta;
            _testScene = SceneManager.CreateScene("EnemyNavigationWallTest");
            SceneManager.SetActiveScene(_testScene);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 1.5f, 5f);
            wall.transform.localScale = new Vector3(12f, 3f, 0.6f);

            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = ground.transform.localScale,
                    transform = Matrix4x4.TRS(ground.transform.position, Quaternion.identity, Vector3.one), area = 0 },
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = wall.transform.localScale,
                    transform = Matrix4x4.TRS(wall.transform.position, Quaternion.identity, Vector3.one), area = 0 },
            };
            NavMeshData navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Vector3.zero, new Vector3(50f, 10f, 50f)), Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(navData, "測試用 NavMesh 建置失敗");
            _navMesh = NavMesh.AddNavMeshData(navData);

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Y Bot.prefab");
            GameObject enemyObject = Object.Instantiate(prefab, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            foreach (Animator animator in enemyObject.GetComponentsInChildren<Animator>(true))
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var farTarget = new GameObject("FarTarget");
            farTarget.transform.position = new Vector3(18f, 0f, -18f);
            var waypoint = new GameObject("BehindWall");
            waypoint.transform.position = new Vector3(0f, 0f, 10f);
            var movement = enemyObject.GetComponent<AIMovementSource>();
            movement.SetTargetForTests(farTarget.transform);
            var territory = enemyObject.GetComponent<EnemyTerritory>();
            territory.ConfigureForTests(new Vector3(0f, 0.05f, 0f), roam: 15f, soft: 20f, hard: 25f, cooldown: 2f,
                dwellMin: 0.3f, dwellMax: 0.3f, points: new[] { waypoint.transform });

            float maxAbsX = 0f, closest = float.PositiveInfinity;
            int reachedFrame = -1;
            for (int frame = 0; frame < 900 && reachedFrame < 0; frame++)   // 15 s
            {
                yield return null;
                Vector3 p = enemyObject.transform.position;
                maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(p.x));
                Vector3 d = waypoint.transform.position - p;
                d.y = 0f;
                closest = Mathf.Min(closest, d.magnitude);
                if (d.magnitude <= 0.8f) reachedFrame = frame;
            }

            TestContext.WriteLine($"[NonCombat wall] reachedFrame={reachedFrame} closest={closest:F2} maxAbsX={maxAbsX:F2}");
            Debug.Log($"[NonCombat wall] reachedFrame={reachedFrame} closest={closest:F2} maxAbsX={maxAbsX:F2}");
            Assert.GreaterOrEqual(reachedFrame, 0, $"牆後的 waypoint 必須經尋路抵達（最近 {closest:F2} m）");
            Assert.Greater(maxAbsX, 5.5f, "必須繞過牆（牆寬 ±6 m），不是穿牆或直線推牆");
        }

        // =====================================================================
        // helpers
        // =====================================================================

        private IEnumerator LoadShippedScene()
        {
            Time.captureDeltaTime = FixedDelta;
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(_scene);
            for (int i = 0; i < 5; i++) yield return null;
        }

        private static void PlacePlayerOnSameFloor(CharacterPipelineRunner player, Vector3 home, float distance)
        {
            for (int i = 0; i < 16; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                Vector3 candidate = home + direction * distance;
                if (!Physics.Raycast(candidate + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 20f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (Mathf.Abs(ground.point.y - home.y) > 0.3f) continue;
                if (!NavMesh.SamplePosition(ground.point, out NavMeshHit hit, 0.3f, NavMesh.AllAreas)) continue;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(home, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                var controller = player.GetComponent<CharacterController>();
                controller.enabled = false;
                player.transform.position = hit.position + Vector3.up * 0.05f;
                controller.enabled = true;
                Physics.SyncTransforms();
                return;
            }
            Assert.Fail($"在家周圍 {distance} m 找不到同層、可完整走到的落腳點");
        }

        private CharacterPipelineRunner FindRunner(string name)
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
                foreach (CharacterPipelineRunner runner in root.GetComponentsInChildren<CharacterPipelineRunner>(true))
                    if (runner.gameObject.name == name) return runner;
            Assert.Fail($"場景裡找不到 {name}");
            return null;
        }
    }
}
