using UnityEngine;
using Project.Core.Arbitration.Sources;

namespace Project.App
{
    /// <summary>
    /// 🆕（輪 4.2）**`Cursor` API 的唯一擁有者**：把所有「想要自由游標」的來源 OR 起來，套用一次。
    ///
    /// **為什麼會有這一顆**（輪 4 → 4.1 → 4.2 的完整因果，別把它當成一開始就該有的東西）：
    /// 輪 4 只有 UI 模式一個滑鼠模式，Cursor 的擁有權判給 <see cref="UiModeArbiterSource"/> 是當時的最小解。
    /// 輪 4.1 加入暫停後出現**第二個**滑鼠模式，兩者若各寫各的就有兩個擁有者，而且碰撞可重現——
    /// **暫停中按住 Alt 進 UI 模式、再放開，對方會把游標鎖回去，即使暫停還開著**。
    /// 當時刻意先不解（沒有真實壓力就決定介面形狀＝憑空猜），把它記在 dev-spec §7.3 等壓力；
    /// 「暫停時游標要常駐」這個需求就是那個壓力，於是有了本類別。
    ///
    /// **形狀與 <c>ArbiterPipeline</c> 同源**：來源各自回報自己要什麼，**單一擁有者合併後套用一次**。
    /// 差別只在這裡的來源是 Inspector 明確引用（兩個而已，不預先做成介面集合——
    /// 等第三個滑鼠模式出現再一般化，同 <c>PresentationPipeline</c> 當年的節奏）。
    /// </summary>
    /// <remarks>
    /// **依賴方向**：本類別屬應用層，向下引用 <see cref="UiModeArbiterSource"/>（角色層）與
    /// <see cref="GamePauseController"/>（同層）——**高層認識低層，方向正確**。
    /// 反過來讓角色層元件去讀暫停狀態才是壞味道（低層依賴高層），那正是本設計要避免的。
    ///
    /// ⚠️ **可能有最多一幀的延遲**：本類別的 <c>Update</c> 與 <c>CharacterPipelineRunner</c> 的
    /// <c>Update</c>（UI 模式在順序 4.5 更新）之間沒有腳本執行順序保證。與 <c>BlockInput</c> 的一幀延遲同級，
    /// 此情境無感。**但相機不受影響**：所有 <c>Update</c> 都跑在所有 <c>LateUpdate</c> 之前，
    /// 所以 <c>ThirdPersonCamera.LateUpdate</c> 讀到的必定是本幀已套用的值，不會出現半套狀態。
    ///
    /// ⚠️ **本元件缺席時游標不會被鎖住**（連帶相機不轉，因為相機以 <c>Cursor.lockState</c> 為閘門）。
    /// 這是**刻意讓它大聲壞掉**：<c>ThirdPersonCamera.Start</c> 原本那行初始鎖定已移除，
    /// 否則「唯一擁有者」只是文件上的說法。缺元件的症狀一眼可見，不是靜默的行為漂移。
    /// </remarks>
    public class CursorModeController : MonoBehaviour
    {
        [Header("Free-Cursor Requesters")]
        [Tooltip("角色 Root 上的 UiModeArbiterSource（按住 Alt 的 UI 模式）。留空＝該來源永遠不要求自由游標。")]
        [SerializeField] private UiModeArbiterSource uiModeSource;

        [Tooltip("場景中的 GamePauseController（現行方案＝Esc 暫停）。留空＝該來源永遠不要求自由游標。")]
        [SerializeField] private GamePauseController pauseController;

        /// <summary>
        /// 本幀是否有**任何**來源要求自由游標（純 OR，無優先級——同 <c>ArbiterPipeline</c> 的合併政策）。
        /// 公開的理由：讓 EditMode 測試能在不碰全域 <see cref="Cursor"/> 的前提下驗證合併邏輯。
        /// </summary>
        public bool WantsFreeCursor =>
            (uiModeSource != null && uiModeSource.IsUiModeActive) ||
            (pauseController != null && pauseController.IsPaused);

        private void Update()
        {
            bool wantsFree = WantsFreeCursor;
            CursorLockMode desiredLock = wantsFree ? CursorLockMode.None : CursorLockMode.Locked;

            // ⚠️ **刻意比對 Cursor 的「現值」，而不是快取「我上次寫了什麼」。**
            //    初版就是用快取（只在要求改變時才寫），結果游標會永久卡在可見狀態——
            //    根因是那個快取假設我們是唯一會動 Cursor.lockState 的人，**但 Unity Editor 不是**：
            //    Play 模式按 Esc、以及視窗失焦，Unity 內建都會強制解鎖游標。
            //    一旦被外力改掉，快取就永遠認為「已經套用過了」而不再修正。
            //    比對現值＝自癒：不管誰把它改掉，下一帧都會被拉回正確狀態。
            //
            //    仍然只在不一致時才寫，所以沒有每帧覆寫的成本，Profiler 上也看得出誰在動它。
            //    📌 已知副作用：Editor 內「按 Esc 逃出鎖定游標」這個內建後門會被我們立刻收回。
            //       現行控制方案下不成問題——Esc 本來就是暫停鍵，暫停會正當地放開游標。
            if (Cursor.lockState != desiredLock) Cursor.lockState = desiredLock;
            if (Cursor.visible != wantsFree) Cursor.visible = wantsFree;
        }
    }
}
