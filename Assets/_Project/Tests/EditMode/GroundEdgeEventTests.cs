using NUnit.Framework;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// <c>MotionDriver.TrackGroundStateDivergence</c> / <c>ShouldConfirmGroundStateChange</c>——
    /// <c>JustLanded</c>／<c>JustLeftGround</c> 的 <c>groundEdgeEventGrace</c> 過濾（2026-09-23）。
    ///
    /// <para><b>驗的是什麼</b></para>
    /// 使用者回報下樓梯高頻率觸發落地聲：場景樓梯實測級高 0.195m，純重力自由落體到下一階
    /// 需 ≈0.199s——<c>characterController.isGrounded</c> 本來就會每一階閃爍一次原始訊號，
    /// 而 <c>JustLanded</c>／<c>JustLeftGround</c> 原本無去抖動 ⇒ 每階各灌一次事件。
    /// 本組純函數測試與 <c>JumpState.TrackUngroundedSince</c>／<c>ShouldEnterFallFromLostGround</c>
    /// 同一模式（見 <c>StateMachineConfigTests.JumpState_FallEntryGrace_RequiresContinuousUngroundedTime</c>）。
    ///
    /// ⛔ 驗不到：真實 <c>CharacterController</c> 在真實樓梯 geometry 上的 <c>isGrounded</c> 抖動頻率——
    /// 那需要 Play 測試（<c>docs/31</c> 的 grace 數值仍需人工在 Editor 裡對照手感調整）。
    /// </summary>
    public sealed class GroundEdgeEventTests
    {
        private const float Never = -1f;
        private const float Grace = 0.1f;

        [Test]
        public void TrackGroundStateDivergence_FirstDivergence_LatchesTime()
        {
            float divergingSince = MotionDriver.TrackGroundStateDivergence(
                grounded: false, reportedGrounded: true, divergingSince: Never, now: 10f);

            Assert.AreEqual(10f, divergingSince, "第一次分歧只鎖存 now，不得累加 deltaTime");
        }

        [Test]
        public void TrackGroundStateDivergence_RepeatedQuerySameDivergence_KeepsFirstSnapshot()
        {
            float divergingSince = MotionDriver.TrackGroundStateDivergence(
                grounded: false, reportedGrounded: true, divergingSince: Never, now: 10f);

            divergingSince = MotionDriver.TrackGroundStateDivergence(
                grounded: false, reportedGrounded: true, divergingSince: divergingSince, now: 10.05f);

            Assert.AreEqual(10f, divergingSince, "同一段分歧被重複詢問時必須保留第一次快照");
        }

        [Test]
        public void TrackGroundStateDivergence_ReconvergedBeforeConfirm_ClearsLatch()
        {
            float divergingSince = MotionDriver.TrackGroundStateDivergence(
                grounded: false, reportedGrounded: true, divergingSince: Never, now: 10f);

            // 樓梯單階閃爍：0.03s 後又跟 reportedGrounded 一致（短暫抖動，未達 grace 就消失）。
            divergingSince = MotionDriver.TrackGroundStateDivergence(
                grounded: true, reportedGrounded: true, divergingSince: divergingSince, now: 10.03f);

            Assert.AreEqual(Never, divergingSince, "分歧消失必須清除 latch，不得殘留成下一段分歧的起點");
        }

        [Test]
        public void ShouldConfirmGroundStateChange_BelowGrace_DoesNotConfirm()
        {
            Assert.IsFalse(MotionDriver.ShouldConfirmGroundStateChange(
                divergingSince: 10f, now: 10.099f, grace: Grace),
                "短於 grace 的分歧不得確認為一次真實的 JustLanded／JustLeftGround");
        }

        [Test]
        public void ShouldConfirmGroundStateChange_AtOrAboveGrace_Confirms()
        {
            Assert.IsTrue(MotionDriver.ShouldConfirmGroundStateChange(
                divergingSince: 10f, now: 10.1f, grace: Grace),
                "分歧持續達 grace 必須確認");
        }

        [Test]
        public void ShouldConfirmGroundStateChange_NoDivergence_DoesNotConfirm()
        {
            Assert.IsFalse(MotionDriver.ShouldConfirmGroundStateChange(
                divergingSince: Never, now: 999f, grace: Grace),
                "沒有待確認的分歧（sentinel）永遠不該確認");
        }

        [Test]
        public void StairDescent_ShortPerStepDip_NeverConfirmsAcrossManySteps()
        {
            // 模擬樓梯每階 0.06s 的短暫離地閃爍（快於 grace）：連續 5 階都不該確認任何一次。
            float divergingSince = Never;
            bool reported = true; // 上一次已回報的 grounded 狀態

            for (int step = 0; step < 5; step++)
            {
                float dipStart = step * 0.2f;
                divergingSince = MotionDriver.TrackGroundStateDivergence(
                    grounded: false, reportedGrounded: reported, divergingSince: Never, now: dipStart);
                Assert.IsFalse(MotionDriver.ShouldConfirmGroundStateChange(
                    divergingSince, dipStart + 0.06f, Grace),
                    $"第 {step} 階：0.06s 的短暫離地不得確認為 JustLeftGround");

                divergingSince = MotionDriver.TrackGroundStateDivergence(
                    grounded: true, reportedGrounded: reported, divergingSince: divergingSince, now: dipStart + 0.06f);
                Assert.AreEqual(Never, divergingSince, $"第 {step} 階：重新著地必須清除 latch");
            }
        }

        [Test]
        public void StairDescent_SustainedDip_ExceedingGrace_Confirms()
        {
            // 對照組：若某一階的離地時間真的超過 grace（例如慢走、級高較大），必須確認一次。
            float divergingSince = MotionDriver.TrackGroundStateDivergence(
                grounded: false, reportedGrounded: true, divergingSince: Never, now: 0f);

            Assert.IsTrue(MotionDriver.ShouldConfirmGroundStateChange(
                divergingSince, now: 0.199f, grace: Grace),
                "實測樓梯級高 0.195m 的自由落體時間 ≈0.199s，超過預設 grace 時必須確認為真實離地");
        }
    }
}
