using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// Minimal Humanoid Hand IK adapter for committed traversal targets. It performs no probe,
    /// state, timing, or root-path decisions; OnAnimatorIK only snapshots the animated goals and
    /// applies values authored by TraversalHandIKController.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class TraversalHandIKRig : MonoBehaviour
    {
        private Animator _animator;
        private Transform _leftHandBone;
        private Transform _rightHandBone;
        private TraversalHandIKTargetData _targetData;
        private readonly TraversalHandIKPoseData _poseData = new TraversalHandIKPoseData();

        public TraversalHandIKPoseData PoseData => _poseData;

        public void Bind(TraversalHandIKTargetData targetData)
        {
            _targetData = targetData;
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_animator != null && _animator.isHuman)
            {
                _leftHandBone = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                _rightHandBone = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
        }

        private void OnAnimatorIK(int layerIndex)
        {
            _poseData.LeftHandPosition = _animator.GetIKPosition(AvatarIKGoal.LeftHand);
            _poseData.LeftHandRotation = _animator.GetIKRotation(AvatarIKGoal.LeftHand);
            _poseData.RightHandPosition = _animator.GetIKPosition(AvatarIKGoal.RightHand);
            _poseData.RightHandRotation = _animator.GetIKRotation(AvatarIKGoal.RightHand);
            // 與 goal 同一瞬間取骨骼旋轉：此刻動畫已評估、IK 尚未套用，兩者同源。
            if (_leftHandBone != null) _poseData.LeftHandBoneRotation = _leftHandBone.rotation;
            if (_rightHandBone != null) _poseData.RightHandBoneRotation = _rightHandBone.rotation;
            _poseData.IsWarm = true;

            if (_targetData == null) return;

            _animator.SetIKPositionWeight(
                AvatarIKGoal.LeftHand, _targetData.LeftHandPositionWeight);
            _animator.SetIKRotationWeight(
                AvatarIKGoal.LeftHand, _targetData.LeftHandRotationWeight);
            _animator.SetIKPosition(AvatarIKGoal.LeftHand, _targetData.LeftHandPosition);
            _animator.SetIKRotation(
                AvatarIKGoal.LeftHand,
                ToGoalRotation(
                    _targetData.LeftHandRotation,
                    _poseData.LeftHandBoneRotation,
                    _poseData.LeftHandRotation));

            _animator.SetIKPositionWeight(
                AvatarIKGoal.RightHand, _targetData.RightHandPositionWeight);
            _animator.SetIKRotationWeight(
                AvatarIKGoal.RightHand, _targetData.RightHandRotationWeight);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, _targetData.RightHandPosition);
            _animator.SetIKRotation(
                AvatarIKGoal.RightHand,
                ToGoalRotation(
                    _targetData.RightHandRotation,
                    _poseData.RightHandBoneRotation,
                    _poseData.RightHandRotation));
        }

        /// <summary>
        /// 把 committed 的**骨骼**朝向換算成 Animator 的 **IK goal** 朝向。
        ///
        /// <para><b>為什麼需要這一步</b></para>
        /// `SetIKRotation` 吃的不是骨骼的世界旋轉，而是 humanoid goal 空間的旋轉；
        /// 兩者差一個由 avatar 決定的基底轉換。實測 `angle(goal, bone)` 穩定落在 **95–102°**，
        /// 明顯不是單位旋轉。直接把骨骼朝向餵進去的後果量過：
        /// **2.05／1.80／1.68 三個高度的食指全部掉到頂面下方 0.220 m**，
        /// 連原本正確的 2.05 都壞——三個高度誤差一模一樣，正是「基底錯了」的特徵。
        ///
        /// <para><b>為什麼用「本幀的」K 而不是烘一個常數</b></para>
        /// `K = inverse(bone) × goal` 在 contact window 內實測**近似常數但會漂移**
        /// （左手 8.22°／右手 7.87°）。本幀的 bone 與 goal 都在
        /// <c>OnAnimatorIK</c> 開頭、IK 尚未套用時取樣，是同源的一對
        /// ⇒ 用本幀的 K 換算，那 8° 的漂移自動消掉，也不需要多烘一個 avatar 常數。
        /// ⚠️ 取樣時點是硬性條件：<c>LateUpdate</c> 讀到的骨骼**已經被 IK 解算改寫**，
        /// 與 goal 不同源，用它推出來的 K 是假的。
        ///
        /// <para>⛔ 這個換算只屬於這一層</para>
        /// Core 的 plan builder 沒有 Animator，也不該知道 avatar 的內部慣例；
        /// 它只負責回答「手該朝哪裡」，用的是骨骼朝向這個與 rig 無關的語彙。
        /// </summary>
        private static Quaternion ToGoalRotation(
            Quaternion committedBoneRotation,
            Quaternion sampledBoneRotation,
            Quaternion sampledGoalRotation)
        {
            if (!IsUsable(committedBoneRotation) ||
                !IsUsable(sampledBoneRotation) ||
                !IsUsable(sampledGoalRotation))
            {
                return committedBoneRotation;
            }

            Quaternion boneToGoal = Quaternion.Inverse(sampledBoneRotation) * sampledGoalRotation;
            return committedBoneRotation * boneToGoal;
        }

        /// <summary>四元數是否可用：分量有限且模長接近 1（未初始化的 `(0,0,0,0)` 會被擋下）。</summary>
        private static bool IsUsable(Quaternion value)
        {
            float squared = value.x * value.x + value.y * value.y +
                            value.z * value.z + value.w * value.w;
            return !float.IsNaN(squared) && !float.IsInfinity(squared) &&
                   Mathf.Abs(squared - 1f) < 0.01f;
        }
    }
}
