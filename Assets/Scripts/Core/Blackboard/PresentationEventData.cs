using UnityEngine;

namespace Project.Core.Blackboard
{
    /// <summary>
    /// 🆕（Stage 1）腳步事件的**分類**。
    ///
    /// ⚠️ **住在 `Core.Blackboard` 而不是 `Presentation.Footstep`，是依賴方向的要求**：
    /// <see cref="PresentationEventData"/> 是黑板 schema 的一部分，黑板**不得**反向依賴表現層。
    /// 這個 enum 是「事件的詞彙」，詞彙屬於契約，不屬於偵測器。
    ///
    /// ⚠️ **數值大小即合併優先序**（<c>PresentationPipeline</c> 以 max 合併多來源）：
    /// 同帧同一隻腳若有多個來源回報，較「重」的分類勝出。只增不改不重排。
    /// </summary>
    public enum FootstepKind
    {
        /// <summary>本帧該腳沒有事件（含「被抑制」——抑制與「沒發生」對外一律是 None）。</summary>
        None = 0,

        /// <summary>
        /// 小幅腳部調整：停步收腳、轉身、姿勢微調。抬腳行程明顯小於一次真正的跨步。
        /// 應播較輕的摩擦聲，**不得**使用正常 Walk／Run 的腳步音。
        /// </summary>
        Shuffle = 1,

        /// <summary>真正的 locomotion 落腳。</summary>
        Normal = 2,
    }

    /// <summary>
    /// 🆕（Stage 2）**聲學材質**識別。
    ///
    /// ⚠️ **這是「聽起來像什麼」，不是「視覺上是什麼材質」**——兩者可以不同
    /// （鋪了地毯的木地板、貼石紋貼圖的金屬橋面）。因此它由 `SurfaceMapSO` 顯式對應，
    /// **不從 Material 名稱在 runtime 猜**。
    ///
    /// ⚠️ 只增不改不重排（`FootstepAudioBankSO` 以 `(int)` 值為索引）。
    /// 新增材質＝加一個列舉值 ＋ 在 Bank 補一組 clip pool，程式零改動。
    /// </summary>
    public enum SurfaceId
    {
        /// <summary>
        /// 尚未解析（腳下沒有地面觀測，例如角色在空中）。
        /// **合併的單位元**，也是「資料缺漏」與「已解析成某材質」的區別。
        /// ⚠️ 這**不是** default surface——後者是 `SurfaceMapSO` 上可配置的、真正會播出聲音的那一個。
        /// </summary>
        Unknown = 0,

        /// <summary>泛用硬地板：石材、水泥、磁磚、一般室內硬地。**本專案的 default surface**。</summary>
        Stone = 1,

        /// <summary>木頭：木地板、木橋、棧板。</summary>
        Wood = 2,

        /// <summary>草地、草皮、灌木地被。</summary>
        Grass = 3,

        /// <summary>泥土、土徑、鬆軟地面。</summary>
        Dirt = 4,
    }

    /// <summary>
    /// 🆕（Stage 2）步態檔位。**Locomotion 對移動強度 [0-1] 的命名**（docs/04 §10），
    /// 由決定它的人（`PlayerLocomotionPolicy`，即持有 `GaitProfileSO` 的那一顆）**直接 commit**。
    ///
    /// ⚠️ **下游不得從 `MoveSpeed` 重新推導步態**——那是 `docs/16` 明列的加重缺陷
    /// 「下游重新推導已 commit 的狀態」。音效只讀這個欄位。
    /// </summary>
    public enum GaitTier
    {
        Walk = 0,
        Run = 1,
        Sprint = 2,
    }

    /// <summary>
    /// 🆕（M3.x-B）表現層事件快照。由 <c>PresentationPipeline</c> 於管線順序 6.5 的**末尾**整體覆寫，
    /// 各表現層 Controller 於**下一帧**的 6.5 讀取。
    ///
    /// **廣播快照，不是可消費佇列**：
    /// * 每個 consumer 看到的是同一份快照，**consumer 不得清除**——Audio／未來 VFX／鏡頭震動不會互相吃掉事件。
    /// * 「恰好被每個 consumer 看到一次」是結構保證：發布固定在所有 Controller Tick **之後**，
    ///   消費固定在下一帧 Tick **之中**，兩者永遠隔著一個 frame 邊界。
    /// * **每帧從 <c>default</c> 整體覆寫即是它的復位機制**——因此
    ///   <see cref="PlayerRuntimeData.ResetTransientState"/> **刻意不認識本欄位**。
    ///
    /// ⚠️ **本結構是領域中性的容器，不是通用 Event Bus**：欄位固定、型別靜態、單一寫入者、零配置。
    ///
    /// ---
    /// 🆕 **（Stage 2）本結構現在同時攜帶「這一次接觸的 Surface 決策」。**
    ///
    /// **為什麼 Surface 要放在事件裡、而不是讓音效端自己查**：
    /// 使用者回報的首要問題是「Walk 播 Stone，收步卻冒出草地感」——成因就是不同事件**各自**決定材質。
    /// 把材質做成事件的一部分之後，`Normal` / `Shuffle` / `Landed` 三者**在型別層就不可能不一致**：
    /// 它們讀的是同一個欄位。這不是紀律，是結構。
    /// </summary>
    public struct PresentationEventData
    {
        /// <summary>本帧左腳落地的分類（**動畫落腳事件**，非物理 ground contact）。</summary>
        public FootstepKind LeftFootstep;

        /// <summary>本帧右腳落地的分類。與左腳各自獨立——同帧雙腳落地是合法狀態。</summary>
        public FootstepKind RightFootstep;

        /// <summary>
        /// 🆕（Stage 2）本帧角色**整體落地**（原本由消費端直接讀黑板 <c>JustLanded</c>）。
        ///
        /// **為什麼改由事件來源轉報**：落地音必須與腳步音共用同一次 <see cref="Surface"/> 決策。
        /// 消費端讀到的事件快照慢一帧，而 `JustLanded` 是當帧值——兩者混用會讓落地那一瞬間
        /// 拿到**上一帧（還在空中、沒有地面觀測）**的材質。轉報之後兩者同源同帧，不可能錯開。
        /// ⚠️ 代價：落地音也承擔了那一帧（約 16 ms）的延遲。落地是較重較慢的聲音，這個代價可接受。
        /// </summary>
        public bool Landed;

        /// <summary>
        /// 🆕（Stage 2）本帧腳下的聲學材質。<see cref="LeftFootstep"/>／<see cref="RightFootstep"/>／
        /// <see cref="Landed"/> **三者共用這一個決策**。
        /// <see cref="SurfaceId.Unknown"/> ＝本帧沒有地面觀測（角色在空中）；消費端據此退回 Bank 的 default。
        /// </summary>
        public SurfaceId Surface;

        /// <summary>🆕（Stage 2）左腳本帧的世界位置（供 3D 定位播放）。無事件時無意義。</summary>
        public Vector3 LeftFootPosition;

        /// <summary>🆕（Stage 2）右腳本帧的世界位置（供 3D 定位播放）。無事件時無意義。</summary>
        public Vector3 RightFootPosition;

        /// <summary>本帧左腳是否有任何落腳事件（含 Shuffle）。衍生自 <see cref="LeftFootstep"/>，不獨立儲存。</summary>
        public bool LeftFootPlanted => LeftFootstep != FootstepKind.None;

        /// <summary>本帧右腳是否有任何落腳事件（含 Shuffle）。衍生自 <see cref="RightFootstep"/>，不獨立儲存。</summary>
        public bool RightFootPlanted => RightFootstep != FootstepKind.None;
    }
}
