using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// 🆕（M3.x-C）單腳的**地面觀測結果**——`FootIKController` 每帧 raycast 的產物，對外只讀。
    ///
    /// ⚠️ **語意：這是「本帧腳下有什麼」的物理觀測，不是任何決策。**
    /// 它不代表「腳踩到了」、不代表 IK 有沒有生效、也不決定任何權重
    /// （比照 `docs/29` §C 對三種「接地」的區分：gameplay grounded／physics observation／visual IK correction，
    /// 本結構屬**第二種**——每帧重算、用完即丟、什麼都不決定）。
    /// </summary>
    public struct FootGroundContact
    {
        /// <summary>本帧腳下的 ray 是否命中（IK 被關閉／角色在空中時為 false）。</summary>
        public bool HasGround;

        /// <summary>解算後的腳底平面接觸點（世界座標）。<see cref="HasGround"/> 為 false 時無意義。</summary>
        public Vector3 Point;

        /// <summary>ray 命中的**原始**地面法線（未經 <c>MaxFootAlignAngle</c> 夾限）。</summary>
        public Vector3 Normal;

        /// <summary>解算後的腳底平面高度（世界 Y）。腳步偵測的「地面基準」即為此值。</summary>
        public float GroundY;

        /// <summary>
        /// 命中的碰撞體。⚠️ **本欄位目前沒有任何 runtime 讀取方**——它是 Stage 2 Surface Resolver
        /// 的接點，先在此收束邊界，避免屆時為了拿 collider 而在腳步系統另外補一套重複 raycast。
        /// 讀取方一律**只讀不存**（跨帧持有 <c>Collider</c> 參考會擋住物件銷毀）。
        /// </summary>
        public Collider Collider;
    }

    /// <summary>
    /// 🆕（M3.x-C）Foot IK 地面觀測的**共享管道**——第三條單向資料流，與
    /// <see cref="FootIKTargetData"/>（Controller→Rig）／<see cref="FootIKPoseData"/>（Rig→Controller）並列。
    ///
    /// **lifetime owner ＝ <c>FootIKController</c>，理由與 <see cref="FootIKPoseData"/> 完全同源：
    /// 它是本資料的唯一 Writer。** 擁有權跟著寫入權走（M3.x-A 裁決）。
    ///
    /// **為什麼要有這條管道**：`FootIKController.Tick` 每帧已經對雙腳各發至少一次
    /// `Physics.Raycast`（開啟兩點取樣時是三次）。腳步偵測需要的「腳底離地多高」正是同一份資料——
    /// 若讓腳步系統自己再射一次，等於同一帧對同一個地面問兩次同樣的問題
    /// （這正是 `dropecho/unity_footstep` 與 `FMOD-Unity-Tools` 兩個參考實作共同的成本來源，見 `docs/34` §3）。
    ///
    /// ⚠️ **時序保證（這條管道能成立的唯一依據）**：
    /// `PresentationPipeline.Tick` 的步驟①（驅動所有 <c>IPresentationController</c>，含 `FootIKController`）
    /// **必定**在步驟②（詢問所有 <c>IPresentationEventSource</c>，含 `FootstepDetector`）之前跑完。
    /// 因此 Reader 讀到的恆為**本帧剛算出的**觀測，不是上一帧的殘值，
    /// 且**與 Hierarchy 順序無關**——兩個步驟之間隔著的是管線的結構，不是元件的排列。
    ///
    /// **讀取方契約**：
    /// * **只讀不寫**；
    /// * **不得跨帧快取內容**（每帧整體覆寫，`Collider` 尤其不得留存）；
    /// * 向 owner 取引用（<c>FootIKController.GroundData</c>），**不得**以反射或其他方式伸進 Controller 私有狀態。
    /// </summary>
    public sealed class FootIKGroundData
    {
        public FootGroundContact Left;
        public FootGroundContact Right;

        /// <summary>
        /// Controller 首次完成一輪採樣後恆為 true。Reader 據此避免消費未初始化的全零觀測
        /// （比照 <see cref="FootIKPoseData.IsWarm"/>）。
        /// ⚠️ 「Warm」只保證**這份資料被寫過**，不保證本帧有命中地面——後者看 <see cref="FootGroundContact.HasGround"/>。
        /// </summary>
        public bool IsWarm;
    }
}
