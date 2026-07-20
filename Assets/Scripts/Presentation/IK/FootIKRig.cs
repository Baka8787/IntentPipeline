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
#if UNITY_EDITOR
        [Tooltip("🔬 臨時診斷（確診後移除）：每 30 幀輸出左腳的 goal 讀值 vs 骨骼動畫值 vs 上幀寫入值，" +
                 "用於確診「Playables 下 GetIKRotation 是否回傳 goal 緩衝（=我們上幀 Set 的值）而非動畫姿勢」。")]
        [SerializeField] private bool debugLogGoals = false;
#endif

        private Animator _animator;
        private FootIKTargetData _targetData;
        private FootIKPoseData _poseData;

        /// <summary>
        /// 由 FootIKController 於組裝期（Awake）注入一次：Target 管道（讀）與 Pose 管道（寫）。
        /// 此後兩者僅透過這兩條單向共享數據溝通，執行期無任何方法呼叫／事件／回呼（M3 裁決）。
        /// </summary>
        public void Bind(FootIKTargetData targetData, FootIKPoseData poseData)
        {
            _targetData = targetData;
            _poseData = poseData;
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_targetData == null || _poseData == null) return; // 未綁定（缺 Controller）時安全靜默——防禦線

#if UNITY_EDITOR
            // 🔬 臨時診斷段（與玩法零互動，確診後整段移除）
            if (debugLogGoals && Time.frameCount % 30 == 0)
            {
                Transform footBone = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Quaternion goalRot = _animator.GetIKRotation(AvatarIKGoal.LeftFoot);
                Vector3 goalPos = _animator.GetIKPosition(AvatarIKGoal.LeftFoot);
                float rotVsLastSet = Quaternion.Angle(goalRot, _targetData.LeftFootRotation);
                float rotVsBone = footBone != null ? Quaternion.Angle(goalRot, footBone.rotation) : -1f;
                float posVsBone = footBone != null ? Vector3.Distance(goalPos, footBone.position) : -1f;
                Debug.Log($"[FootIK 診斷] L goalRot={goalRot.eulerAngles:F1}\n" +
                          $"  vs 上幀Set夾角={rotVsLastSet:F2}°（≈0 恆定＝goal 緩衝反饋確診）\n" +
                          $"  vs 骨骼旋轉夾角={rotVsBone:F1}°（恆定值＝軸差；隨動作變＝goal 有跟動畫）\n" +
                          $"  goalPos={goalPos:F3} vs 骨骼距離={posVsBone:F3}m｜權重={_targetData.LeftFootPositionWeight:F2}");
            }
#endif

            // === 出：Pose 快照（必須在套用 IK 之前讀——此刻 GetIK* 回傳該幀動畫原始 goal，未被 IK 改寫）===
            _poseData.LeftFootPosition = _animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            _poseData.LeftFootRotation = _animator.GetIKRotation(AvatarIKGoal.LeftFoot);
            _poseData.RightFootPosition = _animator.GetIKPosition(AvatarIKGoal.RightFoot);
            _poseData.RightFootRotation = _animator.GetIKRotation(AvatarIKGoal.RightFoot);
            _poseData.LeftFootBottomHeight = _animator.leftFeetBottomHeight;   // Avatar 常數：數據真相來自 avatar
            _poseData.RightFootBottomHeight = _animator.rightFeetBottomHeight;
            _poseData.IsWarm = true;

            // === 入：Target 套用（骨盆補償：bodyPosition 每幀由動畫重算，疊加不跨幀累積）===
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
