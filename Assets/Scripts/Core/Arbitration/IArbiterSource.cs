using Project.Core.Blackboard;

namespace Project.Core.Arbitration
{
    /// <summary>
    /// 🆕（輪 4）仲裁來源契約：回報「**本來源自己**」這一幀想要封鎖什麼。
    /// 由 <see cref="ArbiterPipeline"/> 於管線順序 4.5（Update，狀態機之後、動畫表現層之前）集中詢問。
    ///
    /// 設計要點——**刻意回傳值而非 <c>ref ArbiterData</c>**：
    /// 若採 <c>ref</c>，來源就看得見（也就改得掉）其他來源已經抬起的旗標，
    /// 「不得清掉別人的封鎖」只能靠紀律或再寫一條測試去守。改成各自回傳自己的請求後，
    /// 這件事變成**結構上不可能**，且「多來源如何合併」有唯一的家（<see cref="ArbiterPipeline"/>）——
    /// 未來若真要做優先級／強制解封，改的是那一個檔案，所有來源零改動。
    /// 回傳的是 4 個 bool 的 struct（值複製），熱路徑零配置。
    /// </summary>
    /// <remarks>
    /// **實作紀律**：
    /// * **不得自帶 <c>Update</c>／<c>LateUpdate</c>**（比照 <c>IPresentationController</c>）——時序由管線統一保證。
    ///   需要邊沿訊號（<c>WasPressedThisFrame</c>）就在 <see cref="Evaluate"/> 內採樣，那正好是每幀一次。
    /// * **不得回寫 <see cref="PlayerRuntimeData"/>**：黑板仲裁區的唯一寫入者是 <see cref="ArbiterPipeline"/>。
    /// * 來源可以讀狀態機當前狀態（design-doc §2.5 的資料流本就是「Arbiter 讀 state」），
    ///   但反過來讓 <c>BaseState</c> 自己宣告封鎖旗標是**被否決的方向**——那會讓 FSM 認識仲裁概念，
    ///   並把「哪些狀態封鎖什麼」這張表拆散到 N 個 state 檔（輪 4 裁決，見 design-doc §4.5）。
    /// </remarks>
    public interface IArbiterSource
    {
        /// <summary>
        /// 【管線順序 4.5】回傳本來源這一幀請求的封鎖旗標；未請求的旗標留 <c>false</c>。
        /// 合併（目前為 OR）由 <see cref="ArbiterPipeline"/> 負責，來源看不見其他來源的結果。
        /// </summary>
        ArbiterData Evaluate(PlayerRuntimeData data);
    }
}
