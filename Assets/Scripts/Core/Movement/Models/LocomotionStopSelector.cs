using System;
using UnityEngine;
using Project.Presentation.Motion;

namespace Project.Core.Movement
{
    public enum LocomotionStopTier
    {
        None = 0,
        Walk = 1,
        Run = 2
    }

    [Serializable]
    public struct LocomotionStopVariant
    {
        [SerializeField] private MotionBakeData bakeData;
        [SerializeField] private string animationKey;

        public MotionBakeData BakeData => bakeData;
        public string AnimationKey => animationKey;
        public bool IsValid => bakeData != null && bakeData.Duration > 0f && !string.IsNullOrEmpty(animationKey);

        public LocomotionStopVariant(MotionBakeData bakeData, string animationKey)
        {
            this.bakeData = bakeData;
            this.animationKey = animationKey;
        }
    }

    /// <summary>只處理 Catalog 已證實的腳相維度；不碰 Facade/MotionDriver，也不讀 LU/RU 檔名。</summary>
    public static class LocomotionStopSelector
    {
        private const float PhaseValueTieTolerance = 0.0001f;

        /// <summary>
        /// 在腳相選片之前先依入場強度選 Stop 集合。兩帶重疊代表配置含糊，安全退化為不播放 Stop。
        /// </summary>
        public static LocomotionStopTier SelectTier(
            float entryIntensity,
            float walkMinimumIntensity,
            float walkMaximumIntensity,
            float runMinimumIntensity,
            float runMaximumIntensity)
        {
            bool isWalk = IsInsideBand(entryIntensity, walkMinimumIntensity, walkMaximumIntensity);
            bool isRun = IsInsideBand(entryIntensity, runMinimumIntensity, runMaximumIntensity);
            if (isWalk == isRun) return LocomotionStopTier.None;
            return isWalk ? LocomotionStopTier.Walk : LocomotionStopTier.Run;
        }

        public static int SelectByEntryPhase(LocomotionStopVariant[] variants, FootPhase currentPhase)
        {
            if (variants == null || variants.Length == 0) return -1;

            int firstValidIndex = -1;
            for (int i = 0; i < variants.Length; i++)
            {
                LocomotionStopVariant variant = variants[i];
                if (!variant.IsValid) continue;
                if (firstValidIndex < 0) firstValidIndex = i;
                if (variant.BakeData.GetFootPhaseAt(0f) == currentPhase) return i;
            }

            return firstValidIndex;
        }

        public static int SelectNextValid(LocomotionStopVariant[] variants, int startIndex)
        {
            if (variants == null || variants.Length == 0) return -1;

            int normalizedStart = startIndex % variants.Length;
            if (normalizedStart < 0) normalizedStart += variants.Length;

            for (int offset = 0; offset < variants.Length; offset++)
            {
                int index = (normalizedStart + offset) % variants.Length;
                if (variants[index].IsValid) return index;
            }

            return -1;
        }

        /// <summary>
        /// 以 Stop 入場 FootPhaseCurve 的連續值，在 loop 的烘焙鍵中找每支變體最接近的 authored 時刻，
        /// 再選「從目前播放頭往前」最早到達者。只讀既有 Bake Data，不手填 0／0.5 相位。
        /// </summary>
        public static bool TrySelectNearestFuturePhaseMatch(
            MotionBakeData loopBakeData,
            LocomotionStopVariant[] variants,
            float currentNormalizedTime,
            float normalizedTolerance,
            out int variantIndex,
            out float targetNormalizedTime)
        {
            variantIndex = -1;
            targetNormalizedTime = 0f;
            if (loopBakeData == null || loopBakeData.Duration <= 0f ||
                loopBakeData.FootPhaseCurve == null || loopBakeData.FootPhaseCurve.length == 0 ||
                variants == null || variants.Length == 0 ||
                float.IsNaN(currentNormalizedTime) || float.IsInfinity(currentNormalizedTime))
            {
                return false;
            }

            AnimationCurve loopCurve = loopBakeData.FootPhaseCurve;
            float tolerance = Mathf.Max(0f, normalizedTolerance);
            float currentCycle = Mathf.Floor(currentNormalizedTime);
            float bestWait = float.PositiveInfinity;

            for (int variant = 0; variant < variants.Length; variant++)
            {
                LocomotionStopVariant candidateVariant = variants[variant];
                AnimationCurve entryCurve = candidateVariant.BakeData != null
                    ? candidateVariant.BakeData.FootPhaseCurve
                    : null;
                if (!candidateVariant.IsValid || entryCurve == null || entryCurve.length == 0) continue;

                float entryValue = entryCurve.Evaluate(0f);
                float closestValueError = float.PositiveInfinity;
                for (int keyIndex = 0; keyIndex < loopCurve.length; keyIndex++)
                {
                    float error = Mathf.Abs(loopCurve[keyIndex].value - entryValue);
                    if (error < closestValueError) closestValueError = error;
                }

                float variantBestWait = float.PositiveInfinity;
                float variantTarget = 0f;
                for (int keyIndex = 0; keyIndex < loopCurve.length; keyIndex++)
                {
                    Keyframe key = loopCurve[keyIndex];
                    float error = Mathf.Abs(key.value - entryValue);
                    if (error > closestValueError + PhaseValueTieTolerance) continue;

                    float phase = Mathf.Repeat(Mathf.Clamp01(key.time / loopBakeData.Duration), 1f);
                    float target = currentCycle + phase;
                    float wait = target - currentNormalizedTime;
                    if (wait < -tolerance)
                    {
                        target += 1f;
                        wait = target - currentNormalizedTime;
                    }
                    if (Mathf.Abs(wait) <= tolerance)
                    {
                        target = currentNormalizedTime;
                        wait = 0f;
                    }

                    if (wait < variantBestWait)
                    {
                        variantBestWait = wait;
                        variantTarget = target;
                    }
                }

                if (variantBestWait < bestWait)
                {
                    bestWait = variantBestWait;
                    variantIndex = variant;
                    targetNormalizedTime = variantTarget;
                }
            }

            return variantIndex >= 0;
        }

        public static bool ShouldRequest(
            bool wasIntending,
            float desiredSpeedNormalized,
            bool stopIsActive,
            bool isGrounded,
            bool hadAmbientMotionLastFrame,
            float deltaTime,
            float entryIntensity,
            float minimumIntensity,
            float maximumIntensity)
        {
            return IsReleaseRequest(
                       wasIntending, desiredSpeedNormalized, stopIsActive, isGrounded,
                       hadAmbientMotionLastFrame, deltaTime) &&
                   IsInsideBand(entryIntensity, minimumIntensity, maximumIntensity);
        }

        public static bool IsReleaseRequest(
            bool wasIntending,
            float desiredSpeedNormalized,
            bool stopIsActive,
            bool isGrounded,
            bool hadAmbientMotionLastFrame,
            float deltaTime)
        {
            return wasIntending &&
                   desiredSpeedNormalized < LocomotionSpeedSmoother.Epsilon &&
                   !stopIsActive &&
                   isGrounded &&
                   hadAmbientMotionLastFrame &&
                   deltaTime > 0f;
        }

        private static bool IsInsideBand(float value, float minimum, float maximum)
            => minimum <= maximum &&
               value >= minimum - LocomotionSpeedSmoother.Epsilon &&
               value <= maximum + LocomotionSpeedSmoother.Epsilon;
    }
}
