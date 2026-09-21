using Project.Core.Environment;
using Project.Core.Blackboard;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Presentation.Animation;
using Project.Presentation.IK;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.App
{
    /// <summary>
    /// 執行期 debug 顯示入口：作品集用的只讀 uGUI 摘要，以及工程用可視化總開關。
    ///
    /// <para><b>⭐ 它是觀察者／遙控器，不是感測器</b></para>
    /// Portfolio 區只讀 Runner／RuntimeData／AnimancerFacade 已接受的結果；工程區只翻既有
    /// debug owner 上的 <c>bool</c>。兩者都不生產 gameplay 真相。
    /// <list type="bullet">
    /// <item>⛔ **不計算任何 gameplay 資料**——不打 ray、不查幾何、不由字串反推狀態。</item>
    /// <item>⛔ **不持有第二份 gameplay 狀態**——Portfolio 欄位只是顯示快取；
    /// 工程旗標的真相仍住在各自的元件上。
    /// 把 Panel 刪掉，那些旗標維持原樣；Panel 只是換一個地方按。</item>
    /// <item>⛔ **不得成為第二個 sensor／authority**（`docs/17` §3.3 紅線 1）。</item>
    /// </list>
    /// ⇒ 這條紀律不是風格要求：`docs/18` §1 的「**記錄，不要重算**」正是為了避免
    /// debug 圖層畫出一個「看起來很權威、但不是真正決定行為的那個東西」。
    ///
    /// <para><b>🔴 為什麼是鍵盤操作，不是用滑鼠點</b></para>
    /// 遊戲進行中 <c>CursorModeController</c> 把游標鎖住並隱藏（它是 `Cursor` API 的唯一擁有者）
    /// ⇒ **IMGUI 的勾選框根本點不到**。第一版用 `GUILayout.Toggle` 是錯的。
    /// ⇒ F1 切換 Portfolio Basic／Off；backquote 開關工程面板，F2–F5 切換各頻道。
    /// 📌 選 F-key 而不是數字鍵，是因為數字鍵與技能 slot 撞鍵——面板不消費輸入，
    /// 按 1 會**同時**切換圖層並放技能。
    ///
    /// <para><b>為什麼直接讀 <c>Keyboard.current</c></b></para>
    /// 各頻道各開一個序列化 `InputAction` 等於五份要接線的資產，而這是**只在 Editor／
    /// Development build 存在的工具**。⛔ 這個藉口僅限本檔，gameplay 輸入一律走 `IInputSource`。
    ///
    /// <para><b>UI 技術邊界</b></para>
    /// Portfolio 摘要使用專案既有 PlayerHud Canvas 的 uGUI；工程面板仍用 IMGUI，
    /// 因為它不是遊戲 UI，且不需要另一套 prefab 接線。
    /// 而且它**整段被 `UNITY_EDITOR || DEVELOPMENT_BUILD` 包住** ⇒ release build 完全不存在。
    /// ⚠️ IMGUI 每幀配置字串——這是本專案零 GC 紀律的**明確例外**，成立條件有二：
    /// ①只在面板開著時發生；②release build 編不進去。⛔ 不得把這個藉口套用到 gameplay。
    ///
    /// <para><b>掃描時機</b></para>
    /// 開啟面板時掃一次場景（<c>FindObjectsByType</c>），⛔ **不是每幀**。
    /// </summary>
    public sealed class RuntimeDebugPanel : MonoBehaviour
    {
        public enum PortfolioDisplayMode
        {
            Off = 0,
            Basic = 1,
        }

        [Header("Toggle")]
        [Tooltip("Basic / Off 一鍵切換。工程用頻道面板改由 backquote（`）開啟，F2–F5 仍切換各頻道。")]
        public InputAction TogglePanelAction;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const string MissingValue = "—";
        private const float PanelWidth = 430f;
        private const float PanelMargin = 12f;
        private const float RowHeight = 21f;
        private const float PanelPadding = 14f;

        [Header("Portfolio Debug UI")]
        [SerializeField] private PortfolioDisplayMode portfolioDisplayMode = PortfolioDisplayMode.Off;
        [SerializeField] private GameObject portfolioPanelRoot;
        [SerializeField] private TMPro.TextMeshProUGUI stateText;
        [SerializeField] private TMPro.TextMeshProUGUI animationStateText;
        [SerializeField] private TMPro.TextMeshProUGUI clipText;
        [SerializeField] private TMPro.TextMeshProUGUI gaitText;

        [Header("Read-only Sources")]
        [SerializeField] private CharacterPipelineRunner runner;
        [SerializeField] private AnimancerFacade animancerFacade;

        private bool _engineeringOpen;
        private MotionDriver[] _motionDrivers = System.Array.Empty<MotionDriver>();
        private FootIKController[] _footIkControllers = System.Array.Empty<FootIKController>();
        private TraversalProbe[] _traversalProbes = System.Array.Empty<TraversalProbe>();
        private TraversalHandIKController[] _traversalHandIkControllers =
            System.Array.Empty<TraversalHandIKController>();

        private StateType _lastState = (StateType)(-1);
        private GaitTier _lastGait = (GaitTier)(-1);
        private string _lastAnimationState;
        private string _lastClip;

        // ⚠️ 自己的 GUIStyle 實例。**不得改 `GUI.skin.label`**——那是共用樣式，
        //    改了會污染這一幀之後所有 IMGUI 繪製（第一版就是這樣寫錯的）。
        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;

        private void Awake()
        {
            ResolveSources();
            ApplyPortfolioVisibility();
        }

        private void OnEnable()
        {
            TogglePanelAction?.Enable();
            ApplyPortfolioVisibility();
        }

        private void OnDisable()
        {
            TogglePanelAction?.Disable();
            if (portfolioPanelRoot != null) portfolioPanelRoot.SetActive(false);
        }

        private void Update()
        {
            if (TogglePanelAction != null && TogglePanelAction.WasPerformedThisFrame())
            {
                SetPortfolioDisplayMode(
                    portfolioDisplayMode == PortfolioDisplayMode.Basic
                        ? PortfolioDisplayMode.Off
                        : PortfolioDisplayMode.Basic);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.backquoteKey.wasPressedThisFrame)
            {
                _engineeringOpen = !_engineeringOpen;
                if (_engineeringOpen) RescanScene();
            }

            // ⭐ 2026-09-21：**F6 ＝ 全部可視化一鍵開／關。**
            //
            // 為什麼放在 `_engineeringOpen` 閘門**之前**：作品集展示時要的是「一個鍵全開、
            // 一個鍵全關」，而不是「先開工程面板、再逐一按 F2–F5」。它與 backquote 同一層級
            // ——backquote 開的是**面板**，F6 開的是**畫面上的可視化**，兩者互不依賴。
            //
            // ⚠️ F6 是空的（2026-09-15 使用者裁決把舊的文字頻道拔掉後就沒有擁有者），
            //    所以沒有和既有快捷鍵衝突。
            if (keyboard != null && keyboard.f6Key.wasPressedThisFrame) ToggleAllVisuals();

            if (!_engineeringOpen) return;
            ReadChannelHotkeys();
        }

        private void LateUpdate()
        {
            if (portfolioDisplayMode != PortfolioDisplayMode.Basic) return;
            if (runner == null || animancerFacade == null) ResolveSources();
            if (runner == null) return;

            PlayerRuntimeData data = runner.RuntimeData;
            GaitTier gait = data != null ? data.Gait : GaitTier.Run;
            string selectedState = animancerFacade != null
                ? animancerFacade.DebugSelectedStateKey
                : string.Empty;
            string clipName = animancerFacade != null
                ? animancerFacade.DebugDominantClipName
                : string.Empty;

            UpdatePortfolioLabels(runner.CurrentState, selectedState, clipName, gait);
        }

        internal void ResolveSources()
        {
            if (runner == null) runner = GetComponent<CharacterPipelineRunner>();
            if (animancerFacade == null) animancerFacade = GetComponent<AnimancerFacade>();
        }

        private void SetPortfolioDisplayMode(PortfolioDisplayMode mode)
        {
            portfolioDisplayMode = mode;
            ApplyPortfolioVisibility();
            if (mode == PortfolioDisplayMode.Basic) LateUpdate();
        }

        private void ApplyPortfolioVisibility()
        {
            if (portfolioPanelRoot != null)
                portfolioPanelRoot.SetActive(portfolioDisplayMode == PortfolioDisplayMode.Basic);
        }

        private void UpdatePortfolioLabels(
            StateType state,
            string animationState,
            string clip,
            GaitTier gait)
        {
            if (_lastState != state)
            {
                _lastState = state;
                if (stateText != null) stateText.text = string.Concat("State: ", StateLabel(state));
            }

            string safeAnimationState = string.IsNullOrEmpty(animationState) ? MissingValue : animationState;
            if (!string.Equals(_lastAnimationState, safeAnimationState, System.StringComparison.Ordinal))
            {
                _lastAnimationState = safeAnimationState;
                if (animationStateText != null)
                    animationStateText.text = string.Concat("Anim State: ", safeAnimationState);
            }

            string safeClip = string.IsNullOrEmpty(clip) ? MissingValue : clip;
            if (!string.Equals(_lastClip, safeClip, System.StringComparison.Ordinal))
            {
                _lastClip = safeClip;
                if (clipText != null) clipText.text = string.Concat("Clip: ", safeClip);
            }

            if (_lastGait != gait)
            {
                _lastGait = gait;
                if (gaitText != null) gaitText.text = string.Concat("Gait: ", GaitLabel(gait));
            }
        }

        private static string StateLabel(StateType state) => state switch
        {
            StateType.Idle => "Idle",
            StateType.Move => "Move",
            StateType.Jump => "Jump",
            StateType.Roll => "Roll",
            StateType.Action => "Action",
            StateType.Traversal => "Traversal",
            StateType.Death => "Death",
            StateType.Hurt => "Hurt",
            _ => "None",
        };

        private static string GaitLabel(GaitTier gait) => gait switch
        {
            GaitTier.Walk => "Walk",
            GaitTier.Sprint => "Sprint",
            _ => "Run",
        };

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
            _traversalHandIkControllers = FindObjectsByType<TraversalHandIKController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            ForcePanelTextOff();
        }

        // =====================================================================
        // 切換（每一個都只是「讀第一顆的值 → 反過來 → 套給全部」）
        // =====================================================================

        /// <summary>
        /// **全部可視化一鍵開／關。** 語意刻意不是「記一個 master bool 然後翻它」——
        /// 那會在使用者用 F2–F5 個別調過之後和畫面脫節。改成由**當下的實際狀態**推導：
        /// 只要還有任何一個頻道開著 ⇒ 全部關掉；全部關著 ⇒ 全部打開。
        /// 因此不論之前怎麼調，按一下的結果都是可預測的。
        /// </summary>
        private void ToggleAllVisuals()
        {
            EnsureScanned();

            bool anyOn =
                (CapsuleOffsetState() ?? false) ||
                (FootIkState() ?? false) ||
                (TraversalState(0) ?? false) ||
                (TraversalState(1) ?? false);
            SetAllVisuals(!anyOn);
        }

        private void SetAllVisuals(bool on)
        {
            for (int i = 0; i < _motionDrivers.Length; i++)
                _motionDrivers[i].DebugDrawCapsuleOffset = on;
            for (int i = 0; i < _footIkControllers.Length; i++)
                _footIkControllers[i].DebugDrawFootIK = on;
            for (int i = 0; i < _traversalProbes.Length; i++)
            {
                _traversalProbes[i].DebugDrawRuntimeLines = on;
                _traversalProbes[i].DebugDrawSceneGizmos = on;
            }
            for (int i = 0; i < _traversalHandIkControllers.Length; i++)
                _traversalHandIkControllers[i].DebugDrawRuntimeTargets = on;
        }

        /// <summary>
        /// F6 不需要先開工程面板，所以掃描不能只掛在面板開啟上。
        /// 這裡做一次惰性掃描；已掃過就不重複付這個全域查詢的成本。
        /// </summary>
        private void EnsureScanned()
        {
            if (_motionDrivers.Length > 0 || _footIkControllers.Length > 0 ||
                _traversalProbes.Length > 0 || _traversalHandIkControllers.Length > 0)
                return;
            RescanScene();
        }

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
            if (_traversalProbes.Length == 0 && _traversalHandIkControllers.Length == 0) return;
            bool current = _traversalProbes.Length > 0
                ? _traversalProbes[0].DebugDrawRuntimeLines
                : _traversalHandIkControllers[0].DebugDrawRuntimeTargets;
            bool next = !current;
            for (int i = 0; i < _traversalProbes.Length; i++) _traversalProbes[i].DebugDrawRuntimeLines = next;
            for (int i = 0; i < _traversalHandIkControllers.Length; i++)
                _traversalHandIkControllers[i].DebugDrawRuntimeTargets = next;
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
            if (!_engineeringOpen) return;
            EnsureStyles();

            // ⚠️ 高度由列數**算出來**，不是寫死。第一版寫死 260 把內容切掉了一半，
            //    而「面板被切掉」看起來就像「功能沒做完」。
            const int RowCount = 11;
            float height = RowCount * RowHeight + PanelPadding * 2f;
            var rect = new Rect(PanelMargin, PanelMargin, PanelWidth, height);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(
                rect.x + PanelPadding, rect.y + PanelPadding,
                rect.width - PanelPadding * 2f, rect.height - PanelPadding * 2f));

            GUILayout.Label("Engineering Debug      ` 關閉", _titleStyle);
            GUILayout.Label("F2–F5 切換頻道（鍵盤操作：遊戲中游標鎖住，點不到）", _labelStyle);
            GUILayout.Label("F6  全部可視化一鍵開／關（不需要開這個面板也能按）", _titleStyle);
            GUILayout.Space(4f);

            DrawChannelRow("F2", "Capsule Offset",
                _motionDrivers.Length, CapsuleOffsetState());
            DrawChannelRow("F3", "Foot IK",
                _footIkControllers.Length, FootIkState());
            DrawChannelRow("F4", "Traversal + Hand Targets",
                _traversalProbes.Length + _traversalHandIkControllers.Length, TraversalState(0));
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
            if (_traversalProbes.Length == 0)
            {
                if (channel == 0 && _traversalHandIkControllers.Length > 0)
                    return _traversalHandIkControllers[0].DebugDrawRuntimeTargets;
                return null;
            }
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
        internal bool IsOpen => _engineeringOpen;

        /// <summary>供 EditMode 驗證：不經輸入裝置直接切換面板。</summary>
        internal void ToggleForTests()
        {
            _engineeringOpen = !_engineeringOpen;
            if (_engineeringOpen) RescanScene();
        }

        /// <summary>供 EditMode 驗證：不經鍵盤直接切換 Capsule Offset 頻道。</summary>
        internal void ToggleCapsuleOffsetForTests() => ToggleCapsuleOffset();

        /// <summary>供 EditMode 驗證：面板掃描到的 MotionDriver 數量。</summary>
        internal int ScannedMotionDriverCount => _motionDrivers.Length;

        internal PortfolioDisplayMode DisplayMode => portfolioDisplayMode;

        internal void SetPortfolioDisplayModeForTests(PortfolioDisplayMode mode)
            => SetPortfolioDisplayMode(mode);

        internal void UpdatePortfolioLabelsForTests(
            StateType state, string animationState, string clip, GaitTier gait)
            => UpdatePortfolioLabels(state, animationState, clip, gait);
#endif
    }
}
