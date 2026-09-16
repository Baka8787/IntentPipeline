namespace Project.Core.Actions
{
    /// <summary>
    /// Action 的身分（ADR-005 D1）。輸入映射、per-slot 冷卻、external request、
    /// Action→Action 中斷規則**全部以本 enum 為鍵**——不得有第二把鍵。
    ///
    /// <para><b>成長規則（2026-09-02 修訂，取代原本的 Primary／Secondary／Tertiary）</b></para>
    /// 本 enum 分成兩段，加東西時先問「它是玩家按出來的嗎」：
    /// <list type="bullet">
    /// <item><b>玩家觸發段（1 起，依序編號）</b>：<c>Slot1</c>／<c>Slot2</c>／… 想加就往下接一個號碼。
    /// 這一段**本來就是欄位編號**——像 action bar 的格子，沒有內建語意。</item>
    /// <item><b>保留段（100 起，語意命名）</b>：**沒有輸入來源**的身分，只由 <c>ActionRequestTarget</c>
    /// 這條 external seam 提交。因為它們沒有「第幾格」可言，所以用名字而不是號碼。</item>
    /// </list>
    ///
    /// <para><b>為什麼原本的 Primary／Secondary／Tertiary 要換掉</b></para>
    /// 那三個名字**自稱有語意，實際上是拉丁文寫的序號**——`Primary` 就是「第 1 個」。
    /// 於是同一個 enum 裡混了兩套命名法（三個序號 ＋ 一個語意 <c>Reaction</c>），
    /// 既講不出成長規則（第 4 個要叫 Quaternary 嗎），也讓身分實質綁在「按哪顆鍵」上
    /// （原註解就是這樣寫的：Primary ＝ 左鍵、Secondary ＝ Q）。**按鍵是 Presentation 的事，不是身分。**
    /// 改成明確編號後，「這是第幾格」與「這格綁哪顆鍵」重新分開：後者住在管線順序 2 的輸入映射。
    ///
    /// <para><b>為什麼不用具體技能名（Fireball／IceSpell…）</b></para>
    /// 那會讓 <c>Core</c> 認識遊戲內容，與本專案「可獨立抽取的通用套件」定位直接衝突
    /// （README／UPM <c>com.baka8787.intentpipeline</c>）。技能叫什麼是資產的事，
    /// Core 只需要知道「有幾格、哪一格」。
    ///
    /// <para>⚠️ <b>數值＝穩定身分，且刻意不要求連續</b></para>
    /// 每個成員的**數值本身就是那個身分**——Unity 以 <b>int 值</b>序列化 enum，不是名字。
    /// 因此：
    /// <list type="bullet">
    /// <item><b>改名安全</b>（資產不受影響）；<b>改值不安全</b>——會讓既有資產默默指向別的 slot。
    /// 數值一旦被資產引用就不得再動；要淘汰某一格請**留著它的數值**、另加新的，不要遞補。</item>
    /// <item><b>不要求連續。</b>玩家段與保留段之間的空隙（4–99）是**設計的一部分**，不是待填的洞——
    /// 它讓兩段各自獨立成長，玩家段加到第 4 格時不會撞上保留段。
    /// 任何「把號碼補滿比較整齊」的念頭都會改到既有數值，也就是改到身分本身。</item>
    /// <item>因為不連續，以 slot 值當索引的表（如 <c>ActionState</c> 的 per-slot 冷卻陣列）
    /// **必然是稀疏的**，容量取 enum 的最大值 +1。那是刻意的取捨，不是浪費——詳見該處註解。</item>
    /// </list>
    ///
    /// ⚠️ 本 enum 的成員數**不是**架構不變量（<c>StateType</c> 才是）。要加就加，
    /// 但每加一個都應該回答「這是新的身分，還是既有身分的變體？」。
    /// </summary>
    public enum ActionSlot
    {
        /// <summary>無 request。<c>IntentData</c> 的復位值，亦即「這一幀沒有人要出手」。</summary>
        None = 0,

        // ───────── 玩家觸發段：依序編號，加就往下接 ─────────

        /// <summary>玩家第 1 格。目前的輸入映射綁滑鼠左鍵（ADR-004 期間的 Throw 沿用此格）。</summary>
        Slot1 = 1,

        /// <summary>玩家第 2 格。目前的輸入映射綁 Q。</summary>
        Slot2 = 2,

        /// <summary>玩家第 3 格。目前的輸入映射綁 E。</summary>
        Slot3 = 3,

        // ───────── 保留段（100 起）：非輸入驅動，語意命名 ─────────

        /// <summary>
        /// ⚰️ **RETIRED（2026-09-14，`docs/26` Model B）——不要使用。**
        ///
        /// <para><b>它曾經是什麼</b></para>
        /// 受擊反應的 Action 身分：`DamageDefinition.asset` → <c>ActionState</c> 播 "Damage"。
        ///
        /// <para><b>為什麼退役</b></para>
        /// **受擊不是出手。** <c>ActionState</c> 曾為它留兩處語意反轉
        /// （`ShouldFaceTargetOnEnter` 排除它、`AllowsSameSlotReentry` 只允許它），
        /// 兩處的註解都寫著「受擊不是出手」——程式自己說了兩次它不屬於這裡。
        /// 受擊已改為獨立的 <c>StateType.Hurt</c>，由 <c>SurvivabilityData.JustTookDamage</c> 驅動。
        /// 完整論證見 `docs/26-fsm-interruption-review.md`。
        ///
        /// <para><b>⚠️ 為什麼成員與數值仍然保留</b></para>
        /// ADR-005：「**數值一旦被資產引用就不得再動；要淘汰某一格請留著它的數值、另加新的，不要遞補**」。
        /// Unity 以 **int** 序列化 enum ⇒ 移除成員或回收 100 這個數值，
        /// 會讓任何殘存的舊資產**默默指向別的 slot**，而不是變成無效值。
        /// 留著它，殘存引用就仍然顯示為 `Reaction`，**可辨識、可清理**。
        ///
        /// <para><b>不變量</b></para>
        /// `A5x_RetiredReactionSlot_HasNoRuntimeCaller` 守住「runtime 程式不得再引用它」；
        /// `A5x` 的資產面斷言守住「沒有任何 `ActionDefinitionSO` 使用它」。
        /// ⛔ 新的非輸入驅動身分請往下接 `101`，**不要**重用 100。
        /// </summary>
        [System.Obsolete(
            "ActionSlot.Reaction 已退役（docs/26 Model B）：受擊改為 StateType.Hurt，" +
            "由 SurvivabilityData.JustTookDamage 驅動。成員與數值 100 僅為 serialization 相容而保留，" +
            "用於辨識與清理舊資產；⛔ 不得在新程式或新資產中使用。")]
        Reaction = 100
    }
}
