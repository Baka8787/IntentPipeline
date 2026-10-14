namespace Project.Core.Blackboard
{
    /// <summary>
    /// 🆕（2026-10-14，ADR-012 Trial）**最近一次基礎移動的執行結果——純觀測。**
    ///
    /// <para><b>寫入者</b>：只有 <c>MotionDriver.ExecuteBaseMovement</c>，在 <c>CharacterController.Move</c> **之後**寫（事實，不是預測）。A5 守。</para>
    /// <para><b>時序</b>：順序 6（LateUpdate，第 N 帧）寫、順序 2.5（Update，第 N＋1 帧）讀——與 <c>IsGrounded</c> 同一個「上一帧結果」先例。</para>
    ///
    /// <para><b>「這一帧有沒有被允許照意圖移動」不另設布林</b></para>
    /// 讀者以 <see cref="Frame"/> 是否等於上一帧判定。沒有呼叫 <c>ExecuteBaseMovement</c> 的帧（Action 的 Bake 曲線、
    /// Hurt／Death 的垂直移動、Traversal、停步曲線、時間凍結）自然留下過期的 <see cref="Frame"/> ⇒ 讀者視為「未被允許」。
    /// 這樣任何一條沒有補寫的位移路徑都只會變成「未執行」，永遠不會變成假的「已執行」。
    ///
    /// <para><b>⛔ 只回報，不授權</b>：讀者不得用它允許或禁止移動、不得用它改寫 <c>MovementIntent</c>、不得用它推導 FSM 狀態（ADR-012 D3）。</para>
    /// </summary>
    public struct MotionExecutionData
    {
        /// <summary>寫入當帧的 <c>Time.frameCount</c>。從未寫入時為 0（要求量也為 0 ⇒ 讀者視為可忽略的請求）。</summary>
        public int Frame;

        /// <summary>依 Movement Output 要求的水平位移（m）＝ <c>MoveSpeed × moveSpeed × Δt</c>；不含重力與 step-down 的垂直修正。</summary>
        public float RequestedHorizontalDistance;

        /// <summary><c>CharacterController.Move</c> 結算後實際的水平位移（m）。</summary>
        public float AchievedHorizontalDistance;

        /// <summary>這份報告是否描述 <paramref name="frame"/> 那一帧的基礎移動。</summary>
        public readonly bool DescribesFrame(int frame) => Frame == frame;
    }
}
