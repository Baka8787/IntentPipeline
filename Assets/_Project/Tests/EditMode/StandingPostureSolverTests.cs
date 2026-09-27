using NUnit.Framework;
using UnityEngine;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 停步站直（docs/05 §3.5.4.6）純函數：①Early Stop Recovery ②Primary Support Rise（承接速度）
    /// ③尾腳前瞻可達上限（只封頂、不牽引）④Final Standing Settle，以及取消／重設的連續性。
    /// 幾何：腿長 1.0（0.5＋0.5）、髖在腳上方 1.0、包絡 −0.35 ⇒ 起始腿長 0.65。
    /// </summary>
    public class StandingPostureSolverTests
    {
        private const float Dt = 1f / 60f;
        private const float Eps = 1e-5f;
        private static readonly PostureTuning Tuning = PostureTuning.Default;

        private static PostureFootInputs Foot(bool reliable, FootLockPhase phase = FootLockPhase.Locked,
            float beta = 1f, float horizontalSpeed = 0f, float verticalSpeed = 0f) =>
            new PostureFootInputs
            {
                Reliable = reliable,
                LiveContact = reliable || phase == FootLockPhase.Locked,
                Phase = phase,
                Beta = beta,
                HorizontalSpeed = horizontalSpeed,
                VerticalSpeed = verticalSpeed,
            };

        private static PostureFootInputs Swing() => Foot(false, FootLockPhase.Unlocked, 0.2f, 1.5f, 0.3f);

        private static PostureInputs Input(float e = -0.35f, float r = -0.1f, float c = 0f,
            float rootSpeed = 0.5f, bool movement = false,
            PostureFootInputs left = default, PostureFootInputs right = default,
            float hipY = 1f, float hipNextY = float.NaN, Vector3? rightTarget = null) =>
            new PostureInputs
            {
                Envelope = e,
                Preferred = r,
                HardLimit = c,
                RootSpeed = rootSpeed,
                MovementRequested = movement,
                PreferredExtension = 0.995f,
                LeftHip = new Vector3(0f, float.IsNaN(hipNextY) ? hipY : hipNextY, 0f),
                RightHip = new Vector3(0.2f, float.IsNaN(hipNextY) ? hipY : hipNextY, 0f),
                LeftHipCurrent = new Vector3(0f, hipY, 0f),
                RightHipCurrent = new Vector3(0.2f, hipY, 0f),
                LeftTarget = Vector3.zero,
                RightTarget = rightTarget ?? new Vector3(0.2f, 0f, 0f),
                LeftUpperLegLength = 0.5f,
                LeftLowerLegLength = 0.5f,
                RightUpperLegLength = 0.5f,
                RightLowerLegLength = 0.5f,
                Left = left,
                Right = right,
            };

        private static void Step(ref PostureState state, in PostureInputs input, ref float pelvis)
            => StandingPostureSolver.Step(ref state, in input, Dt, ref pelvis, in Tuning);

        /// <summary>Return 第一帧：速度原樣承接（不重設），位移只差一帧的彈簧加速度。</summary>
        private static void AssertReturnCarries(in PostureState state, float before, float velocity, float after, float envelope)
        {
            Assert.AreEqual(PosturePhase.Return, state.Phase);
            Assert.AreEqual(velocity, state.DebugVelocityBefore, 1e-6f, "進入 Return 時速度不重設");
            float springAccel = Tuning.ReturnOmega * Tuning.ReturnOmega * Mathf.Abs(envelope - before)
                                + 2f * Tuning.ReturnOmega * Mathf.Abs(velocity);
            Assert.AreEqual(before + velocity * Dt, after, springAccel * Dt * Dt + 1e-4f, "位置連續、只多一帧彈簧加速度");
        }

        /// <summary>移動中收斂到包絡，然後放開（Early Recovery），再讓左腳成為新可靠落腳（Primary）。</summary>
        private static void MoveReleaseAndPlantLeft(ref PostureState state, ref float pelvis,
            PostureFootInputs trailing, Vector3? rightTarget = null)
        {
            for (int i = 0; i < 30; i++)
            {
                var moving = Input(movement: true, rootSpeed: 2f, left: Swing(), right: trailing, rightTarget: rightTarget);
                Step(ref state, in moving, ref pelvis);
            }
            var release = Input(rootSpeed: 2f, left: Swing(), right: trailing, rightTarget: rightTarget);
            Step(ref state, in release, ref pelvis);
            Assert.AreEqual(PosturePhase.EarlyRecovery, state.Phase);
            var plant = Input(rootSpeed: 0.5f, left: Foot(true), right: trailing, rightTarget: rightTarget);
            Step(ref state, in plant, ref pelvis);
            Assert.AreEqual(PosturePhase.StopProgress, state.Phase);
            Assert.AreEqual(PostureFoot.Left, state.PrimaryFoot);
        }

        // ---- geometry helpers ------------------------------------------------------------------------------------

        [Test]
        public void LegBound_StraightDown_IsReachMinusHeight()
        {
            float p = StandingPostureSolver.LegBound(new Vector3(0f, 1f, 0f), Vector3.zero, 0.9f);
            Assert.AreEqual(-0.1f, p, Eps);
        }

        [Test]
        public void LegBound_IsContinuousAtHorizontalReach()
        {
            Vector3 hip = new Vector3(0f, 1f, 0f);
            float inside = StandingPostureSolver.LegBound(hip, new Vector3(0.9f - 1e-4f, 0f, 0f), 0.9f);
            float outside = StandingPostureSolver.LegBound(hip, new Vector3(0.9f + 1e-4f, 0f, 0f), 0.9f);
            Assert.Less(Mathf.Abs(inside - outside), 0.02f);
            Assert.AreEqual(-1f, outside, Eps);
        }

        [Test]
        public void SupportLegPelvisOffset_DerivesHeightFromFixedFootAndDesiredLength()
        {
            float pelvis = StandingPostureSolver.SupportLegPelvisOffset(new Vector3(0.3f, 1f, 0f), Vector3.zero, 0.5f);
            Assert.AreEqual(-0.6f, pelvis, Eps);
        }

        [Test]
        public void KneeAngle_IsZeroAtFullExtensionAndIncreasesWhenBent()
        {
            Assert.AreEqual(0f, StandingPostureSolver.KneeAngle(1f, 0.5f, 0.5f), Eps);
            Assert.AreEqual(120f, StandingPostureSolver.KneeAngle(0.5f, 0.5f, 0.5f), 0.001f);
        }

        [Test]
        public void SaturateBelow_IsIdentityBelowBandAndNeverExceedsCap()
        {
            Assert.AreEqual(-0.2f, StandingPostureSolver.SaturateBelow(-0.2f, 0f, 0.03f), Eps);
            Assert.AreEqual(-0.03f, StandingPostureSolver.SaturateBelow(-0.03f, 0f, 0.03f), Eps);
            float previous = float.NegativeInfinity;
            for (float v = -0.05f; v < 0.3f; v += 0.001f)
            {
                float s = StandingPostureSolver.SaturateBelow(v, 0f, 0.03f);
                Assert.LessOrEqual(s, 0f);
                Assert.GreaterOrEqual(s, previous - Eps, "單調");
                previous = s;
            }
            Assert.AreEqual(0.1f, StandingPostureSolver.SaturateBelow(0.1f, 0.1f, 0f), Eps, "band 0 ⇒ min");
        }

        [Test]
        public void HermiteToRest_StartsWithGivenSlopeAndEndsAtRest()
        {
            float h = 1e-4f;
            float slope0 = (StandingPostureSolver.HermiteToRest(0.65f, 0.3f, 0.995f, h) - 0.65f) / h;
            Assert.AreEqual(0.3f, slope0, 1e-3f);
            Assert.AreEqual(0.995f, StandingPostureSolver.HermiteToRest(0.65f, 0.3f, 0.995f, 1f), Eps);
            float slope1 = (0.995f - StandingPostureSolver.HermiteToRest(0.65f, 0.3f, 0.995f, 1f - h)) / h;
            Assert.AreEqual(0f, slope1, 1e-2f);
        }

        // ---- Moving -------------------------------------------------------------------------------------------------

        [Test]
        public void ZeroDeltaTime_DoesNothing()
        {
            var state = default(PostureState);
            float pelvis = -0.2f;
            var input = Input(left: Foot(true));
            StandingPostureSolver.Step(ref state, in input, 0f, ref pelvis, in Tuning);
            Assert.AreEqual(-0.2f, pelvis);
            Assert.AreEqual(PosturePhase.Moving, state.Phase);
            Assert.IsFalse(state.PreviousLeftReliable);
        }

        [Test]
        public void Moving_IsExactlyExistingEnvelopeMoveTowards()
        {
            var state = default(PostureState);
            float pelvis = 0f;
            float expected = 0f;
            for (int i = 0; i < 20; i++)
            {
                var input = Input(movement: true);
                Step(ref state, in input, ref pelvis);
                expected = Mathf.MoveTowards(expected, -0.35f, 5f * Dt);
                Assert.AreEqual(expected, pelvis);
            }
        }

        [Test]
        public void ExistingReliableFootAndFastPlant_DoNotStartPrimaryRise()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            var moving = Input(movement: true, left: Foot(true));
            Step(ref state, in moving, ref pelvis);
            var alreadyReliable = Input(left: Foot(true));
            Step(ref state, in alreadyReliable, ref pelvis);
            Assert.AreNotEqual(PosturePhase.StopProgress, state.Phase, "不是新邊沿");

            state = default;
            var swing = Input(movement: true, left: Swing());
            Step(ref state, in swing, ref pelvis);
            var fastPlant = Input(rootSpeed: 2f, left: Foot(true));
            Step(ref state, in fastPlant, ref pelvis);
            Assert.AreNotEqual(PosturePhase.StopProgress, state.Phase, "root 太快：不是最後支撐");
        }

        // ---- ① Early Stop Recovery ---------------------------------------------------------------------------------

        [Test]
        public void Release_EntersEarlyRecovery_AndDoesNotLiftWhileRootKeepsSpeed()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            var moving = Input(movement: true, rootSpeed: 2f, left: Swing());
            Step(ref state, in moving, ref pelvis);
            for (int i = 0; i < 10; i++)
            {
                var released = Input(rootSpeed: 2f, left: Swing());
                Step(ref state, in released, ref pelvis);
                Assert.AreEqual(PosturePhase.EarlyRecovery, state.Phase);
                Assert.AreEqual(-0.35f, pelvis, Eps, "root 還沒減速 ⇒ 停步進度 0 ⇒ 包絡路徑原樣");
            }
        }

        [Test]
        public void EarlyRecovery_LiftsContinuouslyAsRootSlows_AndStaysUnderHardReach()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            var moving = Input(movement: true, rootSpeed: 2f, left: Swing());
            Step(ref state, in moving, ref pelvis);
            float previous = pelvis;
            for (int i = 0; i < 40; i++)
            {
                float root = Mathf.Lerp(2f, 0.2f, i / 39f);
                var slowing = Input(c: -0.15f, rootSpeed: root, left: Swing());
                Step(ref state, in slowing, ref pelvis);
                Assert.AreEqual(PosturePhase.EarlyRecovery, state.Phase);
                Assert.LessOrEqual(pelvis, -0.15f + Eps, "硬可達上限");
                Assert.GreaterOrEqual(pelvis, previous - Eps, "只釋放、不回壓");
                Assert.Less(pelvis - previous, 0.01f, "逐帧連續");
                previous = pelvis;
            }
            Assert.Greater(pelvis, -0.30f, "root 減速期間確實開始站起");
        }

        [Test]
        public void EarlyRecovery_RetreatsAtLimitedSpeedWhenHeadroomCollapses()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            var moving = Input(movement: true, rootSpeed: 2f, left: Swing());
            Step(ref state, in moving, ref pelvis);
            var release = Input(rootSpeed: 2f, left: Swing());
            Step(ref state, in release, ref pelvis);
            for (int i = 0; i < 40; i++)
            {
                var slowing = Input(c: 0f, rootSpeed: 0.2f, left: Swing());
                Step(ref state, in slowing, ref pelvis);
            }
            float lifted = pelvis;
            var collapsed = Input(c: -0.34f, rootSpeed: 0.2f, left: Swing());
            Step(ref state, in collapsed, ref pelvis);
            Assert.GreaterOrEqual(pelvis, lifted - Tuning.EarlyLiftRetreatSpeed * Dt - Eps, "不瞬間下拉");
            Assert.Less(pelvis, lifted);
        }

        [Test]
        public void EarlyRecovery_CancelReturnsWithContinuousPositionAndVelocity()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            var moving = Input(movement: true, rootSpeed: 2f, left: Swing());
            Step(ref state, in moving, ref pelvis);
            for (int i = 0; i < 20; i++)
            {
                var slowing = Input(rootSpeed: Mathf.Lerp(2f, 0.5f, i / 19f), left: Swing());
                Step(ref state, in slowing, ref pelvis);
            }
            float before = pelvis;
            float velocity = state.Velocity;
            Assert.Greater(velocity, 0f);
            var restart = Input(movement: true, rootSpeed: 0.6f, left: Swing());
            Step(ref state, in restart, ref pelvis);
            AssertReturnCarries(in state, before, velocity, pelvis, -0.35f);
            for (int i = 0; i < 90 && state.Phase != PosturePhase.Moving; i++)
            {
                float prev = pelvis;
                Step(ref state, in restart, ref pelvis);
                Assert.Less(Mathf.Abs(pelvis - prev), 0.03f);
            }
            Assert.AreEqual(PosturePhase.Moving, state.Phase);
        }

        // ---- ② Primary Support Rise --------------------------------------------------------------------------------

        [Test]
        public void PrimaryRise_CarriesIncomingPelvisVelocity_InsteadOfRestartingFromZero()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            var moving = Input(movement: true, rootSpeed: 2f, left: Swing(), hipY: 1f);
            Step(ref state, in moving, ref pelvis);
            // 放開帧：髖（未外插）上升中 1.2 m/s；offset 仍在包絡。
            var release = Input(rootSpeed: 2f, left: Swing(), hipY: 1f, hipNextY: 1.02f);
            Step(ref state, in release, ref pelvis);
            float displayedBefore = 1f + pelvis;
            var plant = Input(rootSpeed: 0.5f, left: Foot(true), hipY: 1.02f, hipNextY: 1.04f);
            Step(ref state, in plant, ref pelvis);
            Assert.AreEqual(PosturePhase.StopProgress, state.Phase);
            Assert.AreEqual(1.2f, state.PrimaryExtensionSpeed, 0.01f, "承接畫面上的支撐腿伸展速度");
            float displayedAtPlant = 1.02f + (-0.35f);
            float displayedNext = 1.04f + pelvis;
            float incoming = (displayedAtPlant - displayedBefore) / Dt;
            float outgoing = (displayedNext - displayedAtPlant) / Dt;
            Assert.AreEqual(incoming, outgoing, 0.2f, "進入 primary 的那一帧不減速、不從 0 起步");
        }

        [Test]
        public void PrimaryRise_ReachesIdleLegLength_AndKeepsSupportFootFixed()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            MoveReleaseAndPlantLeft(ref state, ref pelvis, Foot(false, FootLockPhase.Unlocked, 0.9f, 0.5f, -0.2f));
            for (int i = 0; i < 60; i++)
            {
                var hold = Input(rootSpeed: 0.1f, left: Foot(true), right: Foot(false, FootLockPhase.Unlocked, 0.9f, 0.5f, -0.2f));
                Step(ref state, in hold, ref pelvis);
            }
            Assert.AreEqual(Vector3.zero, state.PrimarySupportPosition);
            Assert.AreEqual(0.995f, state.PrimaryDesiredLegLength, 1e-4f);
            Assert.AreEqual(0.995f, 1f + pelvis, 1e-3f, "髖在腳正上方 ⇒ 顯示髖高 ＝ 目標腿長");
            Assert.AreEqual(PosturePhase.StopProgress, state.Phase, "尾腳未落定前不進 Upright");
        }

        // ---- ③ Trailing prospective reach cap ----------------------------------------------------------------------

        [Test]
        public void SwingingTrailingFoot_IsNeverACap()
        {
            var lowTarget = new Vector3(0.2f, -0.5f, 0.4f);
            var stateA = default(PostureState); float a = -0.35f;
            var stateB = default(PostureState); float b = -0.35f;
            MoveReleaseAndPlantLeft(ref stateA, ref a, Swing(), lowTarget);
            MoveReleaseAndPlantLeft(ref stateB, ref b, Swing());
            for (int i = 0; i < 40; i++)
            {
                var ia = Input(rootSpeed: 0.1f, left: Foot(true), right: Foot(false, FootLockPhase.Releasing, 0.4f, 0.8f), rightTarget: lowTarget);
                var ib = Input(rootSpeed: 0.1f, left: Foot(true), right: Foot(false, FootLockPhase.Releasing, 0.4f, 0.8f));
                Step(ref stateA, in ia, ref a);
                Step(ref stateB, in ib, ref b);
                Assert.AreEqual(b, a, Eps, "擺盪／釋放中的尾腳不給上限：它的即時目標不能改變主軌跡");
                Assert.IsFalse(stateA.TrailingLatched);
            }
        }

        [Test]
        public void LockedTrailingFoot_CapsRiseWithoutActivationJump_AndStaysReachable()
        {
            // 尾腳鎖在低一階（−0.18），跨階站姿；對照組的尾腳在同一階（上限永遠不會生效）。
            var lower = new Vector3(0.2f, -0.18f, 0.35f);
            var level = new Vector3(0.2f, 0f, 0f);
            var capped = default(PostureState); float c = -0.35f;
            var free = default(PostureState); float f = -0.35f;
            MoveReleaseAndPlantLeft(ref capped, ref c, Foot(true), lower);
            MoveReleaseAndPlantLeft(ref free, ref f, Foot(true), level);
            Assert.IsTrue(capped.TrailingLatched, "已 Locked 且可靠的尾腳：第一支撐當帧就鎖存");
            bool activated = false;
            for (int i = 0; i < 60; i++)
            {
                var rollingLow = Input(rootSpeed: 0.1f, left: Foot(true), right: Foot(false, FootLockPhase.Locked, 0.8f, 0.6f), rightTarget: lower);
                var rollingLevel = Input(rootSpeed: 0.1f, left: Foot(true), right: Foot(false, FootLockPhase.Locked, 0.8f, 0.6f), rightTarget: level);
                Step(ref capped, in rollingLow, ref c);
                Step(ref free, in rollingLevel, ref f);
                float cap = StandingPostureSolver.LegBound(new Vector3(0.2f, 1f, 0f), lower, 1f);
                Assert.LessOrEqual(c, cap + Eps, "尾腳必須一直搆得到");
                if (!activated)
                {
                    if (capped.DebugTrailingCapApplied)
                    {
                        activated = true;
                        Assert.AreEqual(f, c, 0.003f, "上限啟用的那一帧與不受限的上升幾乎相同（單邊飽和，C1 起點）");
                    }
                    else
                    {
                        Assert.AreEqual(f, c, Eps, "低於上限 − 帶寬：支撐腿上升原樣不動");
                    }
                }
            }
            Assert.IsTrue(activated, "此幾何下上限確實生效");
        }

        [Test]
        public void TrailingCap_LatchesOncePerLock_IgnoringLiveTargetSpikes()
        {
            var lower = new Vector3(0.2f, -0.18f, 0.35f);
            var spiked = new Vector3(0.2f, -0.01f, 0.35f); // 抬腳時射線打到上一階（+17 cm）
            var stateA = default(PostureState); float a = -0.35f;
            var stateB = default(PostureState); float b = -0.35f;
            MoveReleaseAndPlantLeft(ref stateA, ref a, Foot(true), lower);
            MoveReleaseAndPlantLeft(ref stateB, ref b, Foot(true), lower);
            for (int i = 0; i < 40; i++)
            {
                Vector3 live = i % 7 == 3 ? spiked : lower;
                // 尾腳仍 Locked、但動畫腳在滾動（不可靠）：即時目標被射線拉跳，鎖存值不能跟著動。
                var ia = Input(rootSpeed: 0.1f, left: Foot(true), right: Foot(false, FootLockPhase.Locked, 0.9f, 0.4f), rightTarget: live);
                var ib = Input(rootSpeed: 0.1f, left: Foot(true), right: Foot(false, FootLockPhase.Locked, 0.9f, 0.4f), rightTarget: lower);
                Step(ref stateA, in ia, ref a);
                Step(ref stateB, in ib, ref b);
                Assert.AreEqual(b, a, Eps, "鎖定期間不更新鎖存值：即時射線的跳動不影響骨盆");
                Assert.AreEqual(lower, stateA.TrailingLatchPosition);
            }
        }

        [Test]
        public void TrailingLeavingLock_ReleasesCapWithoutUpwardJump()
        {
            var lower = new Vector3(0.2f, -0.18f, 0.35f);
            var state = default(PostureState);
            float pelvis = -0.35f;
            MoveReleaseAndPlantLeft(ref state, ref pelvis, Foot(true), lower);
            for (int i = 0; i < 20; i++)
            {
                var locked = Input(rootSpeed: 0.1f, left: Foot(true), right: Foot(false, FootLockPhase.Locked, 0.8f, 0.6f), rightTarget: lower);
                Step(ref state, in locked, ref pelvis);
            }
            Assert.Greater(state.TrailingCapGap, 0.01f, "前提：上限正在壓住上升");
            float previous = pelvis;
            for (int i = 0; i < 40; i++)
            {
                var released = Input(rootSpeed: 0.1f, left: Foot(true), right: Foot(false, FootLockPhase.Releasing, 0.5f, 0.8f), rightTarget: lower);
                Step(ref state, in released, ref pelvis);
                Assert.IsFalse(state.TrailingLatched);
                Assert.Less(pelvis - previous, 0.02f, "上限消失：差距以固定速率收掉，不往上跳");
                previous = pelvis;
            }
        }

        // ---- ④ Final Standing Settle -------------------------------------------------------------------------------

        [Test]
        public void TrailingFinalPlant_AppliesOnlyMinimumTwoFootCorrection()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            var swing = Foot(false, FootLockPhase.Unlocked, 0.9f, 0.5f, -0.2f);
            MoveReleaseAndPlantLeft(ref state, ref pelvis, swing);
            for (int i = 0; i < 30; i++)
            {
                var hold = Input(rootSpeed: 0.1f, left: Foot(true), right: swing);
                Step(ref state, in hold, ref pelvis);
            }
            float before = pelvis;
            var plantedOk = Input(c: before + 0.05f, rootSpeed: 0f, left: Foot(true), right: Foot(true));
            Step(ref state, in plantedOk, ref pelvis);
            Assert.IsTrue(state.TrailingSettled);
            Assert.AreEqual(before, pelvis, 0.005f, "高度有效 ⇒ 保留，不跳到新目標");

            var tighter = Input(c: pelvis - 0.004f, rootSpeed: 0f, left: Foot(true), right: Foot(true));
            float kept = pelvis;
            Step(ref state, in tighter, ref pelvis);
            Assert.AreEqual(kept - 0.004f, pelvis, 1e-3f, "無效 ⇒ 只做最小修正");
        }

        [Test]
        public void TrailingFootAlreadyReliableAtEntry_MustLeaveBeforeItCanSettle()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            MoveReleaseAndPlantLeft(ref state, ref pelvis, Foot(true));
            for (int i = 0; i < 40; i++)
            {
                var both = Input(rootSpeed: 0f, left: Foot(true), right: Foot(true));
                Step(ref state, in both, ref pelvis);
            }
            Assert.IsFalse(state.TrailingSettled, "一直可靠的尾腳沒有「最後落定」邊沿");
            Assert.AreEqual(PosturePhase.StopProgress, state.Phase);
        }

        // ---- cancel / reset ----------------------------------------------------------------------------------------

        [Test]
        public void RestartDuringPrimaryRise_EntersReturnWithContinuousPositionAndVelocity()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            MoveReleaseAndPlantLeft(ref state, ref pelvis, Swing());
            for (int i = 0; i < 5; i++)
            {
                var rising = Input(rootSpeed: 0.1f, left: Foot(true), right: Swing());
                Step(ref state, in rising, ref pelvis);
            }
            float before = pelvis;
            float velocity = state.Velocity;
            Assert.Greater(velocity, 0f);
            var restart = Input(movement: true, rootSpeed: 0.3f, left: Foot(true), right: Swing());
            Step(ref state, in restart, ref pelvis);
            Assert.IsFalse(state.TrailingLatched);
            AssertReturnCarries(in state, before, velocity, pelvis, -0.35f);
        }

        [Test]
        public void PrimarySupportLoss_EntersReturn()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            MoveReleaseAndPlantLeft(ref state, ref pelvis, Swing());
            var lost = Input(rootSpeed: 0.1f, left: Swing(), right: Swing());
            Step(ref state, in lost, ref pelvis);
            Assert.AreEqual(PosturePhase.Return, state.Phase);
        }

        [Test]
        public void ReturnHandsBackToMoving()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            MoveReleaseAndPlantLeft(ref state, ref pelvis, Swing());
            var restart = Input(movement: true, left: Swing(), right: Swing());
            for (int i = 0; i < 120 && state.Phase != PosturePhase.Moving; i++)
                Step(ref state, in restart, ref pelvis);
            Assert.AreEqual(PosturePhase.Moving, state.Phase);
            Assert.AreEqual(-0.35f, pelvis, 0.01f);
        }

        [Test]
        public void Return_RetreatsAtLimitedSpeed_AndNeverBelowEnvelope_WhenReachCollapses()
        {
            // 跑下樓時支撐加權的 C 會瞬間掉到包絡以下（實測 −0.45 vs E −0.31）：Return 不能單帧下拉到 C。
            var state = default(PostureState);
            float pelvis = -0.35f;
            MoveReleaseAndPlantLeft(ref state, ref pelvis, Swing());
            for (int i = 0; i < 8; i++)
            {
                var rising = Input(rootSpeed: 0.1f, left: Foot(true), right: Swing());
                Step(ref state, in rising, ref pelvis);
            }
            Assert.Greater(pelvis, -0.30f, "前提：骨盆已升起");
            var restart = Input(e: -0.34f, c: 1f, movement: true, left: Swing(), right: Swing());
            Step(ref state, in restart, ref pelvis);
            Assert.AreEqual(PosturePhase.Return, state.Phase);
            for (int i = 0; i < 30; i++)
            {
                float before = pelvis;
                var collapsed = Input(e: -0.34f, c: -0.45f, movement: true, left: Swing(), right: Swing());
                Step(ref state, in collapsed, ref pelvis);
                Assert.GreaterOrEqual(pelvis, -0.34f - 0.02f, "C 低於包絡時不被拉到 C（只容許彈簧對包絡約 1 cm 的過衝）");
                Assert.Less(before - pelvis, 0.035f, "逐帧下降有界，不瞬間下拉");
            }
        }

        [Test]
        public void ReleaseDuringReturn_StartsEarlyRecovery()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            MoveReleaseAndPlantLeft(ref state, ref pelvis, Swing());
            var restart = Input(movement: true, rootSpeed: 2f, left: Swing(), right: Swing());
            Step(ref state, in restart, ref pelvis);
            Assert.AreEqual(PosturePhase.Return, state.Phase);
            float before = pelvis;
            var release = Input(rootSpeed: 2f, left: Swing(), right: Swing());
            Step(ref state, in release, ref pelvis);
            Assert.AreEqual(PosturePhase.EarlyRecovery, state.Phase);
            Assert.Less(Mathf.Abs(pelvis - before), 0.03f);
        }

        [Test]
        public void Reset_ClearsAllHistory()
        {
            var state = default(PostureState);
            float pelvis = -0.35f;
            MoveReleaseAndPlantLeft(ref state, ref pelvis, Foot(true), new Vector3(0.2f, -0.18f, 0.35f));
            state.Reset();
            Assert.AreEqual(PosturePhase.Moving, state.Phase);
            Assert.AreEqual(PostureFoot.None, state.PrimaryFoot);
            Assert.IsFalse(state.TrailingLatched);
            Assert.IsFalse(state.HasPreviousMovement);
            Assert.IsFalse(state.HasPreviousDisplayedHips);
        }
    }
}
