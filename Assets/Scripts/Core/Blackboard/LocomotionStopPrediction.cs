using UnityEngine;

namespace Project.Core.Blackboard
{
    /// <summary>
    /// Locomotion Stop 預測快照（ADR-010 🟡 Trial）。唯一 Writer 是 <c>LocomotionModel</c>；Presentation 只讀數字，
    /// 不取得 MotionBakeData／AnimationClip／Animator 參照。
    /// <para>
    /// 一份快照 ＝ 寫入它的那一次順序 3 tick（publish）當下，對「本次停步結束時」的預測；不是軌跡。
    /// 兩個區塊各有有效旗標（ADR-010 D1-R1）：<see cref="Active"/> ⇔ producer 在**本帧**確認停步正在播放、播放頭已前進、Bake 有結束站姿
    /// （結束站姿欄位有效）；<see cref="HasNextPlant"/> ⇔ 本帧已有 committed 的 stop variant 且之後仍有 planned plant（next plant 欄位有效；
    /// 可早於 Active）。publish root 欄位在任一旗標為 true 時有效；兩者皆 false ⇒ 整份為 <c>default</c>。
    /// Writer 每次 tick 先清除 ⇒ 消費端不會讀到前一帧的值，也不得跨帧快取。
    /// </para>
    /// ⛔ 欄位只收正式 consumer 實際讀取的（ADR-010 D2）：結束站姿給 FutureLandingProvider；
    /// 下一次 planned plant（D5）給 LandingSurfaceResolver。擴充需要正式 consumer 的理由與獨立變更。
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

        // === 下一次 planned plant（ADR-010 D5；consumer：LandingSurfaceResolver）===
        /// <summary>本帧已有 committed 的 stop variant，且之後仍有尚未發生的 planned plant（D1-R1：自 variant committed 起，不等播放頭）。false ⇒ 其餘三個欄位無意義（default）。</summary>
        public bool HasNextPlant;
        /// <summary>下一次 planned plant 是左腳（false ＝ 右腳）。</summary>
        public bool NextPlantIsLeft;
        /// <summary>
        /// 下一次 planned plant 落地當下的 Foot 骨骼位置，以 **publish 當下的 root** 為座標系（x 右、y 上、z 前）。
        /// 已含 root 從 publish 到該 plant 的前進量（Bake 位移積分）⇒ 不另存前進距離；世界位置 ＝ publish root ＋ yaw × 此值。
        /// </summary>
        public Vector3 NextPlantAnkleInPublishRoot;
        /// <summary>
        /// 下一次 planned plant 是本次 stop sequence 進入 terminal standing pose 之前的**最後一次 planned foot plant**（Bake 排程的最後一個事件）。
        /// ⛔ 不等於 clip 結束時刻，也不等於最後一次物理接地——它只描述排程。
        /// </summary>
        public bool NextPlantIsFinal;
    }
}
