using System.Collections.Generic;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>
    /// 一條樓梯剖面線（沿上升方向 s、高度 y）的分析結果。正本規格：`docs/44` §3。
    /// </summary>
    public sealed class StairProfile
    {
        public bool IsValid;
        public string Failure;

        public float FirstEdgeS;
        public float LastEdgeS;

        /// <summary>每一級踢面的位置（s 遞增）。</summary>
        public float[] RiserS;

        /// <summary>每一級踏面高度，由低到高（數量 ＝ 踢面數 ＋ 1）。</summary>
        public float[] TreadHeights;

        public float RiserHeight;
        public float MaxRiserDeviation;
        public float TreadDepth;

        /// <summary>踏面前緣連線 y = NosingIntercept + NosingSlope · s。</summary>
        public float NosingIntercept;
        public float NosingSlope;
        public float NosingResidual;

        public int RiserCount => RiserS?.Length ?? 0;
        public float SlopeDegrees => Mathf.Atan(NosingSlope) * Mathf.Rad2Deg;
        public float LastNosingS => RiserS[RiserS.Length - 1];
        public float BottomTreadHeight => TreadHeights[0];
        public float TopTreadHeight => TreadHeights[TreadHeights.Length - 1];

        public float LineAt(float s) => NosingIntercept + NosingSlope * s;
    }

    /// <summary>
    /// 純函數：把一條沿上升方向取樣的剖面（只含命中點）分析成踢面、踏面與踏面前緣連線。
    /// 不碰場景、不碰物理——產生器與驗證器共用同一份判斷，測試可直接餵合成剖面。
    /// </summary>
    public static class StairProfileAnalyzer
    {
        /// <summary>相鄰取樣高度差超過這個值才算踢面（取樣間距 5 mm，平坦踏面的雜訊遠小於此）。</summary>
        public const float RiserDetectThreshold = 0.03f;
        public const float MinRiserHeight = 0.05f;
        public const float MaxRiserHeight = 0.4f;

        /// <summary>各級踢面高度與平均值的容許差。</summary>
        public const float RiserUniformityTolerance = 0.01f;

        /// <summary>單一踏面內的高度起伏上限——超過就不是平踏面（例如斜坡）。</summary>
        public const float TreadFlatnessTolerance = 0.01f;

        /// <summary>踏面前緣到擬合直線的最大距離。</summary>
        public const float NosingResidualTolerance = 0.01f;

        public const int MinRiserCount = 2;

        /// <summary>相鄰命中點的 s 間距超過最小間距的這個倍數，視為剖面缺口。</summary>
        public const float MaxSampleGapRatio = 2.5f;

        /// <param name="samples">x ＝ s（沿上升方向，遞增排序），y ＝ 命中高度。</param>
        public static StairProfile Analyze(IReadOnlyList<Vector2> samples)
        {
            var result = new StairProfile();
            if (samples == null || samples.Count < 4)
                return Fail(result, "取樣點不足");

            result.FirstEdgeS = samples[0].x;
            result.LastEdgeS = samples[samples.Count - 1].x;

            // 取樣只收命中點：若兩個相鄰命中之間跳過了取樣位置，代表那裡射線落空（懸空踏板、破洞）。
            // 不擋的話，缺口兩側的高度差會被當成一道「踢面」，產生錯誤但看似合理的數字。
            float minStep = float.PositiveInfinity;
            for (int i = 1; i < samples.Count; i++)
                minStep = Mathf.Min(minStep, samples[i].x - samples[i - 1].x);
            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].x - samples[i - 1].x > minStep * MaxSampleGapRatio)
                    return Fail(result, $"剖面在 s={samples[i - 1].x:F3}～{samples[i].x:F3} 之間沒有命中——不是實心連續的樓梯");
            }

            var riserS = new List<float>();
            var treadHeights = new List<float>();
            var riserHeights = new List<float>();

            float treadMin = samples[0].y, treadMax = samples[0].y, treadSum = samples[0].y;
            int treadCount = 1;

            for (int i = 1; i < samples.Count; i++)
            {
                float dy = samples[i].y - samples[i - 1].y;
                if (Mathf.Abs(dy) > RiserDetectThreshold)
                {
                    if (dy < 0f) return Fail(result, $"s={samples[i].x:F3} 高度下降 {dy:F3}——剖面不是朝 s 遞增方向上升");
                    if (treadMax - treadMin > TreadFlatnessTolerance)
                        return Fail(result, $"踏面 #{treadHeights.Count} 起伏 {treadMax - treadMin:F3} m，不是平踏面");

                    treadHeights.Add(treadSum / treadCount);
                    riserS.Add((samples[i - 1].x + samples[i].x) * 0.5f);
                    riserHeights.Add(dy);
                    treadMin = treadMax = treadSum = samples[i].y;
                    treadCount = 1;
                }
                else
                {
                    treadMin = Mathf.Min(treadMin, samples[i].y);
                    treadMax = Mathf.Max(treadMax, samples[i].y);
                    treadSum += samples[i].y;
                    treadCount++;
                }
            }

            if (treadMax - treadMin > TreadFlatnessTolerance)
                return Fail(result, $"最上層踏面起伏 {treadMax - treadMin:F3} m，不是平踏面");
            treadHeights.Add(treadSum / treadCount);

            if (riserS.Count < MinRiserCount)
                return Fail(result, $"只偵測到 {riserS.Count} 級踢面（至少需要 {MinRiserCount} 級）——這不是樓梯剖面");

            float riserMean = 0f;
            foreach (float h in riserHeights) riserMean += h;
            riserMean /= riserHeights.Count;
            float maxDeviation = 0f;
            foreach (float h in riserHeights) maxDeviation = Mathf.Max(maxDeviation, Mathf.Abs(h - riserMean));

            result.RiserS = riserS.ToArray();
            result.TreadHeights = treadHeights.ToArray();
            result.RiserHeight = riserMean;
            result.MaxRiserDeviation = maxDeviation;
            result.TreadDepth = riserS.Count > 1
                ? (riserS[riserS.Count - 1] - riserS[0]) / (riserS.Count - 1)
                : 0f;

            if (riserMean < MinRiserHeight || riserMean > MaxRiserHeight)
                return Fail(result, $"平均級高 {riserMean:F3} m 超出 [{MinRiserHeight}, {MaxRiserHeight}]");
            if (maxDeviation > RiserUniformityTolerance)
                return Fail(result, $"級高不一致：最大偏差 {maxDeviation:F3} m（容許 {RiserUniformityTolerance}）");

            // 踏面前緣：最低踏面的下坡端（剖面起點），以及每一道踢面的頂端。
            int nosingCount = riserS.Count + 1;
            var nosingS = new float[nosingCount];
            var nosingY = new float[nosingCount];
            nosingS[0] = result.FirstEdgeS;
            nosingY[0] = treadHeights[0];
            for (int k = 0; k < riserS.Count; k++)
            {
                nosingS[k + 1] = riserS[k];
                nosingY[k + 1] = treadHeights[k + 1];
            }

            FitLine(nosingS, nosingY, out result.NosingIntercept, out result.NosingSlope);
            float residual = 0f;
            for (int k = 0; k < nosingCount; k++)
                residual = Mathf.Max(residual, Mathf.Abs(result.LineAt(nosingS[k]) - nosingY[k]));
            result.NosingResidual = residual;

            if (result.NosingSlope <= 0f)
                return Fail(result, "踏面前緣連線沒有上升");
            if (residual > NosingResidualTolerance)
                return Fail(result, $"踏面前緣不在同一直線上：最大殘差 {residual:F3} m（容許 {NosingResidualTolerance}）");

            result.IsValid = true;
            return result;
        }

        private static void FitLine(float[] xs, float[] ys, out float intercept, out float slope)
        {
            int n = xs.Length;
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                sx += xs[i];
                sy += ys[i];
                sxx += (double)xs[i] * xs[i];
                sxy += (double)xs[i] * ys[i];
            }

            double denominator = n * sxx - sx * sx;
            double b = denominator != 0 ? (n * sxy - sx * sy) / denominator : 0;
            slope = (float)b;
            intercept = (float)((sy - b * sx) / n);
        }

        private static StairProfile Fail(StairProfile result, string reason)
        {
            result.IsValid = false;
            result.Failure = reason;
            return result;
        }
    }
}
