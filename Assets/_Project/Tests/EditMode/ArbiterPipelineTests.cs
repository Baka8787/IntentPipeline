using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Arbitration;
using Project.Core.Blackboard;
using Project.Core.Movement;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 輪 4 ArbiterPipeline（管線順序 4.5）的確定性 EditMode 單元測試（純 C#，不需場景／輸入裝置）。
    /// 驗證 docs/02-dev-spec.md §1.4／§2.1 的契約：
    /// 來源只回報自己的請求、管線負責 OR 合併、每帧從全 false 重算、黑板仲裁區單一寫入者。
    ///
    /// ⚠️ **本檔不測 <c>UiModeArbiterSource</c> 的按鍵路徑**：邊沿訊號需要真實 Input System 更新迴圈，
    /// EditMode 無法確定性重現。Alt 的實際行為屬人工驗收項（dev-spec §7.2-M7）。
    /// 這裡改測它所依賴的**機制**——「零輸入 ⇒ 意圖歸零、型態保留」（見最後一段）。
    /// </summary>
    public class ArbiterPipelineTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        /// <summary>固定回傳同一份請求的測試替身；兼記錄收到的黑板實例與呼叫次數。</summary>
        private sealed class StubSource : IArbiterSource
        {
            private readonly ArbiterData _request;
            public PlayerRuntimeData LastData { get; private set; }
            public int EvaluateCount { get; private set; }

            public StubSource(ArbiterData request) => _request = request;

            public ArbiterData Evaluate(PlayerRuntimeData data)
            {
                EvaluateCount++;
                LastData = data;
                return _request;
            }
        }

        // =====================================================================
        // 合併政策：純 OR，無優先級／強制解封（輪 4 裁決）
        // =====================================================================

        [Test]
        public void Tick_MergesFlagsFromAllSources_WithOr()
        {
            var inputBlocker = new StubSource(new ArbiterData { BlockInput = true });
            var ikBlocker = new StubSource(new ArbiterData { BlockIK = true, BlockAudio = true });
            var data = new PlayerRuntimeData();
            var pipeline = new ArbiterPipeline(new IArbiterSource[] { inputBlocker, ikBlocker });

            pipeline.Tick(data);

            Assert.IsTrue(data.Arbitration.BlockInput, "任一來源要求封鎖輸入即應封鎖");
            Assert.IsTrue(data.Arbitration.BlockIK, "任一來源要求封鎖 IK 即應封鎖");
            Assert.IsTrue(data.Arbitration.BlockAudio, "任一來源要求封鎖音頻即應封鎖");
            Assert.IsFalse(data.Arbitration.BlockExpression, "沒有來源要求的旗標必須維持 false");
            Assert.AreEqual(1, inputBlocker.EvaluateCount, "每個來源每次管線 Tick 應恰好被詢問一次");
            Assert.AreEqual(1, ikBlocker.EvaluateCount, "每個來源每次管線 Tick 應恰好被詢問一次");
            Assert.AreSame(data, inputBlocker.LastData, "來源必須收到同一個黑板實例（非拷貝）");
        }

        [Test]
        public void Tick_SourceReturningNoRequest_CannotClearAnotherSourcesBlock()
        {
            // 這條守的是「回傳值而非 ref」的結構性保證：後面的來源不可能清掉前面來源抬起的旗標。
            // 若哪天有人把介面改回 ref ArbiterData 並在來源內寫 flags.BlockInput = false，此測試立刻變紅。
            var blocker = new StubSource(new ArbiterData { BlockInput = true });
            var abstainer = new StubSource(default);
            var data = new PlayerRuntimeData();

            new ArbiterPipeline(new IArbiterSource[] { blocker, abstainer }).Tick(data);
            Assert.IsTrue(data.Arbitration.BlockInput, "後續來源『不請求』不得解除既有封鎖（OR 合併，無強制解封）");

            var reordered = new PlayerRuntimeData();
            new ArbiterPipeline(new IArbiterSource[] { abstainer, blocker }).Tick(reordered);
            Assert.IsTrue(reordered.Arbitration.BlockInput, "OR 合併的結果必須與來源順序無關");
        }

        // =====================================================================
        // 生命週期：每帧重算，不累積
        // =====================================================================

        [Test]
        public void Tick_RecomputesFromScratch_DoesNotAccumulatePreviousFrame()
        {
            var data = new PlayerRuntimeData
            {
                // 模擬上一帧留下的封鎖狀態（實務上由前一次 Tick 寫入）
                Arbitration = new ArbiterData
                {
                    BlockInput = true, BlockIK = true, BlockAudio = true, BlockExpression = true
                }
            };
            var pipeline = new ArbiterPipeline(new IArbiterSource[] { new StubSource(default) });

            pipeline.Tick(data);

            Assert.IsFalse(data.Arbitration.BlockInput, "封鎖是『本幀有沒有人在要求』，不是累積狀態——沒人要求就必須解除");
            Assert.IsFalse(data.Arbitration.BlockIK);
            Assert.IsFalse(data.Arbitration.BlockAudio);
            Assert.IsFalse(data.Arbitration.BlockExpression);
        }

        [Test]
        public void Tick_WithNullSourceArray_IsSafe_AndClearsFlags()
        {
            var data = new PlayerRuntimeData { Arbitration = new ArbiterData { BlockInput = true } };
            var pipeline = new ArbiterPipeline(null);

            Assert.DoesNotThrow(() => pipeline.Tick(data),
                "null 來源陣列應被建構子正規化為空管線，Tick 不得拋例外");
            Assert.IsFalse(data.Arbitration.BlockInput,
                "沒有任何來源＝沒有人要求封鎖，旗標必須全 false（＝接上仲裁前的行為）");
        }

        [Test]
        public void Tick_WithEmptySourceArray_IsSafe()
        {
            var pipeline = new ArbiterPipeline(new IArbiterSource[0]);
            Assert.DoesNotThrow(() => pipeline.Tick(new PlayerRuntimeData()),
                "角色身上一顆仲裁來源都沒掛是合法情境（如測試場景），Tick 必須安全");
        }

        // =====================================================================
        // BlockInput 閘門的機制：零輸入 ⇒ 意圖歸零、型態保留（§7-M5 裁決的機器化）
        // =====================================================================

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }

        private PlayerLocomotionPolicy CreateToggleModePolicy()
        {
            var profile = ScriptableObject.CreateInstance<GaitProfileSO>();
            _created.Add(profile);
            SetPrivateField(profile, "defaultIntensity", 0.75f);
            SetPrivateField(profile, "sprintIntensity", 1f);
            SetPrivateField(profile, "walkIntensity", 0.3651f);
            SetPrivateField(profile, "respectAnalogMagnitude", true);
            SetPrivateField(profile, "walkIsToggle", true);

            var go = new GameObject("PolicyUnderTest");
            _created.Add(go);
            var policy = go.AddComponent<PlayerLocomotionPolicy>();
            SetPrivateField(policy, "gaitProfile", profile);
            return policy;
        }

        [Test]
        public void BlockedFrame_ZeroedInput_ZeroesMovementIntent_SoSmoothingCanDecelerate()
        {
            // Runner 順序 2 的閘門在封鎖時把 InputData 整份歸零，再照常跑順序 2／2.5。
            // 本測試守的是那個機制的下半段：零輸入餵進 producer 後，意圖必須真的歸零——
            // 唯有如此 LocomotionModel 的 B9 減速才會啟動（收斂到 0 由
            // MovementIntentTests.Smoother_SnapsToExactZero_WhenIntentReleased 守）。
            PlayerLocomotionPolicy policy = CreateToggleModePolicy();
            var data = new PlayerRuntimeData();

            InputData running = default;
            running.MoveInput = Vector2.up;
            policy.ProduceIntent(ref running, data);
            Assert.Greater(data.MovementIntent.DesiredSpeedNormalized, 0f, "前置條件：封鎖前角色正在移動");

            InputData blocked = default; // ＝ Runner 封鎖幀餵給管線的輸入
            policy.ProduceIntent(ref blocked, data);

            Assert.AreEqual(0f, data.MovementIntent.DesiredSpeedNormalized, 1e-6f,
                "封鎖幀的意圖必須歸零（而非凍結在最後一幀），否則角色會以最後速度無限前進");
            Assert.AreEqual(Vector2.zero, data.MovementIntent.DesiredDirection,
                "方向同為純意圖，封鎖時一併歸零；滑行期的方向保留屬 model dynamics");
        }

        [Test]
        public void BlockedFrame_ZeroedInput_DoesNotFlipWalkToggleMode()
        {
            // 零輸入的 WalkButtonDown 同為 false ⇒ toggle 的邊沿不會被誤觸發，
            // 持久型態必須原樣保留，封鎖解除後玩家仍在原本的 Walk／Run 型態。
            PlayerLocomotionPolicy policy = CreateToggleModePolicy();
            var data = new PlayerRuntimeData();

            InputData pressWalk = default;
            pressWalk.MoveInput = Vector2.up;
            pressWalk.WalkButtonDown = true;
            policy.ProduceIntent(ref pressWalk, data);
            Assert.IsTrue(data.MovementIntent.WalkModeActive, "前置條件：Walk 型態已被切開");

            InputData blocked = default;
            policy.ProduceIntent(ref blocked, data);
            policy.ProduceIntent(ref blocked, data);

            Assert.IsTrue(data.MovementIntent.WalkModeActive,
                "封鎖期間不得誤翻 Walk 型態——BlockInput 是『看不到輸入』，不是『按了一下 Ctrl』");
        }
    }
}
