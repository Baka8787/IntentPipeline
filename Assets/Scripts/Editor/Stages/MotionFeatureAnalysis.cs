#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using Project.Presentation.Motion;

namespace Project.Editor
{
    /// <summary>
    /// 單一影格的特徵採樣資料：Feature Analysis Stage 的原始輸入單元。
    /// 由烘焙採樣迴圈在既有 Root Motion 取樣的同一趟迴圈中一併蒐集（不額外重跑 SampleAnimation）。
    /// </summary>
    public readonly struct MotionFeatureSample
    {
        /// <summary>此採樣點對應的動畫時間（秒）。</summary>
        public readonly float Time;

        /// <summary>根節點的世界空間 Y 高度（累計根運動位移），用於推算最高點。</summary>
        public readonly float RootWorldY;

        /// <summary>左腳骨骼的世界空間 Y 高度，與 Rest Pose 基線比較以偵測騰空/觸地。</summary>
        public readonly float LeftFootWorldY;

        /// <summary>右腳骨骼的世界空間 Y 高度，與 Rest Pose 基線比較以偵測騰空/觸地。</summary>
        public readonly float RightFootWorldY;

        /// <summary>建立一筆影格特徵採樣。</summary>
        public MotionFeatureSample(float time, float rootWorldY, float leftFootWorldY, float rightFootWorldY)
        {
            Time = time;
            RootWorldY = rootWorldY;
            LeftFootWorldY = leftFootWorldY;
            RightFootWorldY = rightFootWorldY;
        }
    }

    /// <summary>
    /// Feature Analysis Stage 的共享上下文：封裝整段動畫的採樣緩衝與分析參數，
    /// 傳遞給每一個 <see cref="IMotionFeatureAnalyzer"/> 使用。
    /// </summary>
    public sealed class MotionFeatureContext
    {
        /// <summary>整段動畫逐影格的特徵採樣（依時間遞增）。</summary>
        public IReadOnlyList<MotionFeatureSample> Samples { get; }

        /// <summary>動畫總長度（秒）。</summary>
        public float Duration { get; }

        /// <summary>離地容忍度（公尺）：腳的世界 Y 超過「自身 Rest Pose 基線 + 此值」視為該腳騰空；落地偵測沿用同一容忍度。</summary>
        public float FootLiftThreshold { get; }

        /// <summary>左腳 Rest Pose 基線（世界 Y，公尺）：於第一次 SampleAnimation 前對採樣替身快取，屬 rig 本身、與 clip 內容解耦。</summary>
        public float LeftFootBaselineY { get; }

        /// <summary>右腳 Rest Pose 基線（世界 Y，公尺）：取得方式同左腳。</summary>
        public float RightFootBaselineY { get; }

        /// <summary>安全退化用的標準重力常數（正值，公尺/秒²）。</summary>
        public float StandardGravity { get; }

        /// <summary>可信滯空時間下限（秒）：騰空段短於此值視為非跳躍騰空（如跑步循環的雙腳騰空相），且低於此值不進行重力逆推。</summary>
        public float MinAirTime { get; }

        /// <summary>可信最高點高度下限（公尺）：低於此值不進行重力逆推。</summary>
        public float MinApexHeight { get; }

        /// <summary>建立分析上下文，物理常數採用合理預設值。</summary>
        public MotionFeatureContext(
            IReadOnlyList<MotionFeatureSample> samples,
            float duration,
            float footLiftThreshold,
            float leftFootBaselineY,
            float rightFootBaselineY,
            float standardGravity = 9.81f,
            float minAirTime = 0.1f,
            float minApexHeight = 0.01f)
        {
            Samples = samples;
            Duration = duration;
            FootLiftThreshold = footLiftThreshold;
            LeftFootBaselineY = leftFootBaselineY;
            RightFootBaselineY = rightFootBaselineY;
            StandardGravity = standardGravity;
            MinAirTime = minAirTime;
            MinApexHeight = minApexHeight;
        }
    }

