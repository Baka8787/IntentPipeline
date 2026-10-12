using NUnit.Framework;
using Project.Core.Combat;
using Project.Core.Movement;
using Project.Core.Pipeline;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-10-12，`docs/54` AC-B.2）**出貨場景的 Y Bot 實例，經唯一權威解析到場景裡的 X Bot。**
    ///
    /// 方案③ 只改 prefab、不碰場景：新元件經 prefab 繼承出現在場景實例上，target 仍是場景對
    /// <c>AIMovementSource.target</c> 的 override。這條讀**磁碟上的**場景（唯讀開啟、不存檔）來證明，
    /// 而不是靠 scene diff 推論。執行期的同一件事由 <c>AICombatContextPlayModeTests</c> 再驗一次。
    /// </summary>
    public sealed class AICombatContextSceneTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        private Scene _scene;
        private bool _openedHere;

        [OneTimeTearDown]
        public void CloseScene()
        {
            if (_openedHere && _scene.IsValid()) EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void AB2_ShippedScene_YBotResolvesXBotThroughTheSingleTargetAuthority()
        {
            _scene = SceneManager.GetSceneByPath(ScenePath);
            _openedHere = false;
            if (!_scene.IsValid() || !_scene.isLoaded)
            {
                _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                _openedHere = true;
            }
            else if (_scene.isDirty)
            {
                Assert.Inconclusive("SampleScene 有未存檔的變更——驗證對象是磁碟上的場景");
            }

            CharacterPipelineRunner player = FindRunner("X Bot");
            CharacterPipelineRunner enemy = FindRunner("Y Bot");

            Assert.IsNotNull(enemy.GetComponent<AICombatContextSource>(),
                "場景的 Y Bot 沒有 AICombatContextSource——prefab 遷移沒有經繼承生效");
            Assert.IsNull(enemy.GetComponent<PlayerCombatContextSource>());

            AIMovementSource movement = enemy.GetComponent<AIMovementSource>();
            Assert.IsNotNull(movement);
            Assert.IsNotNull(movement.Target, "場景對 AIMovementSource.target 的 override 沒有解析出來");
            Assert.AreSame(player, movement.Target.GetComponentInParent<CharacterPipelineRunner>(),
                $"Y Bot 的 target '{movement.Target.name}' 不屬於場景裡的 X Bot");
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
