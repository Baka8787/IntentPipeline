using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// 🆕（M3）Foot IK 的單向資料管道：FootIKController（唯一 Writer，管線順序 6.5 寫入）
    /// → 本資料 → FootIKRig（唯一 Reader，OnAnimatorIK 套用）。
    /// 與黑板／MotionDriver 相同的「共享數據＋單一寫入者」資料流——Controller 與 Rig 之間
    /// 執行期不存在任何方法呼叫、事件或回呼（M3 裁決明禁 Event Bus／Message System／Callback）。
    /// 不進 PlayerRuntimeData：IK 目標是表現層內部的中間產物、非玩法數據，進全域黑板會
    /// 承擔不必要的 Owner/Writer/Readers 治理成本（黑板欄位＝玩法契約，此處只是管道）。
    /// 所有欄位以「值」表達語義：權重 0＝該腳不套 IK、PelvisOffsetY 0＝骨盆不補償——
    /// Rig 因此不需要任何布林判斷，維持 Thin Executor。
    /// 生命週期：FootIKController.Awake 一次配置（執行期零 GC），每幀整組覆寫。
    /// </summary>
    public class FootIKRuntimeData
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
