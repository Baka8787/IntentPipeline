using NUnit.Framework;
using Project.Core.Movement;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public class CombatDirectionalSpeedProfileTests
    {
        [TestCase(0f, 1f, 2f)]
        [TestCase(0f, -1f, 1f)]
        [TestCase(-1f, 0f, 3f)]
        [TestCase(1f, 0f, 4f)]
        public void CardinalDirection_UsesThatClipsBakedSpeed(
            float x, float z, float expectedSpeed)
        {
            float normalized = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                new Vector2(x, z),
                forwardSpeed: 2f,
                backwardSpeed: 1f,
                leftSpeed: 3f,
                rightSpeed: 4f,
                maximumSpeed: 8f);

            Assert.That(normalized, Is.EqualTo(expectedSpeed / 8f).Within(0.000001f));
        }

        [Test]
        public void IntermediateDirection_LandsOnAdjacentCardinalSampleBoundary()
        {
            Vector2 direction = new Vector2(1f, 1f).normalized;
            float normalized = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                direction,
                forwardSpeed: 2f,
                backwardSpeed: 1f,
                leftSpeed: 3f,
                rightSpeed: 4f,
                maximumSpeed: 8f);

            float expected = 1f / (direction.x / (4f / 8f) + direction.y / (2f / 8f));
            Assert.That(normalized, Is.EqualTo(expected).Within(0.000001f));
        }

        [Test]
        public void MissingBakeSpeed_FailsOpenWithoutInventingFallbackTuning()
        {
            float normalized = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                Vector2.left,
                forwardSpeed: 2f,
                backwardSpeed: 1f,
                leftSpeed: 0f,
                rightSpeed: 4f,
                maximumSpeed: 8f);

            Assert.AreEqual(1f, normalized,
                "無有效 bake speed 時不得用第二套手填 combat speed 猜值。");
        }

        // ═══════════════════════════════════════════════════════════════════
        // 🆕 2026-09-15：八向模型 ＋ playback 預算上限
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>X Bot spell locomotion 的實測值（m/s），分母為 Bake_SprintFwdLoop。</summary>
        private const float XBotMaximum = 6.2613893f;

        private static CombatDirectionalSpeedProfileSO.DirectionalSpeedOctant XBotOctant() =>
            new CombatDirectionalSpeedProfileSO.DirectionalSpeedOctant(
                forward: 3.5780573f,        // RunFwdLoop
                forwardRight: 3.6216948f,   // RunStrafeRight45Loop
                right: 2.2653751f,          // RunRtLoop
                backwardRight: 2.213624f,   // RunStrafe*135Loop
                backward: 2.213623f,        // RunBwdLoop
                backwardLeft: 2.2136235f,
                left: 2.1647274f,           // RunLtLoop
                forwardLeft: 3.6216946f);   // RunStrafeLeft45Loop

        /// <summary>
        /// 🔴 **有斜向 clip 時不得沿用 cardinal 的 L1 內縮模型。**
        ///
        /// L1 模型是為「只有 cardinal 樣本」設計的，它在 45° 給出保守內縮值。
        /// X Bot 的 45° clip 實測 3.62 m/s（比前跑的 3.58 還快），L1 卻會推出約 0.31 normalized
        /// ⇒ **少了 45%，角色會明顯滑步**。這條測試把那個差距釘住。
        /// </summary>
        [Test]
        public void Octant_DiagonalUsesItsOwnClip_NotTheCardinalL1Fallback()
        {
            Vector2 diagonal = new Vector2(1f, 1f).normalized;

            float octant = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                diagonal, XBotOctant(), XBotMaximum, stretch: 1f);
            float cardinalOnly = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                diagonal, 3.5780573f, 2.213623f, 2.1647274f, 2.2653751f, XBotMaximum);

            Assert.That(octant, Is.EqualTo(3.6216948f / XBotMaximum).Within(0.0001f),
                "45° 必須直接採用該方向自己的 bake 速度");
            Assert.Greater(octant, cardinalOnly * 1.5f,
                "若兩者接近，代表八向模型沒有生效——那正是會造成斜向滑步的情況");
        }

        [TestCase(0f, 1f, 3.5780573f)]
        [TestCase(1f, 0f, 2.2653751f)]
        [TestCase(-1f, 0f, 2.1647274f)]
        [TestCase(0f, -1f, 2.213623f)]
        public void Octant_CardinalDirections_UseTheirOwnClip(float x, float z, float expected)
        {
            float normalized = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                new Vector2(x, z), XBotOctant(), XBotMaximum, stretch: 1f);

            Assert.That(normalized, Is.EqualTo(expected / XBotMaximum).Within(0.0001f));
        }

        /// <summary>
        /// **stretch 是上限預算，不是乘在最終結果上的自由參數。**
        /// 這條確認 1.35 的意義就是「允許腳頻最多被拉 1.35 倍」——
        /// 亦即該方向可跑到 <c>clip 速度 × 1.35</c>，再由呼叫端 <c>Mathf.Min</c> 與 gait 需求取小。
        /// </summary>
        [Test]
        public void Octant_StretchRaisesTheCapExactlyByItsFactor()
        {
            Vector2 left = Vector2.left;

            float plain = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                left, XBotOctant(), XBotMaximum, stretch: 1f);
            float stretched = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                left, XBotOctant(), XBotMaximum, stretch: 1.35f);

            Assert.That(stretched, Is.EqualTo(plain * 1.35f).Within(0.0001f));
            Assert.That(stretched, Is.EqualTo(0.4667308f).Within(0.0005f),
                "X Bot 左側移的實際上限；對應前跑 0.75 的 62.2%（使用者裁決：上限贏）");
        }

        /// <summary>
        /// ⚠️ 反向守門：**stretch 不得小於 1**。小於 1 等於「要求動畫播得比實際位移慢」＝ 滑步。
        /// </summary>
        [Test]
        public void Octant_StretchBelowOne_IsClampedToOne()
        {
            float clamped = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                Vector2.left, XBotOctant(), XBotMaximum, stretch: 0.5f);
            float plain = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                Vector2.left, XBotOctant(), XBotMaximum, stretch: 1f);

            Assert.That(clamped, Is.EqualTo(plain).Within(0.000001f));
        }

        /// <summary>
        /// 🔒 **Y Bot 回歸守門。** cardinal-only ＋ stretch 1 必須與擴充前逐位元相同。
        /// 這次擴充是加法，不得改變任何既有角色的數值。
        /// </summary>
        [Test]
        public void CardinalOnlyPath_IsUnchangedByTheExtension()
        {
            Vector2 direction = new Vector2(0.6f, -0.8f);

            float normalized = CombatDirectionalSpeedProfileSO.ResolveNormalizedSpeed(
                direction, forwardSpeed: 2f, backwardSpeed: 1f, leftSpeed: 3f, rightSpeed: 4f,
                maximumSpeed: 8f);

            // 手算：|x|/(4/8) + |y|/(1/8) = 0.6/0.5 + 0.8/0.125 = 1.2 + 6.4 = 7.6 ⇒ 1/7.6
            Assert.That(normalized, Is.EqualTo(1f / 7.6f).Within(0.000001f));
        }

    }
}