    /// <summary>
    /// 動畫特徵分析器的抽象契約：一個實作負責一組相關特徵，讀取 <see cref="MotionFeatureContext"/>
    /// 並將結果寫入 <see cref="MotionBakeData"/>。新增特徵（如 Stride Length、Trajectory）時，
    /// 實作本介面並註冊到 <see cref="MotionFeatureAnalysisStage"/> 即可，無需改動採樣迴圈或既有分析器。
    /// </summary>
    public interface IMotionFeatureAnalyzer
    {
        /// <summary>此分析器的可讀名稱（供記錄／除錯）。</summary>
        string FeatureName { get; }

        /// <summary>依採樣上下文分析特徵並寫入目標資產。實作必須自帶安全退化，不可拋例外中斷管線。</summary>
        void Analyze(MotionFeatureContext context, MotionBakeData target);
    }

    /// <summary>
    /// 跳躍物理特徵分析器：以「世界空間相對足跡（World-Relative Footprint）」演算法自動提取
    /// 起跳前搖、最高點高度、滯空時間，並逆向推導自洽重力常數。雙 Pass 結構——
    /// Pass 1（事件偵測）：逐影格以「腳世界高度 vs 自身 Rest Pose 基線＋容忍度」分類騰空/觸地，
    /// 找出通過「持續騰空 ≥ MinAirTime」驗證的起跳影格與首次落地影格；單影格觸地視為擦地雜訊忽略，
    /// 騰空過短的候選（如跑步循環的雙腳騰空相）整段丟棄。
    /// Pass 2（精算閉環）：於 [起跳, 落地] 窗內掃描最高點，對起跳/落地做子影格線性插值
    /// （起跳取後離地的腳、落地取先觸地的腳），閉環出精確滯空時間後以 g = 8·h / t_air² 逆推重力。
    /// 安全退化：偵測不到起跳（非跳躍動畫）→ 全欄位維持預設；找不到落地（jump-loop／跳上高台）→
    /// AutoAirTime = 0、重力沿用標準值 9.81，AutoTakeoffDelay / AutoApexHeight 仍寫入量測值。
    /// </summary>
    public sealed class JumpFeatureAnalyzer : IMotionFeatureAnalyzer
    {
        /// <inheritdoc/>
        public string FeatureName => "Jump (Takeoff / Apex / AirTime / Gravity)";

