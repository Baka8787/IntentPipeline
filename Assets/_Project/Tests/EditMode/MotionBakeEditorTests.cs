using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Project.Editor;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// Motion Bake 採樣角色解析契約：Gameplay Root 不必自己持有 Animator，但整個子階層必須恰好有一個
    /// 綁定有效 Humanoid Avatar 的 Animator。守住 X Bot 的 Root／Model 兩層實際組裝方式。
    /// </summary>
    public class MotionBakeEditorTests
    {
        [Test]
        public void HumanoidResolver_XBotGameplayRoot_FindsModelAnimator()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/X Bot.prefab");
            Assert.IsNotNull(prefab, "測試前提：X Bot prefab 必須存在");

            bool resolved = MotionBakeEditor.TryResolveHumanoidAnimator(prefab, out Animator animator, out string error);

            Assert.IsTrue(resolved, error);
            Assert.IsNotNull(animator);
            Assert.IsTrue(animator.avatar.isHuman);
        }

        [Test]
        public void HumanoidResolver_NoAnimator_ReturnsExplicitFailure()
        {
            var root = new GameObject("No Animator Root");
            try
            {
                bool resolved = MotionBakeEditor.TryResolveHumanoidAnimator(root, out Animator animator, out string error);

                Assert.IsFalse(resolved);
                Assert.IsNull(animator);
                StringAssert.Contains("Root／子階層", error);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>ADR-010 D2／D5：結束站姿＝最後一個 sample；plant ＝ 穩定且貼近該腳最低點的連續區，依時間排序。</summary>
        [Test]
        public void FootPlantDetector_FindsStableLowWindows_AndStoresEndPose()
        {
            var samples = new List<FootPlantBakeSample>();
            for (int i = 0; i < 10; i++)
            {
                float leftX = i < 1 ? -0.2f : i <= 4 ? 0f : (i - 4) * 0.1f;
                float rightX = i < 5 ? -0.4f + i * 0.1f : 0f;
                float leftY = i >= 1 && i <= 4 ? 0f : 0.1f;
                float rightY = i >= 5 && i <= 8 ? 0f : 0.1f;
                samples.Add(new FootPlantBakeSample
                {
                    Time = i * 0.1f,
                    LeftAnkleWorld = new Vector3(leftX, leftY, 0f),
                    RightAnkleWorld = new Vector3(rightX, rightY, 0f),
                    LeftAnkleInRoot = new Vector3(-0.1f, leftY, 0.2f + i * 0.01f),
                    RightAnkleInRoot = new Vector3(0.1f, rightY, 0.2f + i * 0.01f),
                });
            }

            FootPlantSchedule schedule = FootPlantScheduleBaker.Build(
                samples, 0.9f, out bool truncated, horizontalSpeedMax: 0.2f,
                heightTolerance: 0.025f, minimumSeparation: 0.12f);

            Assert.IsFalse(truncated);
            Assert.IsTrue(schedule.IsValid);
            Assert.AreEqual(2, schedule.EventCount);
            Assert.AreEqual(FootPlantSide.Left, schedule.GetEvent(0).Side);
            Assert.AreEqual(0.2f, schedule.GetEvent(0).ClipTime, 0.0001f);
            Assert.AreEqual(samples[2].LeftAnkleInRoot, schedule.GetEvent(0).AnkleInRoot, "plant 的位置＝該連續區第一個 sample");
            Assert.AreEqual(FootPlantSide.Right, schedule.GetEvent(1).Side);
            Assert.AreEqual(0.5f, schedule.GetEvent(1).ClipTime, 0.0001f);
            Assert.AreEqual(samples[9].LeftAnkleInRoot, schedule.EndPose.LeftAnkleInRoot);
            Assert.AreEqual(0.9f, schedule.EndPose.ClipDuration, 0.0001f);
        }
    }
}
