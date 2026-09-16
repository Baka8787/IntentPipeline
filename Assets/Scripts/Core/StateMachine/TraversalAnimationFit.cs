using Project.Core.Environment;
using Project.Presentation.Motion;
using UnityEngine;

// ⚠️ 命名空間分工與 `TraversalSelectionPolicy.cs` 相同：
//    **結果型別**（Fit／Reason）放 `Project.Core.Environment`，因為 Probe 要能持有它做觀測；
//    **設定與演算法**（Settings／Fitter）放 `Project.Core.StateMachine`，因為它們要讀 `MotionBakeData`。
//    LayerRule：`Core/Environment` 不得依賴 StateMachine／Presentation，故結果型別不能放 StateMachine。
namespace Project.Core.Environment
{
    /// <summary>
    /// Animation Fitting 的結果分類。**每一種結果都要說得出口**——「沒有 fit」和「fit 過但被夾住」
    /// 是兩件完全不同的事，靜默退化正是 `docs/23` §A10 記過的除錯黑洞。
    /// </summary>
    public enum TraversalFitReason
    {
        /// <summary>Fitting 關閉，或這支動畫沒有可 fit 的量（例如接觸前 root 根本不移動）。</summary>
        NotFitted = 0,

        /// <summary>算出來的 rate 在合法範圍內，直接採用。</summary>
        Fitted,

        /// <summary>需求比動畫短 ⇒ 想加速，但被 <c>MaximumPlaybackRate</c> 夾住。</summary>
        RateClampedFast,

        /// <summary>需求比動畫長 ⇒ 想放慢，但被 <c>MinimumPlaybackRate</c> 夾住。</summary>
        RateClampedSlow,

        /// <summary>缺 bake 或 Traversal block，無法 fit。</summary>
        MissingBakeData,

        /// <summary>需求距離退化（≤0 或非有限值），無法定義 rate。</summary>
        DegenerateRequirement,
    }

    /// <summary>
    /// 一次 traversal 的 Animation Fitting 結果：**在動畫播放之前，先讓動畫去配合現場尺寸**，
    /// 而不是讓 root warp 事後用位置 correction 硬吃落差。
    ///
    /// v1 只做 **playback rate**（時間域），不做 clip selection 與 start-time scrub。
    /// 語意來自 KINEMATION Motion Warping：「障礙物長度是動畫設計長度的兩倍 ⇒ play rate 減半」，
    /// 目的是讓**世界速度維持動畫本來描繪的速度**，避免 warp 之後角色看起來被拉著飛。
    /// 完整比較見 `docs/24-traversal-reference-study.md` §3。
    ///
    /// ⚠️ **rate 不會改變動畫的位移量**（那是 warp 的事），它改變的是走完那段位移所花的時間。
    /// 因此 rate 不能取代 correction，只能讓殘餘 correction 的**速度**合理、滑步不明顯。
    /// 真正把 correction 量降下來的是 <see cref="LandingCommittedDepth"/> 那條路徑——
    /// 讓 Probe 量到動畫真正需要的落地深度，而不是停在一個探測常數上。
    /// </summary>
    public readonly struct TraversalAnimationFit
    {
        /// <summary>動畫播放速率倍率。1 ＝ 不改動。</summary>
        public readonly float PlaybackRate;

        /// <summary>夾限前的原始值。與 <see cref="PlaybackRate"/> 不同就代表被夾過，debug 要看得到。</summary>
        public readonly float RawPlaybackRate;

        /// <summary>動畫自己在「起跳 → 第一次接觸」之間走的水平距離（m）。</summary>
        public readonly float ApproachAnimatedDistance;

        /// <summary>以玩家實際站位換算，同一段實際需要走的水平距離（m）。</summary>
        public readonly float ApproachRequiredDistance;

        /// <summary>動畫自己的自然落點深度（越過邊緣之後，m）。</summary>
        public readonly float LandingAnimatedDepth;

        /// <summary>Probe 實際驗證過、可以落腳的最遠深度（m）。</summary>
        public readonly float LandingAvailableDepth;

        /// <summary>兩者取小＝這次真的敢 commit 的落點深度（m）。</summary>
        public readonly float LandingCommittedDepth;

        public readonly TraversalFitReason Reason;

        public bool IsFitted => Reason == TraversalFitReason.Fitted ||
                                Reason == TraversalFitReason.RateClampedFast ||
                                Reason == TraversalFitReason.RateClampedSlow;

        /// <summary>動畫想走的距離 − 實際需要的距離。&gt;0 代表動畫會走過頭。</summary>
        public float ApproachSurplus => ApproachAnimatedDistance - ApproachRequiredDistance;

        public TraversalAnimationFit(
            float playbackRate,
            float rawPlaybackRate,
            float approachAnimatedDistance,
            float approachRequiredDistance,
            float landingAnimatedDepth,
            float landingAvailableDepth,
            float landingCommittedDepth,
            TraversalFitReason reason)
        {
            PlaybackRate = playbackRate;
            RawPlaybackRate = rawPlaybackRate;
            ApproachAnimatedDistance = approachAnimatedDistance;
            ApproachRequiredDistance = approachRequiredDistance;
            LandingAnimatedDepth = landingAnimatedDepth;
            LandingAvailableDepth = landingAvailableDepth;
            LandingCommittedDepth = landingCommittedDepth;
            Reason = reason;
        }

        /// <summary>未 fit 的中性結果：rate 1、沒有落點意見。行為與導入本層之前完全相同。</summary>
        public static TraversalAnimationFit Unfitted(TraversalFitReason reason) =>
            new TraversalAnimationFit(
                1f, 1f, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, reason);
    }
}

