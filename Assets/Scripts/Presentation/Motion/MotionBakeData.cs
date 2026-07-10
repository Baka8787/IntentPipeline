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