        /// <inheritdoc/>
        public void Analyze(MotionFeatureContext context, MotionBakeData target)
        {
            // 先寫入安全預設值：任何一步判定失敗都會保留這組退化結果（重力 = 標準值）。
            target.AutoTakeoffDelay = 0f;
            target.AutoApexHeight = 0f;
            target.AutoAirTime = 0f;
            target.AutoCalculatedGravity = context.StandardGravity;

            IReadOnlyList<MotionFeatureSample> samples = context.Samples;
            if (samples == null || samples.Count < 2) return;

            int count = samples.Count;
            float leftLine = context.LeftFootBaselineY + context.FootLiftThreshold;
            float rightLine = context.RightFootBaselineY + context.FootLiftThreshold;

            // ---------- Pass 1：事件偵測（起跳候選 → 持續騰空驗證 → 首次落地） ----------
            int takeoffIndex = -1; // 驗證通過的騰空段第一幀
            int landingIndex = -1; // 終止騰空的真實接觸幀；-1 = 騰空延續到片尾（找不到落地）

            int i = 1;
            while (i < count)
            {
                // 起跳候選：前一幀觸地、本幀起連續兩幀騰空（單幀騰空視為採樣雜訊）。
                bool isCandidate =
                    !IsAirborne(samples[i - 1], leftLine, rightLine) &&
                    IsAirborne(samples[i], leftLine, rightLine) &&
                    i + 1 < count &&
                    IsAirborne(samples[i + 1], leftLine, rightLine);

                if (!isCandidate) { i++; continue; }

                // 沿騰空段前進找真實接觸：單幀觸地（下一幀又騰空）視為擦地雜訊忽略。
                int contact = -1;
                for (int j = i + 1; j < count; j++)
                {
                    if (IsAirborne(samples[j], leftLine, rightLine)) continue;

                    bool isSingleFrameGraze = j + 1 < count && IsAirborne(samples[j + 1], leftLine, rightLine);
                    if (!isSingleFrameGraze) { contact = j; break; }
                }

                // 持續騰空驗證：過濾跑步循環的雙腳騰空相與小碎跳等非跳躍騰空。
                float flightEndTime = contact >= 0 ? samples[contact].Time : samples[count - 1].Time;
                if (flightEndTime - samples[i].Time >= context.MinAirTime)
                {
                    takeoffIndex = i;
                    landingIndex = contact;
                    break; // 第一段通過驗證的飛行即為本 clip 的跳躍
                }

                // 候選不合格：跳過本段騰空與其接觸幀，續掃下一個候選（騰空到片尾則無候選可掃）。
                if (contact < 0) break;
                i = contact + 1;
            }

            if (takeoffIndex < 0) return; // 非跳躍動畫 → 維持退化結果

            // ---------- Pass 2：精算閉環（子影格插值 → 窗內最高點 → 自洽逆推） ----------

            // 2-1. 起跳時刻：在起跳前後兩採樣間對「後離地的那隻腳」做線性插值。
            float takeoffTime = RefineCrossingTime(samples[takeoffIndex - 1], samples[takeoffIndex], leftLine, rightLine, ascending: true);
            target.AutoTakeoffDelay = takeoffTime;

            // 2-2. 最高點高度 h：只在 [起跳, 落地] 窗內掃描根節點世界 Y 的最大值，
            //      基準取插值後起跳時刻的根節點高度（拋體弧線的真正起點）。
            //      頂點本身不需插值：頂點速度趨近 0，30fps 下量化誤差僅約 g·(dt/2)²/2 ≈ 1.4mm。
            int windowEnd = landingIndex >= 0 ? landingIndex : count - 1;
            float takeoffRootY = LerpRootY(samples[takeoffIndex - 1], samples[takeoffIndex], takeoffTime);
            float maxRootY = takeoffRootY;
            for (int w = takeoffIndex; w <= windowEnd; w++)
            {
                if (samples[w].RootWorldY > maxRootY) maxRootY = samples[w].RootWorldY;
            }
            float apexHeight = Mathf.Max(0f, maxRootY - takeoffRootY);
            target.AutoApexHeight = apexHeight;

            // 2-3. 找不到落地（jump-loop／跳上高台的不對稱拋物線）→ 誠實退化：
            //      AutoAirTime 維持 0（明示未量測）、重力維持標準值；前搖與最高點仍為有效量測。
            if (landingIndex < 0) return;

            // 2-4. 落地時刻：在落地前後兩採樣間對「先觸地的那隻腳」做線性插值，閉環出精確滯空時間。
            float landingTime = RefineCrossingTime(samples[landingIndex - 1], samples[landingIndex], leftLine, rightLine, ascending: false);
            float airTime = Mathf.Max(0f, landingTime - takeoffTime);
            target.AutoAirTime = airTime;

            // 2-5. 逆向推導重力：對稱拋體 h = g·t_air²/8 → g = 8·h / t_air²（與執行期 v = √(2gh) 自洽）。
            //      滯空過短或最高點過小（例如 Root Motion Y 被 Bake Into Pose 導致採不到上升量）時安全退化。
            if (airTime > context.MinAirTime && apexHeight > context.MinApexHeight)
            {
                target.AutoCalculatedGravity = (8f * apexHeight) / (airTime * airTime);
            }
        }

        /// <summary>騰空判定：雙腳世界高度同時超過「自身 Rest Pose 基線＋容忍度」的門檻線。</summary>
        private static bool IsAirborne(in MotionFeatureSample sample, float leftLine, float rightLine)
        {
            return sample.LeftFootWorldY > leftLine && sample.RightFootWorldY > rightLine;
        }

