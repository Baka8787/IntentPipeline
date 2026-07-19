using NUnit.Framework;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// M3 Foot IK 純函數的確定性 EditMode 單元測試（不需場景／Animator／Physics）。
    /// 驗證 docs/02-dev-spec.md §3.5 的兩個決策核心：
    /// Q3 Pose Heuristic 權重曲線（ComputeFootWeight）與 Q2 骨盆補償（ComputePelvisOffset）。
    /// IK 的視覺效果（貼地／法線對齊／一幀延遲平滑）屬 Play 實測範疇，不在此涵蓋。
    /// </summary>
    public class FootIKTests
    {
        private const float Epsilon = 1e-5f;

        // === ComputeFootWeight（Q3：腳骨高度 → 貼地權重）===

        [Test]
        public void FootWeight_BelowGroundedMin_ReturnsOne()
        {
            Assert.AreEqual(1f, FootIKController.ComputeFootWeight(0.05f, 0.1f, 0.3f), Epsilon,
                "腳骨低於踩地閾值時，IK 應全接管（權重 1）");
        }

        [Test]
        public void FootWeight_AboveGroundedMax_ReturnsZero()
        {
            Assert.AreEqual(0f, FootIKController.ComputeFootWeight(0.4f, 0.1f, 0.3f), Epsilon,
                "腳骨高於抬腳閾值時，動畫應全接管（權重 0）");
        }

        [Test]
        public void FootWeight_AtMidpoint_ReturnsHalf_LinearFalloff()
        {
            Assert.AreEqual(0.5f, FootIKController.ComputeFootWeight(0.2f, 0.1f, 0.3f), Epsilon,
                "閾值帶中點應為 0.5——權重在 min~max 之間必須線性遞減");
        }

        [Test]
        public void FootWeight_DegenerateRange_FallsBackToHardCut()
        {
            // min >= max 的異常配置：退化為以 groundedMin 硬切，不拋例外（防呆契約）
            Assert.AreEqual(1f, FootIKController.ComputeFootWeight(0.1f, 0.1f, 0.1f), Epsilon,
                "退化配置下，高度 ≤ min 應回 1");
            Assert.AreEqual(0f, FootIKController.ComputeFootWeight(0.11f, 0.1f, 0.1f), Epsilon,
                "退化配置下，高度 > min 應回 0");
        }

        // === ComputePelvisOffset（Q2：雙腳地面高差 → 骨盆下沉）===

        [Test]
        public void PelvisOffset_FlatGround_ReturnsZero()
        {
            Assert.AreEqual(0f, FootIKController.ComputePelvisOffset(1f, 1f, 1f, 0.35f), Epsilon,
                "平地（雙腳命中點＝Root 平面）不應有骨盆補償");
        }

        [Test]
        public void PelvisOffset_LowerFoot_ReturnsNegativeDelta()
        {
            Assert.AreEqual(-0.2f, FootIKController.ComputePelvisOffset(0.8f, 1.0f, 1f, 0.35f), Epsilon,
                "低腳在 Root 平面下 0.2m 時，骨盆應下沉 0.2m（取較低腳）");
        }

        [Test]
        public void PelvisOffset_ExceedsMaxDrop_IsClamped()
        {
            Assert.AreEqual(-0.35f, FootIKController.ComputePelvisOffset(0.4f, 1.0f, 1f, 0.35f), Epsilon,
                "高差超過 maxPelvisDrop 時應夾在最大下沉量（低腳搆不到屬設計極限）");
        }

        [Test]
        public void PelvisOffset_GroundAboveRoot_ClampsToZero()
        {
            Assert.AreEqual(0f, FootIKController.ComputePelvisOffset(1.1f, 1.2f, 1f, 0.35f), Epsilon,
                "地面高於 Root 平面（上坡側）不得上抬——骨盆只下沉，抬升交給 CharacterController 地面跟隨");
        }

        // 註（M3.5 最終形）：M3.2 的 ComputeHeightFade／ClampReach 純函數已隨實驗機制移除，
        // 對應 7 條測試一併退場（總數回 42）——實驗結論與復刻指引見 changelog v0.18.2~v0.18.6。
    }
}
