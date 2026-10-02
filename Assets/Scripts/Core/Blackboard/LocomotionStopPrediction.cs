using UnityEngine;

namespace Project.Core.Blackboard
{
    /// <summary>
    /// Locomotion Stop 預測快照（ADR-010 🟡 Trial）。唯一 Writer 是 <c>LocomotionModel</c>；Presentation 只讀數字，
    /// 不取得 MotionBakeData／AnimationClip／Animator 參照。
    /// <para>
    /// 一份快照 ＝ 寫入它的那一次順序 3 tick（publish）當下，對「本次停步結束時」的預測；不是軌跡。
    /// <see cref="Active"/> 為 true ⇔ producer 在**本帧**確認停步正在播放、播放頭已前進、Bake 有結束站姿，並寫出了本帧的值；
    /// 否則整份為 <c>default</c>。Writer 每次 tick 先清除 ⇒ 消費端不會讀到前一帧的值，也不得跨帧快取。
    /// </para>
    /// ⛔ 欄位只收 FutureLandingProvider 實際讀取的（ADR-010 D2）；擴充需要正式 consumer 的理由與獨立變更。
    /// </summary>
    public struct LocomotionStopPrediction
    {
        public bool Active;
        /// <summary>publish 當下的 root 世界位置。</summary>
        public Vector3 RootWorldPositionAtPublish;
        /// <summary>publish 當下的 root yaw（度）。</summary>
        public float RootYawDegreesAtPublish;
        /// <summary>從 publish 到 clip 結束，root 沿 publish 朝向還會前進的距離（m；Bake 位移積分）。</summary>
        public float RemainingForwardDisplacement;
        /// <summary>clip 結束站姿的左腳 Foot 骨骼位置（root 空間；Bake）。</summary>
        public Vector3 EndLeftAnkleInRoot;
        /// <summary>clip 結束站姿的右腳 Foot 骨骼位置（root 空間；Bake）。</summary>
        public Vector3 EndRightAnkleInRoot;
    }
}
