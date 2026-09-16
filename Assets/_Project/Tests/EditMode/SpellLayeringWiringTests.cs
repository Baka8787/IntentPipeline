using System.Collections.Generic;
using Animancer;
using NUnit.Framework;
using Project.Core.Movement;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// ADR-006 Trial 的 authored wiring 護欄：State 仍只送 key，layer／mask／Layer 0 companion
    /// 全部由玩家 prefab 的 TransitionMapping 決定。
    /// </summary>
    public class SpellLayeringWiringTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/X Bot.prefab";
        private const string SpellWalkRunPath =
            "Assets/ScriptableObjects/Animation/Locomotion_2D_Spell_WalkRun.asset";
        private const string BaselineLocomotionPath =
            "Assets/ScriptableObjects/Animation/Locomotion.asset";
        private const string MaximumSpeedBakePath =
            "Assets/ScriptableObjects/Motion/Bake_SprintFwdLoop.asset";
        private const string GaitProfilePath =
            "Assets/ScriptableObjects/Movement/Gait_ActionRPG.asset";
        private const string UpperBodyMaskPath =
            "Assets/Kevin Iglesias/Human Animations/Models/Avatar Masks/Human Body Upper Mask.mask";
        private const string FireballDefinitionPath =
            "Assets/ScriptableObjects/StateMachine/Actions/FireballDefinition.asset";
        private const string IceDefinitionPath =
            "Assets/ScriptableObjects/StateMachine/Actions/IceSpellDefinition.asset";

        private static readonly Dictionary<string, string> SpellTransitions = new()
        {
            { "Spell_Fireball_1", "Assets/ScriptableObjects/Animation/Spell_Fireball_1.asset" },
            { "Spell_Fireball_2", "Assets/ScriptableObjects/Animation/Spell_Fireball_2.asset" },
            { "Spell_Fireball_3", "Assets/ScriptableObjects/Animation/Spell_Fireball_3.asset" },
            { "Spell_Ice", "Assets/ScriptableObjects/Animation/Spell_Ice.asset" }
        };

        [Test]
        public void SL1_PlayerSpellMappings_UseUpperBodyLayerAndWalkRunDirectionalMixer()
        {
            SerializedProperty mappings = LoadMappings();
            Object expectedMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            Object expectedMixer = AssetDatabase.LoadAssetAtPath<Object>(SpellWalkRunPath);
            Assert.IsNotNull(expectedMask, $"找不到 upper-body mask：{UpperBodyMaskPath}");
            Assert.IsNotNull(expectedMixer, $"找不到 Spell Walk／Run 8-way mixer：{SpellWalkRunPath}");

            foreach (KeyValuePair<string, string> expected in SpellTransitions)
            {
                SerializedProperty mapping = FindMapping(mappings, expected.Key);
                Assert.AreEqual(1, mapping.FindPropertyRelative("LayerIndex").intValue,
                    $"{expected.Key} 必須播放在 Layer 1");
                Assert.AreSame(expectedMask, mapping.FindPropertyRelative("AvatarMask").objectReferenceValue,
                    $"{expected.Key} 必須使用同一份 upper-body AvatarMask");
                Assert.AreSame(expectedMixer,
                    mapping.FindPropertyRelative("BaseLayerTransition").objectReferenceValue,
                    $"{expected.Key} 必須讓 Layer 0 使用 Walk／Run 分環、仍由 MoveX／MoveZ 驅動的 mixer");

                Object expectedTransition = AssetDatabase.LoadAssetAtPath<Object>(expected.Value);
                Assert.AreSame(expectedTransition,
                    mapping.FindPropertyRelative("Transition").objectReferenceValue,
                    $"{expected.Key} 不得換掉原本 spell transition");
            }
        }

        [Test]
        public void SL2_PlayerNormalLocomotion_RemainsOnBaseLayer()
        {
            SerializedProperty mappings = LoadMappings();
            foreach (string key in new[] { "Idle", "Move" })
            {
                SerializedProperty mapping = FindMapping(mappings, key);
                Assert.AreEqual(0, mapping.FindPropertyRelative("LayerIndex").intValue);
                Assert.IsNull(mapping.FindPropertyRelative("AvatarMask").objectReferenceValue);
                Assert.IsNull(mapping.FindPropertyRelative("BaseLayerTransition").objectReferenceValue);
            }
        }

        [Test]
        public void SL3_UpperBodyMask_ExcludesRootAndLegs()
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            Assert.IsNotNull(mask);
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK));
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body));
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm));
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm));
        }

        [Test]
        public void SL4_PlayerSpellDefinitions_RequestCameraConeSoftTarget()
        {
            ActionDefinitionSO fireball =
                AssetDatabase.LoadAssetAtPath<ActionDefinitionSO>(FireballDefinitionPath);
            ActionDefinitionSO ice = AssetDatabase.LoadAssetAtPath<ActionDefinitionSO>(IceDefinitionPath);
            Assert.IsNotNull(fireball);
            Assert.IsNotNull(ice);
            Assert.AreEqual(ActionTargetingPolicy.CameraConeSoftTarget, fireball.Targeting);
            Assert.AreEqual(ActionTargetingPolicy.CameraConeSoftTarget, ice.Targeting);
        }

        [Test]
        public void SL5_SpellDirectionalMixer_UsesBakeDerivedDirectionalSpeedWithinPlaybackBudget()
        {
            TransitionAsset asset = AssetDatabase.LoadAssetAtPath<TransitionAsset>(SpellWalkRunPath);
            Assert.IsNotNull(asset);
            Assert.IsInstanceOf<MixerTransition2D>(asset.Transition);
            var mixer = (MixerTransition2D)asset.Transition;

            TransitionAsset baseline = AssetDatabase.LoadAssetAtPath<TransitionAsset>(BaselineLocomotionPath);
            Assert.IsNotNull(baseline);
            Assert.IsInstanceOf<LinearMixerTransition>(baseline.Transition);
            var linear = (LinearMixerTransition)baseline.Transition;
            float walkRadius = FindThreshold(linear, "WalkFwdLoop");
            float runRadius = FindThreshold(linear, "RunFwdLoop");

            Assert.AreEqual(0.35f, walkRadius, 0.00001f);
            Assert.AreEqual(0.75f, runRadius, 0.00001f);
            Assert.AreEqual(17, mixer.Animations.Length, "中心 Idle ＋ Walk 8-way ＋ Run 8-way");
            Assert.AreEqual(17, mixer.Thresholds.Length);
            Assert.AreEqual(17, mixer.Speeds.Length);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Object>(
                "Assets/ScriptableObjects/Animation/MoveX.asset"), mixer.ParameterNameX);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Object>(
                "Assets/ScriptableObjects/Animation/MoveZ.asset"), mixer.ParameterNameY);

            string[] walkNames =
            {
                "WalkFwdLoop", "WalkBwdLoop", "StrafeLeftLoop", "StrafeRightLoop",
                "StrafeLeft45Loop", "StrafeRight45Loop", "StrafeLeft135Loop", "StrafeRight135Loop"
            };
            string[] runNames =
            {
                "RunFwdLoop", "RunBwdLoop", "RunLtLoop", "RunRtLoop",
                "RunStrafeLeft45Loop", "RunStrafeRight45Loop",
                "RunStrafeLeft135Loop", "RunStrafeRight135Loop"
            };
            Vector2[] directions =
            {
                Vector2.up, Vector2.down, Vector2.left, Vector2.right,
                new(-0.70710678f, 0.70710678f), new(0.70710678f, 0.70710678f),
                new(-0.70710678f, -0.70710678f), new(0.70710678f, -0.70710678f)
            };

            MotionBakeData maximumBake =
                AssetDatabase.LoadAssetAtPath<MotionBakeData>(MaximumSpeedBakePath);
            Assert.IsNotNull(maximumBake);
            float maximumSpeed = maximumBake.GetRepresentativeSpeed();

            // maxPlaybackStretch 的唯一真相是 X Bot 的 profile 資產本身——測試不得自帶第二份數字，
            // 否則調整預算時會出現「資產改了、測試還在守舊值」的靜默分歧。
            var profile = AssetDatabase.LoadAssetAtPath<CombatDirectionalSpeedProfileSO>(
                "Assets/ScriptableObjects/Motion/XBotSpellDirectionalSpeedProfile.asset");
            Assert.IsNotNull(profile,
                "X Bot 的 spell directional speed profile 缺席——新契約要求 threshold 由它的 "
                + "maxPlaybackStretch 推導，沒有它整條契約不成立");
            float stretch = new SerializedObject(profile)
                .FindProperty("maxPlaybackStretch").floatValue;

            AssertDirectionalRing(mixer, 1, walkNames, directions, walkRadius, maximumSpeed, stretch);
            AssertDirectionalRing(mixer, 9, runNames, directions, runRadius, maximumSpeed, stretch);

            // 🔴 新契約的可觀察後果：**前向不變、側向與後向降速**。
            // 這兩條不是重述上面的公式，而是釘住「這次 invariant change 到底換到了什麼」——
            // 若哪天有人把 stretch 調到足以讓所有方向都追上 gait anchor，這裡會失敗並要求重新裁決。
            Assert.AreEqual(runRadius, mixer.Thresholds[9].magnitude, 0.00001f,
                "Forward 必須維持原 gait 速度（素材追得上，預算沒有咬到）");
            Assert.AreEqual(runRadius, mixer.Thresholds[13].magnitude, 0.00001f,
                "Forward Diagonal 同樣維持原速度");
            for (int index = 10; index <= 12; index++)
            {
                Assert.Less(mixer.Thresholds[index].magnitude, runRadius - 0.01f,
                    $"index {index}（側移／後退）必須低於 gait anchor——素材追不上，依 bake 自動降速");
            }
            Assert.Less(mixer.Thresholds[15].magnitude, runRadius - 0.01f, "Back Diagonal 必須降速");
            Assert.Less(mixer.Thresholds[16].magnitude, runRadius - 0.01f, "Back Diagonal 必須降速");

            for (int i = 0; i < mixer.Animations.Length; i++)
            {
                Assert.IsFalse(mixer.Animations[i].name.Contains("Sprint"),
                    "Sprint gameplay gait 保留，但本輪不得提前 author directional Sprint ring");
            }
        }

        [Test]
        public void SL6_SprintGameplayGaitAndMaximumSpeedSource_RemainUnchanged()
        {
            Object gait = AssetDatabase.LoadAssetAtPath<Object>(GaitProfilePath);
            Assert.IsNotNull(gait);
            var gaitObject = new SerializedObject(gait);
            Assert.AreEqual(0.75f, gaitObject.FindProperty("defaultIntensity").floatValue, 0.00001f);
            Assert.AreEqual(1f, gaitObject.FindProperty("sprintIntensity").floatValue, 0.00001f);
            Assert.AreEqual(0.3651f, gaitObject.FindProperty("walkIntensity").floatValue, 0.00001f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var driver = prefab.GetComponentInChildren<Project.Presentation.Motion.MotionDriver>(true);
            Assert.IsNotNull(driver);
            var driverObject = new SerializedObject(driver);
            Assert.IsFalse(driverObject.FindProperty("overrideMoveSpeed").boolValue);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Object>(MaximumSpeedBakePath),
                driverObject.FindProperty("moveSpeedSource").objectReferenceValue);
        }

        /// <summary>
        /// 🔄 **2026-09-15 invariant change（使用者明確裁決，`docs/11` §12 ／ `docs/21` §1.5.1）。**
        ///
        /// <para><b>舊契約（已正式廢除）</b></para>
        /// 「Combat 8-way 的每個方向都座落在 gait anchor 半徑上」——亦即方向不影響速度，
        /// 由 playback 倍率吸收素材速度差。代價是側移／後退被拉到 <b>2.07–2.17×</b>，腳頻明顯過快。
        ///
        /// <para><b>新契約：bake-derived directional speed</b></para>
        /// <code>
        ///   clipN_i    = bake_i.speed / maximumSpeed          ← 量測事實
        ///   threshold_i = dir_i × min(gaitAnchor, clipN_i × maxPlaybackStretch)
        ///   playback_i  = |threshold_i| / clipN_i   ⇒ 恆 ≤ maxPlaybackStretch
        /// </code>
        /// **沒有任何手填的方向倍率**——`maxPlaybackStretch` 是本契約唯一的設計參數，
        /// 其餘全部由 bake 推導。素材能在預算內追上 gameplay speed 的方向（Forward、Forward Diagonal）
        /// **原速度不變**；追不上的（側移、後方）自動降速而不是踩快。
        /// ⛔ 使用者明確裁決：**不得**新增最低方向速度比例或其他手感補償，先以純推導結果進 Play 驗收。
        /// </summary>
        private static void AssertDirectionalRing(
            MixerTransition2D mixer,
            int startIndex,
            IReadOnlyList<string> expectedNames,
            IReadOnlyList<Vector2> directions,
            float gaitAnchor,
            float maximumSpeed,
            float maxPlaybackStretch)
        {
            for (int i = 0; i < expectedNames.Count; i++)
            {
                int index = startIndex + i;
                Assert.IsInstanceOf<AnimationClip>(mixer.Animations[index]);
                var clip = (AnimationClip)mixer.Animations[index];
                Assert.AreEqual(expectedNames[i], clip.name, $"directional child index {index}");

                MotionBakeData bake = AssetDatabase.LoadAssetAtPath<MotionBakeData>(
                    $"Assets/ScriptableObjects/Motion/Bake_{clip.name}.asset");
                Assert.IsNotNull(bake, $"{clip.name} 缺少 MotionBakeData");
                Assert.AreSame(clip, bake.SourceClip, $"{clip.name} bake provenance 不一致");

                float clipNormalized = bake.GetRepresentativeSpeed() / maximumSpeed;
                float cap = Mathf.Min(gaitAnchor, clipNormalized * maxPlaybackStretch);

                Assert.Less(Vector2.Distance(directions[i] * cap, mixer.Thresholds[index]), 0.00001f,
                    $"{clip.name} 的 threshold 必須等於 min(gait {gaitAnchor}, bake {clipNormalized:F4} × "
                    + $"{maxPlaybackStretch}) = {cap:F4}——threshold 就是「這個方向實際能跑多快」，"
                    + "不再是共用的 gait 半徑");

                float expectedPlayback = cap / clipNormalized;
                Assert.AreEqual(expectedPlayback, mixer.Speeds[index], 0.00001f,
                    $"{clip.name} 的 playback 必須由 threshold ÷ bake 推導");
                Assert.LessOrEqual(mixer.Speeds[index], maxPlaybackStretch + 0.00001f,
                    $"{clip.name} playback {mixer.Speeds[index]:F4} 超過預算 {maxPlaybackStretch}"
                    + "——那正是舊契約被廢除的原因（腳頻過快）");
            }
        }

        private static float FindThreshold(LinearMixerTransition mixer, string clipName)
        {
            for (int i = 0; i < mixer.Animations.Length; i++)
            {
                if (mixer.Animations[i] is AnimationClip clip && clip.name == clipName)
                    return mixer.Thresholds[i];
            }

            Assert.Fail($"1D Locomotion 找不到 {clipName}");
            return -1f;
        }

        private static SerializedProperty LoadMappings()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab, $"找不到玩家 prefab：{PlayerPrefabPath}");
            AnimancerFacade facade = prefab.GetComponentInChildren<AnimancerFacade>(true);
            Assert.IsNotNull(facade, "X Bot 缺少 AnimancerFacade");
            SerializedProperty mappings = new SerializedObject(facade).FindProperty("transitionMappings");
            Assert.IsNotNull(mappings, "AnimancerFacade 缺少 transitionMappings");
            return mappings;
        }

        private static SerializedProperty FindMapping(SerializedProperty mappings, string key)
        {
            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty item = mappings.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("StateKey").stringValue == key) return item;
            }

            Assert.Fail($"X Bot transitionMappings 找不到 '{key}'");
            return null;
        }
    }
}
