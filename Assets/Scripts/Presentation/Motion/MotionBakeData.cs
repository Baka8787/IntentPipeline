using UnityEngine;

namespace Project.Presentation.Motion
{
    /// <summary>
    /// 🆕 v0.7：動畫結束瞬間的落地腳相，供上層動作銜接（步相對齊）判斷該用哪隻腳起步。
    /// </summary>
    public enum FootPhase
    {
        LeftFootDown,
        RightFootDown
    }

    [CreateAssetMenu(fileName = "MotionBakeData", menuName = "Project/Motion/BakeData")]
    public class MotionBakeData : ScriptableObject
    {
        [Header("來源資訊")]
        public AnimationClip SourceClip;
        public float SampleRate = 60f;

        [Header("物理特徵曲線 (X軸為實際時間/秒)")]
        [Tooltip("瞬時速度曲線 (m/s)")]
        public AnimationCurve SpeedCurve;

        [Tooltip("連續累計偏航角曲線 (Degrees)")]
        public AnimationCurve RotationCurve;

        [Header("進階物理特徵（v0.7 新增，取自參考演算法的純數學部分）")]
        [Tooltip("旋轉在第幾秒就已經收斂到終值附近（依容忍度演算法計算）。" +
                 "超過這個時間點後，剩餘的角度變化視為動畫尾段的抖動雜訊，" +
                 "下游狀態可以用這個時間點提早停止套用 deltaYaw，避免收尾抖動被誤判成有意義的轉向。")]
        public float RotationFinishedTime;

        [Tooltip("動畫播放結束的瞬間，左右腳何者落地，供動作銜接（例如翻滾接跑步時要接對步相）使用")]
        public FootPhase EndPhase;

        [Tooltip("整段動畫的總位移方向，換算成『動畫開始那一刻』角色本地座標系下的方向向量，" +
                 "可直接餵給 Blend Tree 的 X/Z 方向參數；若位移量太小或方向太接近正前方（死區內），視為 Vector3.zero（原地動作）。")]
        public Vector3 TargetLocalDirection;

        [Header("自動化特徵分析（Feature Analysis Stage 自動提取，跳躍動畫適用）")]

        /// <summary>
        /// 起跳前搖時間（秒）。偵測到「雙腳同時離開根節點參考高度」的瞬間即為真正離地時刻；
        /// 此時間點之前屬於預備/蓄力姿勢。非跳躍動畫（偵測不到離地）安全退化為 0。
        /// </summary>
        [Tooltip("起跳前搖（秒）：雙腳同時離地的瞬間；之前屬預備/蓄力。非跳躍動畫為 0。")]
        public float AutoTakeoffDelay;

        /// <summary>
        /// 最高點高度 h_max（公尺）。起跳後根節點世界空間 Y 相對起跳基準的最大上升量。
        /// </summary>
        [Tooltip("最高點高度 h_max（公尺）：起跳後根節點 Y 相對起跳基準的最大上升量。")]
        public float AutoApexHeight;

        /// <summary>
        /// 滯空時間 t_air（秒）。目前採 Duration - AutoTakeoffDelay 的簡化估計；
        /// 更精確的落地時刻屬於未來的 Landing Time 特徵。
        /// </summary>
        [Tooltip("滯空時間 t_air（秒）：Duration - AutoTakeoffDelay 的簡化估計。")]
        public float AutoAirTime;

        /// <summary>
        /// 逆向推導的完美重力常數（正值，公尺/秒²）。由拋體運動 g = 8·h_max / t_air² 反推。
        /// 非跳躍動畫、滯空過短或最高點過低時安全退化為標準重力 9.81。
        /// </summary>
        [Tooltip("逆推重力：g = 8·h_max / t_air²。非跳躍/滯空過短/高度過低時退化為 9.81。")]
        public float AutoCalculatedGravity = 9.81f;

        // 便利擴充：取得動畫總長度
        public float Duration => SourceClip != null ? SourceClip.length : 0f;

        /// <summary>
        /// 取得特定時間點的理論「瞬時速度」
        /// </summary>
        public float GetSpeedAt(float time)
        {
            if (SpeedCurve == null || SpeedCurve.length == 0) return 0f;
            return SpeedCurve.Evaluate(time);
        }

        /// <summary>
        /// 取得特定時間點的理論「累計偏航角度」
        /// </summary>
        public float GetRotationAt(float time)
        {
            if (RotationCurve == null || RotationCurve.length == 0) return 0f;
            return RotationCurve.Evaluate(time);
        }

        /// <summary>
        /// 🆕 便利方法：詢問「此刻旋轉是否已經收斂完成」。
        /// 可用於狀態內部判斷是否該停止套用 deltaYaw，或是否可以提早允許自然過渡。
        /// </summary>
        public bool IsRotationFinished(float time) => time >= RotationFinishedTime;
    }
}