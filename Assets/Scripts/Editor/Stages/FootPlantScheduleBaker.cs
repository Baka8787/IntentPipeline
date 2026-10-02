#if UNITY_EDITOR
using System.Collections.Generic;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>MotionBakeEditor 同一趟 clip sampling 產生的 plant 偵測輸入。</summary>
    public struct FootPlantBakeSample
    {
        public float Time;
        public Vector3 LeftAnkleWorld;
        public Vector3 RightAnkleWorld;
        public Vector3 LeftAnkleInRoot;
        public Vector3 RightAnkleInRoot;
    }

    /// <summary>
    /// Editor-only：Stop clip 的結束站姿與 planned plant 排程（ADR-010 D2／D5）。
    /// plant ＝ 世界 XZ 水平速度低於門檻，且 ankle 高度落在該腳 clip-local 最低點上方容忍帶內；
    /// 連續至少兩個 sample 才成立，每個連續區只發布一次（時間＝該區第一個 sample）。
    /// </summary>
    public static class FootPlantScheduleBaker
    {
        public const float DefaultHorizontalSpeedMax = 0.20f;
        public const float DefaultHeightTolerance = 0.025f;
        public const float DefaultMinimumSeparation = 0.12f;
        private const int MinimumPlantSamples = 2;

        public static FootPlantSchedule Build(
            IReadOnlyList<FootPlantBakeSample> samples,
            float duration,
            out bool truncated,
            float horizontalSpeedMax = DefaultHorizontalSpeedMax,
            float heightTolerance = DefaultHeightTolerance,
            float minimumSeparation = DefaultMinimumSeparation)
        {
            truncated = false;
            var schedule = new FootPlantSchedule();
            if (samples == null || samples.Count < 2 || duration <= 0f) return schedule;

            float leftMinimum = float.PositiveInfinity;
            float rightMinimum = float.PositiveInfinity;
            for (int i = 0; i < samples.Count; i++)
            {
                leftMinimum = Mathf.Min(leftMinimum, samples[i].LeftAnkleWorld.y);
                rightMinimum = Mathf.Min(rightMinimum, samples[i].RightAnkleWorld.y);
            }

            var events = new List<FootPlantScheduleEvent>(FootPlantSchedule.Capacity + 2);
            AppendSide(samples, FootPlantSide.Left, leftMinimum, Mathf.Max(0f, horizontalSpeedMax),
                Mathf.Max(0f, heightTolerance), Mathf.Max(0f, minimumSeparation), events);
            AppendSide(samples, FootPlantSide.Right, rightMinimum, Mathf.Max(0f, horizontalSpeedMax),
                Mathf.Max(0f, heightTolerance), Mathf.Max(0f, minimumSeparation), events);
            events.Sort((a, b) => a.ClipTime.CompareTo(b.ClipTime));

            schedule.EventCount = Mathf.Min(events.Count, FootPlantSchedule.Capacity);
            truncated = events.Count > FootPlantSchedule.Capacity;
            for (int i = 0; i < schedule.EventCount; i++)
            {
                FootPlantScheduleEvent value = events[i];
                schedule.SetEvent(i, in value);
            }

            FootPlantBakeSample end = samples[samples.Count - 1];
            schedule.EndPose = new FootPlantEndPose
            {
                LeftAnkleInRoot = end.LeftAnkleInRoot,
                RightAnkleInRoot = end.RightAnkleInRoot,
                ClipDuration = duration,
            };
            return schedule;
        }

        private static void AppendSide(
            IReadOnlyList<FootPlantBakeSample> samples,
            FootPlantSide side,
            float minimumHeight,
            float horizontalSpeedMax,
            float heightTolerance,
            float minimumSeparation,
            List<FootPlantScheduleEvent> output)
        {
            float previousEventTime = float.NegativeInfinity;
            int index = 0;
            while (index < samples.Count)
            {
                if (!IsCandidate(samples, index, side, minimumHeight, horizontalSpeedMax, heightTolerance))
                {
                    index++;
                    continue;
                }

                int first = index;
                while (index + 1 < samples.Count &&
                       IsCandidate(samples, index + 1, side, minimumHeight, horizontalSpeedMax, heightTolerance))
                {
                    index++;
                }
                int last = index;
                index++;
                if (last - first + 1 < MinimumPlantSamples) continue;

                FootPlantBakeSample sample = samples[first];
                if (sample.Time - previousEventTime < minimumSeparation) continue;
                previousEventTime = sample.Time;
                output.Add(new FootPlantScheduleEvent
                {
                    Side = side,
                    ClipTime = sample.Time,
                    AnkleInRoot = side == FootPlantSide.Left ? sample.LeftAnkleInRoot : sample.RightAnkleInRoot,
                });
            }
        }

        private static bool IsCandidate(
            IReadOnlyList<FootPlantBakeSample> samples,
            int index,
            FootPlantSide side,
            float minimumHeight,
            float horizontalSpeedMax,
            float heightTolerance)
        {
            FootPlantBakeSample sample = samples[index];
            Vector3 ankle = side == FootPlantSide.Left ? sample.LeftAnkleWorld : sample.RightAnkleWorld;
            if (ankle.y > minimumHeight + heightTolerance) return false;

            int before = Mathf.Max(0, index - 1);
            int after = Mathf.Min(samples.Count - 1, index + 1);
            float dt = samples[after].Time - samples[before].Time;
            if (dt <= 0f) return false;
            Vector3 a = side == FootPlantSide.Left ? samples[before].LeftAnkleWorld : samples[before].RightAnkleWorld;
            Vector3 b = side == FootPlantSide.Left ? samples[after].LeftAnkleWorld : samples[after].RightAnkleWorld;
            float speed = new Vector2(b.x - a.x, b.z - a.z).magnitude / dt;
            return speed <= horizontalSpeedMax;
        }
    }
}
#endif
