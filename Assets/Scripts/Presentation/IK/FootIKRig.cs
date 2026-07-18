using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// 🆕（M3）Foot IK 執行端（Thin Executor）——掛 Model 子物件、與 Animator 同物件
    /// （OnAnimatorIK 回呼只發給 Animator 同物件上的元件，Unity 硬性限制；
    /// 亦即 ADR-001 預留的「需要直接操作骨骼的功能掛 Model 層」情境）。
    ///
    /// 職責（M3 裁決）：唯讀 <see cref="FootIKRuntimeData"/>（唯一 Reader）→ 原樣套入
    /// SetIKPosition／SetIKRotation／SetIK*Weight／bodyPosition，僅此而已。
    /// 不做 raycast、不讀黑板、不判斷 IsGrounded／狀態、不算權重——一切語義由資料的「值」表達
    /// （權重 0＝不生效、PelvisOffsetY 0＝不補償），本類別零決策分支
    /// （null 防護屬防禦線，非邏輯判斷）。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FootIKRig : MonoBehaviour
    {
        private Animator _animator;
        private FootIKRuntimeData _data;

        /// <summary>
        /// 由 FootIKController 於組裝期（Awake）注入一次。此後兩者僅透過共享數據溝通，
        /// 執行期無任何方法呼叫／事件／回呼（M3 裁決）。
        /// </summary>
        public void Bind(FootIKRuntimeData data) => _data = data;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_data == null) return; // 未綁定（缺 Controller）時安全靜默——防禦線

            // 骨盆補償：bodyPosition 每幀由 Animator 依動畫姿勢重算，此處疊加不會跨幀累積。
            _animator.bodyPosition += Vector3.up * _data.PelvisOffsetY;

            _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, _data.LeftFootPositionWeight);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, _data.LeftFootRotationWeight);
            _animator.SetIKPosition(AvatarIKGoal.LeftFoot, _data.LeftFootPosition);
            _animator.SetIKRotation(AvatarIKGoal.LeftFoot, _data.LeftFootRotation);

            _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, _data.RightFootPositionWeight);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, _data.RightFootRotationWeight);
            _animator.SetIKPosition(AvatarIKGoal.RightFoot, _data.RightFootPosition);
            _animator.SetIKRotation(AvatarIKGoal.RightFoot, _data.RightFootRotation);
        }
    }
}
