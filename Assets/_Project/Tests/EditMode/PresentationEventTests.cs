using System.Collections.Generic;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Presentation;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// M3.x-B 表現層事件發布的確定性 EditMode 測試（純 C#，不需場景／MonoBehaviour）。
    ///
    /// 守的是三組不變量：
    /// * **契約**——`IPresentationController` 對黑板只讀不寫（由 §7-A5 靜態守），事件來源回傳 value struct
    /// * **發布**——`PresentationPipeline` 是唯一寫入者、發布在所有 Controller Tick **之後**、
    ///   結果與 Hierarchy／收集順序無關、廣播快照不由 consumer 清除
    /// * **生命週期**——第 N 帧發布、第 N+1 帧可讀，且 `ResetTransientState()` 不會提前清掉它
    /// </summary>
    public class PresentationEventTests
    {
        /// <summary>固定回報同一份事件的測試替身；兼記錄收到的黑板實例與呼叫次數。</summary>
        private sealed class StubEventSource : IPresentationEventSource
        {
            private PresentationEventData _report;
            public PlayerRuntimeData LastData { get; private set; }
            public int EvaluateCount { get; private set; }

            public StubEventSource(PresentationEventData report) => _report = report;

            /// <summary>讓測試能在兩次 Tick 之間改變回報內容（模擬「這一帧沒有腳步」）。</summary>
            public void SetReport(PresentationEventData report) => _report = report;

            public PresentationEventData Evaluate(PlayerRuntimeData data)
            {
                EvaluateCount++;
                LastData = data;
                return _report;
            }
        }

        /// <summary>每次 Tick 記下當下讀到的事件快照——用來驗證「Controller 讀到的是上一帧的值」。</summary>
        private sealed class SnapshotRecordingController : IPresentationController
        {
            private readonly List<SnapshotRecordingController> _callOrder;
            public readonly List<PresentationEventData> Seen = new();

            public SnapshotRecordingController(List<SnapshotRecordingController> callOrder = null) => _callOrder = callOrder;

            public void Tick(PlayerRuntimeData data)
            {
                Seen.Add(data.PresentationEvents);
                _callOrder?.Add(this);
            }
        }

        private static PresentationEventData Left => new PresentationEventData { LeftFootPlanted = true };
        private static PresentationEventData Right => new PresentationEventData { RightFootPlanted = true };

        // =====================================================================
        // 發布時序：必須在所有 Controller Tick 完成之後
        // =====================================================================

        [Test]
        public void Tick_PublishesEvents_OnlyAfterAllControllersHaveTicked()
        {
            // 🎯 這條是「事件正確性與 Hierarchy 順序無關」的**根據**。
            // 若發布混在迭代中途，Controller 讀不讀得到就取決於它在階層裡的位置。
            var controller = new SnapshotRecordingController();
            var source = new StubEventSource(Left);
            var data = new PlayerRuntimeData();
            var pipeline = new PresentationPipeline(
                new IPresentationController[] { controller }, new IPresentationEventSource[] { source });

            pipeline.Tick(data);

            Assert.IsFalse(controller.Seen[0].LeftFootPlanted,
                "本帧發布的事件不得在同一次 Tick 內被 Controller 讀到——否則發布時機混進了迭代");
            Assert.IsTrue(data.PresentationEvents.LeftFootPlanted,
                "Tick 結束後，事件必須已經發布到黑板");
            Assert.AreEqual(1, source.EvaluateCount, "每個來源每次 Tick 應恰好被詢問一次");
            Assert.AreSame(data, source.LastData, "來源必須收到同一個黑板實例（非拷貝）");
        }

        [Test]
        public void PublishedEvent_IsReadableByControllers_OnTheNextTick()
        {
            var controller = new SnapshotRecordingController();
            var source = new StubEventSource(Left);
            var data = new PlayerRuntimeData();
            var pipeline = new PresentationPipeline(
                new IPresentationController[] { controller }, new IPresentationEventSource[] { source });

            pipeline.Tick(data);                       // 第 N 帧：發布
            source.SetReport(default);                 // 第 N+1 帧：來源已無事件
            pipeline.Tick(data);                       // 第 N+1 帧：Controller 應讀到第 N 帧的值

            Assert.IsFalse(controller.Seen[0].LeftFootPlanted, "第 N 帧讀到的是更早之前的（空）快照");
            Assert.IsTrue(controller.Seen[1].LeftFootPlanted, "第 N+1 帧必須讀得到第 N 帧發布的事件");
        }

        [Test]
        public void EventCorrectness_IsIndependentOfControllerCollectionOrder()
        {
            // 🎯 收集順序來自 GetComponentsInChildren（＝Hierarchy 誰在上面）。
            // 拖動物件不得改變任何 Controller 看到的事件內容。
            var a1 = new SnapshotRecordingController();
            var b1 = new SnapshotRecordingController();
            var data1 = new PlayerRuntimeData();
            var p1 = new PresentationPipeline(new IPresentationController[] { a1, b1 },
                                              new IPresentationEventSource[] { new StubEventSource(Left) });

            var a2 = new SnapshotRecordingController();
            var b2 = new SnapshotRecordingController();
            var data2 = new PlayerRuntimeData();
            var p2 = new PresentationPipeline(new IPresentationController[] { b2, a2 }, // 順序顛倒
                                              new IPresentationEventSource[] { new StubEventSource(Left) });

            p1.Tick(data1); p1.Tick(data1);
            p2.Tick(data2); p2.Tick(data2);

            Assert.IsTrue(a1.Seen[1].LeftFootPlanted);
            Assert.IsTrue(b1.Seen[1].LeftFootPlanted);
            Assert.IsTrue(a2.Seen[1].LeftFootPlanted, "顛倒收集順序後，事件內容必須完全相同");
            Assert.IsTrue(b2.Seen[1].LeftFootPlanted, "顛倒收集順序後，事件內容必須完全相同");
        }

        // =====================================================================
        // 廣播快照語意：多 consumer 不互吃、不由 consumer 清除
        // =====================================================================

        [Test]
        public void Event_IsBroadcast_NotConsumedByTheFirstReader()
        {
            // 🎯 若做成可消費佇列，第一個 consumer 會吃掉事件，未來的 VFX／鏡頭震動就收不到。
            var first = new SnapshotRecordingController();
            var second = new SnapshotRecordingController();
            var data = new PlayerRuntimeData();
            var pipeline = new PresentationPipeline(
                new IPresentationController[] { first, second },
                new IPresentationEventSource[] { new StubEventSource(Right) });

            pipeline.Tick(data);
            pipeline.Tick(data);

            Assert.IsTrue(first.Seen[1].RightFootPlanted, "第一個 consumer 應讀到事件");
            Assert.IsTrue(second.Seen[1].RightFootPlanted,
                "第二個 consumer 必須讀到**同一份**事件——快照是廣播，不是佇列");
        }

        [Test]
        public void Tick_MergesEventsFromAllSources_WithOr()
        {
            var data = new PlayerRuntimeData();
            var pipeline = new PresentationPipeline(null, new IPresentationEventSource[]
            {
                new StubEventSource(Left),
                new StubEventSource(Right),
            });

            pipeline.Tick(data);

            Assert.IsTrue(data.PresentationEvents.LeftFootPlanted, "任一來源回報即應發布");
            Assert.IsTrue(data.PresentationEvents.RightFootPlanted, "任一來源回報即應發布");
        }

        [Test]
        public void Tick_SourceReportingNothing_CannotClearAnotherSourcesEvent()
        {
            var data = new PlayerRuntimeData();
            var reporter = new StubEventSource(Left);
            var silent = new StubEventSource(default);

            new PresentationPipeline(null, new IPresentationEventSource[] { reporter, silent }).Tick(data);
            Assert.IsTrue(data.PresentationEvents.LeftFootPlanted, "後續來源『不回報』不得抹掉既有事件（純 OR）");

            var reordered = new PlayerRuntimeData();
            new PresentationPipeline(null, new IPresentationEventSource[] { silent, reporter }).Tick(reordered);
            Assert.IsTrue(reordered.PresentationEvents.LeftFootPlanted, "OR 合併的結果必須與來源順序無關");
        }

        // =====================================================================
        // 生命週期：整體覆寫即復位；ResetTransientState() 不得插手
        // =====================================================================

        [Test]
        public void Tick_RecomputesEventsEachFrame_DoesNotAccumulate()
        {
            var data = new PlayerRuntimeData();
            var source = new StubEventSource(Left);
            var pipeline = new PresentationPipeline(null, new IPresentationEventSource[] { source });

            pipeline.Tick(data);
            Assert.IsTrue(data.PresentationEvents.LeftFootPlanted, "前置條件：事件已發布");

            source.SetReport(default);
            pipeline.Tick(data);

            Assert.IsFalse(data.PresentationEvents.LeftFootPlanted,
                "沒有來源回報時必須回到全 false——整體覆寫就是事件的復位機制，不需要任何人去清它");
        }

        [Test]
        public void ResetTransientState_DoesNotClearPresentationEvents()
        {
            // 🎯 事件的生命週期橫跨順序 7，必須活到下一帧的 6.5 才會被消費。
            // 若哪天有人「順手」把它加進統一復位，事件會在被讀到前就死掉——這條會紅。
            var data = new PlayerRuntimeData
            {
                PresentationEvents = new PresentationEventData { LeftFootPlanted = true, RightFootPlanted = true },
                JustLanded = true,
                JustLeftGround = true,
            };
            data.Intent.JumpRequested = true;

            data.ResetTransientState();

            Assert.IsFalse(data.JustLanded, "前置條件：單幀事件確實被順序 7 復位了");
            Assert.IsFalse(data.Intent.JumpRequested, "前置條件：trigger 意圖確實被順序 7 復位了");
            Assert.IsTrue(data.PresentationEvents.LeftFootPlanted,
                "表現層事件**不得**由 ResetTransientState 清除——它要活到下一帧的 6.5 才被消費");
            Assert.IsTrue(data.PresentationEvents.RightFootPlanted, "同上");
        }
    }
}
