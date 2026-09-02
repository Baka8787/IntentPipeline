using System;
using UnityEngine;
using Project.Presentation.Motion;
using Project.Core.Actions;

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
        [Tooltip("這份 Definition 的身分（ADR-005 D1）。輸入映射、冷卻、external request、中斷規則全以它為鍵。\n" +
                 "同一角色的多份 Definition 不得共用同一個 Slot；None 視為未設定。")]
        public ActionSlot Slot = ActionSlot.Primary;

        [Tooltip("Phase 集合；Start 必須存在，其餘可省略。順序不重要，以 Phase 欄位查找。")]
        public ActionPhaseEntry[] Phases;

        [Min(0f)] public float Cooldown = 0.5f;
        public bool RequiresGrounded = true;
        [Min(0f)] public float CancelMoveIntentThreshold = 0.1f;
    }
}
