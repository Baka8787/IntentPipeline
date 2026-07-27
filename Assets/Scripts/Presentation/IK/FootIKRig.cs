using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// （M3.1，M3.5 回歸此形）Foot IK 的 **Presentation Adapter**——掛 Model 子物件、與 Animator 同物件
    /// （OnAnimatorIK 回呼只發給 Animator 同物件上的元件，Unity 硬性限制；
    /// ADR-001 §5 預留的「需要直接操作骨骼的功能掛 Model 層」情境）。
    ///
    /// Adapter＝動畫系統邊界上的雙向轉接器，兩個方向各自守單一寫入者：
    /// - **出**（動畫系統 → 資料）：OnAnimatorIK 開頭把該幀動畫原始 IK goal（`GetIKPosition/Rotation`，
    ///   IK 套用前的無污染 pose）＋`FeetBottomHeight` 寫入 <see cref="FootIKPoseData"/>（唯一 Writer）。
    /// - **入**（資料 → 動畫系統）：把 <see cref="FootIKTargetData"/>（唯一 Reader）原樣套入 SetIK*／bodyPosition。
    ///
    /// 不做：raycast、讀黑板、IsGrounded／狀態判斷、權重演算法——兩個方向都是純轉接
    /// （讀值→賦值），零決策分支（null 防護屬防禦線）。
    /// 🆕（M3.5 最終形）M3.2 為 Reach Clamp 加的腿骨快取與髖／腿長量測已隨該機制移除。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FootIKRig : MonoBehaviour
    {
        private Animator _animator;
        private FootIKTargetData _targetData;

        // 🆕（M3.x-A）Pose 快照的 **lifetime / authority owner**：
        //     本元件是這份快照的唯一 Writer，因此由本元件持有它的生命週期。
        //
        // ⚠️ **owner 在此僅指「誰保證這份快照的存在與唯一性」，不是「誰負責解讀它」。**
        //     Rig 不因此取得任何業務職責——它**不判定 plant/lift、不認識 Footstep、不做任何決策**，
        //     兩個方向依然是純轉接（讀值→賦值）。解讀屬各 Reader 自己的事。
        //
        // 以**欄位初始式**建立（而非 Awake）：初始式在元件建構時執行、**早於所有 Awake**，
        // 因此任何讀取方都不需要關心元件之間的 Awake 順序——這讓「拿得到同一份實例」
        // 從時序紀律變成結構保證。
        // 📌 刻意寫成 `new FootIKPoseData()` 而非 target-typed `new()`：讓「唯一建構點」
        //     這條不變量可被靜態掃描守住（見 §7.1-A11）。
        private readonly FootIKPoseData _poseData = new FootIKPoseData();

        /// <summary>
        /// 動畫原始 pose 快照。本元件為**唯一 Writer**；回傳引用而非拷貝，讀取方共享同一份實例。
        /// **讀取方契約：只讀不寫。**
        /// 現有讀取方＝<c>FootIKController</c>；未來的 Foot Contact 偵測器以同一途徑取得同一份實例，
        /// 不需要任何新的抽象或組裝層。
        /// </summary>
        public FootIKPoseData PoseData => _poseData;

        /// <summary>
        /// 由 FootIKController 於組裝期（Awake）注入一次：**Target 管道（本元件讀）**。
        /// 🔄（M3.x-A）Pose 管道**不再由此注入**——它的 Writer 是本元件，故由本元件自行持有
        /// （擁有權跟著寫入權走，見 <c>_poseData</c> 的說明）。
        /// 此後兩者僅透過這兩條單向共享數據溝通，執行期無任何方法呼叫／事件／回呼（M3 裁決）。
        /// </summary>
        public void Bind(FootIKTargetData targetData)
        {
            _targetData = targetData;
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            // 🔄（M3.x-A）兩條管道各自守自己的前提，不再共用一個 return。
            //     Pose 由本元件擁有，恆非 null，所以「出」這一段不再有任何前提；
            //     只有「入」那段需要等 Controller 綁進 Target。
            // 這讓兩條管道**真正獨立**：未來的 Pose Reader 不會因為場上沒有 FootIKController
            // 就拿不到快照。⚠️ 實務上是不可觀察的變化——目前 Pose 的唯一讀取方正是 Controller 本身。

            // === 出：Pose 快照（必須在套用 IK 之前讀——此刻 GetIK* 回傳該幀動畫原始 goal，未被 IK 改寫）===
            _poseData.LeftFootPosition = _animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            _poseData.LeftFootRotation = _animator.GetIKRotation(AvatarIKGoal.LeftFoot);
            _poseData.RightFootPosition = _animator.GetIKPosition(AvatarIKGoal.RightFoot);
            _poseData.RightFootRotation = _animator.GetIKRotation(AvatarIKGoal.RightFoot);
            _poseData.LeftFootBottomHeight = _animator.leftFeetBottomHeight;   // Avatar 常數：數據真相來自 avatar
            _poseData.RightFootBottomHeight = _animator.rightFeetBottomHeight;
            _poseData.IsWarm = true;

            // === 入：Target 套用（骨盆補償：bodyPosition 每幀由動畫重算，疊加不跨幀累積）===
            if (_targetData == null) return; // 未綁定（缺 Controller）時安全靜默——防禦線

            _animator.bodyPosition += Vector3.up * _targetData.PelvisOffsetY;

            _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, _targetData.LeftFootPositionWeight);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, _targetData.LeftFootRotationWeight);
            _animator.SetIKPosition(AvatarIKGoal.LeftFoot, _targetData.LeftFootPosition);
            _animator.SetIKRotation(AvatarIKGoal.LeftFoot, _targetData.LeftFootRotation);

            _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, _targetData.RightFootPositionWeight);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, _targetData.RightFootRotationWeight);
            _animator.SetIKPosition(AvatarIKGoal.RightFoot, _targetData.RightFootPosition);
            _animator.SetIKRotation(AvatarIKGoal.RightFoot, _targetData.RightFootRotation);
        }
    }
}
