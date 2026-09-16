using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Project.Core.Actions;
using Project.Core.Pipeline;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// `CharacterPipelineRunner.ResolveActionLifecycleSinks` 的組裝期行為。
    ///
    /// <para><b>為什麼這段值得獨立的測試</b></para>
    /// 2026-09-14（使用者裁決）把 `IActionLifecycleSink` 由 **per-slot 單顆**改為 **per-slot 多顆**。
    /// 新的解析是兩趟掃描（先驗證計數、再配置剛好大小的陣列並依序填入）＋每個 slot 一個寫入游標——
    /// 那正是 off-by-one 最愛藏身的形狀，而失敗模式**完全靜默**：
    /// 某一顆 sink 收不到通知，畫面上只是「特效有時候沒出來」。
    ///
    /// <para><b>只測解析，不測派送</b></para>
    /// 「多顆 sink 都會收到通知、且順序穩定」由 `ActionStateTests.T26` 守；
    /// 「資產上的接線合不合法」由 `PrefabWiringTests.W3` 守。本檔只回答
    /// 「Inspector 清單 → 執行期派送表」這一步對不對。
    /// </summary>
    public sealed class ActionSinkResolutionTests
    {
        private readonly List<UnityEngine.Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        /// <summary>測試用的最小 sink。`ActionSinkBinding.Sink` 的型別是 `MonoBehaviour`，因此必須是元件。</summary>
        private sealed class ProbeSink : MonoBehaviour, IActionLifecycleSink
        {
            public void Begin() { }
            public void Release(in ActionReleaseContext context) { }
            public void Cleanup() { }
        }

        /// <summary>綁上去卻沒有實作介面——接線手滑的第二種形狀。</summary>
        private sealed class NotASink : MonoBehaviour
        {
        }

        [Test]
        public void OneSinkPerSlot_StillResolvesExactlyAsBefore()
        {
            CharacterPipelineRunner runner = CreateRunner(out GameObject host);
            ProbeSink slot1 = host.AddComponent<ProbeSink>();
            ProbeSink slot2 = host.AddComponent<ProbeSink>();

            Resolve(runner, Binding(ActionSlot.Slot1, slot1), Binding(ActionSlot.Slot2, slot2));

            IActionLifecycleSink[][] map = SinkMap(runner);
            CollectionAssert.AreEqual(new IActionLifecycleSink[] { slot1 }, map[(int)ActionSlot.Slot1]);
            CollectionAssert.AreEqual(new IActionLifecycleSink[] { slot2 }, map[(int)ActionSlot.Slot2]);
            Assert.IsEmpty(map[(int)ActionSlot.Slot3], "沒綁的 slot 必須是空陣列");
        }

        /// <summary>
        /// 本輪改動的核心：同一個 slot 綁多顆**不同的** sink 全部保留，且**順序 ＝ 清單順序**。
        /// 舊版會把第二顆當成錯誤丟掉。
        /// </summary>
        [Test]
        public void MultipleDifferentSinksOnOneSlot_AreAllKept_InBindingOrder()
        {
            CharacterPipelineRunner runner = CreateRunner(out GameObject host);
            ProbeSink hitbox = host.AddComponent<ProbeSink>();
            ProbeSink weaponVisibility = host.AddComponent<ProbeSink>();
            ProbeSink trail = host.AddComponent<ProbeSink>();

            Resolve(
                runner,
                Binding(ActionSlot.Slot1, hitbox),
                Binding(ActionSlot.Slot1, weaponVisibility),
                Binding(ActionSlot.Slot1, trail));

            CollectionAssert.AreEqual(
                new IActionLifecycleSink[] { hitbox, weaponVisibility, trail },
                SinkMap(runner)[(int)ActionSlot.Slot1],
                "同一個 slot 的多顆 sink 必須全部保留，且維持 Inspector 上的順序");
        }

        /// <summary>不同 slot 之間的填入互不干擾——兩趟掃描的寫入游標最容易在這裡串位。</summary>
        [Test]
        public void SinksInterleavedAcrossSlots_LandInTheRightSlot()
        {
            CharacterPipelineRunner runner = CreateRunner(out GameObject host);
            ProbeSink a1 = host.AddComponent<ProbeSink>();
            ProbeSink b1 = host.AddComponent<ProbeSink>();
            ProbeSink a2 = host.AddComponent<ProbeSink>();
            ProbeSink b2 = host.AddComponent<ProbeSink>();

            // 刻意交錯：slot1、slot2、slot1、slot2
            Resolve(
                runner,
                Binding(ActionSlot.Slot1, a1),
                Binding(ActionSlot.Slot2, a2),
                Binding(ActionSlot.Slot1, b1),
                Binding(ActionSlot.Slot2, b2));

            IActionLifecycleSink[][] map = SinkMap(runner);
            CollectionAssert.AreEqual(new IActionLifecycleSink[] { a1, b1 }, map[(int)ActionSlot.Slot1]);
            CollectionAssert.AreEqual(new IActionLifecycleSink[] { a2, b2 }, map[(int)ActionSlot.Slot2]);
        }

        /// <summary>
        /// **同一顆 sink 不得在同一個 slot 註冊兩次。**
        /// 這是使用者在放寬「一 slot 一 sink」時明確保留的限制：它必定是接線手滑，
        /// 而症狀（命中判定跑兩次 ⇒ 傷害變兩倍）幾乎不可能被聯想到接線。
        /// </summary>
        [Test]
        public void SameSinkTwiceOnSameSlot_IsRejected()
        {
            CharacterPipelineRunner runner = CreateRunner(out GameObject host);
            ProbeSink sink = host.AddComponent<ProbeSink>();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("重複註冊了同一顆 sink"));
            Resolve(runner, Binding(ActionSlot.Slot1, sink), Binding(ActionSlot.Slot1, sink));

            CollectionAssert.AreEqual(
                new IActionLifecycleSink[] { sink },
                SinkMap(runner)[(int)ActionSlot.Slot1],
                "重複的那一列必須被丟掉，只留一份");
        }

        /// <summary>同一顆 sink 綁到**不同** slot 是合法的（例如共用的音效 sink）。</summary>
        [Test]
        public void SameSinkOnDifferentSlots_IsAllowed()
        {
            CharacterPipelineRunner runner = CreateRunner(out GameObject host);
            ProbeSink shared = host.AddComponent<ProbeSink>();

            Resolve(runner, Binding(ActionSlot.Slot1, shared), Binding(ActionSlot.Slot2, shared));

            IActionLifecycleSink[][] map = SinkMap(runner);
            CollectionAssert.AreEqual(new IActionLifecycleSink[] { shared }, map[(int)ActionSlot.Slot1]);
            CollectionAssert.AreEqual(new IActionLifecycleSink[] { shared }, map[(int)ActionSlot.Slot2]);
        }

        /// <summary>無效的列被丟掉之後，**剩下的列仍要填在正確位置**——不能留下一格 null 或整排位移。</summary>
        [Test]
        public void InvalidBindings_AreDroppedWithoutShiftingTheValidOnes()
        {
            CharacterPipelineRunner runner = CreateRunner(out GameObject host);
            NotASink notASink = host.AddComponent<NotASink>();
            ProbeSink valid = host.AddComponent<ProbeSink>();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Slot 無效"));
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("沒有實作 IActionLifecycleSink"));

            Resolve(
                runner,
                Binding(ActionSlot.None, valid),        // slot 無效
                Binding(ActionSlot.Slot1, notASink),    // 沒實作介面
                Binding(ActionSlot.Slot1, valid));

            IActionLifecycleSink[][] map = SinkMap(runner);
            CollectionAssert.AreEqual(
                new IActionLifecycleSink[] { valid },
                map[(int)ActionSlot.Slot1],
                "只有合法的那一列留下，而且不得留下 null 洞");
            Assert.IsEmpty(map[(int)ActionSlot.None], "Slot.None 永遠不得被填入任何東西");
        }

        /// <summary>
        /// **Legacy 相容**：清單留空時，單顆 sink 仍然套用到所有 slot。
        /// 既有 prefab 不必為了這次改動重新接線。
        /// </summary>
        [Test]
        public void EmptyBindingList_FallsBackToLegacySingleSinkForEverySlot()
        {
            CharacterPipelineRunner runner = CreateRunner(out GameObject host);
            ProbeSink legacy = host.AddComponent<ProbeSink>();
            SetPrivateField(runner, "actionReleaseSinkComponent", legacy);

            Resolve(runner);   // 不給任何 binding

            IActionLifecycleSink[][] map = SinkMap(runner);
            Assert.IsEmpty(map[(int)ActionSlot.None], "Slot.None 不得取得 legacy sink");
            for (int slot = 1; slot < map.Length; slot++)
            {
                CollectionAssert.AreEqual(
                    new IActionLifecycleSink[] { legacy }, map[slot],
                    $"legacy 單顆 sink 的語意是所有 Action 共用（slot {slot}）");
            }
        }

        // =====================================================================
        // 治具
        // =====================================================================

        private CharacterPipelineRunner CreateRunner(out GameObject host)
        {
            host = new GameObject("Action-Sink-Resolution-Host");
            _created.Add(host);
            // RequireComponent 會自動補上 CharacterFacingSource／MotionDriver；
            // 本檔只呼叫組裝期的解析方法，不跑管線，因此不需要其餘接線。
            return host.AddComponent<CharacterPipelineRunner>();
        }

        private static ActionSinkBinding Binding(ActionSlot slot, MonoBehaviour sink)
            => new ActionSinkBinding { Slot = slot, Sink = sink };

        /// <summary>寫入 Inspector 清單後呼叫組裝期解析。production 走 `Awake`，時機與結果不變。</summary>
        private static void Resolve(CharacterPipelineRunner runner, params ActionSinkBinding[] bindings)
        {
            SetPrivateField(runner, "actionSinkBindings", new List<ActionSinkBinding>(bindings));
            MethodInfo method = typeof(CharacterPipelineRunner).GetMethod(
                "ResolveActionLifecycleSinks", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "找不到 ResolveActionLifecycleSinks（方法名稱可能已變更）");
            method.Invoke(runner, Array.Empty<object>());
        }

        private static IActionLifecycleSink[][] SinkMap(CharacterPipelineRunner runner)
        {
            FieldInfo field = typeof(CharacterPipelineRunner).GetField(
                "_actionLifecycleSinks", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "找不到 _actionLifecycleSinks（欄位名稱可能已變更）");
            var map = (IActionLifecycleSink[][])field.GetValue(runner);
            Assert.IsNotNull(map, "解析後派送表不得為 null");
            for (int i = 0; i < map.Length; i++)
                Assert.IsNotNull(map[i], $"slot {i} 的 sink 清單不得為 null——空 slot 應為 Array.Empty");
            return map;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }
    }
}
