using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.App;
using Project.Core.Arbitration.Sources;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 輪 4.2 `Cursor` 唯一擁有者的合併邏輯測試。
    ///
    /// ⚠️ **刻意只測 <see cref="CursorModeController.WantsFreeCursor"/>，不碰 <c>Cursor</c> 本身**：
    /// <c>Cursor.lockState</c> 是全域且與編輯器視窗焦點互動的狀態，在 EditMode 斷言它既不穩定、
    /// 又會污染測試回合（同 <c>GamePauseControllerTests</c> 對 <c>Time.timeScale</c> 的顧慮，
    /// 但游標連「還原」都不可靠）。要驗的是**合併政策**，那正是 <c>WantsFreeCursor</c>。
    /// 實際套用到 <c>Cursor</c> 的行為屬人工驗收（dev-spec §7.2-M9）。
    /// </summary>
    public class CursorModeControllerTests
    {
        private readonly List<Object> _created = new();
        private float _originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            // ⚠️ 本檔會透過 GamePauseController 間接改動全域的 Time.timeScale。
            //    不還原的話，後續**所有**測試都會在 timeScale = 0 下執行。
            //    同 GamePauseControllerTests 的紀律：SetUp 記錄、TearDown 無條件還原。
            _originalTimeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();

            Time.timeScale = _originalTimeScale;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }

        private T Create<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go.AddComponent<T>();
        }

        /// <summary>建立已接好兩個來源的控制器，並回傳兩個來源供測試操作。</summary>
        private CursorModeController CreateWired(out UiModeArbiterSource uiMode, out GamePauseController pause)
        {
            uiMode = Create<UiModeArbiterSource>("UiModeSource");
            pause = Create<GamePauseController>("PauseController");

            CursorModeController controller = Create<CursorModeController>("CursorModeController");
            SetPrivateField(controller, "uiModeSource", uiMode);
            SetPrivateField(controller, "pauseController", pause);
            return controller;
        }

        private static void SetUiModeActive(UiModeArbiterSource source, bool active)
        {
            SetPrivateField(source, "_uiMode", active);
        }

        [Test]
        public void WantsFreeCursor_IsFalse_WhenNoSourceRequests()
        {
            CursorModeController controller = CreateWired(out _, out _);

            Assert.IsFalse(controller.WantsFreeCursor,
                "沒有任何來源要求自由游標時，游標應被鎖住（＝一般遊玩狀態）");
        }

        [Test]
        public void WantsFreeCursor_IsFalse_WhenSourcesAreUnassigned()
        {
            // 未接線是合法情境（例如測試場景只放了控制器）。不得丟例外，也不得誤判為要求自由游標。
            CursorModeController controller = Create<CursorModeController>("CursorModeControllerAlone");

            Assert.IsFalse(controller.WantsFreeCursor,
                "來源留空應視為「該來源不要求自由游標」，而非 NullReference 或誤判");
        }

        [Test]
        public void WantsFreeCursor_IsTrue_WhilePaused()
        {
            CursorModeController controller = CreateWired(out _, out GamePauseController pause);

            pause.SetPaused(true);

            Assert.IsTrue(controller.WantsFreeCursor, "暫停時游標必須自由（本輪需求：暫停時游標常駐）");
        }

        [Test]
        public void WantsFreeCursor_IsTrue_WhileUiModeActive()
        {
            CursorModeController controller = CreateWired(out UiModeArbiterSource uiMode, out _);

            SetUiModeActive(uiMode, true);

            Assert.IsTrue(controller.WantsFreeCursor, "UI 模式（按住 Alt）期間游標必須自由");
        }

        [Test]
        public void WantsFreeCursor_StaysTrue_WhenUiModeReleasesButPauseIsStillActive()
        {
            // 🎯 **這條是本輪回報的 bug 的回歸測試**。
            // 舊架構下 UiModeArbiterSource 自己寫 Cursor：暫停中按住 Alt 進 UI 模式、再放開，
            // 它的 ApplyCursor(false) 會把游標鎖回去——即使暫停還開著。
            // 改為「單一擁有者 OR 合併所有來源」之後，這在結構上不可能發生。
            CursorModeController controller = CreateWired(out UiModeArbiterSource uiMode, out GamePauseController pause);

            pause.SetPaused(true);
            SetUiModeActive(uiMode, true);
            Assert.IsTrue(controller.WantsFreeCursor, "前置條件：兩個來源同時要求自由游標");

            SetUiModeActive(uiMode, false); // 放開 Alt

            Assert.IsTrue(controller.WantsFreeCursor,
                "其中一個來源收手時，另一個仍在要求的封鎖不得被解除——游標必須留在自由狀態");
        }

        [Test]
        public void WantsFreeCursor_ReturnsFalse_OnlyAfterEverySourceReleases()
        {
            CursorModeController controller = CreateWired(out UiModeArbiterSource uiMode, out GamePauseController pause);

            pause.SetPaused(true);
            SetUiModeActive(uiMode, true);

            SetUiModeActive(uiMode, false);
            Assert.IsTrue(controller.WantsFreeCursor, "還有一個來源在要求");

            pause.SetPaused(false);
            Assert.IsFalse(controller.WantsFreeCursor, "所有來源都收手後才回到鎖定游標");
        }
    }
}
