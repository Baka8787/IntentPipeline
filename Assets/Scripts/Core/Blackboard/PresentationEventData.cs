namespace Project.Core.Blackboard
{
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
    /// </summary>
    public struct PresentationEventData
    {
        /// <summary>本帧左腳落地（**動畫落腳事件**，非物理 ground contact）。</summary>
        public bool LeftFootPlanted;

        /// <summary>本帧右腳落地。與左腳各自獨立——同帧雙腳落地是合法狀態。</summary>
        public bool RightFootPlanted;
    }
}
