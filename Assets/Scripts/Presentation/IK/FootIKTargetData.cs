using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// 🆕（M3，M3.1 由 FootIKRuntimeData 更名）Foot IK 的 **Target 管道**：
    /// FootIKController（唯一 Writer，管線順序 6.5 寫入）→ 本資料 → FootIKRig（唯一 Reader，OnAnimatorIK 套用）。
    /// 與 <see cref="FootIKPoseData"/>（Rig → Controller 的 pose 快照）方向相反、各自獨立的單向資料流；
    /// 兩條管道皆守單一寫入者，Controller 與 Rig 執行期之間**無任何方法呼叫／事件／回呼**
    /// （M3 裁決明禁 Event Bus／Message System／Callback），與黑板／MotionDriver 同款資料流哲學。
    ///
    /// 不進 PlayerRuntimeData：IK 目標是表現層內部的中間產物、非玩法契約，進全域黑板會
    /// 承擔不必要的 Owner/Writer/Readers 治理成本。
    /// 所有欄位以「值」表達語義：權重 0＝該腳不套 IK、PelvisOffsetY 0＝骨盆不補償——
    /// Rig（Presentation Adapter）因此零布林判斷。
    /// 生命週期：FootIKController.Awake 一次配置（執行期零 GC），每幀整組覆寫。
    /// </summary>
    public class FootIKTargetData
    {
        // === 左腳 IK 目標（世界空間）===
        public Vector3 LeftFootPosition;
        public Quaternion LeftFootRotation = Quaternion.identity;
        public float LeftFootPositionWeight;
        public float LeftFootRotationWeight;

        // === 右腳 IK 目標（世界空間）===
        public Vector3 RightFootPosition;
        public Quaternion RightFootRotation = Quaternion.identity;
        public float RightFootPositionWeight;
        public float RightFootRotationWeight;

        /// <summary>骨盆垂直補償（公尺，恆 ≤0：雙腳地面高差時向下沉），由 Rig 疊加到 Animator.bodyPosition。</summary>
        public float PelvisOffsetY;
    }
}
