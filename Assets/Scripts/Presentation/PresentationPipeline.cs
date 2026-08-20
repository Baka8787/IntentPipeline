using Project.Core.Blackboard;

namespace Project.Presentation
{
    /// <summary>
    /// 🆕（M2）表現層驅動骨架：集中持有角色身上所有 IPresentationController，
    /// Runner 只呼叫本類別的 Tick、不認識任何具體 Controller——新增表現模組（IK / Facial / VFX）
    /// 只需實作介面掛上角色階層，Runner 零改動。
    /// 🆕（M3.x-B）**兼任表現層事件的唯一黑板寫入者**：收集 <see cref="IPresentationEventSource"/> 的
    /// value-type 回報，於所有 Controller Tick 完成後整體覆寫 <c>PlayerRuntimeData.PresentationEvents</c>。
    /// 這讓 <see cref="IPresentationController"/> 的「對黑板只讀不寫」契約**一個字都不用改**——
    /// 偵測者回傳值、管線負責寫，兩種權責由型別分開。
    /// 零 GC：兩個陣列皆於建構時一次配置（來源為 Runner Start 的 GetComponentsInChildren），
    /// 執行期 Tick 為純索引 for 迴圈＋struct 值複製，無任何堆配置。
    /// 邊界：本類別只做「依序驅動」，不做**輸出**仲裁；未來若多個 Controller 競爭同一輸出
    /// （例如兩個模組都要控制 AudioSource／同一根骨骼），屆時升級為表現層的輸出仲裁並補 ADR
    /// （見 docs/01-design-doc.md §4.6）。
    /// ⚠️ 這個預留**不等於**輪 4 已落地的 <c>Core/Arbitration/ArbiterPipeline</c>：
    ///    那一顆解的是「**誰可以運作**」（功能封鎖旗標，經黑板 ArbiterData 溝通）；
    ///    這裡預留的是「**同一個輸出由誰說了算**」。兩者層次不同，前者落地不代表後者已解決。
    /// </summary>
    public class PresentationPipeline
    {
        private readonly IPresentationController[] _controllers;
        private readonly IPresentationEventSource[] _eventSources;

        public PresentationPipeline(IPresentationController[] controllers, IPresentationEventSource[] eventSources)
        {
            _controllers = controllers ?? System.Array.Empty<IPresentationController>();
            _eventSources = eventSources ?? System.Array.Empty<IPresentationEventSource>();
        }

        /// <summary>
        /// 【管線順序 6.5】兩個子步驟，順序不可調換：
        /// ① 依序驅動所有 <see cref="IPresentationController"/>（它們讀到的是**上一帧**發布的事件快照）
        /// ② 詢問所有 <see cref="IPresentationEventSource"/> → OR 合併 → **整體覆寫**黑板事件區
        ///
        /// ⚠️ **發布必須在①全部跑完之後**，這是「事件正確性與 Hierarchy 順序無關」的唯一依據：
        /// 若在迭代中途發布，同窗口的 consumer 會依 <c>GetComponentsInChildren</c> 的回傳順序
        /// （＝Hierarchy 誰在上面）決定讀不讀得到——把拖動物件變成正確性條件。
        /// 拆成兩步之後，每個事件**恰好被每個 consumer 看到一次**，代價是固定一帧延遲（約 16ms，聽不出來）。
        /// </summary>
        public void Tick(PlayerRuntimeData data)
        {
            // ① 驅動
            for (int i = 0; i < _controllers.Length; i++)
            {
                _controllers[i].Tick(data);
            }

            // ② 收集 → 合併 → 發布
            // ⚠️ 每帧從 default 重算，**不以黑板現值為起點**——整體覆寫即是事件的復位機制，
            //    因此 ResetTransientState()（順序 7）刻意不認識本區，不需要任何例外。
            PresentationEventData events = default;

            for (int i = 0; i < _eventSources.Length; i++)
            {
                PresentationEventData reported = _eventSources[i].Evaluate(data);

                // 合併政策：純 OR（同 ArbiterPipeline）。來源只回報自己，看不見也改不掉別人的結果。
                events.LeftFootPlanted |= reported.LeftFootPlanted;
                events.RightFootPlanted |= reported.RightFootPlanted;
            }

            data.PresentationEvents = events;
        }
    }
}
