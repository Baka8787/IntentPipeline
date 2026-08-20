namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（M2）表現層音效事件識別——Event → Definition 解耦的鍵：
    /// 玩法端只認事件語義（「落地了」），實際播什麼（clip 池 / 音量 / 音高）由 AudioDefinitionSO 決定，
    /// 兩者透過 AudioLibrarySO 查表對接。
    /// ⚠️ 顯式數值＝序列化與查表真相（AudioLibrarySO 以 (int) 值為陣列索引）：只增不改不重排。
    /// ~~M2 裁決：只做 Landing；腳步音（Footstep）需要腳相事件源，延後至 Foot IK 週邊再擴充。~~
    /// → 🆕（M3.x-B）**延後條件已滿足**：事件源＝`Presentation/Footstep/FootstepDetector`（讀 Foot IK 的
    /// pre-IK pose 快照），走黑板 `PresentationEvents` 廣播。當初「延後至 Foot IK 週邊」的判斷成立。
    /// ⚠️ 未註冊於 AudioLibrarySO 的事件會靜默跳過（`Get()` 回 null），故可先加 enum、之後再填資產。
    /// </summary>
    public enum AudioEventId
    {
        Landing = 0,

        // 🆕（M3.x-B）左右分開的理由不是「現在就要立體聲」，而是 enum 只增不改：
        // 合成一個 Footstep 之後想再拆左右就得改動既有值，而拆開的成本只有一個空的查表格。
        LeftFootstep = 1,
        RightFootstep = 2,
    }
}
