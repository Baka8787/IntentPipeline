using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.App;
using Project.Core.Arbitration;
using Project.Core.Blackboard;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 輪 4.1 應用層暫停控制器的確定性 EditMode 測試（純 C#，不需場景／輸入裝置）。
    ///
    /// ⚠️ **本檔不測 Tap 按鍵路徑**：Tap／Hold interaction 需要真實 Input System 更新迴圈，
    /// EditMode 無法確定性重現（同 <c>ArbiterPipelineTests</c> 對 Alt 的處理）。按鍵行為屬人工驗收
    /// （dev-spec §7.2-M8）。這裡測的是它底下的**狀態機**：暫停／還原／冪等／防呆／停用時的自我還原。
    ///
    /// ⚠️ <c>Time.timeScale</c> 是**全域**狀態——本檔每條測試都必須還原，否則會污染整個測試回合
    /// （其他測試會在 timeScale = 0 下執行）。還原邏輯放在 <see cref="TearDown"/>，
    /// 即使斷言失敗提前中止也會執行。
    /// </summary>
    public class GamePauseControllerTests
    {
        private readonly List<Object> _created = new();
        private float _originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;

            // 建立確定性基準：不假設進場時的 timeScale 是多少（Editor 可能被別處改過），
            // 否則「解除暫停後應為 1」這類斷言會依賴外部狀態而偶發變紅。
            // 需要非 1 基準的測試自行覆寫。原值由 TearDown 還原。
            Time.timeScale = 1f;
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

        private GamePauseController CreateController()
        {
            var go = new GameObject("PauseControllerUnderTest");
            _created.Add(go);
            return go.AddComponent<GamePauseController>();
        }

        [Test]
        public void SetPaused_True_FreezesTimeAndReportsPaused()
        {
            GamePauseController controller = CreateController();

            controller.SetPaused(true);

            Assert.IsTrue(controller.IsPaused, "暫停後 IsPaused 應為 true");
            Assert.AreEqual(0f, Time.timeScale, "暫停＝timeScale 歸零");
        }

        [Test]
        public void SetPaused_False_RestoresThePreviousTimeScale_NotHardcodedOne()
        {
            // 刻意用非 1 的值：若實作寫死「解除＝1」，這條會紅。
            // 這是為未來的慢動作／加速等其他 timeScale 使用者預留的正確性。
            Time.timeScale = 0.5f;
            GamePauseController controller = CreateController();

            controller.SetPaused(true);
            controller.SetPaused(false);

            Assert.IsFalse(controller.IsPaused);
            Assert.AreEqual(0.5f, Time.timeScale, 1e-6f,
                "解除暫停應還原暫停前的 timeScale，而非寫死的 1");
        }

        [Test]
        public void SetPaused_CalledTwiceWithSameValue_DoesNotCaptureZeroAsResumeScale()
        {
            // 這條守的是冪等防線：第二次 SetPaused(true) 若沒有提前返回，
            // 會把「已經是 0 的 timeScale」記成還原值，導致解除暫停後世界永遠停著。
            GamePauseController controller = CreateController();

            controller.SetPaused(true);
            controller.SetPaused(true);
            controller.SetPaused(false);

            Assert.AreEqual(1f, Time.timeScale, 1e-6f,
                "重複要求暫停不得污染還原值，否則解除後會卡在 timeScale = 0");
        }

        [Test]
        public void SetPaused_WhenTimeAlreadyFrozenByAnotherSource_FallsBackToOne()
        {
            // 防呆：進暫停時現值已是 0（例如另一個來源先凍結了）。
            // 記錄 0 當還原值會讓世界永遠解不開，故實作退回 1——
            // 寧可還原錯速度，也不要還原成「還是暫停」。
            Time.timeScale = 0f;
            GamePauseController controller = CreateController();

            controller.SetPaused(true);
            controller.SetPaused(false);

            Assert.AreEqual(1f, Time.timeScale, 1e-6f,
                "還原值為 0 時必須退回 1，否則暫停無法解除");
        }

        [Test]
        public void TogglePause_AlternatesBetweenPausedAndRunning()
        {
            GamePauseController controller = CreateController();

            controller.TogglePause();
            Assert.IsTrue(controller.IsPaused, "第一次切換應進入暫停");
            Assert.AreEqual(0f, Time.timeScale);

            controller.TogglePause();
            Assert.IsFalse(controller.IsPaused, "第二次切換應解除暫停");
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);
        }

        [Test]
        public void Evaluate_RequestsBlockInput_OnlyWhilePaused()
        {
            // 🎯 這條守的是「暫停中按跳躍不會跳」**是被設計出來的**，而不是別的 bug 的副作用。
            // 背景：timeScale = 0 不會阻止 trigger 意圖寫入黑板，也不會阻止 FSM 轉移——
            // JumpState.CanEnter ＝ JumpRequested && IsGrounded，兩者皆與時間無關；
            // 而它的落地判定靠 _airborneTimer += deltaTime，暫停時恆加 0 ⇒ 進得去退不出來。
            // 先前之所以「看起來沒事」，是因為 Move(Vector3.zero) 讓 isGrounded 變 false 剛好擋住；
            // 那個 bug 修掉後保護就消失了，所以缺口必須由本來源正式關閉。
            GamePauseController controller = CreateController();
            var data = new PlayerRuntimeData();

            Assert.IsFalse(controller.Evaluate(data).BlockInput, "未暫停時不得要求封鎖輸入");

            controller.SetPaused(true);
            Assert.IsTrue(controller.Evaluate(data).BlockInput, "暫停時必須要求封鎖輸入");

            controller.SetPaused(false);
            Assert.IsFalse(controller.Evaluate(data).BlockInput, "解除暫停後必須立即停止要求封鎖");

            // 本來源只管輸入：封鎖 IK／音效／表情是別人的決定，不得順手抬別人的旗標
            ArbiterData request = controller.Evaluate(data);
            Assert.IsFalse(request.BlockIK, "暫停來源不得順手要求封鎖 IK");
            Assert.IsFalse(request.BlockAudio, "暫停來源不得順手要求封鎖音效");
            Assert.IsFalse(request.BlockExpression, "暫停來源不得順手要求封鎖表情");
        }

        [Test]
        public void Disabled_WhilePaused_GivesTimeBackToTheGame()
        {
            // 防禦線：元件被停用／銷毀時若仍在暫停，會留下
            // 「整個世界凍結、且沒有任何東西能解除」的死狀態——比游標收不回來嚴重得多。
            //
            // ⚠️ 刻意用反射直接呼叫 OnDisable，而不是靠 DestroyImmediate 觸發：
            //    EditMode 下 Unity 是否派送生命週期訊息不在本測試的掌控範圍內，
            //    依賴它會讓這條測試的成敗取決於引擎行為而非我們的程式。
            //    這裡要驗的是「OnDisable 這段防禦碼寫對了沒有」，就直接驗它。
            GamePauseController controller = CreateController();
            controller.SetPaused(true);
            Assert.AreEqual(0f, Time.timeScale, "前置條件：世界已凍結");

            MethodInfo onDisable = typeof(GamePauseController)
                .GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(onDisable, "找不到 GamePauseController.OnDisable（方法名稱可能已變更）");
            onDisable.Invoke(controller, null);

            Assert.IsFalse(controller.IsPaused, "停用後不應仍自認為暫停中");
            Assert.AreEqual(1f, Time.timeScale, 1e-6f,
                "停用／銷毀暫停控制器必須把時間還給遊戲，否則場景會永久凍結且無法解除");
        }
    }
}
