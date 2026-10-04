using NUnit.Framework;
using Project.Presentation.IK;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🧪（docs/45 §21，A2 實驗）一帧水平外插比例。實際效果（錯階暫態、落地附近連續性）屬 PlayMode 探針 A/B。
    /// </summary>
    public class PlantAwareExtrapolationTests
    {
        private const float Near = 1.5f, VMin = 2f, VMax = 6f, Eps = 1e-5f;

        private static float K(Vector2 step, float speed, bool hasPlant, Vector2 plant)
            => FootIKController.PlantAwareExtrapolationScale(Vector2.zero, step, speed, hasPlant, plant, Near, VMin, VMax);

        [Test]
        public void FastFoot_NoPlant_FullExtrapolation()
            => Assert.AreEqual(1f, K(new Vector2(0.15f, 0f), 9f, false, default), Eps);

        [Test]
        public void SlowFoot_NoPlant_NoExtrapolation()
            => Assert.AreEqual(0f, K(new Vector2(0.02f, 0f), 1f, false, default), Eps);

        [Test]
        public void SpeedRamp_IsSmoothstep()
            => Assert.AreEqual(0.5f, K(new Vector2(0.07f, 0f), 4f, false, default), Eps);

        [Test]
        public void PlannedPlantWithinNearSteps_NoExtrapolation()
        {
            // 煞車步過衝那一帧：步長 15 cm、預測落點只在前方 10.5 cm ⇒ 1.5 步內。
            Assert.AreEqual(0f, K(new Vector2(0.15f, 0f), 9f, true, new Vector2(0.105f, 0f)), Eps);
        }

        [Test]
        public void PlannedPlantFarAhead_FallsBackToSpeedRamp()
            => Assert.AreEqual(1f, K(new Vector2(0.15f, 0f), 9f, true, new Vector2(0.60f, 0f)), Eps);

        [Test]
        public void PlannedPlantBehind_NoExtrapolation()
            => Assert.AreEqual(0f, K(new Vector2(0.15f, 0f), 9f, true, new Vector2(-0.10f, 0f)), Eps,
                "已越過預測落點（沿外插方向為負）＝ 落點就在腳下或後方。");

        [Test]
        public void PlannedPlant_ZeroStep_NoExtrapolation()
            => Assert.AreEqual(0f, K(Vector2.zero, 0f, true, new Vector2(0.3f, 0f)), Eps);
    }
}
