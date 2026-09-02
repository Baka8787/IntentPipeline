namespace Project.Core.Actions
{
    /// <summary>
    /// Action 的身分（ADR-005 D1）。輸入映射、per-slot 冷卻、external request、
    /// Action→Action 中斷規則**全部以本 enum 為鍵**——不得有第二把鍵。
    ///
    /// 為什麼是具語意的 enum 而不是 int index：敵人的受擊是一個 **reaction**，
    /// 不是「第 3 號技能」。匿名索引無法承載這個區別，且資產排序改變即行為改變。
    /// 代價是新增 slot 要改程式——比照 <c>StateType</c> 先例，**該成本本來就該被看見**。
    ///
    /// ⚠️ 本 enum 的成員數**不是**架構不變量（StateType 才是）。要加就加，
    /// 但每加一個都應該回答「這是新的身分，還是既有身分的變體？」。
    /// </summary>
    public enum ActionSlot
    {
        /// <summary>無 request。<c>IntentData</c> 的復位值，亦即「這一幀沒有人要出手」。</summary>
        None = 0,

        /// <summary>主要動作。目前綁滑鼠左鍵（ADR-004 期間的 Throw 沿用此槽）。</summary>
        Primary = 1,

        /// <summary>次要動作。目前綁 Q。</summary>
        Secondary = 2,

        /// <summary>第三動作。目前綁 E。</summary>
        Tertiary = 3,

        /// <summary>
        /// 被動反應（受擊）。**沒有輸入來源**——只由 <c>ActionRequestTarget</c> 這條
        /// external seam 提交（FU-3：讓「我被打到」與「我要出手」在 mailbox 上可區分）。
        /// </summary>
        Reaction = 4
    }
}
