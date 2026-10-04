using NUnit.Framework;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🧪（docs/45 §18）擺盪腳長參考：參考面 ＝ 腳踝下方參考與腳跟／腳尖下方移動面的最高者。
    /// 實際效果（瞬拉次數、擺盪高度、骨盆）屬 PlayMode 探針（LandingSurfaceProbe.Probe_FootLengthReferenceAB）。
    /// </summary>
    public class FootLengthReferenceTests
    {
        private const float Epsilon = 1e-6f;

        [Test]
        public void FlatGround_AllPointsEqual_Unchanged()
            => Assert.AreEqual(0.5f, FootIKController.FootLengthReference(0.5f, true, 0.5f, true, 0.5f), Epsilon);

        [Test]
        public void Ascending_ToeOverHigherSurface_RaisesReference()
            => Assert.AreEqual(0.62f, FootIKController.FootLengthReference(0.5f, true, 0.43f, true, 0.62f), Epsilon,
                "上樓：腳尖下方的移動面較高 ⇒ 整隻腳以它為參考，腳尖保有自己的離地高度。");

        [Test]
        public void Descending_HeelOverHigherSurface_RaisesReference()
            => Assert.AreEqual(0.57f, FootIKController.FootLengthReference(0.5f, true, 0.57f, true, 0.40f), Epsilon,
                "下樓：後面的腳跟仍在較高的階緣上方。");

        [Test]
        public void MissedPoints_DoNotParticipate()
            => Assert.AreEqual(0.5f, FootIKController.FootLengthReference(0.5f, false, 9f, false, 9f), Epsilon,
                "射線落空的端點不得把參考拉高或拉低。");

        [Test]
        public void NeverLowersBelowAnkleReference()
            => Assert.AreEqual(0.5f, FootIKController.FootLengthReference(0.5f, true, 0.2f, true, 0.3f), Epsilon);
    }
}