        /// <summary>
        /// 子影格精算：在事件前後兩採樣間，逐腳解「跨越自身門檻線」的線性插值交點。
        /// 起跳（ascending）取交點較晚者＝後離地的腳；落地取較早者＝先觸地的腳。
        /// </summary>
        private static float RefineCrossingTime(in MotionFeatureSample prev, in MotionFeatureSample curr, float leftLine, float rightLine, bool ascending)
        {
            bool leftCrossed = TryGetFootCrossingTime(prev.Time, curr.Time, prev.LeftFootWorldY, curr.LeftFootWorldY, leftLine, ascending, out float leftTime);
            bool rightCrossed = TryGetFootCrossingTime(prev.Time, curr.Time, prev.RightFootWorldY, curr.RightFootWorldY, rightLine, ascending, out float rightTime);

            if (leftCrossed && rightCrossed) return ascending ? Mathf.Max(leftTime, rightTime) : Mathf.Min(leftTime, rightTime);
            if (leftCrossed) return leftTime;
            if (rightCrossed) return rightTime;
            return curr.Time; // 防禦：事件邊界理論上必有至少一腳跨線；無交點時退回事件影格時間
        }

        /// <summary>單腳跨線判定與線性插值：腳在前後兩採樣間由門檻線一側跨到另一側時，解出精確跨越時刻。</summary>
        private static bool TryGetFootCrossingTime(float prevTime, float currTime, float prevY, float currY, float line, bool ascending, out float crossingTime)
        {
            crossingTime = currTime;

            bool crossed = ascending ? (prevY <= line && currY > line) : (prevY > line && currY <= line);
            if (!crossed) return false;

            float deltaY = currY - prevY;
            if (Mathf.Abs(deltaY) < 1e-6f) return true; // 兩採樣幾乎等高（防除零）→ 取事件影格時間

            crossingTime = Mathf.Lerp(prevTime, currTime, Mathf.Clamp01((line - prevY) / deltaY));
            return true;
        }

        /// <summary>取指定時刻的根節點世界 Y（於前後兩採樣間線性插值），作為拋體弧線的起點基準。</summary>
        private static float LerpRootY(in MotionFeatureSample prev, in MotionFeatureSample curr, float time)
        {
            float span = curr.Time - prev.Time;
            if (span <= 1e-6f) return curr.RootWorldY;
            return Mathf.Lerp(prev.RootWorldY, curr.RootWorldY, Mathf.Clamp01((time - prev.Time) / span));
        }
    }

    /// <summary>
    /// Feature Analysis Stage：Bake Pipeline 中的特徵分析階段。持有一組 <see cref="IMotionFeatureAnalyzer"/>，
    /// 依序對同一份採樣上下文執行，把結果寫進 <see cref="MotionBakeData"/>。
    /// 位置：Root Motion 曲線提取「之後」、資產存檔「之前」，完全不干涉既有曲線／旋轉收斂／腳相邏輯。
    /// </summary>
    public sealed class MotionFeatureAnalysisStage
    {
        private readonly List<IMotionFeatureAnalyzer> _analyzers;

        /// <summary>建立階段並註冊預設分析器（目前為跳躍特徵）。未來新增特徵在此註冊即可。</summary>
        public MotionFeatureAnalysisStage()
        {
            _analyzers = new List<IMotionFeatureAnalyzer>
            {
                new JumpFeatureAnalyzer(),
            };
        }

        /// <summary>允許外部注入自訂分析器清單（例如測試或特殊管線）。</summary>
        public MotionFeatureAnalysisStage(IEnumerable<IMotionFeatureAnalyzer> analyzers)
        {
            _analyzers = new List<IMotionFeatureAnalyzer>(analyzers);
        }

        /// <summary>依序執行所有已註冊的分析器；單一分析器失敗不影響其餘分析器與既有烘焙結果。</summary>
        public void Run(MotionFeatureContext context, MotionBakeData target)
        {
            if (context == null || target == null) return;

            foreach (IMotionFeatureAnalyzer analyzer in _analyzers)
            {
                try
                {
                    analyzer.Analyze(context, target);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[MotionFeatureAnalysis] 分析器 '{analyzer.FeatureName}' 執行失敗，已略過：{ex.Message}");
                }
            }
        }
    }
}
#endif
