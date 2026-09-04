using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Project.App;
using Project.Core.Arbitration;
using Project.Core.Blackboard;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// **Verification Ladder L4**（`docs/12-workflow.md` §6）：需要真實帧迴圈／Unity 生命週期的行為。
    ///
    /// <para><b>這個檔案取代了什麼</b></para>
    /// dev-spec §7.2-**M8** 的 ①②⑦——暫停／恢復與 `timeScale`、以及暫停期間的輸入封鎖。
    /// M8 把整段列為人工，理由寫的是「interaction 與 `timeScale` 的互動需要真實 Input System 更新迴圈，
    /// **EditMode 無法確定性重現**」。**那句話否定的是 EditMode，不是自動化。**
    /// 帧迴圈與 `timeScale` 正是 PlayMode 測試存在的理由；只有「按鍵 interaction」那一半才真的需要輸入裝置。
    ///
    /// <para><b>本批刻意不做的事</b></para>
    /// ⛔ 不碰 `PauseToggleAction` 的按鍵路徑（那需要 Input System 的 `InputTestFixture`，
    ///    屬下一個 slice；本 asmdef 因此也刻意還沒有引用 `Unity.InputSystem.TestFramework`）。
    /// ⛔ 不組裝完整角色（Runner ＋ Facade ＋ MotionDriver ＋ Config）。那會讓第一批
    ///    PlayMode 測試的失敗原因難以歸因——先證明這一層可用且穩定，再往上疊。
    /// ⛔ 不斷言 `Cursor.lockState`：Editor 的視窗焦點會影響它（dev-spec §7.2-M9 已記錄），
    ///    在 Test Runner 下噪音過大 ⇒ 依 `docs/12-workflow.md` §6.3 **明示降級**為人工項。
    ///    合併政策（`WantsFreeCursor`）本來就已由 EditMode 的 `CursorModeControllerTests` 守住。
    ///
    /// <para><b>⚠️ 寫 PlayMode 測試的陷阱</b></para>
    /// 本檔全程用 <c>yield return null</c> 推帧。**絕對不要用 <c>WaitForSeconds</c>**——
    /// 它吃的是縮放後的時間，在 <c>timeScale == 0</c> 下會永遠等不到，測試直接掛住。
    /// </summary>
    public class PauseLifecyclePlayModeTests
    {
        private GameObject _host;
        private float _timeScaleAtSetup;

        /// <summary>Update 呼叫次數計數器——用來證明「暫停期間 Update 仍在跑」。</summary>
        private sealed class UpdateCounter : MonoBehaviour
        {
            public int Count;
            private void Update() => Count++;
        }

        [SetUp]
        public void SetUp()
        {
            _timeScaleAtSetup = Time.timeScale;
            _host = new GameObject(nameof(PauseLifecyclePlayModeTests));
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.Destroy(_host);

            // ⚠️ timeScale 是全域狀態：測試若讓它停在 0，**後續每一個 PlayMode 測試都會掛住**。
            // 這條還原是整個 PlayMode 測試層能否穩定運作的前提，不是禮貌性清理。
            Time.timeScale = _timeScaleAtSetup;
        }

        // =====================================================================
        // P1 — 暫停期間 Update 仍會執行
        // =====================================================================

        /// <summary>
        /// **M8 ② 原話：「這條是關鍵——它驗證暫停中輸入仍能被處理（`Update` 在 `timeScale == 0` 下照跑）。
        /// 若失敗，暫停將無法解除。」**
        ///
        /// 這是整個暫停設計的承重假設，而 EditMode **結構上不可能**驗證它（沒有帧迴圈）。
        /// 它成立是因為 Unity 的 `Update` 掛在 PlayerLoop 上、與 `timeScale` 無關；
        /// 受 `timeScale` 影響的是 `Time.deltaTime` 與 `FixedUpdate` 的頻率。
        /// </summary>
        [UnityTest]
        public IEnumerator P1_Update_StillRuns_WhileTimeScaleIsZero()
        {
            var pause = _host.AddComponent<GamePauseController>();
            var counter = _host.AddComponent<UpdateCounter>();

            yield return null; // 讓元件走完 Awake／OnEnable，並跑一帧

            pause.SetPaused(true);
            Assert.AreEqual(0f, Time.timeScale, "SetPaused(true) 之後 timeScale 應為 0");

            int before = counter.Count;

            yield return null;
            yield return null;
            yield return null;

            Assert.Greater(counter.Count, before,
                "timeScale == 0 期間 Update 沒有被呼叫。\n" +
                "  contract : 暫停靠 Update 讀取解除輸入（dev-spec §7.2-M8 ②）\n" +
                "  症狀     : 一旦暫停就再也無法解除——世界永久凍結");

            Assert.AreEqual(0f, Time.deltaTime,
                "timeScale == 0 期間 deltaTime 應為 0——MotionDriver.IsTimeFrozen 與 B9 平滑都依賴這個前提");
        }

        // =====================================================================
        // P2 — 跨真實帧的暫停 → 恢復，還原的是「暫停前的值」而不是硬編 1
        // =====================================================================

        /// <summary>
        /// 取代 M8 ② 的後半：「恢復後的 `timeScale` 是**暫停前的值**」。
        /// 用非 1 的初始值才測得出「硬編 `Time.timeScale = 1f`」這個常見寫法的錯誤——
        /// 若實作寫死 1，慢動作／加速等未來的全域時間效果會在每次暫停後被靜默清掉。
        /// </summary>
        [UnityTest]
        public IEnumerator P2_Resume_RestoresTimeScaleFromBeforePause()
        {
            var pause = _host.AddComponent<GamePauseController>();
            yield return null;

            const float slowMotion = 0.35f;
            Time.timeScale = slowMotion;

            pause.SetPaused(true);
            yield return null;
            yield return null;

            Assert.AreEqual(0f, Time.timeScale, "暫停期間 timeScale 應為 0");
            Assert.IsTrue(pause.IsPaused, "IsPaused 應為 true");

            pause.SetPaused(false);
            yield return null;

            Assert.AreEqual(slowMotion, Time.timeScale, 0.0001f,
                "恢復後沒有還原成暫停前的 timeScale。\n" +
                "  contract : 還原「暫停前的值」，不是硬編 1（GamePauseController.SetPaused）\n" +
                "  症狀     : 任何全域時間效果（慢動作／加速）都會在暫停一次後被靜默清掉");
        }

        // =====================================================================
        // P3 — 暫停期間持續要求封鎖輸入（跨帧）
        // =====================================================================

        /// <summary>
        /// 取代 M8 ⑦：「站在地上暫停 → 按 Space → `[Current State]` 必須維持 IDLE」。
        ///
        /// 這裡驗的是那條人工檢查背後的**機制**：暫停期間 `IArbiterSource` 必須**每一帧**都要求
        /// `BlockInput`，而不是只在切換的那一帧。仲裁管線每帧從全 false 重算（dev-spec §1.4），
        /// 因此「只在切換帧回報」會讓封鎖在下一帧就消失。
        ///
        /// ⚠️ 完整的「按 Space 不會進 JumpState」需要組裝整條管線，屬下一個 slice；
        ///    本條先鎖住它的必要條件。
        /// </summary>
        [UnityTest]
        public IEnumerator P3_PauseRequestsInputBlock_OnEveryFrame()
        {
            var pause = _host.AddComponent<GamePauseController>();
            var blackboard = new PlayerRuntimeData();
            yield return null;

            Assert.IsFalse(pause.Evaluate(blackboard).BlockInput,
                "未暫停時不應要求封鎖輸入");

            pause.SetPaused(true);

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;

                ArbiterData request = pause.Evaluate(blackboard);
                Assert.IsTrue(request.BlockInput,
                    $"暫停後第 {frame + 1} 帧沒有要求封鎖輸入。\n" +
                    "  contract : 仲裁每帧從全 false 重算（dev-spec §1.4），來源必須每帧回報自己的請求\n" +
                    "  症狀     : 封鎖只在切換的那一帧成立，下一帧起 Jump／Roll 又能切進 FSM");
            }

            pause.SetPaused(false);
            yield return null;

            Assert.IsFalse(pause.Evaluate(blackboard).BlockInput,
                "解除暫停後不應繼續要求封鎖輸入");
        }

        // =====================================================================
        // P4 — 元件被停用時必須把時間還回去
        // =====================================================================

        /// <summary>
        /// `GamePauseController.OnDisable` 的防禦線：「元件被停用／銷毀時若仍在暫停，必須把時間還給遊戲——
        /// 否則會留下『整個世界凍結，而且沒有任何東西能解除』的死狀態」。
        ///
        /// 這條**只有** PlayMode 測得到：它驗的是 Unity 的 `OnDisable` 生命週期回呼，
        /// 而不是某個可以直接呼叫的方法。場景切換、物件被 Destroy、元件被關掉都會走到這裡。
        /// </summary>
        [UnityTest]
        public IEnumerator P4_DisablingPauseController_ReleasesTimeScale()
        {
            var pause = _host.AddComponent<GamePauseController>();
            yield return null;

            pause.SetPaused(true);
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, "前置條件：應處於暫停");

            pause.enabled = false;
            yield return null;

            Assert.AreNotEqual(0f, Time.timeScale,
                "停用 GamePauseController 之後世界仍然凍結。\n" +
                "  contract : OnDisable 必須把 timeScale 還回去（GamePauseController.OnDisable 防禦線）\n" +
                "  症狀     : 場景切換或物件被銷毀時，遊戲永久凍結且沒有任何東西能解除");
        }
    }
}
