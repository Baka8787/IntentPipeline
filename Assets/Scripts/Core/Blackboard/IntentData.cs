using Project.Core.Actions;

namespace Project.Core.Blackboard
{
    /// <summary>
    /// 意圖區：記錄這一瞬間「想」做什麼。
    /// 結構體 + 整體覆寫 = 復位時零 GC。
    /// </summary>
    public struct IntentData
    {
        public bool JumpRequested;
        public bool RollRequested;

        /// <summary>
        /// 🆕（ADR-005 D1）本幀被請求的 Action 身分。<see cref="ActionSlot.None"/> ＝ 沒有請求。
        /// **取代原本的 <c>bool FireRequested</c>**——單一布林無法表達「請求的是哪一個技能」，
        /// 那正是 FU-2／FU-3 的共同根因。
        ///
        /// 沿用與 Jump／Roll 相同的**單幀邊沿語意**：由順序 2 的 Intent Processor 寫入、
        /// 狀態機於順序 4 讀取、順序 7 的 <see cref="Reset"/> 統一復位為 <c>None</c>。
        /// enum 欄位，零配置、零裝箱。
        /// </summary>
        public ActionSlot RequestedActionSlot;

        /// <summary>
        /// 每帧結尾呼叫，將所有單帧意圖復位
        /// </summary>
        public void Reset()
        {
            JumpRequested = false;
            RollRequested = false;
            RequestedActionSlot = ActionSlot.None;
        }
    }
}