using System;
using UnityEngine;
using Project.Presentation.Motion;

namespace Project.Core.StateMachine.Actions
{
    [Serializable]
    public struct ActionPhaseEntry
    {
        public ActionPhase Phase;
        public string AnimationKey;
        public MotionBakeData Bake;
        public float FallbackDuration;
        public bool Interruptible;
        public bool WaitForTrigger;
        public bool EmitsRelease;
        [Range(0f, 1f)] public float ReleaseNormalizedTime;
    }

    [CreateAssetMenu(fileName = "ActionDefinition", menuName = "Project/Core/Action/ActionDefinition")]
    public sealed class ActionDefinitionSO : StateParamsSO
    {
        [Tooltip("Phase 集合；Start 必須存在，其餘可省略。順序不重要，以 Phase 欄位查找。")]
        public ActionPhaseEntry[] Phases;

        [Min(0f)] public float Cooldown = 0.5f;
        public bool RequiresGrounded = true;
        [Min(0f)] public float CancelMoveIntentThreshold = 0.1f;
    }
}
