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

        /// <summary>
        /// 🆕（Stage 1）小幅腳部調整（停步收腳／轉身／姿勢微調）。
        /// **刻意不分左右**：這是很輕的摩擦聲，左右差異在聽感上不可辨，
        /// 多開一個查表格換不到任何東西（對比 Left/RightFootstep 拆開是為了「enum 只增不改」的將來性，
        /// 那裡拆開的成本只有一個空格子，這裡拆開的成本是每個材質都要多維護一組資產）。
        /// </summary>
        Shuffle = 3,

        // =====================================================================
        // 🆕（2026-09-17，戰鬥音效）以下三個的共同點：**觸發源是已 commit 的黑板欄位**，
        // 因此走 AudioLibrarySO 這條「角色層級的事件查表」。
        //
        // ⚠️ 對比：Cast／Release／Impact 這類**每個 Action slot 各不相同**的音效
        //    刻意**不**進本 enum——見 `ActionAudioSink` 的說明。
        //    把「火球施法音」變成一個列舉值，等於宣告「全域只會有一種施法音」，
        //    而下一個法術出現時就得再加一個值、再填一格全域表。
        //    那些音效的正確歸屬是**那個 slot 的資產**，不是全域詞彙。
        // =====================================================================

        /// <summary>
        /// 受到**未致死**傷害。來源：<c>SurvivabilityData.JustTookDamage</c>。
        ///
        /// ⚠️ **玩家與敵人共用這一個列舉值**，聽起來不同是因為兩隻角色掛的是
        /// **不同的 <see cref="AudioLibrarySO"/> 資產**（X Bot 一份、Y Bot 一份）。
        /// ⛔ 不要為了「敵人受擊」再開一個 `EnemyHurt`——那會把**陣營**寫進表現層的詞彙，
        /// 而陣營在本專案是不存在的概念（`CharacterHealth` 的第一行註解：生存語意不分陣營）。
        /// </summary>
        Hurt = 4,

        /// <summary>
        /// 由活轉死的那一幀。來源：<c>SurvivabilityData.JustDied</c>（**事件**，不是 `IsDead` 狀態）。
        /// 同樣玩家／敵人共用列舉值、各自用自己的 Library 資產區分聲音。
        /// </summary>
        Death = 5,

        /// <summary>
        /// 起跳離地。來源：黑板 <c>JustLeftGround</c>——它自 M2 起就存在且**一直沒有消費者**
        /// （`PlayerRuntimeData` 原註解：「M2 尚無消費者，與 JustLanded 成對維護」）。
        ///
        /// ⚠️ **語意誠實說明**：`JustLeftGround` 是「離地」，**不是「起跳」**——
        /// 走下平台邊緣也會抬起它。本輪刻意接受這個近似，因為：
        /// ①成本是零（欄位已存在、寫入者已存在）；
        /// ②要精確區分就得讓 `JumpState` 也發布一個事件，那是為了一顆音效動 FSM。
        /// 若 Play 時「走下台階也有起跳聲」明顯不對，正確修法是加 Jump 自己的事件源，
        /// ⛔ 不是在這裡讀速度或狀態去猜。
        /// </summary>
        JumpTakeoff = 6,
    }
}
