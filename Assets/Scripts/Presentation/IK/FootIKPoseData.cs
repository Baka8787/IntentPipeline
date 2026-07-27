using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// （M3.1；🔄 M3.x-A 修正擁有權）Foot IK 的 **Pose 快照管道**：
    /// FootIKRig（唯一 Writer，OnAnimatorIK 開頭寫入）→ 本資料 → 各 Reader（順序 6.5 讀取）。
    /// 與 <see cref="FootIKTargetData"/>（Controller → Rig）方向相反、各自獨立的第二條單向資料流。
    ///
    /// **lifetime owner ＝ <c>FootIKRig</c>，理由是它是本快照的唯一 Writer**（M3.x-A 裁決）。
    /// ⚠️ owner 在此指的是**生命週期與唯一性的權責**，**不是**業務職責——Rig 不解讀這份資料、
    /// 不判定 plant/lift、不認識任何消費端。解讀屬各 Reader 自己的事。
    ///
    /// **單寫多讀**：Target 管道是單寫單讀，本管道自 M3.x-A 起明確允許多個 Reader
    /// （現有＝<c>FootIKController</c>）。新增 Reader 的正確途徑是向 owner 取引用（<c>FootIKRig.PoseData</c>），
    /// **不是**向其他 Reader 要——後者會構成 Controller 互相引用，違反 <c>IPresentationController</c> 契約。
    /// 讀取方一律**只讀不寫**。
    ///
    /// 內容＝該幀 Animator 評估出的**動畫原始 IK goal**（`GetIKPosition/GetIKRotation`，IK 套用前）
    /// ＋ Avatar 常數（`FeetBottomHeight`）。這是「混合後動畫 Pose」的唯一無污染來源：
    /// 骨骼 Transform 在 LateUpdate 時已被上一幀 IK 改寫，直接採樣會形成反饋迴路
    /// （M3 腳踝抽搐的根因，見 changelog v0.18.1）。
    /// 🆕（M3.5 最終形）M3.2 為 Reach Clamp 加的髖位置／腿長欄位已隨該機制移除。
    ///
    /// 時序：OnAnimatorIK（Animator 評估流程）早於 LateUpdate——Controller 每幀讀到的是
    /// **本幀**的新鮮快照；算出的目標下一幀套用（一幀延遲屬 Unity Humanoid IK 正常行為）。
    /// </summary>
    public class FootIKPoseData
    {
        // === 動畫原始 IK goal（世界空間，IK 套用前）===
        public Vector3 LeftFootPosition;
        public Quaternion LeftFootRotation = Quaternion.identity;
        public Vector3 RightFootPosition;
        public Quaternion RightFootRotation = Quaternion.identity;

        /// <summary>Avatar 常數：腳踝（IK goal）到腳底的垂直距離（數據真相來自 avatar，非手填）。</summary>
        public float LeftFootBottomHeight;
        public float RightFootBottomHeight;

        /// <summary>
        /// Rig 首次寫入後恆為 true。Controller 據此避免消費未初始化的快照
        /// （IK pass 未開啟／Animator 未評估前，全零快照會產生無意義的 raycast）。
        /// </summary>
        public bool IsWarm;
    }
}
