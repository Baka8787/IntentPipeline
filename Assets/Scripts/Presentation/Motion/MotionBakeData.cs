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

    public enum TraversalHandContactMode
    {
        None = 0,
        LeftHand,
        RightHand,
        BothHands,
    }

    /// <summary>
    /// Per-animation collision mapping. V3 activates the center channel only; height/radius
    /// remain explicit evidence-gated seams and are disabled by default.
    /// </summary>
    [System.Serializable]
    public struct TraversalCollisionProfile
    {
        [SerializeField] private bool enableCenterShift;
        [SerializeField] private AnimationCurve centerX;
        [SerializeField] private AnimationCurve centerY;
        [SerializeField] private AnimationCurve centerZ;

        [Header("Reserved — enable only after C1 evidence")]
        [SerializeField] private bool enableHeightAdjustment;
        [SerializeField] private AnimationCurve heightScale;
        [SerializeField] private bool enableRadiusAdjustment;
        [SerializeField] private AnimationCurve radiusScale;

        public bool EnableCenterShift => enableCenterShift;
        public bool HeightAdjustmentReserved => enableHeightAdjustment;
        public bool RadiusAdjustmentReserved => enableRadiusAdjustment;
        public AnimationCurve HeightScaleCurve => heightScale;
        public AnimationCurve RadiusScaleCurve => radiusScale;

        public TraversalCollisionProfile(
            bool enableCenterShift,
            AnimationCurve centerX,
            AnimationCurve centerY,
            AnimationCurve centerZ)
        {
            this.enableCenterShift = enableCenterShift;
            this.centerX = centerX;
            this.centerY = centerY;
            this.centerZ = centerZ;
            enableHeightAdjustment = false;
            heightScale = null;
            enableRadiusAdjustment = false;
            radiusScale = null;
        }

        public Vector3 EvaluateCenterOffset(float normalizedTime)
        {
            if (!enableCenterShift || !IsFinite(normalizedTime)) return Vector3.zero;
            float time = Mathf.Clamp01(normalizedTime);
            Vector3 value = new Vector3(
                EvaluateFinite(centerX, time),
                EvaluateFinite(centerY, time),
                EvaluateFinite(centerZ, time));
            return IsFinite(value) ? value : Vector3.zero;
        }

        private static float EvaluateFinite(AnimationCurve curve, float time)
        {
            if (curve == null || curve.length == 0) return 0f;
            float value = curve.Evaluate(time);
            return IsFinite(value) ? value : 0f;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [System.Serializable]
    public struct TraversalBakedAnchor
    {
        [SerializeField] private float normalizedTime;
        [SerializeField] private Vector3 rootLocalPosition;
        [SerializeField] private float rootLocalYaw;
        [SerializeField] private Vector3 leftHandInRoot;
        [SerializeField] private Vector3 rightHandInRoot;
        [SerializeField] private bool hasLeftHand;
        [SerializeField] private bool hasRightHand;

        public float NormalizedTime => normalizedTime;
        public Vector3 RootLocalPosition => rootLocalPosition;
        public float RootLocalYaw => rootLocalYaw;
        public Vector3 LeftHandInRoot => leftHandInRoot;
        public Vector3 RightHandInRoot => rightHandInRoot;
        public bool HasLeftHand => hasLeftHand;
        public bool HasRightHand => hasRightHand;
        public bool IsFinite =>
            IsFiniteValue(normalizedTime) && IsFiniteValue(rootLocalPosition) &&
            IsFiniteValue(rootLocalYaw) && IsFiniteValue(leftHandInRoot) &&
            IsFiniteValue(rightHandInRoot);

        public TraversalBakedAnchor(
            float normalizedTime,
            Vector3 rootLocalPosition,
            float rootLocalYaw,
            Vector3 leftHandInRoot,
            Vector3 rightHandInRoot,
            bool hasLeftHand,
            bool hasRightHand)
        {
            this.normalizedTime = normalizedTime;
            this.rootLocalPosition = rootLocalPosition;
            this.rootLocalYaw = rootLocalYaw;
            this.leftHandInRoot = leftHandInRoot;
            this.rightHandInRoot = rightHandInRoot;
            this.hasLeftHand = hasLeftHand;
            this.hasRightHand = hasRightHand;
        }

        private static bool IsFiniteValue(Vector3 value) =>
            IsFiniteValue(value.x) && IsFiniteValue(value.y) && IsFiniteValue(value.z);

        private static bool IsFiniteValue(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Optional traversal authoring plus automatically sampled spatial data. Marker time/role
    /// and humanoid bone choice are authored; root/hand transforms are bake outputs.
    /// </summary>
    [System.Serializable]
    public struct TraversalHandIKWindow
    {
        [SerializeField] private bool enabled;
        [SerializeField, Range(0f, 1f)] private float startNormalizedTime;
        [SerializeField, Range(0f, 1f)] private float fullNormalizedTime;
        [SerializeField, Range(0f, 1f)] private float releaseNormalizedTime;

        public bool Enabled => enabled;
        public float StartNormalizedTime => startNormalizedTime;
        public float FullNormalizedTime => fullNormalizedTime;
        public float ReleaseNormalizedTime => releaseNormalizedTime;
        public bool IsValid =>
            enabled && IsNormalized(startNormalizedTime) && IsNormalized(fullNormalizedTime) &&
            IsNormalized(releaseNormalizedTime) && startNormalizedTime <= fullNormalizedTime &&
            fullNormalizedTime <= releaseNormalizedTime;

        public TraversalHandIKWindow(
            bool enabled,
            float startNormalizedTime,
            float fullNormalizedTime,
            float releaseNormalizedTime)
        {
            this.enabled = enabled;
            this.startNormalizedTime = startNormalizedTime;
            this.fullNormalizedTime = fullNormalizedTime;
            this.releaseNormalizedTime = releaseNormalizedTime;
        }

        /// <summary>
        /// Pure deterministic 0→1→hold→0 envelope. Release starts the fade; the authored Exit
        /// marker is its zero endpoint so IK cannot survive into Recovery.
        /// </summary>
        public float Evaluate(float normalizedTime, float exitNormalizedTime)
        {
            if (!IsValid || !IsNormalized(normalizedTime) || !IsNormalized(exitNormalizedTime) ||
                exitNormalizedTime < releaseNormalizedTime)
            {
                return 0f;
            }

            // ⭐ 2026-09-13：淡出終點原本**直接用 Exit marker**。那對 `Climb2m` 是災難——
            //    即使最晚到 transfer(0.269) 才放開，exit 卻在 0.833 ⇒ 淡出長達整支 clip 的
            //    56%（約 2 秒），
            //    手會被黏在邊緣上跟著身體往上拖。
            //    改為**淡出與淡入等長**（對稱，不引入新的 authored 數字），再用 Exit 夾住上限
            //    ——「IK 不得存活到 Recovery」這個保證原樣成立。
            float fadeSpan = Mathf.Max(0f, fullNormalizedTime - startNormalizedTime);
            float fadeEnd = Mathf.Min(exitNormalizedTime, releaseNormalizedTime + fadeSpan);

            if (normalizedTime <= startNormalizedTime || normalizedTime >= exitNormalizedTime)
                return 0f;
            if (normalizedTime < fullNormalizedTime)
                return Smooth01(Mathf.InverseLerp(startNormalizedTime, fullNormalizedTime, normalizedTime));
            if (normalizedTime <= releaseNormalizedTime) return 1f;
            if (normalizedTime >= fadeEnd) return 0f;
            return 1f - Smooth01(Mathf.InverseLerp(
                releaseNormalizedTime, fadeEnd, normalizedTime));
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static bool IsNormalized(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
    }

    /// <summary>
    /// Bake-time humanoid geometry converted into a root-local support correction curve.
    /// The runtime plan only evaluates this data; it never reads Animator bones or performs Physics queries.
    /// </summary>
    [System.Serializable]
    public struct TraversalRootReachConstraint
    {
        [SerializeField] private bool enabled;
        [SerializeField, Range(0f, 1f)] private float maximumReachRatio;
        [SerializeField, Range(0f, 1f)] private float anchorReleaseNormalizedTime;
        [SerializeField, Range(0f, 1f)] private float correctionEndNormalizedTime;
        [SerializeField] private AnimationCurve localX;
        [SerializeField] private AnimationCurve localY;
        [SerializeField] private AnimationCurve localZ;

        public bool Enabled => enabled;
        public float MaximumReachRatio => maximumReachRatio;
        public float AnchorReleaseNormalizedTime => anchorReleaseNormalizedTime;
        public float CorrectionEndNormalizedTime => correctionEndNormalizedTime;
        public bool IsValid =>
            enabled && IsFinite(maximumReachRatio) && maximumReachRatio > 0f &&
            maximumReachRatio < 1f && IsNormalized(anchorReleaseNormalizedTime) &&
            IsNormalized(correctionEndNormalizedTime) &&
            correctionEndNormalizedTime >= anchorReleaseNormalizedTime &&
            HasFiniteKeys(localX) && HasFiniteKeys(localY) && HasFiniteKeys(localZ);

        public TraversalRootReachConstraint(
            float maximumReachRatio,
            float anchorReleaseNormalizedTime,
            float correctionEndNormalizedTime,
            AnimationCurve localX,
            AnimationCurve localY,
            AnimationCurve localZ)
        {
            enabled = true;
            this.maximumReachRatio = maximumReachRatio;
            this.anchorReleaseNormalizedTime = anchorReleaseNormalizedTime;
            this.correctionEndNormalizedTime = correctionEndNormalizedTime;
            this.localX = localX;
            this.localY = localY;
            this.localZ = localZ;
        }

        public Vector3 Evaluate(float normalizedTime)
        {
            if (!IsValid || !IsNormalized(normalizedTime)) return Vector3.zero;
            float n = Mathf.Clamp01(normalizedTime);
            return new Vector3(localX.Evaluate(n), localY.Evaluate(n), localZ.Evaluate(n));
        }

        private static bool HasFiniteKeys(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return false;
            for (int i = 0; i < curve.length; i++)
            {
                Keyframe key = curve[i];
                if (!IsFinite(key.time) || !IsFinite(key.value) ||
                    !IsFinite(key.inTangent) || !IsFinite(key.outTangent))
                    return false;
            }
            return true;
        }

        private static bool IsNormalized(float value) =>
            IsFinite(value) && value >= 0f && value <= 1f;

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [System.Serializable]
    public struct TraversalMotionBakeBlock
    {
        [SerializeField] private bool enabled;
        [SerializeField] private TraversalHandContactMode contactMode;
        [SerializeField] private HumanBodyBones leftHandBone;
        [SerializeField] private HumanBodyBones rightHandBone;
        [SerializeField, Range(0f, 1f)] private float leftHandContactNormalizedTime;
        [SerializeField, Range(0f, 1f)] private float rightHandContactNormalizedTime;
        [SerializeField, Range(0f, 1f)] private float transferNormalizedTime;
        [SerializeField, Range(0f, 1f)] private float exitNormalizedTime;
        [SerializeField, Range(0f, 1f)] private float recoveryNormalizedTime;
        [SerializeField, Min(0f)] private float requiredVerticalClearance;

        [Header("Hand IK quality pass — authored; optional")]
        [SerializeField] private bool handIKEnabled;
        [SerializeField] private TraversalHandIKWindow leftHandIKWindow;
        [SerializeField] private TraversalHandIKWindow rightHandIKWindow;
        [SerializeField, Range(0f, 1f)] private float handIKRotationWeight;
        [SerializeField] private TraversalRootReachConstraint rootReachConstraint;

        [Header("Automatically sampled — do not hand author")]
        [Header("Grip edge — 動畫自身的邊緣在 root 空間的位置（自動測出）")]
        [SerializeField] private Vector3 leftGripEdgeInRoot;
        [SerializeField] private Vector3 rightGripEdgeInRoot;
        [SerializeField] private bool hasGripEdges;

        [SerializeField] private TraversalBakedAnchor entryAnchor;
        [SerializeField] private TraversalBakedAnchor leftContactAnchor;
        [SerializeField] private TraversalBakedAnchor rightContactAnchor;
        [SerializeField] private TraversalBakedAnchor transferAnchor;
        [SerializeField] private TraversalBakedAnchor exitAnchor;
        [SerializeField] private TraversalBakedAnchor recoveryAnchor;

        public bool Enabled => enabled;
        public TraversalHandContactMode ContactMode => contactMode;
        public HumanBodyBones LeftHandBone => leftHandBone;
        public HumanBodyBones RightHandBone => rightHandBone;
        public float LeftHandContactNormalizedTime => leftHandContactNormalizedTime;
        public float RightHandContactNormalizedTime => rightHandContactNormalizedTime;
        public float TransferNormalizedTime => transferNormalizedTime;
        public float ExitNormalizedTime => exitNormalizedTime;
        public float RecoveryNormalizedTime => recoveryNormalizedTime;
        public float RequiredVerticalClearance => requiredVerticalClearance;
        public bool HandIKEnabled => handIKEnabled;
        public TraversalHandIKWindow LeftHandIKWindow => leftHandIKWindow;
        public TraversalHandIKWindow RightHandIKWindow => rightHandIKWindow;
        public float HandIKRotationWeight => IsFinite(handIKRotationWeight)
            ? Mathf.Clamp01(handIKRotationWeight)
            : 0f;
        public bool HasValidHandIK =>
            handIKEnabled && (!UsesLeftHand || leftHandIKWindow.IsValid) &&
            (!UsesRightHand || rightHandIKWindow.IsValid);
        public TraversalRootReachConstraint RootReachConstraint => rootReachConstraint;
        public bool HasValidRootReachConstraint => rootReachConstraint.IsValid;
        public TraversalBakedAnchor EntryAnchor => entryAnchor;
        public TraversalBakedAnchor LeftContactAnchor => leftContactAnchor;
        public TraversalBakedAnchor RightContactAnchor => rightContactAnchor;
        public TraversalBakedAnchor TransferAnchor => transferAnchor;
        public TraversalBakedAnchor ExitAnchor => exitAnchor;
        public TraversalBakedAnchor RecoveryAnchor => recoveryAnchor;

        public bool UsesLeftHand =>
            contactMode == TraversalHandContactMode.LeftHand ||
            contactMode == TraversalHandContactMode.BothHands;
        public bool UsesRightHand =>
            contactMode == TraversalHandContactMode.RightHand ||
            contactMode == TraversalHandContactMode.BothHands;

        public bool HasValidAuthoredMarkers
        {
            get
            {
                if (!enabled || contactMode == TraversalHandContactMode.None ||
                    !IsNormalized(transferNormalizedTime) || !IsNormalized(exitNormalizedTime) ||
                    !IsNormalized(recoveryNormalizedTime) || transferNormalizedTime >= exitNormalizedTime ||
                    exitNormalizedTime > recoveryNormalizedTime || !IsFinite(requiredVerticalClearance) ||
                    requiredVerticalClearance < 0f)
                {
                    return false;
                }

                float latestContact = 0f;
                if (UsesLeftHand)
                {
                    if (!IsNormalized(leftHandContactNormalizedTime)) return false;
                    latestContact = leftHandContactNormalizedTime;
                }
                if (UsesRightHand)
                {
                    if (!IsNormalized(rightHandContactNormalizedTime)) return false;
                    if (contactMode == TraversalHandContactMode.BothHands &&
                        rightHandContactNormalizedTime < leftHandContactNormalizedTime)
                    {
                        return false;
                    }
                    latestContact = Mathf.Max(latestContact, rightHandContactNormalizedTime);
                }
                return latestContact < transferNormalizedTime;
            }
        }

        public bool HasValidBakedData =>
            HasValidAuthoredMarkers && entryAnchor.IsFinite && leftContactAnchor.IsFinite &&
            rightContactAnchor.IsFinite && transferAnchor.IsFinite && exitAnchor.IsFinite &&
            recoveryAnchor.IsFinite && (!UsesLeftHand || leftContactAnchor.HasLeftHand) &&
            (!UsesRightHand || rightContactAnchor.HasRightHand);

        public TraversalMotionBakeBlock(
            bool enabled,
            TraversalHandContactMode contactMode,
            HumanBodyBones leftHandBone,
            HumanBodyBones rightHandBone,
            float leftHandContactNormalizedTime,
            float rightHandContactNormalizedTime,
            float transferNormalizedTime,
            float exitNormalizedTime,
            float recoveryNormalizedTime,
            float requiredVerticalClearance)
        {
            this.enabled = enabled;
            this.contactMode = contactMode;
            this.leftHandBone = leftHandBone;
            this.rightHandBone = rightHandBone;
            this.leftHandContactNormalizedTime = leftHandContactNormalizedTime;
            this.rightHandContactNormalizedTime = rightHandContactNormalizedTime;
            this.transferNormalizedTime = transferNormalizedTime;
            this.exitNormalizedTime = exitNormalizedTime;
            this.recoveryNormalizedTime = recoveryNormalizedTime;
            this.requiredVerticalClearance = requiredVerticalClearance;
            handIKEnabled = false;
            leftHandIKWindow = default;
            rightHandIKWindow = default;
            handIKRotationWeight = 0f;
            rootReachConstraint = default;
            leftGripEdgeInRoot = Vector3.zero;
            rightGripEdgeInRoot = Vector3.zero;
            hasGripEdges = false;
            entryAnchor = default;
            leftContactAnchor = default;
            rightContactAnchor = default;
            transferAnchor = default;
            exitAnchor = default;
            recoveryAnchor = default;
        }

        public TraversalMotionBakeBlock(
            bool enabled,
            TraversalHandContactMode contactMode,
            HumanBodyBones leftHandBone,
            HumanBodyBones rightHandBone,
            float leftHandContactNormalizedTime,
            float rightHandContactNormalizedTime,
            float transferNormalizedTime,
            float exitNormalizedTime,
            float recoveryNormalizedTime,
            float requiredVerticalClearance,
            bool handIKEnabled,
            TraversalHandIKWindow leftHandIKWindow,
            TraversalHandIKWindow rightHandIKWindow,
            float handIKRotationWeight)
            : this(
                enabled,
                contactMode,
                leftHandBone,
                rightHandBone,
                leftHandContactNormalizedTime,
                rightHandContactNormalizedTime,
                transferNormalizedTime,
                exitNormalizedTime,
                recoveryNormalizedTime,
                requiredVerticalClearance)
        {
            this.handIKEnabled = handIKEnabled;
            this.leftHandIKWindow = leftHandIKWindow;
            this.rightHandIKWindow = rightHandIKWindow;
            this.handIKRotationWeight = handIKRotationWeight;
        }

        /// <summary>
        /// 🆕（docs/22 §17）**動畫自己那個「邊緣」在 root 空間的位置**（接觸瞬間，yaw-only）。
        ///
        /// 為什麼需要它：對齊**手腕**是錯的。實測 `Climb2m` 的抓握格，手腕在動畫自身平台頂面
        /// **下方 16.3 cm、後方 10.3 cm**——因為抓握時四指扣在頂面、拇指按在牆面，手腕自然垂在下面。
        /// 把手腕釘到真實邊緣上，等於把整個角色抬高 16 cm 並往前推 10 cm（＝穿模 ＋ 手的高度不準）。
        ///
        /// 改成對齊這個點之後，**整隻手的 authored 姿勢（五指與拇指）會原樣落位**，
        /// 不需要逐指調整。X 取手腕自身的橫向座標，因此左右手分佈仍由 ledge interval 決定。
        /// </summary>
        public Vector3 LeftGripEdgeInRoot => leftGripEdgeInRoot;
        public Vector3 RightGripEdgeInRoot => rightGripEdgeInRoot;
        public bool HasGripEdges => hasGripEdges;

        public TraversalMotionBakeBlock WithHandGripEdges(Vector3 left, Vector3 right)
        {
            leftGripEdgeInRoot = left;
            rightGripEdgeInRoot = right;
            hasGripEdges = IsFiniteVector(left) && IsFiniteVector(right);
            return this;
        }

        /// <summary>
        /// 🆕（docs/24 §14）把 Hand IK 的接觸窗寫進 block。
        ///
        /// 窗的語意 ＝「**動畫說手黏在上面的期間**」，因此由 <c>TraversalContactDerivation</c>
        /// 測出來的 plant 直接換算，不是人填的。Root warp 只在接觸那**一格**把身體對齊，
        /// 其餘每一格的手是由這個窗釘住的——沒有它，手就只能跟著動畫在真實牆面上穿進穿出。
        /// </summary>
        public TraversalMotionBakeBlock WithHandIK(
            TraversalHandIKWindow left, TraversalHandIKWindow right, float rotationWeight)
        {
            leftHandIKWindow = left;
            rightHandIKWindow = right;
            handIKRotationWeight = rotationWeight;
            handIKEnabled = (!UsesLeftHand || left.IsValid) && (!UsesRightHand || right.IsValid);
            return this;
        }

        public TraversalMotionBakeBlock WithRootReachConstraint(
            in TraversalRootReachConstraint constraint)
        {
            rootReachConstraint = constraint;
            return this;
        }

        private static bool IsFiniteVector(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        public TraversalMotionBakeBlock WithBakedAnchors(
            TraversalBakedAnchor entry,
            TraversalBakedAnchor leftContact,
            TraversalBakedAnchor rightContact,
            TraversalBakedAnchor transfer,
            TraversalBakedAnchor exit,
            TraversalBakedAnchor recovery)
        {
            entryAnchor = entry;
            leftContactAnchor = leftContact;
            rightContactAnchor = rightContact;
            transferAnchor = transfer;
            exitAnchor = exit;
            recoveryAnchor = recovery;
            return this;
        }

        private static bool IsNormalized(float value) =>
            IsFinite(value) && value >= 0f && value <= 1f;

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [CreateAssetMenu(fileName = "MotionBakeData", menuName = "Project/Motion/BakeData")]
    public class MotionBakeData : ScriptableObject
    {
        // ⚠️（2026-07-26）本欄位為 **Editor-side provenance／烘焙來源**，執行期**不得**讀取。
        // 執行期需要的動畫長度請讀 <see cref="BakedDuration"/>（烘焙時快照的序列化值）。
        // 誠實揭露：欄位保留序列化引用，因此 clip 仍會被打包進 build、並隨本資產一起載入；
        // 要連「載入」都斷開需改成 Editor-only 序列化（sidecar），屬另一個決定，本輪不做。
        [Header("來源資訊（Editor-side provenance；執行期不讀）")]
        public AnimationClip SourceClip;
        public float SampleRate = 60f;

        [Header("物理特徵曲線 (X軸為實際時間/秒)")]
        [Tooltip("瞬時速度曲線 (m/s)")]
        public AnimationCurve SpeedCurve;

        [Tooltip("相對動畫第一幀的垂直位移曲線 (m)")]
        public AnimationCurve VerticalCurve;

        [Header("Traversal V3 — optional authored markers + baked spatial samples")]
        public TraversalMotionBakeBlock Traversal;

        [Tooltip("連續累計偏航角曲線 (Degrees)")]
        public AnimationCurve RotationCurve;

        [Header("進階物理特徵（v0.7 新增，取自參考演算法的純數學部分）")]
        [Tooltip("旋轉在第幾秒就已經收斂到終值附近（依容忍度演算法計算）。" +
                 "超過這個時間點後，剩餘的角度變化視為動畫尾段的抖動雜訊，" +
                 "下游狀態可以用這個時間點提早停止套用 deltaYaw，避免收尾抖動被誤判成有意義的轉向。")]
        public float RotationFinishedTime;

        [Tooltip("動畫播放結束的瞬間，左右腳何者落地，供動作銜接（例如翻滾接跑步時要接對步相）使用")]
        public FootPhase EndPhase;

        [Tooltip("連續腳相曲線（🆕 Foot Phase Curve）：值 = 左腳世界Y − 右腳世界Y。<0 左腳觸地、>0 右腳觸地" +
                 "（與 EndPhase 符號一致）。零交越＝換腳時刻，供 Start/Stop 選腳別與未來 Footstep／相位同步。")]
        public AnimationCurve FootPhaseCurve;

        [Tooltip("整段動畫的總位移方向，換算成『動畫開始那一刻』角色本地座標系下的方向向量，" +
                 "可直接餵給 Blend Tree 的 X/Z 方向參數；若位移量太小或方向太接近正前方（死區內），視為 Vector3.zero（原地動作）。")]
        public Vector3 TargetLocalDirection;

        [Header("自動化特徵分析（Feature Analysis Stage 自動提取，跳躍動畫適用）")]

        /// <summary>
        /// 起跳前搖時間（秒）。以「世界空間相對足跡」偵測：雙腳世界高度同時超過「自身 Rest Pose 基線＋容忍度」
        /// 且通過持續騰空驗證，經子影格線性插值後的精確離地時刻；此時間點之前屬於預備/蓄力姿勢。
        /// 非跳躍動畫（偵測不到離地）安全退化為 0。
        /// </summary>
        [Tooltip("起跳前搖（秒）：雙腳同時離地（相對各自 Rest Pose 基線）的精確時刻；之前屬預備/蓄力。非跳躍動畫為 0。")]
        public float AutoTakeoffDelay;

        /// <summary>
        /// 最高點高度 h_max（公尺）。根節點世界空間 Y 相對「起跳時刻」基準的最大上升量（於起跳→落地窗內掃描）。
        /// </summary>
        [Tooltip("最高點高度 h_max（公尺）：起跳→落地窗內，根節點 Y 相對起跳時刻的最大上升量。")]
        public float AutoApexHeight;

        /// <summary>
        /// 滯空時間 t_air（秒）。雙 Pass 精確量測：起跳離地 → 首次真實落地，雙端皆經子影格線性插值。
        /// 找不到落地（jump-loop／跳上高台等不對稱拋物線）時為 0，明示未量測。
        /// </summary>
        [Tooltip("滯空時間 t_air（秒）：起跳→首次落地的精確量測（子影格插值）。找不到落地時為 0。")]
        public float AutoAirTime;

        /// <summary>
        /// 逆向推導的完美重力常數（正值，公尺/秒²）。由拋體運動 g = 8·h_max / t_air² 反推。
        /// 非跳躍動畫、找不到落地、滯空過短或最高點過低時安全退化為標準重力 9.81。
        /// </summary>
        [Tooltip("逆推重力：g = 8·h_max / t_air²。非跳躍/無落地/滯空過短/高度過低時退化為 9.81。")]
        public float AutoCalculatedGravity = 9.81f;

        /// <summary>
        /// 代表移動速度（公尺/秒）：<see cref="SpeedCurve"/> 的平均瞬時速度，於烘焙時算好存檔。
        /// 用途——把「動畫天生跑多快」變成可被 Config／MotionDriver 引用的**資料真相**：
        /// 例如最高速 clip（Fast Run）的此值 = gameplay 滿速，讓腳步視覺與位移速度一致、根除滑步；
        /// Locomotion Mixer 門檻亦可由各段此值正規化推導（dev-spec §3.2 資料流規範）。
        /// 語意為整段平均；loop locomotion clip（Walk／Run）為穩態，平均即代表速度。
        /// 舊資產（此欄為 0、未重烘焙）由 <see cref="GetRepresentativeSpeed"/> 即時從 SpeedCurve 回退計算，
        /// 因此無需為了取得此值強制立即重烘焙全部資產。
        /// </summary>
        [Tooltip("代表移動速度 (m/s)：SpeedCurve 平均值，烘焙時寫入。最高速 clip 的此值可作 MotionDriver 滿速來源／Mixer 門檻推導。")]
        public float AutoAverageSpeed;

        /// <summary>
        /// 🆕（2026-07-26）動畫總長度（秒），**烘焙時從 `SourceClip.length` 快照的序列化值**。
        ///
        /// **為什麼不直接讀 clip**：`Duration` 原本是 `SourceClip.length`，那是全專案唯一一條
        /// 「執行期 gameplay 邏輯讀 `AnimationClip`」的耦合——動畫資產一旦缺席或 GUID 變動
        /// （fresh clone、重匯入、換動畫來源），`Duration` 會靜默變 0，而 `RollState` 的 fallback
        /// 只檢查「Bake 資產是否為 null」、檢查不到「clip 是否為 null」，於是翻滾第一帧就結束。
        /// 改為序列化快照後，Bake Data 成為自足的純資料資產，與 <see cref="AutoAverageSpeed"/> 同一個 pattern。
        ///
        /// ⚠️ **快照語意**：clip 長度日後若變動，需**重跑烘焙**才會同步——這與其他 Auto* 特徵一致，
        /// 也正是「MotionBakeData 才是動畫真實運動值的來源」（CLAUDE.md）的直接體現。
        /// </summary>
        [Tooltip("動畫總長度（秒）：烘焙時自 SourceClip.length 快照。執行期一律讀本欄位，不讀 clip。")]
        public float BakedDuration;

        // 便利擴充：取得動畫總長度（**純序列化值，不觸碰 AnimationClip**）
        public float Duration => BakedDuration;

        /// <summary>
        /// 取得特定時間點的理論「瞬時速度」
        /// </summary>
        public float GetSpeedAt(float time)
        {
            if (SpeedCurve == null || SpeedCurve.length == 0) return 0f;
            return SpeedCurve.Evaluate(time);
        }

        /// <summary>取得相對動畫第一幀的垂直位移；舊資產沒有曲線時安全退化為 0。</summary>
        public float GetVerticalAt(float time)
        {
            if (VerticalCurve == null || VerticalCurve.length == 0) return 0f;
            return VerticalCurve.Evaluate(time);
        }

        /// <summary>
        /// 垂直位移「不再變化」的容差（公尺）。這是**數值定義**而非可調參數：
        /// 三支 traversal bake 的尾段 <see cref="VerticalCurve"/> 是精確的常數平台
        /// （Climb1m ＝ 1.0134075 重複、Climb2m ＝ 1.978 重複、Vault1m ＝ 0 重複），
        /// 因此只需要一個浮點雜訊等級的 epsilon，不需要百分比門檻。
        /// </summary>
        private const float VerticalSettleEpsilon = 1e-4f;

        /// <summary>
        /// 🆕（docs/23 §R1／§E-2 缺陷③）**由資料推導的垂直動作結束時刻**（normalized 0–1）。
        ///
        /// 回傳 <see cref="VerticalCurve"/> 最後一次實際改變之後的第一個取樣點——亦即動畫自己
        /// 「爬完了」的時刻。三支正式 bake 實測：Vault1m ≈ 0.48、Climb1m ≈ 0.585、Climb2m ≈ 0.45，
        /// 其後分別有 52%／41.5%／55% 的 clip 是站定或跑出。
        ///
        /// 用途：traversal 的 correction 窗預設收斂到這個時刻，而不是 author 一個涵蓋整支 clip 的
        /// 常數（舊預設 0.8）。**這不是 tuning 值，是從 bake 讀出來的事實。**
        /// 曲線缺失／全程都在變化時回傳 1（＝退化為「整段都是動作」，行為與舊版一致）。
        /// </summary>
        public float GetVerticalSettleNormalizedTime()
        {
            float duration = Duration;
            if (VerticalCurve == null || VerticalCurve.length < 2 ||
                float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
            {
                return 1f;
            }

            int lastIndex = VerticalCurve.length - 1;
            float finalValue = VerticalCurve[lastIndex].value;
            if (float.IsNaN(finalValue) || float.IsInfinity(finalValue)) return 1f;

            // 由後往前找最後一個「值仍與終值不同」的樣本；它的下一個樣本就是平台起點。
            for (int i = lastIndex; i >= 0; i--)
            {
                float value = VerticalCurve[i].value;
                if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
                if (Mathf.Abs(value - finalValue) <= VerticalSettleEpsilon) continue;

                if (i >= lastIndex) return 1f;
                float settleTime = VerticalCurve[i + 1].time;
                if (float.IsNaN(settleTime) || float.IsInfinity(settleTime)) return 1f;
                return Mathf.Clamp01(settleTime / duration);
            }

            // 整條曲線都等於終值（例如完全沒有垂直位移）：沒有垂直動作可等。
            return 0f;
        }

        /// <summary>
        /// 🆕（docs/22 §14.5／Phase 2）**這支動畫希望角色從離邊緣多遠的地方進場**（公尺）。
        ///
        /// 推導自動畫自己的兩個量，沒有任何手填常數：
        /// <code>
        /// 進場距離 = 接觸瞬間「手相對 root 的前伸距離」 + 「root 到該瞬間已走的水平位移」
        /// </code>
        /// 理由：手要落在邊緣上，接觸當下 root 就必須退在邊緣後方剛好一個手臂前伸距離；
        /// 而 root 在接觸前已經自己往前走了一段，所以進場點要再往後推那一段。
        ///
        /// 實測：`Climb2m` ≈ 0.36 m（原地起攀，root 在接觸前不前進）、
        /// `Vault1m` ≈ 1.29 m（跑動翻越，接觸前已跑 0.57 m）。
        /// **同一個全域常數服務不了兩者**——這正是把它下放到 bake 的原因。
        /// </summary>
        public bool TryGetDesiredEntryDistance(out float distance)
        {
            distance = float.NaN;
            float duration = Duration;
            if (!Traversal.HasValidBakedData ||
                float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
            {
                return false;
            }

            // ⚠️ **必須與 TraversalPlanBuilder 的 root solve 用同一個基準點。**
            //    2026-09-13 的實測 bug：solve 改用 grip edge（抓握邊緣）之後，這裡仍用手腕前伸距離，
            //    兩者相差 10.2 cm（Climb2m 手腕 0.359 / grip edge 0.461）
            //    ⇒ **玩家就算站在「理想距離」也會在接觸瞬間被硬拉 10.7 cm**，
            //    站偏一點就變成 30 cm 的猛拉。基準不一致比基準不準更糟。
            float total = 0f;
            int count = 0;
            if (Traversal.UsesLeftHand &&
                TryAccumulateEntryDistance(
                    Traversal.LeftHandContactNormalizedTime,
                    Traversal.HasGripEdges
                        ? Traversal.LeftGripEdgeInRoot.z
                        : Traversal.LeftContactAnchor.LeftHandInRoot.z,
                    duration, ref total))
            {
                count++;
            }
            if (Traversal.UsesRightHand &&
                TryAccumulateEntryDistance(
                    Traversal.RightHandContactNormalizedTime,
                    Traversal.HasGripEdges
                        ? Traversal.RightGripEdgeInRoot.z
                        : Traversal.RightContactAnchor.RightHandInRoot.z,
                    duration, ref total))
            {
                count++;
            }
            if (count == 0) return false;

            float value = total / count;
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f) return false;
            distance = value;
            return true;
        }

        /// <summary>
        /// 🆕（docs/22 §18）**這支動畫自然會停在邊緣內側多遠**（公尺）。
        ///
        /// <code>自然落點深度 = 動畫在 Exit marker 當下的水平位移 − 理想進場距離</code>
        ///
        /// 為什麼需要：Probe 的 <c>Vault1mMaxDepth</c>（0.6 m）是**探測用的距離**——
        /// 拿來找落腳面在哪。把它直接當成 committed 終點，等於要求角色比動畫自然落點多走一段：
        /// `Climb2m` 自然停在 0.213 m，卻被要求走到 0.600 m ⇒ **放手之後還要被往前推 0.39 m**，
        /// 那段位移動畫裡沒有，看起來就是腳在滑。
        ///
        /// ⚠️ **必須量在 Exit marker，不是量在 clip 結尾**（2026-09-13 修正，docs/24 §11）。
        /// 這個值唯一的消費者是 warp plan 的 **Exit knot**，而那個 knot 在
        /// <c>Traversal.ExitNormalizedTime</c> 觸發。量在結尾等於拿「動畫最後會走到哪」
        /// 去要求「動畫在 exit 當下就要到那裡」：
        /// `Vault1m` 結尾 3.259 m ⇒ 舊值 1.857 m，但 exit(0.460) 當下只走到 2.519 m ⇒ 實際只有 1.116 m，
        /// 差的 0.74 m 全部變成 exit correction 把角色往前扯。
        /// （`Climb2m` 的 exit 之後是靜止站姿，兩種量法同值 0.213，因此舊 bug 只在 Vault 顯現——
        /// 又一次「兩處用了不同基準」，與 §18.2 同型。）
        /// </summary>
        public bool TryGetNaturalExitDepth(out float depth)
        {
            depth = float.NaN;
            if (!TryGetDesiredEntryDistance(out float entryDistance)) return false;

            float duration = Duration;
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f) return false;

            float exitNormalized = Traversal.ExitNormalizedTime;
            float exitTime = float.IsNaN(exitNormalized) || float.IsInfinity(exitNormalized) ||
                             exitNormalized <= 0f
                ? duration
                : Mathf.Clamp01(exitNormalized) * duration;

            float travelled = GetHorizontalDisplacementAt(exitTime);
            if (float.IsNaN(travelled) || float.IsInfinity(travelled)) return false;
            float value = travelled - entryDistance;
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            depth = value;
            return true;
        }

        private bool TryAccumulateEntryDistance(
            float contactNormalizedTime, float handForwardReach, float duration, ref float total)
        {
            if (float.IsNaN(contactNormalizedTime) || float.IsInfinity(contactNormalizedTime) ||
                float.IsNaN(handForwardReach) || float.IsInfinity(handForwardReach))
            {
                return false;
            }
            float travelled = GetHorizontalDisplacementAt(
                Mathf.Clamp01(contactNormalizedTime) * duration);
            if (float.IsNaN(travelled) || float.IsInfinity(travelled)) return false;
            total += handForwardReach + travelled;
            return true;
        }

        /// <summary>
        /// 將 SpeedCurve 以相鄰烘焙樣本的梯形積分轉成累計水平位移。
        /// Traversal V2 用同一個絕對取樣函式計算前後 warped pose，避免逐幀補償累積誤差。
        /// </summary>
        public float GetHorizontalDisplacementAt(float time)
        {
            if (SpeedCurve == null || SpeedCurve.length == 0 ||
                float.IsNaN(time) || float.IsInfinity(time) || time <= 0f)
            {
                return 0f;
            }

            float endTime = Mathf.Min(time, Duration);
            if (endTime <= 0f) return 0f;

            Keyframe previous = SpeedCurve[0];
            float distance = 0f;
            if (SpeedCurve.length == 1)
                return Mathf.Max(0f, previous.value) * endTime;

            for (int i = 1; i < SpeedCurve.length; i++)
            {
                Keyframe current = SpeedCurve[i];
                if (current.time <= previous.time)
                {
                    previous = current;
                    continue;
                }

                float segmentEnd = Mathf.Min(endTime, current.time);
                if (segmentEnd > previous.time)
                {
                    float t = (segmentEnd - previous.time) / (current.time - previous.time);
                    float endSpeed = Mathf.Lerp(previous.value, current.value, t);
                    distance += 0.5f * (Mathf.Max(0f, previous.value) + Mathf.Max(0f, endSpeed)) *
                                (segmentEnd - previous.time);
                }

                if (endTime <= current.time) return distance;
                previous = current;
            }

            if (endTime > previous.time)
                distance += Mathf.Max(0f, previous.value) * (endTime - previous.time);
            return distance;
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

        /// <summary>
        /// 🆕 查詢某時刻哪隻腳觸地（連續腳相）。<see cref="FootPhaseCurve"/> 缺（舊資產未重烘焙）時退回單點
        /// <see cref="EndPhase"/>。符號約定：曲線值 &lt; 0 = 左腳觸地、&gt; 0 = 右腳觸地
        /// （與烘焙時「LeftFoot 較低判 LeftFootDown」一致）。
        /// </summary>
        public FootPhase GetFootPhaseAt(float time)
        {
            if (FootPhaseCurve == null || FootPhaseCurve.length == 0) return EndPhase;
            return FootPhaseCurve.Evaluate(time) < 0f ? FootPhase.LeftFootDown : FootPhase.RightFootDown;
        }

        /// <summary>
        /// 取得代表移動速度（公尺/秒），供「動畫數據 → 配置」資料流使用（dev-spec §3.2）。
        /// 優先回傳烘焙時存檔的 <see cref="AutoAverageSpeed"/>；若為 0（舊資產未重烘焙）則即時從
        /// <see cref="SpeedCurve"/> 計算平均值作安全回退——確保現有資產無需立即重烘焙也能被
        /// MotionDriver 速度來源／Mixer 門檻推導引用。回傳 0 代表此 clip 無有效速度資料（非移動動畫）。
        /// </summary>
        public float GetRepresentativeSpeed()
        {
            if (AutoAverageSpeed > 0f) return AutoAverageSpeed;
            return ComputeAverageSpeed(SpeedCurve);
        }

        /// <summary>
        /// 計算速度曲線的平均瞬時速度（對有效關鍵影格值取算術平均）。烘焙採等距取樣、關鍵影格均勻分布，
        /// 故算術平均即時間平均。<c>MotionBakeEditor</c> 為了建立曲線起點，會在尚無前一帧可計算速度的
        /// time=0 寫入 value=0 哨兵；曲線還有後續實際樣本時，此哨兵不參與平均，避免代表速度被系統性低估。
        /// 曲線為 null／空時回傳 0。供烘焙工具寫入 <see cref="AutoAverageSpeed"/> 與執行期回退共用同一套定義，
        /// 杜絕兩處計算不一致。
        /// </summary>
        public static float ComputeAverageSpeed(AnimationCurve speedCurve)
        {
            if (speedCurve == null || speedCurve.length == 0) return 0f;

            int firstSampleIndex = 0;
            if (speedCurve.length > 1)
            {
                Keyframe firstKey = speedCurve[0];
                if (firstKey.time == 0f && firstKey.value == 0f)
                    firstSampleIndex = 1;
            }

            float sum = 0f;
            for (int i = firstSampleIndex; i < speedCurve.length; i++) sum += speedCurve[i].value;
            return sum / (speedCurve.length - firstSampleIndex);
        }
    }
}
