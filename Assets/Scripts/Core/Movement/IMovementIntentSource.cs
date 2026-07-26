using Project.Core.Blackboard;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（ADR-003 D2，Migration Stage 1）Movement 意圖的 **Producer 抽象**（DIP seam）。
    ///
    /// 契約：
    /// 1. **每 domain 任一時刻只有一個 active producer** 寫該 intent region（single-writer 不破）。
    /// 2. **Producer context-free**：input → intensity 走固定 profile，**不每幀回讀 gameplay state**
    ///    （避免 producer → state 的同幀回圈；ADR-003 §13.1-R3）。
    /// 3. **不處理 context-sensitive input**：同一顆實體鍵在不同情境的語意切換屬更上游的 Input 層
    ///    （Input System action map 切換／Input Router）。輸入抵達 producer 時語意已定（ADR-003 §13.3）。
    ///
    /// 擴充方式（皆為加法，**不改 Runner**）：<c>AIMovementSource</c>（planner→intent）／
    /// <c>ReplaySource</c>／<c>NetworkSource</c> 各實作本介面，於 Inspector 換掛即可切換 active producer。
    /// </summary>
    /// <remarks>
    /// ⚠️ 已知限制（Stage 1，落地時記錄）：本簽名帶 <see cref="InputData"/> 參數——因管線順序 1 已在 Runner
    /// 集中採樣一次（<c>ref struct</c> 無法被任何 class 持有為欄位，只能沿呼叫堆疊傳遞），故沿用單一採樣點傳址給 producer。
    /// 非輸入驅動的 producer（AI／Replay）可直接忽略此參數。**待第二個 producer 真正進場時**再複驗：
    /// 是否要改為「各 producer 自持資料來源」的無參數簽名。此為實作取捨，不涉 ADR 契約變更。
    /// </remarks>
    public interface IMovementIntentSource
    {
        /// <summary>
        /// 【管線順序 2.5】產生本幀的 Movement 意圖，寫入黑板的 <c>MovementIntent</c> region。
        /// </summary>
        /// <param name="input">本幀原始輸入快照（player producer 使用；AI／Replay 等非輸入驅動的 producer 可忽略）。</param>
        /// <param name="data">黑板；本方法**只**寫入自己 domain 的 intent region。</param>
        void ProduceIntent(ref InputData input, PlayerRuntimeData data);
    }
}
