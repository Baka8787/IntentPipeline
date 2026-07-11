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

        /// <summary>左腳相對根節點的本地 Y 高度，用於偵測離地。</summary>
        public readonly float LeftFootLocalY;

        /// <summary>右腳相對根節點的本地 Y 高度，用於偵測離地。</summary>
        public readonly float RightFootLocalY;

        /// <summary>建立一筆影格特徵採樣。</summary>
        public MotionFeatureSample(float time, float rootWorldY, float leftFootLocalY, float rightFootLocalY)
        {
            Time = time;
            RootWorldY = rootWorldY;
            LeftFootLocalY = leftFootLocalY;
            RightFootLocalY = rightFootLocalY;
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

        /// <summary>離地判定門檻（公尺）：雙腳本地 Y 同時超過此值視為離地。</summary>
        public float FootLiftThreshold { get; }

        /// <summary>安全退化用的標準重力常數（正值，公尺/秒²）。</summary>
        public float StandardGravity { get; }

        /// <summary>可信滯空時間下限（秒）：低於此值不進行重力逆推。</summary>
        public float MinAirTime { get; }

        /// <summary>可信最高點高度下限（公尺）：低於此值不進行重力逆推。</summary>
        public float MinApexHeight { get; }

        /// <summary>建立分析上下文，物理常數採用合理預設值。</summary>
        public MotionFeatureContext(
            IReadOnlyList<MotionFeatureSample> samples,
            float duration,
            float footLiftThreshold,
            float standardGravity = 9.81f,
            float minAirTime = 0.1f,
            float minApexHeight = 0.01f)
        {
            Samples = samples;
            Duration = duration;
            FootLiftThreshold = footLiftThreshold;
            StandardGravity = standardGravity;
            MinAirTime = minAirTime;
            MinApexHeight = minApexHeight;
        }
    }

    /// <summary>
    /// 動畫特徵分析器的抽象契約：一個實作負責一組相關特徵，讀取 <see cref="MotionFeatureContext"/>
    /// 並將結果寫入 <see cref="MotionBakeData"/>。新增特徵（如 Landing Time、Stride Length、Trajectory）時，
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
    /// 跳躍物理特徵分析器：自動提取起跳前搖、最高點高度、滯空時間，並逆向推導完美重力常數。
    /// 安全退化：偵測不到起跳（非跳躍動畫）、滯空過短或最高點過低時，重力沿用標準值 9.81。
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

            // 1. 起跳偵測：第一個「雙腳本地 Y 同時超過離地門檻」的影格 = 真正離地時刻。
            int takeoffIndex = -1;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].LeftFootLocalY > context.FootLiftThreshold &&
                    samples[i].RightFootLocalY > context.FootLiftThreshold)
                {
                    takeoffIndex = i;
                    break;
                }
            }
            if (takeoffIndex < 0) return; // 非跳躍動畫 → 維持退化結果

            float takeoffTime = samples[takeoffIndex].Time;
            target.AutoTakeoffDelay = takeoffTime;

            // 2. 最高點高度 h_max：起跳後根節點世界 Y 相對起跳基準的最大上升量。
            float baselineY = samples[takeoffIndex].RootWorldY;
            float maxY = baselineY;
            for (int i = takeoffIndex; i < samples.Count; i++)
            {
                if (samples[i].RootWorldY > maxY) maxY = samples[i].RootWorldY;
            }
            float apexHeight = Mathf.Max(0f, maxY - baselineY);
            target.AutoApexHeight = apexHeight;

            // 3. 滯空時間 t_air（簡化估計）：起跳後的剩餘動畫長度。精確落地時刻屬未來 Landing Time 特徵。
            float airTime = Mathf.Max(0f, context.Duration - takeoffTime);
            target.AutoAirTime = airTime;

            // 4. 逆向推導重力：拋體對稱飛行 h = g·t_air²/8 → g = 8·h / t_air²。
            //    滯空過短或高度過小（例如 Root Motion Y 被 Bake Into Pose 導致採不到上升量）時安全退化。
            if (airTime > context.MinAirTime && apexHeight > context.MinApexHeight)
            {
                target.AutoCalculatedGravity = (8f * apexHeight) / (airTime * airTime);
            }
            else
            {
                target.AutoCalculatedGravity = context.StandardGravity;
            }
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
