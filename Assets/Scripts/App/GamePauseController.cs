using UnityEngine;
using UnityEngine.InputSystem;
using Project.Core.Arbitration;
using Project.Core.Blackboard;

namespace Project.App
{
    /// <summary>
    /// 🆕（輪 4.1）**應用層**暫停控制器：Tap Left Alt 切換 <c>Time.timeScale</c>。
    ///
    /// **為什麼它不在角色身上、也不是 <see cref="Project.Core.Arbitration.IArbiterSource"/>**（使用者裁決）：
    /// <c>Time.timeScale</c> 是**應用全域**狀態，而 <c>PlayerRuntimeData</c>／<c>ArbiterData</c> 是
    /// **單一角色**的黑板與仲裁旗標。把暫停做成第 5 個 Block 旗標，會讓 per-character 結構擁有全域狀態——
    /// 第二隻角色進場立刻露餡（兩塊黑板都聲稱擁有暫停）。因此暫停自成一層，
    /// **不進 `PlayerRuntimeData`／`ArbiterData`／`ArbiterPipeline`，也不由 `CharacterPipelineRunner` 管理**。
    ///
    /// **不是 Singleton**（CLAUDE.md 明禁）：本類別沒有靜態實例、沒有全域存取點。它就是一顆掛在場景裡的元件，
    /// 自己擁有自己的狀態；需要驅動它的人（未來的暫停選單按鈕）用 Inspector 引用，不靠全域查詢。
    /// </summary>
    /// <remarks>
    /// **為什麼它自帶 <c>Update</c>**：這是與 <c>IArbiterSource</c>／<c>IPresentationController</c>
    /// 「不得自帶 Update、時序由管線保證」紀律的**刻意差異**——那條紀律的前提是「你屬於角色管線」，
    /// 而本元件明確不屬於。它沒有管線可以掛，只能自己推進。
    /// ⚠️ <c>Update</c> 在 <c>timeScale == 0</c> 時**仍會執行**（歸零的是 <c>deltaTime</c>，不是 <c>Update</c>），
    /// 這正是暫停後還能再按一次解除的物理基礎。
    ///
    /// **本輪刻意不做的事**（最小可驗證範圍，使用者裁決）：
    /// * **不碰 <c>Cursor</c>**——輪 4 已把 Cursor API 的擁有權判給 <c>UiModeArbiterSource</c>，
    ///   這裡再寫一次就會出現兩個擁有者，並產生具體對撞（暫停中按住再放開 Alt，
    ///   對方的 <c>ApplyCursor(false)</c> 會把游標收回去，即使暫停還開著）。
    ///   等真的有面板要點時，「Cursor 擁有權要不要抽出去」才是有真實壓力的裁決。
    /// * **不封鎖角色輸入**——<c>timeScale = 0</c> 已讓 <c>deltaTime</c> 歸零、位移與動畫全停。
    ///   已知殘留：trigger 意圖（Jump／Roll）仍會寫入黑板、FSM 仍以 <c>deltaTime = 0</c> Tick，
    ///   故暫停中按跳躍可能在解除暫停時才「補跳」一下。屬已知缺口，見 dev-spec §7.3。
    /// * **不做 Pause Menu／Canvas／EventSystem／UI navigation**。
    /// </remarks>
    public class GamePauseController : MonoBehaviour, IArbiterSource
    {
        [Header("Pause Action")]
        [Tooltip("切換暫停的按鍵。🔄 現行控制方案＝Esc（獨立鍵，不與 UI 模式共用）。\n" +
                 "此時不需要 Tap interaction——獨佔一顆鍵，一般 Button 綁定即可。\n" +
                 "⚠️ 若日後改回與 UiModeArbiterSource 共用 Left Alt，則必須掛 Tap interaction，\n" +
                 "   且 Tap 門檻 ≤ 對方的 Hold 門檻，否則兩者會同時觸發。\n" +
                 "未綁定＝永遠不會暫停，行為與加入本元件前等價。")]
        public InputAction PauseToggleAction;

