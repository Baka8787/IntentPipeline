using UnityEngine;

namespace Project.Core.Blackboard
{
    /// <summary>
    /// 角色當前戰鬥語境的值快照。目標物件由 producer 私有持有；黑板只公開當幀位置，
    /// 讓下游不依賴 Unity 物件生命週期，也不建立第二份目標清單。
    /// </summary>
    public struct CombatContextData
    {
        public bool InCombat;
        public bool HasTarget;
        public Vector3 TargetPosition;

        // 同一個 combat context producer 每幀無記憶地產生；不是第二個 target authority，
        // 只是 Action 在 commitment boundary 可採用的 soft-target 候選快照。
        public bool HasSoftTarget;
        public Vector3 SoftTargetPosition;
    }
}