namespace Project.Core.StateMachine
{
    /// <summary>
    /// Animation Fitting 的 authored 參數。**只有夾限**——rate 本身一律由資料推導，
    /// 這裡不提供「手動 rate」欄位，否則就又變成一個要人維護的 magic number。
    /// </summary>
    [System.Serializable]
    public struct TraversalAnimationFitSettings
    {
        private const float DefaultMinimumRate = 0.6f;
        private const float DefaultMaximumRate = 1.6f;

        [Tooltip("關掉之後 PlaybackRate 恆為 1，行為與導入 Animation Fitting 之前相同。")]
        [SerializeField] private bool enabled;

        [Tooltip("播放速率下限。需求位移比動畫長時會想放慢，過慢會看起來像慢動作。")]
        [SerializeField, Min(0.05f)] private float minimumPlaybackRate;

        [Tooltip("播放速率上限。需求位移比動畫短時會想加速，過快會看起來像快轉。")]
        [SerializeField, Min(0.05f)] private float maximumPlaybackRate;

        public bool Enabled => enabled;

        public float MinimumPlaybackRate => IsFinitePositive(minimumPlaybackRate)
            ? minimumPlaybackRate
            : DefaultMinimumRate;

        public float MaximumPlaybackRate
        {
            get
            {
                float maximum = IsFinitePositive(maximumPlaybackRate)
                    ? maximumPlaybackRate
                    : DefaultMaximumRate;
                return Mathf.Max(maximum, MinimumPlaybackRate);
            }
        }

        public static TraversalAnimationFitSettings Default =>
            new TraversalAnimationFitSettings(true, DefaultMinimumRate, DefaultMaximumRate);

        public TraversalAnimationFitSettings(
            bool enabled, float minimumPlaybackRate, float maximumPlaybackRate)
        {
            this.enabled = enabled;
            this.minimumPlaybackRate = minimumPlaybackRate;
            this.maximumPlaybackRate = maximumPlaybackRate;
        }

        private static bool IsFinitePositive(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }

    /// <summary>
    /// **Animation Fitting layer**（`docs/24` §8：`Probe → Selection → Animation Fitting →
    /// TraversalPlan → Root Warp → Contact → IK`）。
    ///
    /// 純函式：只讀 bake ／ candidate ／ entry evaluation，不查 Physics、不讀 Transform、不寫黑板。
    /// 它回答的問題是「**這支動畫要怎麼播，才會貼近現場的尺寸**」，
    /// 而 <see cref="TraversalPlanBuilder"/> 回答的是「**剩下的落差要怎麼用位置修正吃掉**」。
    /// 兩者順序不可對調：先讓動畫盡量合身，剩下的才交給 warp。
    /// </summary>
    public static class TraversalAnimationFitter
    {
        private const float Epsilon = 0.0001f;

