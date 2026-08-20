using UnityEngine;

namespace Project.Core.Movement
{
    /// <summary>
    /// Forward Stop 的單一跨幀狀態。由 LocomotionModel 值型別內嵌持有，不進黑板。
    /// 完成或中斷時保留單調遞增的播放世代，讓淡出中的舊回調失效。
    /// </summary>
    public struct LocomotionStopRuntime
    {
        private bool _isActive;
        private bool _isPending;
        private bool _completionRequested;
        private LocomotionStopTier _tier;
        private int _variantIndex;
        private int _generation;
        private float _targetNormalizedTime;
        private float _normalizedTime;
        private float _previousNormalizedTime;
        private float _elapsedRealTime;

        public bool IsActive => _isActive;
        public bool IsPending => _isActive && _isPending;
        public bool IsPlaying => _isActive && !_isPending;
        public bool CompletionRequested => _completionRequested;
        public LocomotionStopTier Tier => _tier;
        public int VariantIndex => _variantIndex;
        public int Generation => _generation;
        public float TargetNormalizedTime => _targetNormalizedTime;
        public float NormalizedTime => _normalizedTime;
        public float PreviousNormalizedTime => _previousNormalizedTime;
        public float ElapsedRealTime => _elapsedRealTime;

        public int Begin(LocomotionStopTier tier, int variantIndex)
            => BeginInternal(tier, variantIndex, false, 0f);

        public int BeginPending(LocomotionStopTier tier, int variantIndex, float targetNormalizedTime)
            => BeginInternal(tier, variantIndex, true, targetNormalizedTime);

        private int BeginInternal(
            LocomotionStopTier tier,
            int variantIndex,
            bool isPending,
            float targetNormalizedTime)
        {
            _generation = NextGeneration(_generation);
            _isActive = true;
            _isPending = isPending;
            _completionRequested = false;
            _tier = tier;
            _variantIndex = variantIndex;
            _targetNormalizedTime = targetNormalizedTime;
            _normalizedTime = 0f;
            _previousNormalizedTime = 0f;
            _elapsedRealTime = 0f;
            return _generation;
        }

        public bool StartPlaying()
        {
            if (!IsPending) return false;

            _isPending = false;
            _normalizedTime = 0f;
            _previousNormalizedTime = 0f;
            _elapsedRealTime = 0f;
            return true;
        }

        public void AdvancePending(float deltaTime)
        {
            if (IsPending && deltaTime > 0f) _elapsedRealTime += deltaTime;
        }

        public void Advance(float normalizedTime, float deltaTime)
        {
            if (!IsPlaying) return;

            _previousNormalizedTime = _normalizedTime;
            float clampedTime = Mathf.Clamp01(normalizedTime);
            if (clampedTime > _normalizedTime) _normalizedTime = clampedTime;
            if (deltaTime > 0f) _elapsedRealTime += deltaTime;
        }

        /// <summary>End Event 只提出完成要求；真正完成由下一次 model Tick 仲裁。</summary>
        public bool TryRequestCompletion(int generation)
        {
            if (!IsPlaying || generation != _generation) return false;
            _completionRequested = true;
            return true;
        }

        public bool HasTimedOut(float duration, float margin)
        {
            return IsPlaying && duration > 0f &&
                   _elapsedRealTime > duration + Mathf.Max(0f, margin);
        }

        public bool HasPendingTimedOut(float timeout)
            => IsPending && timeout > 0f && _elapsedRealTime > timeout;

        /// <summary>
        /// 不可用 this = default：那會把 Generation 歸零，使舊回調可能誤完成新播放。
        /// </summary>
        public void Invalidate()
        {
            _generation = NextGeneration(_generation);
            _isActive = false;
            _isPending = false;
            _completionRequested = false;
            _tier = LocomotionStopTier.None;
            _variantIndex = -1;
            _targetNormalizedTime = 0f;
            _normalizedTime = 0f;
            _previousNormalizedTime = 0f;
            _elapsedRealTime = 0f;
        }

        private static int NextGeneration(int current) => unchecked(current + 1);
    }
}
