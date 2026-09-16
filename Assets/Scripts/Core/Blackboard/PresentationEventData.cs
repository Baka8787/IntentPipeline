namespace Project.Core.Blackboard
{
    /// <summary>
    /// 🆕（Stage 1）腳步事件的**分類**。
    ///
    /// ⚠️ **住在 `Core.Blackboard` 而不是 `Presentation.Footstep`，是依賴方向的要求**：
    /// <see cref="PresentationEventData"/> 是黑板 schema 的一部分，黑板**不得**反向依賴表現層。
    /// 這個 enum 是「事件的詞彙」，詞彙屬於契約，不屬於偵測器。
    ///
    /// ⚠️ **數值大小即合併優先序**（<c>PresentationPipeline</c> 以 max 合併多來源）：
    /// 同帧同一隻腳若有多個來源回報，較「重」的分類勝出。只增不改不重排。
    /// </summary>
    public enum FootstepKind
    {
        /// <summary>本帧該腳沒有事件（含「被抑制」——抑制與「沒發生」對外一律是 None）。</summary>
        None = 0,

        /// <summary>
        /// 小幅腳部調整：停步收腳、轉身、姿勢微調。抬腳行程明顯小於一次真正的跨步。
        /// 應播較輕的摩擦聲，**不得**使用正常 Walk／Run 的腳步音。
        /// </summary>
        Shuffle = 1,

        /// <summary>真正的 locomotion 落腳。</summary>
        Normal = 2,
    }

    /// <summary>
    /// 🆕（M3.x-B）表現層事件快照。由 <c>PresentationPipeline</c> 於管線順序 6.5 的**末尾**整體覆寫，
    /// 各表現層 Controller 於**下一帧**的 6.5 讀取。
    ///
    /// **廣播快照，不是可消費佇列**：
    /// * 每個 consumer 看到的是同一份快照，**consumer 不得清除**——Audio／未來 VFX／鏡頭震動不會互相吃掉事件。
    /// * 不需要 sequence number、frame identity、consumer acknowledgement。
    ///   「恰好被每個 consumer 看到一次」是結構保證：發布固定在所有 Controller Tick **之後**，
    ///   消費固定在下一帧 Tick **之中**，兩者永遠隔著一個 frame 邊界。
    /// * **每帧從 <c>default</c> 整體覆寫即是它的復位機制**——因此
    ///   <see cref="PlayerRuntimeData.ResetTransientState"/> **刻意不認識本欄位**，
    ///   不需要為它開任何例外（對比 <c>Intent</c>／<c>JustLanded</c> 走順序 7 統一復位）。
    ///
    /// ⚠️ **本結構是領域中性的容器，不是通用 Event Bus**：欄位固定、型別靜態、單一寫入者、零配置。
    /// 新增事件走「只增不改」的加法（同 <c>ArbiterData</c> 從 4 個旗標起步、當時只有 1 個有 reader 的先例）。
    ///
    /// 🔄（Stage 1）兩個 <c>bool</c> 升級為 <see cref="FootstepKind"/>：
    /// 「有沒有落腳」與「那是哪一種落腳」是兩個問題，之前只答得出前者，consumer 因此只能把
    /// 停步收腳的小碎步當成完整腳步播。既有的 <see cref="LeftFootPlanted"/>／<see cref="RightFootPlanted"/>
    /// 保留為衍生屬性，讓「只想知道有沒有落腳」的讀取方零改動。
    /// </summary>
    public struct PresentationEventData
    {
        /// <summary>本帧左腳落地的分類（**動畫落腳事件**，非物理 ground contact）。</summary>
        public FootstepKind LeftFootstep;

        /// <summary>本帧右腳落地的分類。與左腳各自獨立——同帧雙腳落地是合法狀態。</summary>
        public FootstepKind RightFootstep;

        /// <summary>本帧左腳是否有任何落腳事件（含 Shuffle）。衍生自 <see cref="LeftFootstep"/>，不獨立儲存。</summary>
        public bool LeftFootPlanted => LeftFootstep != FootstepKind.None;

        /// <summary>本帧右腳是否有任何落腳事件（含 Shuffle）。衍生自 <see cref="RightFootstep"/>，不獨立儲存。</summary>
        public bool RightFootPlanted => RightFootstep != FootstepKind.None;
    }
}