        public static TraversalAnimationFit Fit(
            MotionBakeData bake,
            in TraversalCandidate candidate,
            in TraversalEntryEvaluation entry,
            in TraversalAnimationFitSettings settings)
        {
            if (!settings.Enabled)
                return TraversalAnimationFit.Unfitted(TraversalFitReason.NotFitted);
            if (bake == null || !bake.Traversal.HasValidBakedData)
                return TraversalAnimationFit.Unfitted(TraversalFitReason.MissingBakeData);
            if (!bake.TryGetDesiredEntryDistance(out float desiredEntryDistance))
                return TraversalAnimationFit.Unfitted(TraversalFitReason.MissingBakeData);

            float landingAnimated = bake.TryGetNaturalExitDepth(out float naturalDepth)
                ? naturalDepth
                : float.NaN;
            float landingAvailable = candidate.LandingSurfaceDepth;
            float landingCommitted = ResolveCommittedLandingDepth(landingAnimated, landingAvailable);

            // ── 接觸前的助跑：動畫自己走多遠 vs 這次實際需要走多遠 ──────────────────
            //    desiredEntryDistance ＝ 接觸瞬間手的前伸距離 ＋ 接觸前 root 已走的水平位移，
            //    因此「實際需要的助跑」＝「動畫的助跑」＋「玩家站位與理想站位的差」。
            //    站得比理想近 ⇒ longitudinalError < 0 ⇒ 需要走的更短 ⇒ 要播快一點。
            float approachAnimated = GetApproachDistance(bake);
            float longitudinalError = entry.LongitudinalError;
            if (!IsFinite(approachAnimated) || !IsFinite(longitudinalError) ||
                !IsFinite(desiredEntryDistance))
            {
                return Unfittable(
                    TraversalFitReason.DegenerateRequirement,
                    approachAnimated, float.NaN,
                    landingAnimated, landingAvailable, landingCommitted);
            }

            float approachRequired = approachAnimated + longitudinalError;

            // 動畫在接觸前 root 根本不動（`Climb2m` 實測 H(contact) = 0）⇒ 沒有速度可以配。
            // 這不是失敗，是「這支動畫沒有這個維度」，據實回報 NotFitted 而不是硬算出一個 rate。
            if (approachAnimated <= Epsilon)
            {
                return Unfittable(
                    TraversalFitReason.NotFitted,
                    approachAnimated, approachRequired,
                    landingAnimated, landingAvailable, landingCommitted);
            }
            if (approachRequired <= Epsilon)
            {
                return Unfittable(
                    TraversalFitReason.DegenerateRequirement,
                    approachAnimated, approachRequired,
                    landingAnimated, landingAvailable, landingCommitted);
            }

            // rate = 動畫位移 / 需求位移。需求較短 ⇒ rate > 1（播快，時間同比例縮短 ⇒ 世界速度不變）。
            float rawRate = approachAnimated / approachRequired;
            if (!IsFinite(rawRate) || rawRate <= 0f)
            {
                return Unfittable(
                    TraversalFitReason.DegenerateRequirement,
                    approachAnimated, approachRequired,
                    landingAnimated, landingAvailable, landingCommitted);
            }

            float minimum = settings.MinimumPlaybackRate;
            float maximum = settings.MaximumPlaybackRate;
            float rate = Mathf.Clamp(rawRate, minimum, maximum);
            TraversalFitReason reason = TraversalFitReason.Fitted;
            if (rawRate > maximum + Epsilon) reason = TraversalFitReason.RateClampedFast;
            else if (rawRate < minimum - Epsilon) reason = TraversalFitReason.RateClampedSlow;

            return new TraversalAnimationFit(
                rate, rawRate,
                approachAnimated, approachRequired,
                landingAnimated, landingAvailable, landingCommitted,
                reason);
        }

        /// <summary>
        /// 這次要 commit 的落點深度：動畫的自然落點與 Probe 驗證過的落腳面**取小**。
        ///
        /// 為什麼取小：比動畫遠 ⇒ 放手後還要被往前推（就是 Playtest 的「抓牢後還在位移」）；
        /// 比驗證過的面遠 ⇒ 踩空。兩個都不能要。
        /// 任一端未知時退回另一端；都未知時回 <c>NaN</c>，呼叫端維持既有行為。
        /// </summary>
        public static float ResolveCommittedLandingDepth(float animatedDepth, float availableDepth)
        {
            bool hasAnimated = IsFinite(animatedDepth) && animatedDepth > Epsilon;
            bool hasAvailable = IsFinite(availableDepth) && availableDepth > Epsilon;
            if (hasAnimated && hasAvailable) return Mathf.Min(animatedDepth, availableDepth);
            if (hasAnimated) return animatedDepth;
            if (hasAvailable) return availableDepth;
            return float.NaN;
        }

        /// <summary>動畫自己在「起點 → 第一次接觸」之間的水平位移。</summary>
        public static float GetApproachDistance(MotionBakeData bake)
        {
            if (bake == null || !bake.Traversal.HasValidBakedData) return float.NaN;
            float duration = bake.Duration;
            if (!IsFinite(duration) || duration <= 0f) return float.NaN;

            float contact = GetFirstContactNormalizedTime(bake);
            if (!IsFinite(contact)) return float.NaN;
            float value = bake.GetHorizontalDisplacementAt(Mathf.Clamp01(contact) * duration);
            return IsFinite(value) ? Mathf.Max(0f, value) : float.NaN;
        }

        /// <summary>第一隻手著點的時刻——助跑段在那一刻結束。</summary>
        public static float GetFirstContactNormalizedTime(MotionBakeData bake)
        {
            if (bake == null || !bake.Traversal.HasValidBakedData) return float.NaN;
            TraversalMotionBakeBlock block = bake.Traversal;
            float left = block.UsesLeftHand ? block.LeftHandContactNormalizedTime : float.NaN;
            float right = block.UsesRightHand ? block.RightHandContactNormalizedTime : float.NaN;
            if (IsFinite(left) && IsFinite(right)) return Mathf.Min(left, right);
            if (IsFinite(left)) return left;
            if (IsFinite(right)) return right;
            return float.NaN;
        }

        private static TraversalAnimationFit Unfittable(
            TraversalFitReason reason,
            float approachAnimated,
            float approachRequired,
            float landingAnimated,
            float landingAvailable,
            float landingCommitted) =>
            new TraversalAnimationFit(
                1f, 1f,
                approachAnimated, approachRequired,
                landingAnimated, landingAvailable, landingCommitted,
                reason);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
