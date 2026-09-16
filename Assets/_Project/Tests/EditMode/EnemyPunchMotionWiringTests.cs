using Animancer;
using NUnit.Framework;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// Enemy Punch 的「原地動作」不是 AI policy：ActionState 只要拿到有效 Bake，
    /// 就會走既有 baked-motion 分支，而不消費 LocomotionModel 留在 blackboard 的水平移動。
    /// 這組測試鎖住資產接線與零水平位移，同時保護玩家法術既有的 no-Bake fallback。
    /// </summary>
    public sealed class EnemyPunchMotionWiringTests
    {
        private const string DefinitionPath =
            "Assets/ScriptableObjects/StateMachine/Actions/EnemyPunchDefinition.asset";
        private const string TransitionPath =
            "Assets/ScriptableObjects/Animation/Enemy_Punch_R.asset";
        private const string BakePath =
            "Assets/ScriptableObjects/Motion/Bake_Fists_Punch_R.asset";

        [Test]
        public void EnemyPunch_StartPhase_UsesBakeFromMappedTransitionClip()
        {
            ActionDefinitionSO definition = LoadRequired<ActionDefinitionSO>(DefinitionPath);
            ActionPhaseEntry start = FindStart(definition);
            MotionBakeData bake = LoadRequired<MotionBakeData>(BakePath);
            TransitionAsset transitionAsset = LoadRequired<TransitionAsset>(TransitionPath);

            Assert.AreSame(bake, start.Bake,
                "Enemy Punch Start 缺少 Bake 時，ActionState 會退回 ExecuteBaseMovement 並消費 AI locomotion。 ");
            Assert.AreEqual("Enemy_Punch_R", start.AnimationKey);
            Assert.IsTrue(transitionAsset.HasTransition);
            Assert.That(transitionAsset.Transition, Is.TypeOf<ClipTransition>());

            var transition = (ClipTransition)transitionAsset.Transition;
            Assert.IsNotNull(transition.Clip);
            Assert.AreSame(transition.Clip, bake.SourceClip,
                "Bake provenance 必須與 Enemy_Punch_R transition 使用同一支 FBX sub-clip。");
            Assert.That(bake.Duration, Is.EqualTo(transition.Clip.length).Within(0.0001f),
                "Bake duration 是 clip duration 的快照；clip 變更後必須重烘焙。");
        }

        [Test]
        public void EnemyPunch_Bake_HasNearZeroHorizontalDisplacement()
        {
            MotionBakeData bake = LoadRequired<MotionBakeData>(BakePath);

            Assert.Greater(bake.Duration, 0f, "有效 duration 是 ActionState 選擇 baked-motion 分支的必要條件。");
            Assert.IsNotNull(bake.SpeedCurve);
            Assert.Greater(bake.SpeedCurve.length, 0);
            Assert.That(bake.GetRepresentativeSpeed(), Is.LessThanOrEqualTo(0.001f));
            Assert.That(Mathf.Abs(bake.TargetLocalDirection.x), Is.LessThanOrEqualTo(0.0001f));
            Assert.That(Mathf.Abs(bake.TargetLocalDirection.z), Is.LessThanOrEqualTo(0.0001f));

            for (int i = 0; i < bake.SpeedCurve.length; i++)
            {
                Assert.That(Mathf.Abs(bake.SpeedCurve[i].value), Is.LessThanOrEqualTo(0.001f),
                    $"Enemy Punch speed sample {i} 不應產生可見的 gameplay 水平位移。");
            }
        }

        [TestCase("Assets/ScriptableObjects/StateMachine/Actions/FireballDefinition.asset")]
        [TestCase("Assets/ScriptableObjects/StateMachine/Actions/IceSpellDefinition.asset")]
        public void PlayerSpellDefinitions_RemainWithoutBake(string definitionPath)
        {
            ActionDefinitionSO definition = LoadRequired<ActionDefinitionSO>(definitionPath);
            AssertEntriesHaveNoBake(definition.Phases, definition.name + ".Phases");
            AssertEntriesHaveNoBake(definition.ChainSegments, definition.name + ".ChainSegments");
        }

        private static ActionPhaseEntry FindStart(ActionDefinitionSO definition)
        {
            Assert.IsNotNull(definition.Phases);
            for (int i = 0; i < definition.Phases.Length; i++)
            {
                if (definition.Phases[i].Phase == ActionPhase.Start)
                    return definition.Phases[i];
            }

            Assert.Fail($"{definition.name} 缺少 Start phase。");
            return default;
        }

        private static void AssertEntriesHaveNoBake(ActionPhaseEntry[] entries, string label)
        {
            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
                Assert.IsNull(entries[i].Bake, $"{label}[{i}] 必須維持 no-Bake movement fallback。");
        }

        private static T LoadRequired<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"找不到資產：{path}");
            return asset;
        }
    }
}
