namespace Project.Core.Environment
{
    public enum TraversalRejectReason
    {
        None = 0,
        NotGrounded,
        TooSlow,
        NoForwardHit,
        TooHigh,
        NoValidTop,
        TopTooSteep,
        DestinationBlocked,
        CorridorBlocked,

        /// <summary>
        /// 🆕（docs/22 §22，J2）越過邊緣之後**量不到任何落腳面**——精確地說：Probe 在 `Vault1mMaxDepth`（0.6 m）處往下打的那一根 depth ray 在搜尋範圍內沒有命中（獨立審查 F2：牆後只有一條比 0.6 m 更窄的地面也會落在這裡；結論同樣是不能承諾）。
        /// 沒有面就不是可通過的候選；此時 Probe 的 destination 是構造出來的點，淨空檢查沒有意義，
        /// 因此在拒絕順序上排在 <see cref="DestinationBlocked"/> 之前（值放最後只是為了不改既有數值）。
        /// </summary>
        NoLandingSurface,
    }
}
