using Project.Core.Environment;
using Project.Presentation.IK;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.App
{
    /// <summary>
    /// 執行期的 debug 可視化總開關（2026-09-15 使用者裁決）。
    ///
    /// <para><b>⭐ 它是遙控器，不是感測器</b></para>
    /// 本元件**只做一件事：把既有元件上的 <c>bool</c> 翻過來翻過去。**
    /// <list type="bullet">
    /// <item>⛔ **不計算任何 gameplay 資料**——不打 ray、不查幾何、不讀黑板。</item>
    /// <item>⛔ **不持有任何可視化狀態**——旗標的真相仍住在各自的元件上。
    /// 把 Panel 刪掉，那些旗標維持原樣；Panel 只是換一個地方按。</item>
    /// <item>⛔ **不得成為第二個 sensor／authority**（`docs/17` §3.3 紅線 1）。</item>
    /// </list>
    /// ⇒ 這條紀律不是風格要求：`docs/18` §1 的「**記錄，不要重算**」正是為了避免
    /// debug 圖層畫出一個「看起來很權威、但不是真正決定行為的那個東西」。
    ///
    /// <para><b>🔴 為什麼是鍵盤操作，不是用滑鼠點</b></para>
    /// 遊戲進行中 <c>CursorModeController</c> 把游標鎖住並隱藏（它是 `Cursor` API 的唯一擁有者）
    /// ⇒ **IMGUI 的勾選框根本點不到**。第一版用 `GUILayout.Toggle` 是錯的。
    /// ⇒ 改為 **F1 開關面板、F2–F5 切換各頻道**，完全不依賴游標狀態，也不需要進 UI 模式。
    /// 📌 選 F-key 而不是數字鍵，是因為數字鍵與技能 slot 撞鍵——面板不消費輸入，
    /// 按 1 會**同時**切換圖層並放技能。
    ///
    /// <para><b>為什麼直接讀 <c>Keyboard.current</c></b></para>
    /// 各頻道各開一個序列化 `InputAction` 等於五份要接線的資產，而這是**只在 Editor／
    /// Development build 存在的工具**。⛔ 這個藉口僅限本檔，gameplay 輸入一律走 `IInputSource`。
    ///
    /// <para><b>為什麼用 IMGUI 而不是 uGUI</b></para>
    /// 它不是遊戲 UI，是工具。IMGUI 不需要 Canvas／prefab／接線，整個檔案自足；
    /// 而且它**整段被 `UNITY_EDITOR || DEVELOPMENT_BUILD` 包住** ⇒ release build 完全不存在。
    /// ⚠️ IMGUI 每幀配置字串——這是本專案零 GC 紀律的**明確例外**，成立條件有二：
    /// ①只在面板開著時發生；②release build 編不進去。⛔ 不得把這個藉口套用到 gameplay。
    ///
    /// <para><b>掃描時機</b></para>
    /// 開啟面板時掃一次場景（<c>FindObjectsByType</c>），⛔ **不是每幀**。
    /// </summary>
    public sealed class RuntimeDebugPanel : MonoBehaviour
    {
        [Header("Toggle")]
        [Tooltip("開關 debug 面板的按鍵。未綁定 ⇒ 面板永遠不出現，行為與加入本元件前等價。\n" +
                 "面板開啟後，各頻道用 F2–F5 切換（不需要滑鼠——遊戲中游標是鎖住的）。")]
        public InputAction TogglePanelAction;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const float PanelWidth = 430f;
        private const float PanelMargin = 12f;
        private const float RowHeight = 21f;
        private const float PanelPadding = 14f;

        private bool _open;
        private MotionDriver[] _motionDrivers = System.Array.Empty<MotionDriver>();
        private FootIKController[] _footIkControllers = System.Array.Empty<FootIKController>();
        private TraversalProbe[] _traversalProbes = System.Array.Empty<TraversalProbe>();

        // ⚠️ 自己的 GUIStyle 實例。**不得改 `GUI.skin.label`**——那是共用樣式，
        //    改了會污染這一幀之後所有 IMGUI 繪製（第一版就是這樣寫錯的）。
        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;

        private void OnEnable() => TogglePanelAction?.Enable();

        private void OnDisable() => TogglePanelAction?.Disable();

        private void Update()
        {
            if (WasTogglePressed())
            {
                _open = !_open;
                if (_open) RescanScene();
            }

            if (!_open) return;
            ReadChannelHotkeys();
        }

        /// <summary>
        /// 開關鍵：序列化的 <see cref="TogglePanelAction"/>（F1）**或** backquote（`）。
        ///
        /// <para><b>為什麼要有第二個鍵</b></para>
        /// 🔴 **Unity Editor 自己把 F1 綁成 Help**（開啟選取物件的文件），在 Editor 裡按下去
        /// 有機會被編輯器吃掉而不是進到遊戲。backquote 是經典的 debug console 鍵，
        /// 沒有任何 Unity Editor 快捷鍵佔用它。
        ///
        /// ⚠️ 另一個常見的「按了沒反應」原因與按鍵無關：**Input System 只有在 Game view
        /// 有焦點時才收得到鍵盤**。進 Play 後要先點一下 Game view。
        /// </summary>
        private bool WasTogglePressed()
        {
            if (TogglePanelAction != null && TogglePanelAction.WasPerformedThisFrame()) return true;

            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.backquoteKey.wasPressedThisFrame;
        }

        /// <summary>
        /// F2–F5 切換各頻道。**只在面板開著時讀**，關掉面板就完全不碰鍵盤。
        /// ⛔ 這裡只翻 bool，沒有任何 gameplay 語意。
        /// </summary>
        private void ReadChannelHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f2Key.wasPressedThisFrame) ToggleCapsuleOffset();
            if (keyboard.f3Key.wasPressedThisFrame) ToggleFootIk();
            if (keyboard.f4Key.wasPressedThisFrame) ToggleTraversalRuntimeLines();
            if (keyboard.f5Key.wasPressedThisFrame) ToggleTraversalSceneGizmos();
        }

        /// <summary>
        /// 掃描場景上的可視化擁有者。**只在面板開啟時呼叫一次。**
        /// ⚠️ 這是全域查詢，成本不低——但它是 debug 工具的開啟動作，不在熱路徑上。
        /// </summary>
        private void RescanScene()
        {
            _motionDrivers = FindObjectsByType<MotionDriver>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _footIkControllers = FindObjectsByType<FootIKController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _traversalProbes = FindObjectsByType<TraversalProbe>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            ForcePanelTextOff();
        }

        // =====================================================================
        // 切換（每一個都只是「讀第一顆的值 → 反過來 → 套給全部」）
        // =====================================================================

        private void ToggleCapsuleOffset()
        {
            if (_motionDrivers.Length == 0) return;
            bool next = !_motionDrivers[0].DebugDrawCapsuleOffset;
            for (int i = 0; i < _motionDrivers.Length; i++) _motionDrivers[i].DebugDrawCapsuleOffset = next;
        }

        private void ToggleFootIk()
        {
            if (_footIkControllers.Length == 0) return;
            bool next = !_footIkControllers[0].DebugDrawFootIK;
            for (int i = 0; i < _footIkControllers.Length; i++) _footIkControllers[i].DebugDrawFootIK = next;
        }

        private void ToggleTraversalRuntimeLines()
        {
            if (_traversalProbes.Length == 0) return;
            bool next = !_traversalProbes[0].DebugDrawRuntimeLines;
            for (int i = 0; i < _traversalProbes.Length; i++) _traversalProbes[i].DebugDrawRuntimeLines = next;
        }

        private void ToggleTraversalSceneGizmos()
        {
            if (_traversalProbes.Length == 0) return;
            bool next = !_traversalProbes[0].DebugDrawSceneGizmos;
            for (int i = 0; i < _traversalProbes.Length; i++) _traversalProbes[i].DebugDrawSceneGizmos = next;
        }

        /// <summary>
        /// 🔄 2026-09-15：**文字頻道已移除**（使用者：「f6 文字直接拔掉不需要」）。
        /// 掃描時順手把它壓成 false，確保任何殘留的 authored `true` 都不會讓文字冒出來。
        /// ⚠️ 底層旗標本身保留（它住在 frozen 的 traversal 程式裡，移除繪製機制的風險
        /// 大於收益），但**沒有任何 UI 能把它打開**。
        /// </summary>
        private void ForcePanelTextOff()
        {
            for (int i = 0; i < _traversalProbes.Length; i++) _traversalProbes[i].DebugDrawPanelText = false;
        }

        // =====================================================================
        // 繪製
        // =====================================================================

        private void OnGUI()
        {
            if (!_open) return;
            EnsureStyles();

            // ⚠️ 高度由列數**算出來**，不是寫死。第一版寫死 260 把內容切掉了一半，
            //    而「面板被切掉」看起來就像「功能沒做完」。
            const int RowCount = 10;
            float height = RowCount * RowHeight + PanelPadding * 2f;
            var rect = new Rect(PanelMargin, PanelMargin, PanelWidth, height);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(
                rect.x + PanelPadding, rect.y + PanelPadding,
                rect.width - PanelPadding * 2f, rect.height - PanelPadding * 2f));

            GUILayout.Label("Runtime Debug      F1 或 ` 關閉", _titleStyle);
            GUILayout.Label("F2–F5 切換頻道（鍵盤操作：遊戲中游標鎖住，點不到）", _labelStyle);
            GUILayout.Space(4f);

            DrawChannelRow("F2", "Capsule Offset",
                _motionDrivers.Length, CapsuleOffsetState());
            DrawChannelRow("F3", "Foot IK",
                _footIkControllers.Length, FootIkState());
            DrawChannelRow("F4", "Traversal Runtime Lines",
                _traversalProbes.Length, TraversalState(0));
            DrawChannelRow("F5", "Traversal Scene Gizmos",
                _traversalProbes.Length, TraversalState(1));

            GUILayout.Space(4f);
            GUILayout.Label("—  不可用（不是還沒做，是畫不出來）", _titleStyle);
            GUILayout.Label("Direction Authority   快照有記錄，但無繪製實作（docs/18 §1 刻意收斂）", _labelStyle);
            GUILayout.Label("Ground Probe/Normal   grounded 來自 CC 內部 sweep，Unity 未曝露 hit/normal", _labelStyle);

            GUILayout.EndArea();
        }

        /// <summary>一列：`F2  [x] Capsule Offset  (2)`。沒有擁有者時明說，不要靜默消失。</summary>
        private void DrawChannelRow(string hotkey, string label, int ownerCount, bool? state)
        {
            string mark = state.HasValue ? (state.Value ? "[x]" : "[ ]") : "[-]";
            string suffix = ownerCount > 0 ? $"({ownerCount})" : "場景上沒有對應元件";
            GUILayout.Label($"{hotkey}  {mark}  {label}   {suffix}", _labelStyle);
        }

        private bool? CapsuleOffsetState()
            => _motionDrivers.Length > 0 ? _motionDrivers[0].DebugDrawCapsuleOffset : (bool?)null;

        private bool? FootIkState()
            => _footIkControllers.Length > 0 ? _footIkControllers[0].DebugDrawFootIK : (bool?)null;

        private bool? TraversalState(int channel)
        {
            if (_traversalProbes.Length == 0) return null;
            TraversalProbe probe = _traversalProbes[0];
            return channel switch
            {
                0 => probe.DebugDrawRuntimeLines,
                1 => probe.DebugDrawSceneGizmos,
                _ => probe.DebugDrawRuntimeLines,
            };
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null) return;

            // ⚠️ `new GUIStyle(GUI.skin.label)` ＝ 複製一份，不是改共用的那一份。
            _labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, fontSize = 12 };
            _titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12 };
        }

        /// <summary>供 EditMode 驗證：面板目前是否開著。</summary>
        internal bool IsOpen => _open;

        /// <summary>供 EditMode 驗證：不經輸入裝置直接切換面板。</summary>
        internal void ToggleForTests()
        {
            _open = !_open;
            if (_open) RescanScene();
        }

        /// <summary>供 EditMode 驗證：不經鍵盤直接切換 Capsule Offset 頻道。</summary>
        internal void ToggleCapsuleOffsetForTests() => ToggleCapsuleOffset();

        /// <summary>供 EditMode 驗證：面板掃描到的 MotionDriver 數量。</summary>
        internal int ScannedMotionDriverCount => _motionDrivers.Length;
#endif
    }
}
