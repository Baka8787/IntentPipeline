namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（M2）表現層音效事件識別——Event → Definition 解耦的鍵：
    /// 玩法端只認事件語義（「落地了」），實際播什麼（clip 池 / 音量 / 音高）由 AudioDefinitionSO 決定，
    /// 兩者透過 AudioLibrarySO 查表對接。
    /// ⚠️ 顯式數值＝序列化與查表真相（AudioLibrarySO 以 (int) 值為陣列索引）：只增不改不重排。
    /// M2 裁決：只做 Landing；腳步音（Footstep）需要腳相事件源，延後至 Foot IK 週邊再擴充。
    /// </summary>
    public enum AudioEventId
    {
        Landing = 0,
    }
}
