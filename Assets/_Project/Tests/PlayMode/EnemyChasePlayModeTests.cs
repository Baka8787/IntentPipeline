using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Pipeline;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🆕（2026-10-13，`docs/55` P0.6／P0.7）**追擊不退步的量測基準。**
    ///
    /// 出貨場景、真 Runner、真 NavMesh、固定 60 fps：把玩家放到 Y Bot 前方 6 m（未受擊 aggro 8 m 內），
    /// 量 Y Bot 抵達近戰帶上界（1.6 m＋容差）所需的帧數。導航基礎修正（P0）前後各跑一次，
    /// 這個數字不得變差；同時記錄追擊期間的路徑請求次數（P0 之前沒有計數器，記為 n/a）。
    /// </summary>
    public sealed class EnemyChasePlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const float FixedDelta = 1f / 60f;
        private const float StartDistance = 6f;
        private const float ArriveDistance = 1.7f;
        private const int MaximumFrames = 360;   // 6 s

        private Scene _scene;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        /// <summary>P0 之前在本測試量到的抵達帧數（2026-10-13，三次一致：165 帧＝2.75 s）。</summary>
        private const int BaselineArrivedFrame = 165;

        [UnityTest]
        public IEnumerator YBot_ChasesToMeleeBand_WithinBaselineTime()
        {
            yield return LoadAndPlacePlayer();
            CharacterPipelineRunner player = FindRunner("X Bot");
            CharacterPipelineRunner enemy = FindRunner("Y Bot");
            PlayerRuntimeData enemyData = enemy.RuntimeData;

            int requestsBefore = ReadRequestCount(enemy);
            int arrivedFrame = -1;
            int inCombatFrames = 0;
            for (int frame = 0; frame < MaximumFrames; frame++)
            {
                yield return null;
                if (enemyData.CombatContext.InCombat) inCombatFrames++;
                if (HorizontalDistance(player.transform.position, enemy.transform.position) <= ArriveDistance)
                {
                    arrivedFrame = frame;
                    break;
                }
            }
            int requestsAfter = ReadRequestCount(enemy);

            string requests = requestsBefore < 0
                ? "n/a（P0 前沒有計數器；舊程式每帧請求一次）"
                : $"{requestsAfter - requestsBefore}";
            string summary = $"[EnemyChase] arrivedFrame={arrivedFrame} ({arrivedFrame * FixedDelta:F2}s) " +
                             $"inCombatFrames={inCombatFrames} pathRequests={requests} spot={player.transform.position.x:F2},{player.transform.position.z:F2}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);

            Assert.GreaterOrEqual(arrivedFrame, 0, $"Y Bot 在 {MaximumFrames} 帧內沒有追到 {ArriveDistance} m 內");
            Assert.Greater(inCombatFrames, 0, "前提：追擊期間必須在交戰中");
            Assert.LessOrEqual(arrivedFrame, BaselineArrivedFrame + 2,
                $"追擊變慢了：基準 {BaselineArrivedFrame} 帧（P0 前，三次量測一致），本次 {arrivedFrame} 帧");
            if (requestsBefore >= 0)
            {
                Assert.LessOrEqual(requestsAfter - requestsBefore, arrivedFrame / 3 + 1,
                    "路徑請求必須被節流（舊版每帧一次）");
            }
        }

        private IEnumerator LoadAndPlacePlayer()
        {
            Time.captureDeltaTime = FixedDelta;
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(_scene);
            for (int i = 0; i < 5; i++) yield return null;

            CharacterPipelineRunner player = FindRunner("X Bot");
            CharacterPipelineRunner enemy = FindRunner("Y Bot");

            // 在敵人同一層地面上找一個 NavMesh 可走、而且直線上方沒有東西的點（8 個方位依序試）。
            Vector3 origin = enemy.transform.position;
            Vector3 spotPosition = default;
            bool found = false;
            RaycastHit ground = default;
            for (int i = 0; i < 8 && !found; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                Vector3 candidate = origin + direction * StartDistance;
                if (!Physics.Raycast(candidate + Vector3.up * 5f, Vector3.down, out ground, 20f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (Mathf.Abs(ground.point.y - origin.y) > 0.3f) continue;
                if (!UnityEngine.AI.NavMesh.SamplePosition(ground.point, out UnityEngine.AI.NavMeshHit hit, 0.3f, UnityEngine.AI.NavMesh.AllAreas))
                    continue;
                spotPosition = hit.position;
                found = true;
            }
            Assert.IsTrue(found, "在敵人周圍 6 m 找不到同層、可走的落腳點");
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = spotPosition + Vector3.up * 0.05f;
            controller.enabled = true;
            Physics.SyncTransforms();

        }

        /// <summary>
        /// （P0.8）**真 producer＋真 navigator 不配置記憶體。** 在同一帧內直接呼叫 <c>ProduceIntent</c> 很多次，
        /// 量這段同步程式碼的配置量——不受引擎其他系統的配置干擾。敵人在交戰中、站在真 NavMesh 上。
        /// </summary>
        [UnityTest]
        public IEnumerator YBot_MovementProducer_DoesNotAllocatePerCall()
        {
            yield return LoadAndPlacePlayer();
            CharacterPipelineRunner enemy = FindRunner("Y Bot");
            for (int i = 0; i < 30; i++) yield return null;
            Assert.IsTrue(enemy.RuntimeData.CombatContext.InCombat, "前提：敵人在交戰中（會走到路徑查詢）");

            var movement = enemy.GetComponent<AIMovementSource>();
            PlayerRuntimeData data = enemy.RuntimeData;
            InputData input = default;
            for (int i = 0; i < 10; i++) movement.ProduceIntent(ref input, data);   // 暖身

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) movement.ProduceIntent(ref input, data);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

            TestContext.WriteLine($"[EnemyChase] ProduceIntent ×1000 allocated {allocated} bytes");
            Assert.AreEqual(0, allocated, "敵人移動 producer（含 EnemyNavigator）每次呼叫都不得配置記憶體");
        }

        /// <summary>P0 之後 <c>EnemyNavigator</c> 有請求計數器；P0 之前回 -1（不存在）。</summary>
        private static int ReadRequestCount(CharacterPipelineRunner enemy)
        {
            var movement = enemy.GetComponent<AIMovementSource>();
            FieldInfo navigatorField = typeof(AIMovementSource).GetField("_navigator", BindingFlags.NonPublic | BindingFlags.Instance);
            object navigator = navigatorField?.GetValue(movement);
            if (navigator == null) return -1;
            PropertyInfo count = navigator.GetType().GetProperty("PathRequestCount", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return count != null ? (int)count.GetValue(navigator) : -1;
        }

        private CharacterPipelineRunner FindRunner(string name)
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
                foreach (CharacterPipelineRunner runner in root.GetComponentsInChildren<CharacterPipelineRunner>(true))
                    if (runner.gameObject.name == name) return runner;
            Assert.Fail($"場景裡找不到 {name}");
            return null;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            Vector3 d = a - b;
            d.y = 0f;
            return d.magnitude;
        }
    }
}
