using NUnit.Framework;
using Project.Core.StateMachine;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 以固定 60 Hz 逐 tick 驅動兩條失地 grace 路徑，守住判定 tick 不受 <c>Time.time</c>
    /// 絕對起點影響。Phase A 捕捉到舊「絕對時刻相減」在 float32 邊界會讓部分案例晚一 tick；
    /// Phase B 保留相同案例作為 elapsed 方案的回歸條件。
    /// </summary>
    public sealed class GroundLossGraceDeterminismTests
    {
        private const float Never = -1f;
        private const int MaxTicks = 120;

        private enum GracePath
        {
            JumpState,
            MotionDriver
        }

        [TestCase(1.0f, 0.1f, 6)]
        [TestCase(2.0f, 0.1f, 6)]
        [TestCase(500.0f, 0.1f, 6)]
        [TestCase(1000.0f, 0.1f, 6)]
        [TestCase(1.0f, 0.35f, 21)]
        [TestCase(2.0f, 0.35f, 21)]
        [TestCase(500.0f, 0.35f, 21)]
        [TestCase(1000.0f, 0.35f, 21)]
        public void JumpState_LostGroundGrace_PassesOnSameTickRegardlessOfAbsoluteStart(
            float start,
            float grace,
            int expectedTick)
        {
            int actualTick = FindFirstPassingTick(GracePath.JumpState, start, grace);

            Assert.AreEqual(expectedTick, actualTick,
                $"JumpState 的失地 grace 不得受絕對起點影響（start={start}, grace={grace}）");
        }

        [TestCase(1.0f, 0.1f, 6)]
        [TestCase(2.0f, 0.1f, 6)]
        [TestCase(500.0f, 0.1f, 6)]
        [TestCase(1000.0f, 0.1f, 6)]
        [TestCase(1.0f, 0.35f, 21)]
        [TestCase(2.0f, 0.35f, 21)]
        [TestCase(500.0f, 0.35f, 21)]
        [TestCase(1000.0f, 0.35f, 21)]
        public void MotionDriver_GroundEdgeGrace_PassesOnSameTickRegardlessOfAbsoluteStart(
            float start,
            float grace,
            int expectedTick)
        {
            int actualTick = FindFirstPassingTick(GracePath.MotionDriver, start, grace);

            Assert.AreEqual(expectedTick, actualTick,
                $"MotionDriver 的 ground edge grace 不得受絕對起點影響（start={start}, grace={grace}）");
        }

        [Test]
        public void JumpState_ZeroGrace_FirstLostGroundTickPasses()
        {
            float elapsed = JumpState.AdvanceUngroundedElapsed(
                isGrounded: false, ungroundedElapsed: Never, deltaTime: 1f, isNewTick: true);

            Assert.AreEqual(0f, elapsed, "第一次失地觀測必須從 elapsed=0 開始，不吃本 tick 的 dt");
            Assert.IsTrue(JumpState.ShouldEnterFallFromLostGround(
                isGrounded: false, ungroundedElapsed: elapsed, fallEntryGrace: 0f));
        }

        [Test]
        public void JumpState_ShortLossThenGrounded_DoesNotEnterAndResetsElapsed()
        {
            float elapsed = JumpState.AdvanceUngroundedElapsed(false, Never, 0f, true);
            elapsed = JumpState.AdvanceUngroundedElapsed(false, elapsed, 0.05f, true);
            Assert.IsFalse(JumpState.ShouldEnterFallFromLostGround(false, elapsed, 0.1f));

            elapsed = JumpState.AdvanceUngroundedElapsed(true, elapsed, 0.05f, true);

            Assert.AreEqual(Never, elapsed, "grace 內重新著地必須清除累積時間");
            Assert.IsFalse(JumpState.ShouldEnterFallFromLostGround(true, elapsed, 0.1f));
        }

        [Test]
        public void JumpState_SecondLoss_RestartsAtZeroAndRequiresFullGrace()
        {
            float elapsed = JumpState.AdvanceUngroundedElapsed(false, Never, 0f, true);
            elapsed = JumpState.AdvanceUngroundedElapsed(false, elapsed, 0.05f, true);
            elapsed = JumpState.AdvanceUngroundedElapsed(true, elapsed, 0f, true);

            elapsed = JumpState.AdvanceUngroundedElapsed(false, elapsed, 10f, true);
            Assert.AreEqual(0f, elapsed, "第二段失地的第一次觀測必須重新從 0 開始");
            Assert.IsFalse(JumpState.ShouldEnterFallFromLostGround(false, elapsed, 0.1f));

            elapsed = JumpState.AdvanceUngroundedElapsed(false, elapsed, 0.05f, true);
            Assert.IsFalse(JumpState.ShouldEnterFallFromLostGround(false, elapsed, 0.1f));
            elapsed = JumpState.AdvanceUngroundedElapsed(false, elapsed, 0.05f, true);
            Assert.IsTrue(JumpState.ShouldEnterFallFromLostGround(false, elapsed, 0.1f),
                "第二段失地仍必須累積完整 grace 才能進 Falling");
        }

        [Test]
        public void JumpState_RepeatedCallInSameTick_DoesNotAccumulate()
        {
            float elapsed = JumpState.AdvanceUngroundedElapsed(false, Never, 0.1f, true);
            elapsed = JumpState.AdvanceUngroundedElapsed(false, elapsed, 0.1f, false);

            Assert.AreEqual(0f, elapsed, "同一 tick 的第二次 CanEnter 不得再次累積 deltaTime");
            Assert.IsFalse(JumpState.ShouldEnterFallFromLostGround(false, elapsed, 0.1f));

            elapsed = JumpState.AdvanceUngroundedElapsed(false, elapsed, 0.1f, true);
            Assert.IsTrue(JumpState.ShouldEnterFallFromLostGround(false, elapsed, 0.1f));
        }

        [Test]
        public void JumpState_VariableDeltaTime_UsesElapsedTime()
        {
            float elapsed = JumpState.AdvanceUngroundedElapsed(false, Never, 0f, true);
            float[] variableDeltaTimes = { 0.05f, 0.01f, 0.02f, 0.03f };
            for (int i = 0; i < variableDeltaTimes.Length; i++)
            {
                elapsed = JumpState.AdvanceUngroundedElapsed(
                    false, elapsed, variableDeltaTimes[i], true);
                Assert.AreEqual(i == variableDeltaTimes.Length - 1,
                    JumpState.ShouldEnterFallFromLostGround(false, elapsed, 0.1f),
                    $"可變 dt 第 {i + 1} 次累加後的 grace 判定錯誤");
            }

            elapsed = Never;
            int passingTick = -1;
            for (int tick = 0; tick <= 3; tick++)
            {
                elapsed = JumpState.AdvanceUngroundedElapsed(false, elapsed, 1f / 30f, true);
                if (JumpState.ShouldEnterFallFromLostGround(false, elapsed, 0.1f))
                {
                    passingTick = tick;
                    break;
                }
            }

            Assert.AreEqual(3, passingTick, "1/30 秒 dt 應在 tick 3 累積到 0.1 秒，而非沿用 60 Hz 帧數");
        }

        [Test]
        public void MotionDriver_ZeroGrace_FirstDivergenceTickPasses()
        {
            float elapsed = MotionDriver.TrackGroundStateDivergence(
                grounded: false, reportedGrounded: true, divergingElapsed: Never,
                deltaTime: 1f, isNewTick: true);

            Assert.AreEqual(0f, elapsed, "第一次分歧觀測必須從 elapsed=0 開始，不吃本 tick 的 dt");
            Assert.IsTrue(MotionDriver.ShouldConfirmGroundStateChange(elapsed, grace: 0f));
        }

        [Test]
        public void MotionDriver_ShortDivergenceThenConverged_DoesNotConfirmAndResetsElapsed()
        {
            float elapsed = MotionDriver.TrackGroundStateDivergence(false, true, Never, 0f, true);
            elapsed = MotionDriver.TrackGroundStateDivergence(false, true, elapsed, 0.05f, true);
            Assert.IsFalse(MotionDriver.ShouldConfirmGroundStateChange(elapsed, 0.1f));

            elapsed = MotionDriver.TrackGroundStateDivergence(true, true, elapsed, 0.05f, true);

            Assert.AreEqual(Never, elapsed, "grace 內重新一致必須清除累積時間");
            Assert.IsFalse(MotionDriver.ShouldConfirmGroundStateChange(elapsed, 0.1f));
        }

        [Test]
        public void MotionDriver_SecondDivergence_RestartsAtZeroAndRequiresFullGrace()
        {
            float elapsed = MotionDriver.TrackGroundStateDivergence(false, true, Never, 0f, true);
            elapsed = MotionDriver.TrackGroundStateDivergence(false, true, elapsed, 0.05f, true);
            elapsed = MotionDriver.TrackGroundStateDivergence(true, true, elapsed, 0f, true);

            elapsed = MotionDriver.TrackGroundStateDivergence(false, true, elapsed, 10f, true);
            Assert.AreEqual(0f, elapsed, "第二段分歧的第一次觀測必須重新從 0 開始");
            Assert.IsFalse(MotionDriver.ShouldConfirmGroundStateChange(elapsed, 0.1f));

            elapsed = MotionDriver.TrackGroundStateDivergence(false, true, elapsed, 0.05f, true);
            Assert.IsFalse(MotionDriver.ShouldConfirmGroundStateChange(elapsed, 0.1f));
            elapsed = MotionDriver.TrackGroundStateDivergence(false, true, elapsed, 0.05f, true);
            Assert.IsTrue(MotionDriver.ShouldConfirmGroundStateChange(elapsed, 0.1f),
                "第二段分歧仍必須累積完整 grace 才能確認邊沿");
        }

        [Test]
        public void MotionDriver_RepeatedCallInSameTick_DoesNotAccumulate()
        {
            float elapsed = MotionDriver.TrackGroundStateDivergence(false, true, Never, 0.1f, true);
            elapsed = MotionDriver.TrackGroundStateDivergence(false, true, elapsed, 0.1f, false);

            Assert.AreEqual(0f, elapsed, "同一 tick 的第二次同步不得再次累積 deltaTime");
            Assert.IsFalse(MotionDriver.ShouldConfirmGroundStateChange(elapsed, 0.1f));

            elapsed = MotionDriver.TrackGroundStateDivergence(false, true, elapsed, 0.1f, true);
            Assert.IsTrue(MotionDriver.ShouldConfirmGroundStateChange(elapsed, 0.1f));
        }

        [Test]
        public void MotionDriver_VariableDeltaTime_UsesElapsedTime()
        {
            float elapsed = MotionDriver.TrackGroundStateDivergence(false, true, Never, 0f, true);
            float[] variableDeltaTimes = { 0.05f, 0.01f, 0.02f, 0.03f };
            for (int i = 0; i < variableDeltaTimes.Length; i++)
            {
                elapsed = MotionDriver.TrackGroundStateDivergence(
                    false, true, elapsed, variableDeltaTimes[i], true);
                Assert.AreEqual(i == variableDeltaTimes.Length - 1,
                    MotionDriver.ShouldConfirmGroundStateChange(elapsed, 0.1f),
                    $"可變 dt 第 {i + 1} 次累加後的 grace 判定錯誤");
            }

            elapsed = Never;
            int passingTick = -1;
            for (int tick = 0; tick <= 3; tick++)
            {
                elapsed = MotionDriver.TrackGroundStateDivergence(
                    false, true, elapsed, 1f / 30f, true);
                if (MotionDriver.ShouldConfirmGroundStateChange(elapsed, 0.1f))
                {
                    passingTick = tick;
                    break;
                }
            }

            Assert.AreEqual(3, passingTick, "1/30 秒 dt 應在 tick 3 累積到 0.1 秒，而非沿用 60 Hz 帧數");
        }

        /// <summary>
        /// tick 0 是第一個失地 tick；絕對起點刻意保留在測試資料中，但已不再進入 grace API，
        /// 守住相同的 dt 序列在任何 <c>Time.time</c> 起點都得到相同通過 tick。
        /// </summary>
        private static int FindFirstPassingTick(GracePath path, float start, float grace)
        {
            _ = start;
            float elapsed = Never;

            for (int tick = 0; tick <= MaxTicks; tick++)
            {
                bool passed;

                if (path == GracePath.JumpState)
                {
                    elapsed = JumpState.AdvanceUngroundedElapsed(
                        isGrounded: false,
                        ungroundedElapsed: elapsed,
                        deltaTime: 1f / 60f,
                        isNewTick: true);
                    passed = JumpState.ShouldEnterFallFromLostGround(
                        isGrounded: false, ungroundedElapsed: elapsed, fallEntryGrace: grace);
                }
                else
                {
                    elapsed = MotionDriver.TrackGroundStateDivergence(
                        grounded: false,
                        reportedGrounded: true,
                        divergingElapsed: elapsed,
                        deltaTime: 1f / 60f,
                        isNewTick: true);
                    passed = MotionDriver.ShouldConfirmGroundStateChange(
                        divergingElapsed: elapsed, grace: grace);
                }

                if (passed) return tick;
            }

            return -1;
        }
    }
}
