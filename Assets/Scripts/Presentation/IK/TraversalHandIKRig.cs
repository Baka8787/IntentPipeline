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
        }

        private void OnAnimatorIK(int layerIndex)
        {
            _poseData.LeftHandPosition = _animator.GetIKPosition(AvatarIKGoal.LeftHand);
            _poseData.LeftHandRotation = _animator.GetIKRotation(AvatarIKGoal.LeftHand);
            _poseData.RightHandPosition = _animator.GetIKPosition(AvatarIKGoal.RightHand);
            _poseData.RightHandRotation = _animator.GetIKRotation(AvatarIKGoal.RightHand);
            _poseData.IsWarm = true;

            if (_targetData == null) return;

            _animator.SetIKPositionWeight(
                AvatarIKGoal.LeftHand, _targetData.LeftHandPositionWeight);
            _animator.SetIKRotationWeight(
                AvatarIKGoal.LeftHand, _targetData.LeftHandRotationWeight);
            _animator.SetIKPosition(AvatarIKGoal.LeftHand, _targetData.LeftHandPosition);
            _animator.SetIKRotation(AvatarIKGoal.LeftHand, _targetData.LeftHandRotation);

            _animator.SetIKPositionWeight(
                AvatarIKGoal.RightHand, _targetData.RightHandPositionWeight);
            _animator.SetIKRotationWeight(
                AvatarIKGoal.RightHand, _targetData.RightHandRotationWeight);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, _targetData.RightHandPosition);
            _animator.SetIKRotation(AvatarIKGoal.RightHand, _targetData.RightHandRotation);
        }
    }
}
