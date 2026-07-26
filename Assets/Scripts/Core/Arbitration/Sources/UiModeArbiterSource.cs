using UnityEngine;
using UnityEngine.InputSystem;
using Project.Core.Blackboard;

namespace Project.Core.Arbitration.Sources
{
    /// <summary>
    /// 🆕（輪 4）第一顆 <see cref="IArbiterSource"/>：**UI 模式**——**按住** Left Alt 放開滑鼠，
    /// 角色停止移動（游標可去點畫面上既有的 UI）；**放開即收回**。
    ///
    /// 🔄（輪 4.1）語意由「按一下切換」改為「**按住生效**」，並要求 <see cref="UiModeAction"/> 掛上
    /// Input System 的 **Hold** interaction（門檻建議 0.25s）。
    /// 🔄（輪 4.2）**暫停已改綁獨立的 Esc**（`Project.App.GamePauseController`），兩者不再共用 Left Alt，
    /// 因此原本「Tap 門檻 ≤ Hold 門檻」的相依已解除。⚠️ 但也因此**「按住 Alt 時按 Esc 暫停、再放開 Alt」
    /// 變成做得到的操作**——這正是游標必須有單一擁有者（而非各自存○還原）的實證情境，見 changelog v0.26 §5。
    ///
    /// **本元件是「UI 模式」這個概念的唯一持有者**（輪 4 Ownership 裁決），獨佔兩樣東西：
    /// ① UI 模式的開關狀態　② Left Alt 的 <see cref="InputAction"/>。
    /// 上游的 <see cref="ArbiterPipeline"/> 只收到一顆 bool，**不認識 UI、不認識游標**。
    ///
    /// 🔄（輪 4.2）**Cursor API 已不再屬於本元件**：暫停成為第二個滑鼠模式後，兩個各寫各的會產生
    /// 可重現的碰撞（暫停中進出 UI 模式會把游標鎖回去）。游標的唯一擁有者改為
    /// <c>Project.App.CursorModeController</c>，本元件只透過 <see cref="IsUiModeActive"/> 回報**意圖**。
    /// </summary>
    /// <remarks>
    /// **為什麼 Alt 不進 <c>InputData</c>**（輪 4 裁決，dev-spec §1.4）：
    /// <c>InputData</c> 是**可被 <c>BlockInput</c> 封鎖的角色輸入通道**，而「解除封鎖的那顆鍵」
    /// 絕不能住在可被封鎖的通道裡——放進去就得為它開一條「這顆不受 BlockInput 影響」的例外，
    /// 例外一開，「封鎖＝本幀管線看不到任何輸入」這個乾淨語意就沒了。
    /// Alt 屬**應用層／shell 輸入**（同 Esc 開選單），不是角色輸入；先例是 <c>ThirdPersonCamera</c>
    /// 同樣自持 <c>Mouse.current</c>。這也落地 ADR-003 §13.3「游標狀態切換偏 Input／UI 職責」。
    ///
    /// **不自帶 <c>Update</c>**：邊沿在 <see cref="Evaluate"/> 內採樣——它每幀恰好被管線呼叫一次，
    /// 時序由管線保證（比照 <c>IPresentationController</c> 的紀律）。
    ///
    /// ⚠️ **一幀延遲是刻意的**：管線順序 4.5 在狀態機**之後**（讓仲裁讀得到當幀最新狀態，
    /// dev-spec §2.1 脆弱點警告第 2／7 條），所以 UI 模式生效的當幀游標與相機就反應，
    /// 但**輸入封鎖從下一幀才生效**。約 16ms，此情境無感；不得為了消除它而提前 4.5。
    /// </remarks>
    public class UiModeArbiterSource : MonoBehaviour, IArbiterSource
    {
        [Header("UI Mode Action")]
        [Tooltip("進入 UI 模式的按鍵（現行控制方案＝按住 Left Alt）。\n" +
                 "⚠️ 必須在此 action 掛上 Hold interaction（門檻建議 0.25s）——沒掛的話會變成一按就生效。\n" +
                 "🔄 暫停已改綁獨立的 Esc，兩者不再共用按鍵，故無門檻相依。\n" +
                 "未綁定＝永遠不進 UI 模式，行為與接上仲裁前完全等價。")]
        public InputAction UiModeAction;

        // UI 模式的持久狀態。刻意留在本元件私有欄位而非黑板：它是**應用層 shell 狀態**，
        // 不是角色的 gameplay state（對比 MovementIntent.WalkModeActive 因 netcode snapshot
        // 前提而必須進黑板，ADR-003 D5）。黑板上該有的是它的**結果**——Arbitration.BlockInput。
        private bool _uiMode;

        /// <summary>
        /// 本元件這一刻是否要求自由游標。讀取者：<c>Project.App.CursorModeController</c>（Cursor API 的唯一擁有者）。
        /// 🔄（輪 4.2）本元件**不再自己寫 <c>Cursor</c>**——第二個滑鼠模式（暫停）出現後，
        /// 兩個各寫各的會產生可重現的碰撞（暫停中進出 UI 模式會把游標鎖回去）。詳見該類別的註解。
        /// </summary>
        public bool IsUiModeActive => _uiMode;

        private void OnEnable()
        {
            UiModeAction?.Enable();
        }

        private void OnDisable()
        {
            UiModeAction?.Disable();

            // 防禦線：元件被停用／銷毀時清掉 UI 模式，否則狀態會凍結在 true，
            // 而本元件已不再被詢問——CursorModeController 會據此把游標永遠留在自由狀態。
            _uiMode = false;
        }

        /// <summary>
        /// 【管線順序 4.5】採樣 Hold 的進出邊沿 → 回報本來源的封鎖請求。
        /// 熱路徑零配置（無 new／無 LINQ／無字串）。
        /// </summary>
        public ArbiterData Evaluate(PlayerRuntimeData data)
        {
            if (UiModeAction != null)
            {
                // 進入：Hold interaction 撐過門檻才 performed——**短按不會觸發**。
                // 這正是「Tap 不先閃一下 UI 模式」的實作基礎（輪 4.1 使用者裁決）：
                // 代價是按住後約 0.25s 游標才出現，屬刻意接受的 UX 取捨，日後調的是門檻而非加判定機制。
                if (UiModeAction.WasPerformedThisFrame())
                {
                    _uiMode = true;
                }
                // 離開：刻意用「控制鍵已不再被按住」而非放開的邊沿訊號。
                // IsPressed() 讀的是控制本身的實際狀態、與 interaction 無關，因此**會自癒**——
                // 視窗失焦、Play 模式切換等吃掉放開邊沿的情境，不會讓 UI 模式永久卡住。
                else if (_uiMode && !UiModeAction.IsPressed())
                {
                    _uiMode = false;
                }
            }

            return new ArbiterData { BlockInput = _uiMode };
        }

        // 🗑️（輪 4.2）原本這裡有 SetUiMode／ApplyCursor 一組，直接寫 Cursor.lockState 與 Cursor.visible。
        //     已整組移交 Project.App.CursorModeController——它把所有「想要自由游標」的來源 OR 起來套用一次。
        //     搬走的理由不是美觀，是一個可重現的碰撞：暫停（第二個滑鼠模式）開著時，
        //     在此進出 UI 模式會把游標鎖回去。本元件自此只回報**意圖**（IsUiModeActive），不碰全域狀態。
    }
}
