using UnityEngine;

namespace Project.Presentation.Motion
{
    [CreateAssetMenu(fileName = "MotionBakeData", menuName = "Project/Motion/BakeData")]
    public class MotionBakeData : ScriptableObject
    {
        [Header("來源資訊")]
        public AnimationClip SourceClip;
        public float SampleRate = 30f;

        [Header("物理特徵曲線 (X軸為實際時間/秒)")]
        [Tooltip("瞬時速度曲線 (m/s)")]
        public AnimationCurve SpeedCurve;

        [Tooltip("連續累計偏航角曲線 (Degrees)")]
        public AnimationCurve RotationCurve;

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
    }
}