        // 暫停前的 timeScale。刻意不寫死「解除＝1」——若未來出現慢動作／加速等其他 timeScale 使用者，
        // 寫死會把它們的設定一併清掉。記錄現值再還原，是零假設的作法。
        private float _timeScaleBeforePause = 1f;

        private bool _isPaused;

        /// <summary>本元件是否正處於暫停。唯讀——狀態的唯一寫入者是本元件自己。</summary>
        public bool IsPaused => _isPaused;

        private void OnEnable()
        {
            PauseToggleAction?.Enable();
        }

        private void OnDisable()
        {
            PauseToggleAction?.Disable();

            // 防禦線：元件被停用／銷毀時若仍在暫停，必須把時間還給遊戲——
            // 否則會留下「整個世界凍結，而且沒有任何東西能解除」的死狀態（比游標收不回來嚴重得多）。
            SetPaused(false);
        }

        private void Update()
        {
            // Tap interaction：短按放開才 performed；撐過 Hold 門檻的長按不會走到這裡（見類別註解）。
            if (PauseToggleAction != null && PauseToggleAction.WasPerformedThisFrame())
            {
                SetPaused(!_isPaused);
            }
        }

        /// <summary>
        /// 設定暫停狀態。公開的理由有二：①EditMode 測試可在沒有輸入裝置的情況下確定性驅動；
        /// ②未來的暫停選單按鈕可直接以 Inspector 引用呼叫，不需要走全域查詢（＝不需要 Singleton）。
        /// </summary>
        public void SetPaused(bool paused)
        {
            if (paused == _isPaused) return;

            _isPaused = paused;

            if (paused)
            {
                _timeScaleBeforePause = Time.timeScale;

                // 防呆：若進暫停時現值已是 0（例如兩個暫停來源疊在一起），還原時會永遠卡住。
                // 記成 1 是唯一安全的退路——寧可還原錯速度，也不要還原成「還是暫停」。
                if (_timeScaleBeforePause <= 0f) _timeScaleBeforePause = 1f;

                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = _timeScaleBeforePause;
            }
        }

        /// <summary>切換暫停。供未來 UI 按鈕直接綁定。</summary>
        public void TogglePause() => SetPaused(!_isPaused);

        /// <summary>
        /// 【管線順序 4.5】🆕（2026-07-27）暫停期間要求封鎖角色輸入。
        ///
        /// **為什麼需要**：`timeScale = 0` 讓位移與動畫全停，但**不會**阻止 trigger 意圖被寫入黑板，
        /// 也不會阻止 FSM 轉移——`JumpState.CanEnter` ＝ `JumpRequested && IsGrounded`，兩者皆與時間無關。
        /// 更糟的是它的落地判定靠 `_airborneTimer += deltaTime`，暫停時恆加 0 ⇒ `IsLanded` 永遠 false
        /// ⇒ 一旦切進 `JumpState` 就**退不出來**，解除暫停後才起跳。
        ///
        /// ⚠️ 這個缺口先前之所以「看起來沒事」，是因為另一個 bug 剛好抵銷了它：暫停時
        /// `Move(Vector3.zero)` 讓 `isGrounded` 變 false，`CanEnter` 因此失敗。
        /// 那個 bug 已由 `MotionDriver.IsTimeFrozen` 修掉（它同時是「站著暫停再解除會聽到落地聲」的根因），
        /// **修掉之後保護就消失了**，所以缺口必須在此正式關閉——
        /// 依賴一個不知道為何存在的保護，比沒有保護更危險。
        ///
        /// **架構方向**：本元件屬應用層，但封鎖是**每角色**的狀態，所以走「低層擁有、高層提供來源」——
        /// 由角色的 `CharacterPipelineRunner` 以 Inspector 引用把本元件收為 `IArbiterSource`（DIP），
        /// **而不是**讓角色去查詢全域。與游標的方向相反、兩者都對，判準見 design-doc §4.9。
        /// </summary>
        public ArbiterData Evaluate(PlayerRuntimeData data)
        {
            return new ArbiterData { BlockInput = _isPaused };
        }
    }
}
