using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// v0.16.2「動畫數據 → 配置」資料流的代表速度存取契約 EditMode 單元測試。
    /// 驗證 <see cref="MotionBakeData.ComputeAverageSpeed"/> 的邊界安全與正確性，以及
    /// <see cref="MotionBakeData.GetRepresentativeSpeed"/> 的「烘焙值優先、舊資產即時回退」雙路徑
    /// （dev-spec §3.2）。純資料邏輯，不依賴 AnimationClip / Avatar / 場景。
    /// </summary>
    public class MotionBakeDataTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
                if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        private MotionBakeData NewBake()
        {
            var bake = ScriptableObject.CreateInstance<MotionBakeData>();
            _created.Add(bake);
            return bake;
        }

        // 均勻關鍵影格：算術平均即代表速度（值 = 2, 4, 6 → 平均 4）
        private static AnimationCurve Curve(params float[] values)
        {
            var curve = new AnimationCurve();
            for (int i = 0; i < values.Length; i++) curve.AddKey(i * 0.1f, values[i]);
            return curve;
        }

        // === ComputeAverageSpeed 邊界與正確性 ===

        [Test]
        public void ComputeAverageSpeed_NullCurve_ReturnsZero()
            => Assert.AreEqual(0f, MotionBakeData.ComputeAverageSpeed(null));

        [Test]
        public void ComputeAverageSpeed_EmptyCurve_ReturnsZero()
            => Assert.AreEqual(0f, MotionBakeData.ComputeAverageSpeed(new AnimationCurve()));

        [Test]
        public void ComputeAverageSpeed_KnownValues_ReturnsArithmeticMean()
            => Assert.AreEqual(4f, MotionBakeData.ComputeAverageSpeed(Curve(2f, 4f, 6f)), 1e-4f);

        // === GetRepresentativeSpeed 雙路徑 ===

        [Test]
        public void GetRepresentativeSpeed_BakedFieldSet_TakesPrecedenceOverCurve()
        {
            var bake = NewBake();
            bake.AutoAverageSpeed = 5.66f;                 // 烘焙時存檔的代表速度
            bake.SpeedCurve = Curve(1f, 1f, 1f);           // 曲線平均 = 1（刻意不同）
            // 欄位優先：回傳存檔值，不回退曲線
            Assert.AreEqual(5.66f, bake.GetRepresentativeSpeed(), 1e-4f);
        }

        [Test]
        public void GetRepresentativeSpeed_FieldZero_FallsBackToCurveAverage()
        {
            var bake = NewBake();
            bake.AutoAverageSpeed = 0f;                    // 舊資產：未重烘焙、欄位為 0
            bake.SpeedCurve = Curve(1.5f, 1.7f, 1.9f);     // 回退計算平均 = 1.7
            Assert.AreEqual(1.7f, bake.GetRepresentativeSpeed(), 1e-4f);
        }

        [Test]
        public void GetRepresentativeSpeed_FieldZeroAndNoCurve_ReturnsZero()
        {
            var bake = NewBake();                          // 皆為預設：欄位 0、曲線 null
            Assert.AreEqual(0f, bake.GetRepresentativeSpeed());
        }

        // === Duration：Runtime 不得依賴 AnimationClip（🆕 2026-07-26）===

        [Test]
        public void Duration_ReadsBakedDuration_WithoutAnySourceClip()
        {
            var bake = NewBake();          // SourceClip 恆為 null（EditMode 不指派任何 clip）
            bake.BakedDuration = 1.25f;    // 烘焙時自 clip.length 快照的序列化值

            Assert.AreEqual(1.25f, bake.Duration, 1e-4f,
                "Duration 必須完全來自序列化的 BakedDuration——一旦它回頭讀 SourceClip.length，" +
                "動畫資產缺席或 GUID 變動時 Duration 會靜默歸零（Roll 秒退的根因）");
        }

        [Test]
        public void Duration_StaleAsset_ReturnsZero_SoConsumersCanDetectIt()
        {
            var bake = NewBake();          // 模擬 BakedDuration 導入前烘焙的舊資產

            Assert.AreEqual(0f, bake.Duration,
                "未重烘的舊資產應如實回傳 0，而不是偷偷回退去讀 clip——" +
                "消費端（RollState）據此走 FallbackDuration 並發出『請重烘』警告，" +
                "回退讀 clip 會讓這個遷移缺口永遠隱形");
        }

        // === GetFootPhaseAt（🆕）：連續腳相查詢；曲線缺退回單點 EndPhase ===

        [Test]
        public void GetFootPhaseAt_NegativeCurveValue_ReturnsLeftFootDown()
        {
            var bake = NewBake();
            bake.FootPhaseCurve = Curve(-0.2f, -0.1f); // 全負 → 左腳觸地
            Assert.AreEqual(FootPhase.LeftFootDown, bake.GetFootPhaseAt(0f));
        }

        [Test]
        public void GetFootPhaseAt_PositiveCurveValue_ReturnsRightFootDown()
        {
            var bake = NewBake();
            bake.FootPhaseCurve = Curve(0.2f, 0.1f); // 全正 → 右腳觸地
            Assert.AreEqual(FootPhase.RightFootDown, bake.GetFootPhaseAt(0f));
        }

        [Test]
        public void GetFootPhaseAt_NoCurve_FallsBackToEndPhase()
        {
            var bake = NewBake();                     // FootPhaseCurve = null（未重烘焙的舊資產）
            bake.EndPhase = FootPhase.RightFootDown;
            Assert.AreEqual(FootPhase.RightFootDown, bake.GetFootPhaseAt(0.5f), "曲線缺 → 退回單點 EndPhase");
        }
    }
}
