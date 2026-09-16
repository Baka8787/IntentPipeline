using System.Collections.Generic;
using Animancer;
using NUnit.Framework;
using Project.Core.Environment;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// Traversal V1 的 production asset 接線守衛。這組測試不執行 physics query，
    /// 只驗證 Config／Params／Animancer／Bake 的 authored references 沒有斷線。
    /// </summary>
    public sealed class TraversalIntegrationWiringTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/X Bot.prefab";
        private const string ConfigPath =
            "Assets/ScriptableObjects/StateMachine/PlayerStateMachineConfig.asset";
        private const string ParamsPath =
            "Assets/ScriptableObjects/StateMachine/TraversalStateParams.asset";

        [Test]
        public void PlayerConfig_TraversalOutranksJump_AndIsCommittedUntilFinished()
        {
            StateMachineConfigSO config = Load<StateMachineConfigSO>(ConfigPath);
            config.Initialize();

            Assert.Greater(config.GetPriority(StateType.Traversal), config.GetPriority(StateType.Jump));
            Assert.IsTrue(config.CheckCanInterrupt(StateType.Idle, StateType.Traversal));
            Assert.IsTrue(config.CheckCanInterrupt(StateType.Move, StateType.Traversal));

            // 🔄（`docs/26` Model B ＋ ADR-009，2026-09-14 使用者裁決）
            //    舊 invariant 是「Traversal committed 期間不得被**任何**狀態打斷」。
            //    新 baseline 多了**唯一一個**例外：**Death**。
            //    ⚠️ 這不是「暫停舊 invariant」，是舊 invariant 已被新 baseline 正式取代
            //    （CLAUDE.md：Trial 取代舊 invariant 時，同一工作包內把測試更新成新 baseline）。
            //    ⭐ 特別注意 **Hurt 仍然不得打斷 Traversal**——那是使用者明確裁決的，
            //       而且必須由 config 的清單表示，不是靠 priority 比大小。
            foreach (StateType next in (StateType[])System.Enum.GetValues(typeof(StateType)))
            {
                bool expected = next == StateType.Death;
                Assert.AreEqual(
                    expected,
                    config.CheckCanInterrupt(StateType.Traversal, next),
                    expected
                        ? "lethal damage 必須能在 traversal 途中直接進 Death"
                        : $"Traversal committed 期間不得被 {next} 打斷");
            }

            CollectionAssert.AreEqual(
                new[] { StateType.Jump, StateType.Move, StateType.Idle },
                config.GetValidTransitions(StateType.Traversal));
            Assert.AreSame(
                Load<TraversalStateParamsSO>(ParamsPath),
                config.GetStateParams<TraversalStateParamsSO>(StateType.Traversal));
        }

        [Test]
        public void TraversalParams_AllKindsHaveMatchingAnimationAndBakedMotion()
        {
            TraversalStateParamsSO stateParams = Load<TraversalStateParamsSO>(ParamsPath);

            AssertBinding(stateParams, TraversalKind.Vault1m, "Vault1m");
            AssertBinding(stateParams, TraversalKind.Climb1m, "Climb1m");
            AssertBinding(stateParams, TraversalKind.Climb2m, "Climb2m");
        }

        /// <summary>
        /// ⭐ 2026-09-13：Hand IK 曾經**整條是死路徑**——兩個 MonoBehaviour 被寫成
        /// `FootIKController.cs`／`FootIKRig.cs` 裡的次要類別，Unity 根本無法 AddComponent，
        /// 而 bake 的 `handIKEnabled` 也是 false。結果就是「有程式、有測試、但遊戲裡從來沒跑過」。
        ///
        /// 這條測試守住**實際會執行**的三件事：元件掛在玩家 prefab 上、`motionDriver` 有接、
        /// 以及出貨的 bake 真的啟用了 IK 窗。少任何一項，root warp 對齊完之後就沒有東西
        /// 把手釘在牆上，手會跟著動畫穿進穿出。
        /// </summary>
        [Test]
        public void PlayerPrefab_TraversalHandIK_IsAttachedWiredAndAuthored()
        {
            GameObject prefab = Load<GameObject>(PlayerPrefabPath);

            var controller = prefab.GetComponent<Project.Presentation.IK.TraversalHandIKController>();
            Assert.IsNotNull(controller,
                "玩家 prefab 沒有 TraversalHandIKController ⇒ traversal 期間沒有任何東西釘住手");
            Assert.IsTrue(controller.IsEnabled, "TraversalHandIKController 被停用");

            var driverField = new SerializedObject(controller).FindProperty("motionDriver");
            Assert.IsNotNull(driverField.objectReferenceValue,
                "TraversalHandIKController.motionDriver 沒接 ⇒ 執行期一定停在 NotBound");

            var rig = prefab.GetComponentInChildren<Project.Presentation.IK.TraversalHandIKRig>(true);
            Assert.IsNotNull(rig, "Model 上沒有 TraversalHandIKRig ⇒ OnAnimatorIK 不會寫入手部 goal");
            Assert.IsNotNull(rig.GetComponent<Animator>(),
                "TraversalHandIKRig 必須與 Animator 同物件");

            TraversalStateParamsSO stateParams = Load<TraversalStateParamsSO>(ParamsPath);
            foreach (TraversalKind kind in new[]
                     { TraversalKind.Vault1m, TraversalKind.Climb1m, TraversalKind.Climb2m })
            {
                if (!stateParams.IsKindEnabled(kind)) continue;
                MotionBakeData bake = stateParams.GetBinding(kind).Bake;
                if (bake == null || !bake.Traversal.HasValidBakedData) continue;
                Assert.IsTrue(bake.Traversal.HasValidHandIK,
                    $"{kind} 的 bake 沒有有效的 Hand IK 窗 ⇒ 接觸之後手沒有東西釘住");
            }
        }

        [Test]
        public void PlayerPrefab_UsesPlayerConfig_AndResolvesAllTraversalTransitions()
        {
            GameObject prefab = Load<GameObject>(PlayerPrefabPath);
            StateMachineConfigSO expectedConfig = Load<StateMachineConfigSO>(ConfigPath);

            CharacterPipelineRunner runner = prefab.GetComponent<CharacterPipelineRunner>();
            Assert.IsNotNull(runner, "X Bot root 缺少 CharacterPipelineRunner");
            var runnerObject = new SerializedObject(runner);
            Assert.AreSame(
                expectedConfig,
                runnerObject.FindProperty("stateMachineConfig").objectReferenceValue,
                "X Bot 必須使用已接 Traversal rule 的 PlayerStateMachineConfig");

            AnimancerFacade facade = prefab.GetComponent<AnimancerFacade>();
            Assert.IsNotNull(facade, "X Bot root 缺少 AnimancerFacade");
            var facadeObject = new SerializedObject(facade);
            SerializedProperty mappings = facadeObject.FindProperty("transitionMappings");

            AssertTransitionMapping(mappings, "Vault1m");
            AssertTransitionMapping(mappings, "Climb1m");
            AssertTransitionMapping(mappings, "Climb2m");
        }

        private static void AssertBinding(
            TraversalStateParamsSO stateParams,
            TraversalKind kind,
            string expectedKey)
        {
            TraversalMotionBinding binding = stateParams.GetBinding(kind);
            Assert.AreEqual(expectedKey, binding.AnimationKey);
            Assert.IsNotNull(binding.Bake, $"{kind} 缺少 MotionBakeData");
            Assert.IsNotNull(binding.Bake.SourceClip, $"{kind} bake 缺少 SourceClip");
            Assert.AreEqual(expectedKey, binding.Bake.SourceClip.name);
            Assert.Greater(binding.Bake.Duration, 0f, $"{kind} bake duration 必須大於 0");
            Assert.IsNotNull(binding.Bake.SpeedCurve, $"{kind} 缺少水平 motion curve");
            Assert.Greater(binding.Bake.SpeedCurve.length, 1, $"{kind} 水平 motion curve 不可為空");
            Assert.IsNotNull(binding.Bake.VerticalCurve, $"{kind} 缺少 VerticalCurve");
            Assert.Greater(binding.Bake.VerticalCurve.length, 1, $"{kind} VerticalCurve 不可為空");
            AssertCurveHasMotion(binding.Bake.SpeedCurve, 0.1f, $"{kind} 水平 motion curve 不可為平線");
            AssertCurveHasMotion(binding.Bake.VerticalCurve, 0.5f, $"{kind} VerticalCurve 必須含實際爬升");
        }

        private static void AssertTransitionMapping(SerializedProperty mappings, string expectedKey)
        {
            var matches = new List<TransitionAssetBase>();
            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty mapping = mappings.GetArrayElementAtIndex(i);
                if (mapping.FindPropertyRelative("StateKey").stringValue != expectedKey) continue;

                matches.Add(
                    mapping.FindPropertyRelative("Transition").objectReferenceValue as TransitionAssetBase);
                Assert.AreEqual(0, mapping.FindPropertyRelative("LayerIndex").intValue);
            }

            Assert.AreEqual(1, matches.Count, $"AnimancerFacade 的 '{expectedKey}' mapping 必須恰好一筆");
            Assert.IsNotNull(matches[0], $"AnimancerFacade 的 '{expectedKey}' transition 不可為 null");
            Assert.IsTrue(matches[0].IsValid, $"AnimancerFacade 的 '{expectedKey}' transition 必須有效");
            Assert.IsInstanceOf<TransitionAsset>(matches[0]);
            var transitionAsset = (TransitionAsset)matches[0];
            Assert.IsInstanceOf<ClipTransition>(transitionAsset.Transition);
            Assert.AreEqual(expectedKey, ((ClipTransition)transitionAsset.Transition).Clip.name);
        }

        private static T Load<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"找不到 production asset：{path}");
            return asset;
        }

        private static void AssertCurveHasMotion(AnimationCurve curve, float minimumPeak, string message)
        {
            float peak = float.NegativeInfinity;
            for (int i = 0; i < curve.length; i++)
                peak = Mathf.Max(peak, curve.keys[i].value);

            Assert.Greater(peak, minimumPeak, message);
        }
    }
}
