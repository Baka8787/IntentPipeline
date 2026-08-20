using System.Collections.Generic;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Presentation;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// M2 表現層驅動骨架的確定性 EditMode 單元測試（純 C#，不需場景／MonoBehaviour）。
    /// 驗證 docs/02-dev-spec.md §3.4.1 的契約：依註冊順序驅動所有 Controller、
    /// 傳遞同一個黑板實例（非拷貝）、null／空集合安全。
    /// </summary>
    public class PresentationPipelineTests
    {
        /// <summary>記錄 Tick 呼叫的測試替身：兼驗證收到的黑板實例與跨 Controller 的呼叫順序。</summary>
        private sealed class RecordingController : IPresentationController
        {
            private readonly List<RecordingController> _callOrder;
            public PlayerRuntimeData LastData { get; private set; }
            public int TickCount { get; private set; }

            public RecordingController(List<RecordingController> callOrder) => _callOrder = callOrder;

            public void Tick(PlayerRuntimeData data)
            {
                TickCount++;
                LastData = data;
                _callOrder.Add(this);
            }
        }

        [Test]
        public void Tick_DrivesAllControllersInRegistrationOrder_WithSameBlackboardInstance()
        {
            var order = new List<RecordingController>();
            var a = new RecordingController(order);
            var b = new RecordingController(order);
            var data = new PlayerRuntimeData();
            var pipeline = new PresentationPipeline(new IPresentationController[] { a, b }, null);

            pipeline.Tick(data);

            Assert.AreEqual(1, a.TickCount, "每個 Controller 每次管線 Tick 應恰好被驅動一次");
            Assert.AreEqual(1, b.TickCount, "每個 Controller 每次管線 Tick 應恰好被驅動一次");
            CollectionAssert.AreEqual(new[] { a, b }, order, "驅動順序必須等於註冊（收集）順序");
            Assert.AreSame(data, a.LastData, "Controller 必須收到同一個黑板實例（非拷貝），單幀事件才讀得到");
            Assert.AreSame(data, b.LastData, "Controller 必須收到同一個黑板實例（非拷貝），單幀事件才讀得到");
        }

        [Test]
        public void Tick_WithNullControllerArray_IsSafe()
        {
            var pipeline = new PresentationPipeline(null, null);
            Assert.DoesNotThrow(() => pipeline.Tick(new PlayerRuntimeData()),
                "null 控制器／事件來源陣列應被建構子正規化為空管線，Tick 不得拋例外");
        }

        [Test]
        public void Tick_WithEmptyControllerArray_IsSafe()
        {
            var pipeline = new PresentationPipeline(new IPresentationController[0], new IPresentationEventSource[0]);
            Assert.DoesNotThrow(() => pipeline.Tick(new PlayerRuntimeData()),
                "角色身上一個 Controller 都沒掛（如測試場景）是合法情境，Tick 必須安全");
        }
    }
}
