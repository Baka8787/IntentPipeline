using UnityEngine;

namespace Project.Core.Blackboard
{
    /// <summary>
    /// v0.3 變更：改為 ref struct。
    /// 只能存活於 Stack，徹底根除跨幀持有的鬼影資料風險 (Aliasing)。
    /// </summary>
    public ref struct InputData
    {
        public Vector2 MoveInput;
        public Vector2 LookInput;
        public bool JumpButtonDown;
        public bool RollButtonDown;
        public bool FireButtonDown;
    }
}
