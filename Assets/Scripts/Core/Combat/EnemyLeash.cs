namespace Project.Core.Combat
{
    /// <summary>敵人相對於家的位置分區（`docs/55` §11.3）。</summary>
    internal enum LeashZone
    {
        Inside = 0,
        BeyondSoft = 1,
        BeyondHard = 2,
    }

    /// <summary>未交戰時，這一帧可以用什麼理由進入交戰。</summary>
    internal enum LeashEntry
    {
        /// <summary>不得進入（冷卻中，或在 Hard Leash 外——受擊也不行）。</summary>
        Blocked = 0,

        /// <summary>只有受擊可以進入（Soft～Hard 之間：距離 aggro 不算）。</summary>
        DamageOnly = 1,

        /// <summary>照既有規則（距離 aggro 或受擊）。</summary>
        Normal = 2,
    }

    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.3，N2／N3）**Leash：因為離家太遠而脫戰、以及何時可以再交戰。**
    ///
    /// 純函數，住在 <c>Core/Combat</c>：「還在不在交戰」只有一個 owner（<see cref="AICombatContextSource"/>），
    /// 移動端不得自己決定脫戰（A54 的精神）。
    ///
    /// <list type="bullet">
    /// <item>交戰中：超過 Hard ⇒ 一定脫戰（不看受傷）；超過 Soft 且本次交戰沒被打過 ⇒ 脫戰。</item>
    /// <item>未交戰：冷卻中或在 Hard 外 ⇒ 不得進入；Soft～Hard ⇒ 只有受擊可進入；Soft 內 ⇒ 照舊。</item>
    /// </list>
    /// ⇒ 持續受傷只能讓敵人追過 Soft，**永遠追不過 Hard**；冷卻＋帶狀規則讓脫戰後不會逐帧振盪。
    /// </summary>
    internal static class EnemyLeash
    {
        internal static LeashZone Classify(float distanceFromHome, float softRadius, float hardRadius)
        {
            if (distanceFromHome > hardRadius) return LeashZone.BeyondHard;
            if (distanceFromHome > softRadius) return LeashZone.BeyondSoft;
            return LeashZone.Inside;
        }

        internal static bool ShouldDisengage(LeashZone zone, bool damagedThisEpisode)
        {
            return zone == LeashZone.BeyondHard ||
                   (zone == LeashZone.BeyondSoft && !damagedThisEpisode);
        }

        internal static LeashEntry ResolveEntry(LeashZone zone, bool cooldownActive)
        {
            if (cooldownActive || zone == LeashZone.BeyondHard) return LeashEntry.Blocked;
            return zone == LeashZone.BeyondSoft ? LeashEntry.DamageOnly : LeashEntry.Normal;
        }
    }
}
