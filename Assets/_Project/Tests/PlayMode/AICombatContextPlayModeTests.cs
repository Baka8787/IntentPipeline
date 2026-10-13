using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Combat;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.Survivability;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🆕（2026-10-12，`docs/54` §3.3.2／§3.4，WP-1b＋WP-2）**出貨場景 × 真 Runner** 的敵人交戰驗收。
    ///
    /// EditMode 的 <c>AICombatContextSourceTests</c> 依 Runner 的順序手動呼叫兩顆元件；這裡改由
    /// <c>CharacterPipelineRunner.Update</c> 自己排程——**管線順序寫錯（combat 排回 movement 之後），
    /// 只有這一層會紅**。同時它讀的是磁碟上的 <c>SampleScene</c>：Y Bot 的 target 是場景 override，
    /// 只有真的載入場景才能證明遷移後仍解析到 X Bot（不能只看 scene diff）。
    ///
    /// 場景事實（2026-10-12 live Editor 讀出）：X Bot 與 Y Bot 出生點水平相距約 18.6 m——
    /// 在 12 m（未受擊 leave）之外、20 m（Provoked L）之內，正好分辨兩條規則，不需要搬動角色。
    /// </summary>
    public sealed class AICombatContextPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const int MaximumWaitFrames = 240;

        private Scene _scene;

        private IEnumerator LoadScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            Assert.IsNotNull(load, ScenePath);
            yield return load;
            _scene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(_scene);
            // 讓所有 Awake／Start 跑完、黑板注入完成。
            for (int i = 0; i < 5; i++) yield return null;
        }

        private IEnumerator UnloadScene()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTearDown]
        public IEnumerator EnsureSceneUnloaded()
        {
            yield return UnloadScene();
        }

        /// <summary>
        /// 驗收⑦／AC-B.2：出貨場景的 Y Bot 解析到場景裡的 X Bot，而且三顆 AI 元件看的是同一個。
        /// 驗收③（未受擊半邊）：18.6 m、未受擊 ⇒ 持續不交戰（8／12 m 規則不變）。
        /// 驗收③（Provoked 半邊）：Y Bot 受到未致死傷害 ⇒ **當帧**交戰，且在 18.6 m（> 12 m）持續交戰。
        /// </summary>
        [UnityTest]
        public IEnumerator YBot_InShippedScene_ResolvesXBot_AndOnlyHitsProvokeBeyondTwelveMetres()
        {
            yield return LoadScene();
            CharacterPipelineRunner player = FindRunner("X Bot");
            CharacterPipelineRunner enemy = FindRunner("Y Bot");

            AIMovementSource movement = enemy.GetComponent<AIMovementSource>();
            AICombatContextSource combat = enemy.GetComponent<AICombatContextSource>();
            AIInputSource attack = enemy.GetComponent<AIInputSource>();
            Assert.IsNotNull(movement, "Y Bot 沒有 AIMovementSource");
            Assert.IsNotNull(combat, "Y Bot 沒有 AICombatContextSource（prefab 遷移未生效）");
            Assert.IsNotNull(attack, "Y Bot 沒有 AIInputSource");
            Assert.IsNull(enemy.GetComponent<PlayerCombatContextSource>(), "敵人不得掛玩家的 combat producer");

            Assert.IsNotNull(movement.Target, "場景的 target override 沒有解析出來");
            Assert.AreSame(player, movement.Target.GetComponentInParent<CharacterPipelineRunner>(),
                $"Y Bot 的 target 是 '{movement.Target.name}'，不屬於場景裡的 X Bot");

            PlayerRuntimeData enemyData = RuntimeData(enemy);
            float distance = HorizontalDistance(player.transform.position, enemy.transform.position);
            TestContext.WriteLine($"[AICombatPlayMode] spawn distance {distance:F2} m");
            Assume.That(distance > 12.5f && distance < 19.5f,
                $"場景出生距離 {distance:F2} m 不在 (12.5, 19.5)——本測項的場景前提已改變");

            for (int i = 0; i < 30; i++)
            {
                yield return null;
                Assert.IsFalse(enemyData.CombatContext.InCombat,
                    $"未受擊、{distance:F2} m（> 8 m）⇒ 不得進入交戰（第 {i} 帧）");
                // 🔄（2026-10-13，docs/55 P1–P3）未交戰時會遊走：意圖若存在，必須是非戰鬥行為（walk 速度），不是追擊。
                if (enemyData.MovementIntent.HasMovementRequest)
                {
                    Assert.AreNotEqual(EnemyNonCombatMode.None, movement.NonCombatMode, "未交戰的移動只能來自非戰鬥行為");
                    Assert.Less(enemyData.MovementIntent.DesiredSpeedNormalized, 0.5f, "非戰鬥速度是 walk，不是追擊");
                }
            }

            CharacterHealth enemyHealth = enemy.GetComponent<CharacterHealth>();
            Assert.IsNotNull(enemyHealth, "Y Bot 沒有 CharacterHealth");
            Assert.IsTrue(enemyHealth.ApplyDamage(1f), "前提：未致死傷害要能結算");

            yield return null;
            Assert.IsTrue(enemyData.CombatContext.InCombat, "受擊（任何距離）⇒ 當帧進入交戰（Provoked）");
            Assert.IsTrue(enemyData.CombatContext.HasTarget);
            Assert.Less(Vector3.Distance(player.transform.position, enemyData.CombatContext.TargetPosition), 0.5f,
                "CombatContext 的目標位置必須是 X Bot");

            for (int i = 0; i < 60; i++)
            {
                yield return null;
                float now = HorizontalDistance(player.transform.position, enemy.transform.position);
                if (now > 12f)
                {
                    Assert.IsTrue(enemyData.CombatContext.InCombat,
                        $"受過傷 ⇒ {now:F2} m（> 12 m）仍在交戰（R＝L＝20 m）；第 {i} 帧");
                }
            }

            yield return UnloadScene();
        }

        /// <summary>
        /// 驗收①（真 Runner）：玩家死亡的**那一帧**，Y Bot 的 CombatContext 解除、移動意圖歸零，
        /// 攻擊來源也不再想出手。若 Runner 把 combat 排回 movement 之後，這一帧的移動意圖會是非零。
        /// </summary>
        [UnityTest]
        public IEnumerator YBot_StopsChasingOnTheFrameThePlayerDies()
        {
            yield return LoadScene();
            CharacterPipelineRunner player = FindRunner("X Bot");
            CharacterPipelineRunner enemy = FindRunner("Y Bot");
            PlayerRuntimeData enemyData = RuntimeData(enemy);
            AIInputSource attack = enemy.GetComponent<AIInputSource>();

            // 把玩家放到敵人前方 4 m（在 8 m 內 ⇒ 未受擊也會交戰），沿兩者原本的連線，貼地。
            Vector3 toPlayer = player.transform.position - enemy.transform.position;
            toPlayer.y = 0f;
            Vector3 spot = enemy.transform.position + toPlayer.normalized * 4f;
            Assert.IsTrue(Physics.Raycast(spot + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 20f,
                    ~0, QueryTriggerInteraction.Ignore),
                "找不到玩家落腳的地面");
            Teleport(player, ground.point + Vector3.up * 0.05f);

            bool chasing = false;
            for (int i = 0; i < MaximumWaitFrames && !chasing; i++)
            {
                yield return null;
                chasing = enemyData.CombatContext.InCombat
                          && enemyData.MovementIntent.DesiredSpeedNormalized > 0f;
            }
            Assert.IsTrue(chasing, "前提：玩家在 4 m 內，Y Bot 必須進入交戰並產生移動意圖");

            CharacterHealth playerHealth = player.GetComponent<CharacterHealth>();
            Assert.IsNotNull(playerHealth, "X Bot 沒有 CharacterHealth");
            Assert.IsTrue(playerHealth.ApplyDamage(1000000f));
            Assert.IsTrue(playerHealth.IsDead, "前提：玩家已死");

            yield return null;   // 死亡後的第一個 Update：Runner 依 2.4 → 2.5 跑完
            Assert.AreEqual(default(CombatContextData), enemyData.CombatContext,
                "玩家死亡的那一帧，Y Bot 的 CombatContext 必須整體解除（不給寬限）");
            // 🔄（2026-10-13，docs/55 P1）Y Bot 有 EnemyTerritory 之後，脫戰的那一帧起由非戰鬥行為（回家／遊走）
            //    給意圖，不再是「站著」。這條要守的事沒變：**同一帧不再追擊屍體**——意圖若存在，必須來自
            //    非戰鬥行為（模式非 None、速度＝walk），不得是交戰的 Approach（desiredSpeedNormalized＝1）。
            var movement = enemy.GetComponent<AIMovementSource>();
            if (enemyData.MovementIntent.HasMovementRequest)
            {
                Assert.AreNotEqual(EnemyNonCombatMode.None, movement.NonCombatMode,
                    "同一帧的移動意圖只能來自非戰鬥行為——不得朝屍體多追一帧");
                Assert.Less(enemyData.MovementIntent.DesiredSpeedNormalized, 0.5f,
                    "非戰鬥速度是 walk（≈0.263），不是追擊的 1");
            }
            Assert.IsFalse(attack.WantsToAttack(), "同一個目標已死 ⇒ 攻擊來源也不出手（同一謂詞、同一目標）");

            for (int i = 0; i < 30; i++)
            {
                yield return null;
                Assert.IsFalse(enemyData.CombatContext.InCombat, $"屍體不是目標（第 {i} 帧）");
            }

            yield return UnloadScene();
        }

        private CharacterPipelineRunner FindRunner(string name)
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
                foreach (CharacterPipelineRunner runner in root.GetComponentsInChildren<CharacterPipelineRunner>(true))
                    if (runner.gameObject.name == name) return runner;
            Assert.Fail($"場景裡找不到 {name}");
            return null;
        }

        private static PlayerRuntimeData RuntimeData(CharacterPipelineRunner runner)
        {
            FieldInfo field = typeof(CharacterPipelineRunner).GetField("_runtimeData", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "CharacterPipelineRunner._runtimeData 不存在（欄位名稱可能已變更）");
            var data = (PlayerRuntimeData)field.GetValue(runner);
            Assert.IsNotNull(data, $"{runner.name} 的黑板尚未建立");
            return data;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            Vector3 d = a - b;
            d.y = 0f;
            return d.magnitude;
        }

        private static void Teleport(CharacterPipelineRunner runner, Vector3 position)
        {
            var controller = runner.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            runner.transform.position = position;
            if (controller != null) controller.enabled = true;
            Physics.SyncTransforms();
        }
    }
}
