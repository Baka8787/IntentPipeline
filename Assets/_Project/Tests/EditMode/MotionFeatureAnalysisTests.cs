using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Project.Editor;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// v0.14「世界空間相對足跡（World-Relative Footprint）」跳躍特徵分析演算法的確定性 EditMode 單元測試。
    /// 以手工構造的合成採樣緩衝（<see cref="MotionFeatureSample"/>）直接驅動 <see cref="JumpFeatureAnalyzer"/>，
    /// 不依賴任何 AnimationClip / Avatar / 場景，逐一驗證 docs/02-dev-spec.md §4.3 的規格：
    /// 雙 Pass 事件偵測、子影格線性插值、持續騰空驗證、單幀雜訊過濾、安全退化矩陣與 g = 8h/t² 自洽性。
    /// </summary>
    public class MotionFeatureAnalysisTests
    {
        // === 測試用 rig 基線與容忍度（門檻線：左 0.11 / 右 0.12，刻意讓雙腳不同以驗證逐腳判定）===
        private const float LeftBaseline = 0.08f;
        private const float RightBaseline = 0.09f;
        private const float Threshold = 0.03f;

        // 觸地時的腳高度 = 各自基線；騰空時明確高於兩腳門檻線
        private const float GroundedL = 0.08f;
        private const float GroundedR = 0.09f;
        private const float AirborneY = 0.5f;

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private static MotionFeatureSample Grounded(float time, float rootY)
            => new MotionFeatureSample(time, rootY, GroundedL, GroundedR);

        private static MotionFeatureSample Airborne(float time, float rootY)
            => new MotionFeatureSample(time, rootY, AirborneY, AirborneY);

        /// <summary>以測試共用的基線/容忍度建立上下文，執行分析器並回傳結果資產。</summary>
        private MotionBakeData RunAnalyzer(List<MotionFeatureSample> samples, float duration)
        {
            var target = ScriptableObject.CreateInstance<MotionBakeData>();
            _created.Add(target);

            var context = new MotionFeatureContext(samples, duration, Threshold, LeftBaseline, RightBaseline);
            new JumpFeatureAnalyzer().Analyze(context, target);
            return target;
        }

        /// <summary>
        /// 標準跳躍樣本（dt = 0.1s）：前搖觸地 0~0.4s → 騰空 0.5~1.2s（root 拋物線、頂點 0.9）→ 1.3s 起落地。
        /// 手算期望值（子影格線性插值）：
        ///   起跳 = 後離地的腳 = 右腳：0.4 + 0.1×(0.12−0.09)/(0.5−0.09) ≈ 0.40732
        ///   落地 = 先觸地的腳 = 右腳：1.2 + 0.1×(0.12−0.5)/(0.09−0.5) ≈ 1.29268
        ///   滯空 ≈ 0.88537；起跳時刻 root = lerp(0, 0.2, 0.7317) ≈ 0.01463；apex ≈ 0.9 − 0.01463 = 0.88537
        /// </summary>
        private static List<MotionFeatureSample> BuildIdealJumpSamples()
        {
            return new List<MotionFeatureSample>
            {
                Grounded(0.0f, 0f),
                Grounded(0.1f, 0f),
                Grounded(0.2f, 0f),
                Grounded(0.3f, 0f),
                Grounded(0.4f, 0f),
                Airborne(0.5f, 0.2f),
                Airborne(0.6f, 0.5f),
                Airborne(0.7f, 0.8f),
                Airborne(0.8f, 0.9f),  // 窗內最高點
                Airborne(0.9f, 0.8f),
                Airborne(1.0f, 0.55f),
                Airborne(1.1f, 0.3f),
                Airborne(1.2f, 0.1f),
                Grounded(1.3f, 0f),
                Grounded(1.4f, 0f),
                Grounded(1.5f, 0f),
            };
        }

        // === 完整閉環：起跳/落地子影格插值、窗內最高點、g = 8h/t² 自洽 ===
        [Test]
        public void IdealJump_FullClosedLoop_MeasuresAllFeaturesWithSubFramePrecision()
        {
            var result = RunAnalyzer(BuildIdealJumpSamples(), 1.5f);

            const float expectedTakeoff = 0.4073171f;
            const float expectedAirTime = 0.8853658f;
            const float expectedApex = 0.8853659f;

            Assert.AreEqual(expectedTakeoff, result.AutoTakeoffDelay, 5e-4f,
                "起跳時刻應為『後離地的腳』跨越自身門檻線的子影格插值交點（落在 0.4~0.5 兩採樣之間）");
            Assert.AreEqual(expectedAirTime, result.AutoAirTime, 5e-4f,
                "滯空時間應為起跳→首次落地的精確量測（雙端皆子影格插值）");
            Assert.AreEqual(expectedApex, result.AutoApexHeight, 5e-4f,
                "最高點應以『插值後起跳時刻的根高度』為基準，於 [起跳, 落地] 窗內掃描");

            // 自洽性：分析器以 g = 8h/t² 逆推，執行期 v = √(2gh) 恰為其對稱解（ADR-002 §2.3）
            float selfConsistentGravity = 8f * result.AutoApexHeight / (result.AutoAirTime * result.AutoAirTime);
            Assert.AreEqual(selfConsistentGravity, result.AutoCalculatedGravity, 1e-4f,
                "逆推重力必須與量測出的 h、t_air 精確自洽（g = 8h/t²）");
            Assert.AreNotEqual(9.81f, result.AutoCalculatedGravity,
                "完整閉環時重力應為量測逆推值，不得退化為標準值");
        }

        // === 安全退化：非跳躍動畫（走路循環，單腳交替抬起，永無雙腳同時騰空）===
        [Test]
        public void WalkCycle_SingleFootLifts_DegradesToDefaults()
        {
            var samples = new List<MotionFeatureSample>();
            for (int i = 0; i <= 20; i++)
            {
                float t = i * 0.1f;
                bool liftLeft = (i / 5) % 2 == 0; // 每 0.5 秒換腳抬起，另一腳恆觸地
                samples.Add(new MotionFeatureSample(
                    t,
                    rootWorldY: Mathf.Sin(t * 8f) * 0.03f, // 行走的根節點自然起伏
                    leftFootWorldY: liftLeft ? 0.3f : GroundedL,
                    rightFootWorldY: liftLeft ? GroundedR : 0.3f));
            }

            var result = RunAnalyzer(samples, 2.0f);

            Assert.AreEqual(0f, result.AutoTakeoffDelay, "偵測不到雙腳同時騰空 → 前搖維持 0");
            Assert.AreEqual(0f, result.AutoApexHeight, "非跳躍動畫 → 最高點維持 0");
            Assert.AreEqual(0f, result.AutoAirTime, "非跳躍動畫 → 滯空維持 0");
            Assert.AreEqual(9.81f, result.AutoCalculatedGravity, 1e-6f, "非跳躍動畫 → 重力退化為標準值");
        }

        // === 安全退化：jump-loop（有起跳、騰空延續到片尾、無落地）→ 誠實退化 ===
        [Test]
        public void JumpLoop_NoLanding_ReportsTakeoffAndApexButHonestZeroAirTime()
        {
            var samples = new List<MotionFeatureSample>
            {
                Grounded(0.0f, 0f),
                Grounded(0.1f, 0f),
                Grounded(0.2f, 0f),
                Grounded(0.3f, 0f),
                Grounded(0.4f, 0f),
            };
            for (int i = 5; i <= 20; i++)
            {
                float t = i * 0.1f;
                samples.Add(Airborne(t, 0.2f + 0.4f * Mathf.Sin((t - 0.5f) * 2f))); // 騰空到片尾
            }

            var result = RunAnalyzer(samples, 2.0f);

            Assert.Greater(result.AutoTakeoffDelay, 0.4f, "有真實起跳 → 前搖仍寫入量測值");
            Assert.Greater(result.AutoApexHeight, 0f, "找不到落地時最高點掃描窗至片尾，仍寫入量測值");
            Assert.AreEqual(0f, result.AutoAirTime, "找不到落地 → AutoAirTime = 0，明示未量測");
            Assert.AreEqual(9.81f, result.AutoCalculatedGravity, 1e-6f, "找不到落地 → 重力退回標準值，不得用錯誤估計逆推");
        }

        // === 持續騰空驗證：跑步循環的短騰空相（0.06s < MinAirTime 0.1s）整段丟棄 ===
        [Test]
        public void RunCycle_ShortFlightPhases_AreFilteredOut()
        {
            var samples = new List<MotionFeatureSample>();
            for (int i = 0; i <= 30; i++)
            {
                float t = i * 0.02f;
                bool airborne = (i % 6) >= 3; // 3 幀觸地、3 幀騰空（騰空段僅 0.06s）
                samples.Add(airborne ? Airborne(t, 0.05f) : Grounded(t, 0f));
            }

            var result = RunAnalyzer(samples, 0.6f);

            Assert.AreEqual(0f, result.AutoTakeoffDelay, "騰空段短於 MinAirTime → 候選全數丟棄");
            Assert.AreEqual(0f, result.AutoAirTime);
            Assert.AreEqual(9.81f, result.AutoCalculatedGravity, 1e-6f);
        }

        // === 擦地雜訊：飛行途中單幀觸地（前後皆騰空）應被忽略，滯空跨越該幀 ===
        [Test]
        public void SingleFrameGraze_MidFlight_IsIgnored()
        {
            var samples = new List<MotionFeatureSample>
            {
                Grounded(0.0f, 0f),
                Grounded(0.1f, 0f),
                Grounded(0.2f, 0f),
                Grounded(0.3f, 0f),
                Grounded(0.4f, 0f),
                Airborne(0.5f, 0.2f),
                Airborne(0.6f, 0.5f),
                Airborne(0.7f, 0.8f),
                Airborne(0.8f, 0.9f),
                Grounded(0.9f, 0.75f),  // 單幀擦地（下一幀又騰空）→ 雜訊
                Airborne(1.0f, 0.55f),
                Airborne(1.1f, 0.3f),
                Airborne(1.2f, 0.1f),
                Grounded(1.3f, 0f),     // 連續觸地 → 真實落地
                Grounded(1.4f, 0f),
                Grounded(1.5f, 0f),
            };

            var result = RunAnalyzer(samples, 1.5f);

            // 期望與標準跳躍相同的落地時刻（1.29268），而非在擦地幀（0.9）就終止飛行
            Assert.AreEqual(0.8853658f, result.AutoAirTime, 5e-4f,
                "單幀擦地必須被忽略，滯空時間應量測到後方的真實落地");
        }

        // === 起跳候選門檻：單幀騰空（下一幀即回地）視為採樣雜訊，不構成起跳 ===
        [Test]
        public void SingleAirborneFrame_IsNoiseNotTakeoff()
        {
            var samples = new List<MotionFeatureSample>
            {
                Grounded(0.0f, 0f),
                Grounded(0.1f, 0f),
                Airborne(0.2f, 0.05f), // 單幀騰空雜訊
                Grounded(0.3f, 0f),
                Grounded(0.4f, 0f),
                Grounded(0.5f, 0f),
            };

            var result = RunAnalyzer(samples, 0.5f);

            Assert.AreEqual(0f, result.AutoTakeoffDelay, "單幀騰空不得構成起跳候選（需連續 ≥2 幀）");
            Assert.AreEqual(9.81f, result.AutoCalculatedGravity, 1e-6f);
        }

        // === 邊界：落地發生在片尾最後一幀（無 j+1 可查）仍視為真實接觸，閉環成立 ===
        [Test]
        public void Landing_OnFinalFrame_CountsAsRealContact()
        {
            var samples = new List<MotionFeatureSample>
            {
                Grounded(0.0f, 0f),
                Grounded(0.1f, 0f),
                Grounded(0.2f, 0f),
                Grounded(0.3f, 0f),
                Grounded(0.4f, 0f),
                Airborne(0.5f, 0.2f),
            };
            for (int i = 6; i <= 19; i++)
            {
                float t = i * 0.1f;
                // 1.2s 頂點 1.0，之後下降到片尾前一幀 0.05
                float root = t <= 1.2f ? Mathf.Lerp(0.2f, 1.0f, (t - 0.5f) / 0.7f) : Mathf.Lerp(1.0f, 0.05f, (t - 1.2f) / 0.7f);
                samples.Add(Airborne(t, root));
            }
            samples.Add(Grounded(2.0f, 0f)); // 落地幀 = 最後一幀

            var result = RunAnalyzer(samples, 2.0f);

            Assert.Greater(result.AutoAirTime, 1.5f, "片尾觸地幀應視為真實落地，閉環量測成立");
            float selfConsistentGravity = 8f * result.AutoApexHeight / (result.AutoAirTime * result.AutoAirTime);
            Assert.AreEqual(selfConsistentGravity, result.AutoCalculatedGravity, 1e-4f, "閉環成立 → 重力以量測值逆推");
        }

        // === 防禦：採樣不足（<2 筆）→ 全欄位退化，不拋例外 ===
        [Test]
        public void InsufficientSamples_DegradesWithoutThrowing()
        {
            var result = RunAnalyzer(new List<MotionFeatureSample> { Grounded(0f, 0f) }, 0f);

            Assert.AreEqual(0f, result.AutoAirTime);
            Assert.AreEqual(9.81f, result.AutoCalculatedGravity, 1e-6f);
        }

        // === Stage 契約：單一分析器拋例外不得中斷管線，其餘分析器照常執行；null 入參安全 ===
        private sealed class ThrowingAnalyzer : IMotionFeatureAnalyzer
        {
            public string FeatureName => "Throwing (test double)";
            public void Analyze(MotionFeatureContext context, MotionBakeData target)
                => throw new System.InvalidOperationException("test");
        }

        [Test]
        public void Stage_AnalyzerThrows_PipelineContinuesAndNullInputsAreSafe()
        {
            var target = ScriptableObject.CreateInstance<MotionBakeData>();
            _created.Add(target);
            var context = new MotionFeatureContext(BuildIdealJumpSamples(), 1.5f, Threshold, LeftBaseline, RightBaseline);

            var stage = new MotionFeatureAnalysisStage(new IMotionFeatureAnalyzer[]
            {
                new ThrowingAnalyzer(),
                new JumpFeatureAnalyzer(),
            });

            // 宣告預期的容錯警告：這是 Stage 契約的正確輸出（分析器失敗 → 警告＋管線繼續），不是誤鳴——
            // 本測試親手注入了會拋例外的分析器，警告若未出現代表容錯路徑沒走到，測試理應失敗。
            // 用鬆耦合關鍵詞（Regex）鎖定，警告訊息措辭調整不會誤傷本測試。
            LogAssert.Expect(LogType.Warning, new Regex(@"Throwing \(test double\)"));
            Assert.DoesNotThrow(() => stage.Run(context, target), "單一分析器失敗不得讓 Stage 對外拋例外");
            Assert.Greater(target.AutoAirTime, 0f, "排在拋例外分析器之後的分析器必須照常執行");

            Assert.DoesNotThrow(() => stage.Run(null, target), "context 為 null 時應安全略過");
            Assert.DoesNotThrow(() => stage.Run(context, null), "target 為 null 時應安全略過");
        }
    }
}
