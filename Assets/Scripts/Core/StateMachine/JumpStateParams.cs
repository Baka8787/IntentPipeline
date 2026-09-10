using System.Collections.Generic;
using UnityEngine;
using Project.Core.Movement;
using Project.Presentation.Motion;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// 🆕（ADR-002）單一跳躍段。每段引用一份 <see cref="MotionBakeData"/>，
    /// 其 <c>AutoTakeoffDelay</c> / <c>AutoApexHeight</c> / <c>AutoCalculatedGravity</c>
    /// 即該段的「動畫資料」（唯一真相）。三個 Designer Tuning 倍率屬 <see cref="JumpStateParams"/>
    /// 全域層級，不放在每段。
    /// </summary>
    [System.Serializable]
    public struct JumpStage
    {
        [Tooltip("該段跳躍對應 clip 的烘焙資料，提供 AutoTakeoffDelay / AutoApexHeight / AutoCalculatedGravity")]
        public MotionBakeData Bake;
    }

    /// <summary>
    /// 同一個速度家族的一組 authored 動畫變體。Idle 不需要腳相；Walk／Run 的陣列則交由
    /// <see cref="LocomotionStopSelector"/> 依各自 Bake Data 的入場腳相選取，避免 Jump 再造一套 LU／RU 規則。
    /// </summary>
    [System.Serializable]
    public struct JumpAnimationVariantSet
    {
        [SerializeField] private LocomotionStopVariant idle;
        [SerializeField] private LocomotionStopVariant[] walk;
        [SerializeField] private LocomotionStopVariant[] run;

        public LocomotionStopVariant Idle => idle;
        public LocomotionStopVariant[] Walk => walk;
        public LocomotionStopVariant[] Run => run;

        public JumpAnimationVariantSet(
            LocomotionStopVariant idle,
            LocomotionStopVariant[] walk,
            LocomotionStopVariant[] run)
        {
            this.idle = idle;
            this.walk = walk;
            this.run = run;
        }
    }

    /// <summary>
    /// Jump 三相位的完整 authored 表。重落地只有一支站定收住的素材，因此 <c>hardLand</c>
    /// 刻意是單一 <see cref="LocomotionStopVariant"/>；只有正常落地才依移動意圖分 Stop／To Move，
    /// 再沿用起跳時承諾的速度家族與腳相。每一格仍重用既有 Bake Data＋Animation Key 配對格式。
    /// </summary>
    [System.Serializable]
    public struct JumpAnimationVariantTable
    {
        [SerializeField] private JumpAnimationVariantSet start;
        [SerializeField] private LocomotionStopVariant falling;
        [SerializeField] private JumpAnimationVariantSet normalLand;
        [SerializeField] private JumpAnimationVariantSet normalLandToMove;
        [SerializeField] private LocomotionStopVariant hardLand;

        public JumpAnimationVariantSet Start => start;
        public LocomotionStopVariant Falling => falling;
        public JumpAnimationVariantSet NormalLand => normalLand;
        public JumpAnimationVariantSet NormalLandToMove => normalLandToMove;
        public LocomotionStopVariant HardLand => hardLand;

        public JumpAnimationVariantTable(
            JumpAnimationVariantSet start,
            LocomotionStopVariant falling,
            JumpAnimationVariantSet normalLand,
            JumpAnimationVariantSet normalLandToMove,
            LocomotionStopVariant hardLand)
        {
            this.start = start;
            this.falling = falling;
            this.normalLand = normalLand;
            this.normalLandToMove = normalLandToMove;
            this.hardLand = hardLand;
        }
    }

    /// <summary>
    /// Jump 狀態的參數資產。
    /// 🆕（ADR-002）拔除硬編碼的 TakeoffDelay / ImpulseForce（物理量改由各段 <see cref="MotionBakeData"/>
    /// 提供、單一真相）；改承載「跳躍內容（Stages）」與「設計師手感倍率（Designer Tuning）」。
    /// 透過 <see cref="StateMachineConfigSO"/> 的 paramsMappings 綁定至 <see cref="StateType.Jump"/>，
    /// 由 <c>JumpState</c> 在 Initialize 時查表快取。
    /// </summary>
    /// <remarks>
    /// Movement Assistance（Coyote Time / Jump Buffer）與 Variable Jump（Min/Max Hold /
    /// Early Release Gravity Multiplier）的欄位與行為，依 ADR-002 §6-4 留待後續 ADR 定義其
    /// Owner/Writer/Reader 與輸入來源後再落地，此處刻意不先放無行為的死設定。
    /// </remarks>
    [CreateAssetMenu(fileName = "JumpStateParams", menuName = "Project/Core/StateParams/JumpStateParams")]
    public class JumpStateParams : StateParamsSO
    {
        // 這是「沒有參數資產」時的 Runtime 安全退化值，也是新資產欄位初始值；集中成一份常數，
        // 避免 Inspector 預設與 JumpState fallback 日後各自漂移，讓同一速度在兩條路徑得到不同分類。
        internal const float DefaultHardLandingSpeed = 8f;
        internal const float DefaultFallEntryGrace = 0.1f;

        [Header("Content — Multi Jump")]
        [Tooltip("有序段清單：第 0 段 = 地面跳，其後為空中段。可跳段數上限即本清單長度（不另設欄位）。")]
        public List<JumpStage> Stages = new List<JumpStage>();

        [Header("Designer Tuning（倍率，預設 1 = 完全依烘焙值）")]
        [Tooltip("apex 高度倍率，乘在各段 AutoApexHeight 上")]
        public float HeightMultiplier = 1f;

        [Tooltip("重力倍率，乘在各段 AutoCalculatedGravity 上")]
        public float GravityMultiplier = 1f;

        [Tooltip("起跳初速倍率，乘在逆推出的 v = √(2gh) 上")]
        public float LaunchVelocityMultiplier = 1f;

        [Header("Animation — Segmented Jump（純視覺 authored data）")]
        [Tooltip("Start／Falling／Land 的動畫變體表。留空時 JumpState 維持既有 Jump 單鍵，不改變物理或狀態轉移。")]
        [SerializeField] private JumpAnimationVariantTable animationVariants;

        [Tooltip("Walk locomotion loop 的 Bake Data；只用播放進度與 FootPhaseCurve 推導起跳腳相。")]
        [SerializeField] private MotionBakeData walkLoopBakeData;

        [Tooltip("Run locomotion loop 的 Bake Data；只用播放進度與 FootPhaseCurve 推導起跳腳相。")]
        [SerializeField] private MotionBakeData runLoopBakeData;

        [Tooltip("DesiredSpeedNormalized 高於此值才視為有移動意圖；同時決定 Idle→Walk 與 Land→LandToMove。")]
        [SerializeField, Range(0f, 1f)] private float movementIntentThreshold = 0.2f;

        [Tooltip("DesiredSpeedNormalized 達此值進 Run 家族；介於移動門檻與此值之間為 Walk。")]
        [SerializeField, Range(0f, 1f)] private float runIntentThreshold = 0.75f;

        [Tooltip("重落地的下降速度門檻（m/s），只有 |v_y| 大於此值才使用 JumpIdleLandHard。" +
                 "目前 launch data：apex 0.9535 m、g 16.78 m/s²，正常跳躍落地速度 " +
                 "v = √(2gh) = √(2×16.78×0.9535) ≈ 5.66 m/s，因此門檻必須高於 5.66，" +
                 "否則每次正常跳躍都會誤判為重落地。預設 8 m/s 對應 h = v²/(2g) ≈ 1.9 m，" +
                 "也就是跳下比自己正常跳躍更高的地方才算重落地。")]
        [SerializeField, Min(0f)] private float hardLandingSpeed = DefaultHardLandingSpeed;

        [Tooltip("Land phase 的安全秒數。Bake duration 有效時沿用其 authored 秒數；HardRecovery 的動畫格失效時仍以此值維持 gameplay recovery，避免動畫引用失效連帶解除鎖定。")]
        [SerializeField, Min(0f)] private float landFallbackDuration = 0.1f;

        [Header("Physics — Passive Fall Entry")]
        [Tooltip("非主動失地持續超過此秒數才進入 Falling，用來過濾斜坡／樓梯的 isGrounded 短暫抖動。這不是 Coyote Time，期間不會放寬起跳資格。")]
        [SerializeField, Min(0f)] private float fallEntryGrace = DefaultFallEntryGrace;

        public JumpAnimationVariantTable AnimationVariants => animationVariants;
        public MotionBakeData WalkLoopBakeData => walkLoopBakeData;
        public MotionBakeData RunLoopBakeData => runLoopBakeData;
        public float MovementIntentThreshold => movementIntentThreshold;
        public float RunIntentThreshold => runIntentThreshold;
        public float HardLandingSpeed => hardLandingSpeed;
        public float FallEntryGrace => fallEntryGrace;
        public float LandFallbackDuration => landFallbackDuration;
    }
}
