using NUnit.Framework;
using UnityEngine;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 停步落點預測的參考點轉換（docs/46 §18）：烘焙的 Foot 骨骼點 → T3 的 Humanoid IK goal 參考點。
    /// </summary>
    public class StopLandingReferenceTests
    {
        private const float Eps = 1e-6f;

        [Test]
        public void ZeroOffset_KeepsTheBakedBonePoint()
        {
            var bone = new Vector3(-0.1182f, 0.0887f, -0.0276f);
            Assert.AreEqual(bone, FootIKController.StopLandingGoalInRoot(bone, Vector3.zero));
        }

        [Test]
        public void Offset_ShiftsOnlyHorizontally()
        {
            var bone = new Vector3(0.1235f, 0.088f, -0.0636f);
            var offset = new Vector3(0.0385f, -0.5f, -0.0329f);   // y 故意很大：必須被忽略（預測會重新落地）
            Vector3 goal = FootIKController.StopLandingGoalInRoot(bone, offset);
            Assert.AreEqual(bone.x + 0.0385f, goal.x, Eps);
            Assert.AreEqual(bone.y, goal.y, Eps);
            Assert.AreEqual(bone.z - 0.0329f, goal.z, Eps);
        }

        [Test]
        public void LeftAndRight_UseTheirOwnOffsets()
        {
            var bone = Vector3.zero;
            var left = new Vector3(-0.0486f, 0f, -0.0228f);
            var right = new Vector3(0.0385f, 0f, -0.0329f);
            Assert.AreNotEqual(FootIKController.StopLandingGoalInRoot(bone, left), FootIKController.StopLandingGoalInRoot(bone, right));
        }
    }
}
