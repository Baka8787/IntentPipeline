using Project.Core.Blackboard;

namespace Project.Presentation
{
    /// <summary>
    /// 🆕（M3.x-B）表現層事件來源契約：回報「**本來源自己**」這一帧偵測到的表現層事件。
    /// 由 <see cref="PresentationPipeline"/> 於管線順序 6.5 的**末尾**（所有 <see cref="IPresentationController"/>
    /// Tick 完成後）集中詢問，合併後整體覆寫黑板。
    ///
    /// 設計要點——**刻意回傳值**（與 <c>IArbiterSource</c> 完全同源的理由）：
    /// 來源因此**結構上不可能寫入黑板**，`IPresentationController` 的「對 <see cref="PlayerRuntimeData"/>
    /// 只讀不寫」契約不需要為了事件發布而放寬任何一個字。
    /// 「誰能寫黑板」與「誰負責偵測」由型別分開，而不是靠紀律分開。
    /// </summary>
    /// <remarks>
    /// **實作紀律**：
    /// * **不得自帶 <c>Update</c>／<c>LateUpdate</c>**——時序由管線統一保證（同 <see cref="IPresentationController"/>）。
    /// * **不得持有 <see cref="PlayerRuntimeData"/> 的欄位引用**：黑板是每帧傳入的參數，不是可快取的狀態。
    /// * **不得認識任何 consumer**（AudioController／VFX／鏡頭），也不需要知道黑板上有沒有對應欄位。
    /// * 跨帧偵測狀態由來源自己擁有；**黑板事件的發布與生命週期屬於管線**。兩種擁有權刻意分離。
    ///
    /// ⚠️ 本介面與 <see cref="IPresentationController"/> 是**兩個獨立角色**，同一顆元件可以只實作其中一個。
    /// 事件來源不需要 <c>Tick</c>——它的全部工作就發生在 <see cref="Evaluate"/> 內。
    /// </remarks>
    public interface IPresentationEventSource
    {
        /// <summary>
        /// 【管線順序 6.5 末尾】回報本來源這一帧偵測到的事件；未偵測到的欄位留 <c>false</c>。
        /// 合併（純 OR）與寫黑板由 <see cref="PresentationPipeline"/> 負責。
        /// </summary>
        PresentationEventData Evaluate(PlayerRuntimeData data);
    }
}
