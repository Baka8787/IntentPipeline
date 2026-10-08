using NUnit.Framework;
using UnityEngine;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 統一骨盆求解器——Moving（docs/46 §6）純函數：腳的角色（Support／Landing／無）、Intent 0、
    /// 非對稱臨界阻尼、硬可達（Leg Stretch）與下限，以及啟用時的連續性。
    /// 幾何：腿長 1.0、髖在原點上方 1.0；目標在髖正下方 ⇒ 上限 ＝ 目標高度（完全伸直剛好搆到）。
    /// </summary>
    public class MovingPelvisSolverTests
    {
        private const float Dt = 1f / 60f;
        private const float Eps = 1e-5f;
        private static readonly MovingPelvisTuning Tuning = MovingPelvisTuning.Default;
        private const float NoHint = float.NaN;

        private static MovingPelvisFoot Foot(FootLockPhase phase, float targetY, float soleHeight = 0f,
            float horizontal = 0f, bool hasFloor = true) =>
            new MovingPelvisFoot
            {
                Phase = phase,
                HipNext = new Vector3(0f, 1f, 0f),
                Target = new Vector3(horizontal, targetY, 0f),
                LegLength = 1f,
                HasFloor = hasFloor,
                SoleHeight = soleHeight,
            };

        private static MovingPelvisFoot Free() => Foot(FootLockPhase.Releasing, -5f);

        private static MovingPelvisState Started(float offset = 0f)
        {
            var s = default(MovingPelvisState);
            MovingPelvisSolver.Begin(ref s, offset);
            return s;
        }

        private static void Run(ref MovingPelvisState s, MovingPelvisFoot l, MovingPelvisFoot r, int frames)
        {
            for (int i = 0; i < frames; i++) MovingPelvisSolver.Step(ref s, in l, in r, NoHint, Dt, in Tuning);
        }

        // ---------- 角色 ----------

        [Test]
        public void Locked_IsSupport_WithFullLegAndStretchHardLimit()
        {
            MovingPelvisFoot f = Foot(FootLockPhase.Locked, -0.2f);
            int role = MovingPelvisSolver.FootLimit(in f, false, 0f, in Tuning, out float limit, out float hard);
            Assert.AreEqual(MovingPelvisSolver.RoleSupport, role);
            Assert.AreEqual(-0.2f, limit, Eps);
            Assert.AreEqual(-0.2f + 0.05f, hard, Eps, "硬上限 ＝ 1.05 × 腿長（Humanoid Leg Stretch）");
        }

        [Test]
        public void Releasing_NeverConstrains()
        {
            MovingPelvisFoot f = Foot(FootLockPhase.Releasing, -0.7f);
            int role = MovingPelvisSolver.FootLimit(in f, false, 0f, in Tuning, out float limit, out float hard);
            Assert.AreEqual(MovingPelvisSolver.RoleNone, role);
            Assert.IsTrue(float.IsPositiveInfinity(limit) && float.IsPositiveInfinity(hard));
        }

        [Test]
        public void DescendingUnlocked_IsLanding_RelaxedBySoleHeight()
        {
            MovingPelvisFoot f = Foot(FootLockPhase.Unlocked, -0.3f, soleHeight: 0.1f);
            int role = MovingPelvisSolver.FootLimit(in f, true, 0.12f, in Tuning, out float limit, out float hard);
            Assert.AreEqual(MovingPelvisSolver.RoleLanding, role);
            Assert.AreEqual(-0.3f + 1.5f * 0.1f, limit, Eps);
            Assert.IsTrue(float.IsPositiveInfinity(hard), "Landing 只預告，不是硬約束");
        }

        [Test]
        public void RisingUnlocked_OrNoFloor_IsNotLanding()
        {
            MovingPelvisFoot rising = Foot(FootLockPhase.Unlocked, -0.3f, soleHeight: 0.1f);
            Assert.AreEqual(MovingPelvisSolver.RoleNone,
                MovingPelvisSolver.FootLimit(in rising, true, 0.05f, in Tuning, out _, out _));
            MovingPelvisFoot noFloor = Foot(FootLockPhase.Unlocked, -0.3f, soleHeight: 0.1f, hasFloor: false);
            Assert.AreEqual(MovingPelvisSolver.RoleNone,
                MovingPelvisSolver.FootLimit(in noFloor, true, 0.12f, in Tuning, out _, out _));
        }

        [Test]
        public void Landing_LimitIsContinuousIntoSupport_AtContact()
        {
            // 腳底落到地面（h→0）的那一刻，Landing 上限等於鎖定後的 Support 上限：加入約束不會迫使下跳。
            MovingPelvisFoot landing = Foot(FootLockPhase.Unlocked, -0.25f, soleHeight: 0f);
            MovingPelvisSolver.FootLimit(in landing, true, 0.01f, in Tuning, out float before, out _);
            MovingPelvisFoot locked = Foot(FootLockPhase.Locked, -0.25f);
            MovingPelvisSolver.FootLimit(in locked, true, 0f, in Tuning, out float after, out _);
            Assert.AreEqual(before, after, Eps);
        }

        [Test]
        public void InvalidLeg_NeverConstrains()
        {
            MovingPelvisFoot f = Foot(FootLockPhase.Locked, -0.2f);
            f.LegLength = 0f;
            Assert.AreEqual(MovingPelvisSolver.RoleNone,
                MovingPelvisSolver.FootLimit(in f, false, 0f, in Tuning, out _, out _));
        }

        // ---------- 求解器 ----------

        [Test]
        public void NoConstraint_StaysAtNominalIntent()
        {
            var s = Started(0f);
            Run(ref s, Free(), Free(), 120);
            Assert.AreEqual(0f, s.Offset, Eps);
            Assert.AreEqual(0f, s.Velocity, Eps);
        }

        [Test]
        public void Begin_ContinuesFromCurrentPelvis_WithZeroVelocity()
        {
            var s = Started(-0.3f);
            MovingPelvisFoot l = Free(), r = Free();
            MovingPelvisSolver.Step(ref s, in l, in r, NoHint, Dt, in Tuning);
            // 回升用慢的 ω：第一帧只移動一點點（沒有啟用跳動）
            Assert.Less(Mathf.Abs(s.Offset + 0.3f), 0.001f);
            Assert.Greater(s.Offset, -0.3f);
        }

        [Test]
        public void Drop_IsFasterThanRise()
        {
            // 0.03 的落差在 Leg Stretch 餘裕（0.05）之內 ⇒ 兩邊都是純彈簧，沒有硬夾。
            var down = Started(0f);
            Run(ref down, Foot(FootLockPhase.Locked, -0.03f), Free(), 10);
            Assert.IsFalse(down.DebugHardClampApplied);
            float dropped = -down.Offset;

            var up = Started(-0.03f);
            Run(ref up, Free(), Free(), 10);
            float risen = up.Offset + 0.03f;
            Assert.Greater(dropped, 5f * risen);
        }

        [Test]
        public void SupportLimit_IsNeverViolatedBeyondLegStretch()
        {
            // 約束突然低於目前骨盆（最壞情況）：硬可達把骨盆直接夾在 Leg Stretch 邊界，腳永遠搆得到。
            var s = Started(0f);
            MovingPelvisFoot l = Foot(FootLockPhase.Locked, -0.3f), r = Free();
            MovingPelvisSolver.Step(ref s, in l, in r, NoHint, Dt, in Tuning);
            Assert.LessOrEqual(s.Offset, -0.3f + 0.05f + Eps);
            Assert.IsTrue(s.DebugHardClampApplied);
            Assert.LessOrEqual(s.Velocity, 0f, "被夾住後不得保留向上的速度");
        }

        [Test]
        public void HardClamp_FollowsTheConstraintRate_NotTheClampedDistance()
        {
            // 上限每帧下降 1 cm（0.6 m/s）：骨盆被夾在上面時的速度 ＝ 上限的速度，不是第一帧被夾掉的 20 cm。
            var s = Started(0f);
            MovingPelvisFoot r = Free();
            for (int i = 0; i < 10; i++)
            {
                MovingPelvisFoot l = Foot(FootLockPhase.Locked, -0.25f - 0.01f * i);
                MovingPelvisSolver.Step(ref s, in l, in r, NoHint, Dt, in Tuning);
                Assert.IsTrue(s.DebugHardClampApplied);
                Assert.AreEqual(-0.25f - 0.01f * i + 0.05f, s.Offset, Eps);
            }
            Assert.AreEqual(-0.6f, s.Velocity, 1e-3f);
        }

        [Test]
        public void HardClamp_NewConstraintActivation_IsLegal_WithoutInjectedVelocity_OrOvershoot()
        {
            // docs/46 §27（s1 Run 下樓 #05）：右腳 Support 的硬上限 ≈ 0、骨盆正在往上；
            // 左腳（尾腳）鎖定當帧帶來新的硬上限 −0.184（需求 −0.234）。階躍是「約束身分改變」，不是上限的速度。
            var s = Started(-0.13f);
            MovingPelvisFoot right = Foot(FootLockPhase.Locked, -0.05f);   // limit −0.05、hard 0
            Run(ref s, Free(), right, 10);
            float before = s.Velocity;
            Assert.Greater(before, 0f, "前置：骨盆正在回升");

            MovingPelvisFoot left = Foot(FootLockPhase.Locked, -0.234f);   // limit −0.234、hard −0.184
            MovingPelvisSolver.Step(ref s, in left, in right, NoHint, Dt, in Tuning);
            Assert.IsTrue(s.DebugHardClampApplied);
            Assert.IsTrue(s.DebugHardActivation, "新約束加入要被辨識出來");
            Assert.LessOrEqual(s.Offset, -0.184f + Eps, "啟用當帧位置仍合法");
            Assert.Greater(s.Velocity, -0.5f, "不得把 (新上限 − 舊上限)/dt 當成速度（舊行為 ≈ −11 m/s）");

            float lowest = s.Offset;
            for (int i = 0; i < 90; i++)
            {
                MovingPelvisSolver.Step(ref s, in left, in right, NoHint, Dt, in Tuning);
                lowest = Mathf.Min(lowest, s.Offset);
            }
            Assert.GreaterOrEqual(lowest, -0.234f - 0.005f, "夾住後沒有二次向下過衝（只下到需求 −0.234）");
            Assert.AreEqual(-0.234f, s.Offset, 0.005f);
        }

        [Test]
        public void HardClamp_OwnerSwitch_BetweenActiveConstraints_DoesNotInjectVelocity()
        {
            // 兩腳都 Locked：決定硬上限的腳從右換成左（左的上限一帧內變得更低）——同樣是換約束，不是上限在移動。
            var s = Started(-0.10f);
            MovingPelvisFoot right = Foot(FootLockPhase.Locked, -0.10f);   // hard −0.05
            MovingPelvisFoot leftHigh = Foot(FootLockPhase.Locked, 0.2f);  // hard 0.25（不綁定）
            Run(ref s, leftHigh, right, 10);
            MovingPelvisFoot leftLow = Foot(FootLockPhase.Locked, -0.30f); // hard −0.25（換它綁定）
            MovingPelvisSolver.Step(ref s, in leftLow, in right, NoHint, Dt, in Tuning);
            Assert.IsTrue(s.DebugHardActivation);
            Assert.LessOrEqual(s.Offset, -0.25f + Eps);
            Assert.Greater(s.Velocity, -0.5f);
        }

        // ---------- 下樓回升（docs/46 §33） ----------

        private static MovingPelvisState RunCtx(float offset, float rootVerticalSpeed, bool movementRequested, in MovingPelvisTuning tuning, int frames)
        {
            var s = Started(offset);
            var ctx = new MovingPelvisContext { TerrainHint = NoHint, MovementRequested = movementRequested, RootVerticalSpeed = rootVerticalSpeed };
            MovingPelvisFoot l = Free(), r = Free();
            for (int i = 0; i < frames; i++) MovingPelvisSolver.Step(ref s, in l, in r, in ctx, Dt, in tuning);
            return s;
        }

        [Test]
        public void DescendingMove_RisesWithDescendOmega()
        {
            // 約束解除、root 正在下降（下樓中）：Step 回升改用 DescendRiseOmega，比一般 RiseOmega 快。
            MovingPelvisTuning t = Tuning;
            var down = RunCtx(-0.3f, -1.0f, true, in t, 10);
            var level = RunCtx(-0.3f, 0f, true, in t, 10);
            Assert.AreEqual(MovingPelvisRecovery.Step, down.DebugRecovery);
            Assert.IsTrue(down.DebugDescendingRise);
            Assert.IsFalse(level.DebugDescendingRise);
            Assert.Greater(down.Offset, level.Offset + 0.05f, "下樓回升明顯較快");
        }

        [Test]
        public void NotDescending_IsUnchangedByDescendOmega()
        {
            // 平地／上樓（root 垂直速度 ≥ −門檻）：結果與 DescendRiseOmega 的值無關 ⇒ 上樓與平地逐位元不變。
            MovingPelvisTuning on = Tuning, off = Tuning;
            off.DescendRiseOmega = off.RiseOmega;
            foreach (float vy in new[] { 0f, 1.0f, -0.1f })
            {
                var a = RunCtx(-0.3f, vy, true, in on, 30);
                var b = RunCtx(-0.3f, vy, true, in off, 30);
                Assert.AreEqual(b.Offset, a.Offset, 0f, "vy=" + vy);
                Assert.AreEqual(b.Velocity, a.Velocity, 0f, "vy=" + vy);
            }
        }

        [Test]
        public void StopSemantics_AreUnaffectedByDescendOmega()
        {
            // 停步（意圖放開）：StopHold／Stand 的語意與角頻率不受下樓回升影響，即使 root 仍在下降。
            MovingPelvisTuning on = Tuning, off = Tuning;
            on.UseStopSemantics = off.UseStopSemantics = true;
            off.DescendRiseOmega = off.RiseOmega;
            var a = RunCtx(-0.3f, -1.0f, false, in on, 30);
            var b = RunCtx(-0.3f, -1.0f, false, in off, 30);
            Assert.IsFalse(a.DebugDescendingRise);
            Assert.AreEqual(b.Offset, a.Offset, 0f);
        }

        [Test]
        public void Floor_HoldsAtMaxOffset()
        {
            var s = Started(-0.34f);
            Run(ref s, Foot(FootLockPhase.Locked, -0.9f), Free(), 60);
            Assert.AreEqual(-Tuning.MaxOffset, s.Offset, Eps);
            Assert.GreaterOrEqual(s.Velocity, 0f);
        }

        [Test]
        public void MinimumNecessaryDrop_SettlesExactlyOnTheConstraint()
        {
            var s = Started(0f);
            Run(ref s, Foot(FootLockPhase.Locked, -0.1f), Foot(FootLockPhase.Locked, -0.15f), 240);
            Assert.AreEqual(-0.15f, s.Offset, 1e-4f, "綁住的是較緊的那隻腳，且只降到剛好搆到");
        }

        [Test]
        public void ReleasingConstraint_OnlyRelaxes_NoDownwardJump()
        {
            var s = Started(0f);
            Run(ref s, Foot(FootLockPhase.Locked, -0.2f), Free(), 240);
            float held = s.Offset;
            // 腳進入 Releasing：上限消失 ⇒ 只會慢慢回升
            MovingPelvisFoot l = Foot(FootLockPhase.Releasing, -0.6f), r = Free();
            MovingPelvisSolver.Step(ref s, in l, in r, NoHint, Dt, in Tuning);
            Assert.GreaterOrEqual(s.Offset, held - Eps);
        }

        // ---------- 兩種回升語意（docs/46 §13）----------

        private static void RunHint(ref MovingPelvisState s, MovingPelvisFoot l, MovingPelvisFoot r, float hint, int frames)
        {
            for (int i = 0; i < frames; i++) MovingPelvisSolver.Step(ref s, in l, in r, hint, Dt, in Tuning);
        }

        [Test]
        public void NoHint_OrHintBelowPelvis_IsStepRecovery()
        {
            var a = Started(-0.3f);
            RunHint(ref a, Free(), Free(), NoHint, 1);
            Assert.AreEqual(MovingPelvisRecovery.Step, a.DebugRecovery);

            // 仍在樓梯上：地形要求比骨盆更深 ⇒ 步態內回升，慢
            var b = Started(-0.3f);
            RunHint(ref b, Free(), Free(), -0.35f, 1);
            Assert.AreEqual(MovingPelvisRecovery.Step, b.DebugRecovery);
        }

        [Test]
        public void HintAbovePelvis_IsTerrainExitRecovery_AndRisesFaster()
        {
            var step = Started(-0.3f);
            RunHint(ref step, Free(), Free(), NoHint, 20);
            var exit = Started(-0.3f);
            RunHint(ref exit, Free(), Free(), 0f, 20);
            Assert.AreEqual(MovingPelvisRecovery.TerrainExit, exit.DebugRecovery);
            Assert.Greater(exit.Offset - (-0.3f), 5f * (step.Offset - (-0.3f)));
        }

        [Test]
        public void Hint_IsNeverATarget_ConstraintStillWins()
        {
            // 提示說 0，但 Support 腳要求 −0.2：骨盆停在約束上，不會被提示拉過去。
            var s = Started(-0.3f);
            RunHint(ref s, Foot(FootLockPhase.Locked, -0.2f), Free(), 0f, 600);
            Assert.AreEqual(-0.2f, s.Offset, 1e-4f);
        }

        [Test]
        public void Hint_IsNeverALimit_CannotPullPelvisDown()
        {
            // 提示比骨盆深（還在樓梯上的包絡）：沒有約束時仍回到名目高度 0，不會被提示壓低。
            var s = Started(-0.2f);
            RunHint(ref s, Free(), Free(), -0.35f, 1);
            Assert.AreEqual(MovingPelvisRecovery.Step, s.DebugRecovery);
            RunHint(ref s, Free(), Free(), -0.35f, 1200);
            Assert.AreEqual(0f, s.Offset, 1e-3f);
        }

        [Test]
        public void TerrainExit_HasHysteresis()
        {
            var s = Started(-0.3f);
            MovingPelvisFoot l = Free(), r = Free();
            MovingPelvisSolver.Step(ref s, in l, in r, -0.24f, Dt, in Tuning); // 差 6 cm > 5 cm ⇒ 進入
            Assert.IsTrue(s.TerrainExitLatched);
            float hint = s.Offset + 0.03f;                                   // 差 3 cm：介於 2–5 cm ⇒ 保持
            MovingPelvisSolver.Step(ref s, in l, in r, hint, Dt, in Tuning);
            Assert.IsTrue(s.TerrainExitLatched);
            hint = s.Offset + 0.01f;                                         // 差 1 cm < 2 cm ⇒ 離開
            MovingPelvisSolver.Step(ref s, in l, in r, hint, Dt, in Tuning);
            Assert.IsFalse(s.TerrainExitLatched);
        }

        [Test]
        public void Dropping_IsNeitherRecovery()
        {
            var s = Started(0f);
            RunHint(ref s, Foot(FootLockPhase.Locked, -0.03f), Free(), 0.5f, 1);
            Assert.AreEqual(MovingPelvisRecovery.None, s.DebugRecovery);
        }

        // ---------- 停步語意（docs/46 §14）----------

        private static MovingPelvisTuning StopTuning()
        {
            MovingPelvisTuning t = MovingPelvisTuning.Default;
            t.UseStopSemantics = true;
            return t;
        }

        private static void RunCtx(ref MovingPelvisState s, MovingPelvisFoot l, MovingPelvisFoot r, bool moving,
            float rootSpeed, int frames, MovingPelvisTuning t)
        {
            var ctx = new MovingPelvisContext { TerrainHint = NoHint, MovementRequested = moving, RootHorizontalSpeed = rootSpeed };
            for (int i = 0; i < frames; i++) MovingPelvisSolver.Step(ref s, in l, in r, in ctx, Dt, in t);
        }

        [Test]
        public void StopHold_DoesNotRise_BeforeTheStopSettles()
        {
            var s = Started(-0.3f);
            RunCtx(ref s, Foot(FootLockPhase.Locked, 0.5f), Free(), false, 1.0f, 60, StopTuning());
            Assert.AreEqual(MovingPelvisRecovery.StopHold, s.DebugRecovery);
            Assert.AreEqual(-0.3f, s.Offset, 1e-4f, "還在走最後幾步（一腳沒鎖、root 仍在動）：不回升");
        }

        [Test]
        public void StopHold_StillFollowsConstraintsDown()
        {
            var s = Started(-0.1f);
            RunCtx(ref s, Foot(FootLockPhase.Locked, -0.25f), Free(), false, 1.0f, 120, StopTuning());
            Assert.AreEqual(-0.25f, s.Offset, 1e-3f, "停步中仍然必須搆得到：約束照樣往下拉");
        }

        [Test]
        public void Settled_IsStandRecovery_RisesFasterThanStep()
        {
            MovingPelvisTuning t = StopTuning();
            var stand = Started(-0.3f);
            RunCtx(ref stand, Foot(FootLockPhase.Locked, 0.5f), Foot(FootLockPhase.Locked, 0.5f), false, 0f, 20, t);
            Assert.IsTrue(stand.StopSettled);
            Assert.AreEqual(MovingPelvisRecovery.Stand, stand.DebugRecovery);

            var step = Started(-0.3f);
            RunCtx(ref step, Foot(FootLockPhase.Locked, 0.5f), Foot(FootLockPhase.Locked, 0.5f), true, 0f, 20, t);
            Assert.Greater(stand.Offset + 0.3f, 5f * (step.Offset + 0.3f));
        }

        [Test]
        public void Settled_StandsExactlyWhereBothFeetReach()
        {
            var s = Started(-0.3f);
            RunCtx(ref s, Foot(FootLockPhase.Locked, -0.08f), Foot(FootLockPhase.Locked, -0.12f), false, 0f, 240, StopTuning());
            Assert.AreEqual(-0.12f, s.Offset, 1e-4f, "站立＝兩腳都搆得到的最高處，沒有自己的高度規則");
        }

        [Test]
        public void MovementIntent_ClearsSettled_BackToMovingSemantics()
        {
            MovingPelvisTuning t = StopTuning();
            var s = Started(-0.3f);
            RunCtx(ref s, Foot(FootLockPhase.Locked, 0.5f), Foot(FootLockPhase.Locked, 0.5f), false, 0f, 2, t);
            Assert.IsTrue(s.StopSettled);
            RunCtx(ref s, Foot(FootLockPhase.Locked, 0.5f), Foot(FootLockPhase.Locked, 0.5f), true, 0f, 1, t);
            Assert.IsFalse(s.StopSettled);
            Assert.AreEqual(MovingPelvisRecovery.Step, s.DebugRecovery);
        }

        [Test]
        public void StopSemanticsOff_IntentReleaseBehavesLikeMoving()
        {
            var s = Started(-0.3f);
            RunCtx(ref s, Foot(FootLockPhase.Locked, 0.5f), Free(), false, 1.0f, 1, Tuning);
            Assert.AreEqual(MovingPelvisRecovery.Step, s.DebugRecovery);
        }

        // ---------- SupportTransfer（docs/46 §19）----------

        private static MovingPelvisFoot Reliable(float targetY)
        {
            MovingPelvisFoot f = Foot(FootLockPhase.Locked, targetY);
            f.Reliable = true;
            return f;
        }

        /// <summary>移動中一帧（兩腳都不可靠），接著放開：回傳放開後的 context 範本。</summary>
        private static MovingPelvisContext Release(ref MovingPelvisState s, bool walk, float rootVy, MovingPelvisTuning t,
            bool noStopCommitted = false)
        {
            var moving = new MovingPelvisContext { TerrainHint = NoHint, MovementRequested = true, RootHorizontalSpeed = 1f };
            MovingPelvisFoot l = Free(), r = Free();
            MovingPelvisSolver.Step(ref s, in l, in r, in moving, Dt, in t);
            return new MovingPelvisContext
            {
                TerrainHint = NoHint, MovementRequested = false, RootHorizontalSpeed = 1f,
                WalkStop = walk, RootVerticalSpeed = rootVy, NoStopCommitted = noStopCommitted,
            };
        }

        // ---------- Descending no-stop-commit fallback（docs/48 D1-c）----------

        [Test]
        public void DescendingNoStopCommit_HoldsOnlyUntilTrailingLocks()
        {
            MovingPelvisTuning t = StopTuning();
            var s = Started(-0.3f);
            MovingPelvisContext c = Release(ref s, false, -0.8f, t, noStopCommitted: true);
            RunStop(ref s, Reliable(0.5f), Foot(FootLockPhase.Unlocked, 0.5f), c, 30, t);
            Assert.AreEqual(MovingPelvisCapSource.DescendingNoStopCommit, s.DebugCapSource);
            Assert.AreEqual(-0.3f, s.Offset, 1e-3f, "尾腳還沒鎖定：不回升");
            RunStop(ref s, Reliable(0.5f), Foot(FootLockPhase.Locked, -0.2f), c, 120, t);
            Assert.AreEqual(MovingPelvisCapSource.None, s.DebugCapSource, "尾腳鎖定後 fallback 退出");
            Assert.AreEqual(-0.2f, s.Offset, 1e-3f, "尾腳鎖定後照即時 Support 約束");
        }

        [Test]
        public void DescendingNoStopCommit_OnlyWhenDescendingAndNothingCommitted()
        {
            MovingPelvisTuning t = StopTuning();
            (bool fallback, bool prediction) Latch(bool walkStop, bool noCommit, float vy)
            {
                var s = Started(-0.3f);
                MovingPelvisContext c = Release(ref s, walkStop, vy, t, noCommit);
                MovingPelvisFoot l = Free(), r = Free();
                MovingPelvisSolver.Step(ref s, in l, in r, in c, Dt, in t);   // 放開那一帧（鎖存發生在這裡）
                return (s.StopNoCommitFallback, s.StopUsesPrediction);
            }
            Assert.AreEqual((true, false), Latch(false, true, -0.8f), "下樓 ∧ 沒有 committed 停步 ⇒ fallback");
            Assert.AreEqual((false, false), Latch(false, true, 0f), "平地沒有停步 clip ⇒ 不進 fallback");
            Assert.AreEqual((false, false), Latch(false, true, +0.8f), "上樓沒有停步 clip ⇒ 不進 fallback");
            Assert.AreEqual((false, false), Latch(false, false, -0.8f), "committed Run 停步下樓 ⇒ 維持既有 Run policy");
            Assert.AreEqual((false, true), Latch(true, false, -0.8f), "committed Walk 停步下樓 ⇒ 仍走規則⑦，不是 fallback");
        }

        private static void RunStop(ref MovingPelvisState s, MovingPelvisFoot l, MovingPelvisFoot r, MovingPelvisContext c, int frames, MovingPelvisTuning t)
        {
            for (int i = 0; i < frames; i++) MovingPelvisSolver.Step(ref s, in l, in r, in c, Dt, in t);
        }

        [Test]
        public void PrimarySupport_StartsStandingImmediately_WithoutWaitingForTrailing()
        {
            MovingPelvisTuning t = StopTuning();
            var s = Started(-0.3f);
            MovingPelvisContext c = Release(ref s, false, 0f, t);
            RunStop(ref s, Reliable(0.5f), Foot(FootLockPhase.Releasing, -0.7f), c, 1, t);
            Assert.AreEqual(1, s.PrimaryFoot);
            Assert.AreEqual(MovingPelvisRecovery.SupportTransfer, s.DebugRecovery);
            RunStop(ref s, Reliable(0.5f), Foot(FootLockPhase.Releasing, -0.7f), c, 20, t);
            Assert.Greater(s.Offset, -0.2f, "尾腳還在 Releasing，骨盆已經開始站直");
        }

        [Test]
        public void TrailingSwing_NeverPullsThePelvisDown()
        {
            MovingPelvisTuning t = StopTuning();
            var s = Started(-0.2f);
            MovingPelvisContext c = Release(ref s, false, 0f, t);
            MovingPelvisFoot landing = Foot(FootLockPhase.Unlocked, -0.6f, soleHeight: 0.05f);
            RunStop(ref s, Reliable(0.5f), landing, c, 60, t);
            Assert.AreEqual(MovingPelvisCapSource.LiveLanding, s.DebugCapSource);
            Assert.GreaterOrEqual(s.Offset, -0.2f - 1e-4f, "即時 Landing 上限只擋上升，不往下拉");
        }

        [Test]
        public void FutureReachCap_LimitsHowHighItRises()
        {
            MovingPelvisTuning t = StopTuning();
            var s = Started(-0.3f);
            MovingPelvisContext c = Release(ref s, true, -0.8f, t);
            Assert.IsFalse(s.StopUsesPrediction, "放開那一帧才鎖存");
            c.HasRightLanding = true;
            c.RightLanding = new Vector3(0f, -0.25f, 0f);        // 髖在 y=1、腿長 1 ⇒ 上限 −0.25
            MovingPelvisFoot swing = Foot(FootLockPhase.Unlocked, 0.5f);
            RunStop(ref s, Reliable(0.5f), swing, c, 240, t);
            Assert.IsTrue(s.StopUsesPrediction);
            Assert.AreEqual(MovingPelvisCapSource.PredictedLanding, s.DebugCapSource);
            Assert.AreEqual(-0.25f, s.Offset, 1e-3f, "升到 cap 為止");
        }

        [Test]
        public void FutureReachCap_BelowPelvis_DoesNotLowerIt()
        {
            MovingPelvisTuning t = StopTuning();
            var s = Started(-0.3f);
            MovingPelvisContext c = Release(ref s, true, -0.8f, t);
            c.HasRightLanding = true;
            c.RightLanding = new Vector3(0f, -0.45f, 0f);        // 上限 −0.45，低於目前骨盆
            RunStop(ref s, Reliable(0.5f), Foot(FootLockPhase.Unlocked, 0.5f), c, 120, t);
            Assert.AreEqual(-0.3f, s.Offset, 1e-3f, "cap 只限制最高，不主動降低骨盆");
        }

        [Test]
        public void PredictionPending_HoldsOnlyUntilItArrives()
        {
            MovingPelvisTuning t = StopTuning();
            var s = Started(-0.3f);
            MovingPelvisContext c = Release(ref s, true, -0.8f, t);
            RunStop(ref s, Reliable(0.5f), Foot(FootLockPhase.Unlocked, 0.5f), c, 30, t);
            Assert.AreEqual(MovingPelvisCapSource.PredictionPending, s.DebugCapSource);
            Assert.AreEqual(-0.3f, s.Offset, 1e-3f);
            c.HasRightLanding = true;
            c.RightLanding = new Vector3(0f, 0.2f, 0f);
            RunStop(ref s, Reliable(0.5f), Foot(FootLockPhase.Unlocked, 0.5f), c, 30, t);
            Assert.Greater(s.Offset, -0.25f, "預測一到就開始站直");
        }

        [Test]
        public void TrailingLocked_BecomesLiveSupport_PredictionExits()
        {
            MovingPelvisTuning t = StopTuning();
            var s = Started(-0.1f);
            MovingPelvisContext c = Release(ref s, true, -0.8f, t);
            c.HasRightLanding = true;
            c.RightLanding = new Vector3(0f, 0.5f, 0f);
            c.RootHorizontalSpeed = 1f;                           // 還沒落定
            RunStop(ref s, Reliable(0.5f), Foot(FootLockPhase.Locked, -0.2f), c, 120, t);
            Assert.AreEqual(MovingPelvisCapSource.None, s.DebugCapSource);
            Assert.AreEqual(-0.2f, s.Offset, 1e-3f, "尾腳鎖定後照即時 Support 約束");
        }

        [Test]
        public void PredictionIsOnlyApprovedForWalkDownStops()
        {
            MovingPelvisTuning t = StopTuning();
            bool Approved(bool walk, float vy)
            {
                var s = Started(-0.3f);
                MovingPelvisContext c = Release(ref s, walk, vy, t);
                MovingPelvisFoot l = Free(), r = Free();
                MovingPelvisSolver.Step(ref s, in l, in r, in c, Dt, in t);   // 放開那一帧（鎖存發生在這裡）
                return s.StopUsesPrediction;
            }
            Assert.IsFalse(Approved(false, -0.8f), "Run 下樓不批准");
            Assert.IsFalse(Approved(true, +0.8f), "Walk 上樓不批准");
            Assert.IsFalse(Approved(true, 0f), "Walk 平地不批准");
            Assert.IsTrue(Approved(true, -0.8f), "只有 Walk 下樓");
        }

        // ---------- 下樓停步：落地中的尾腳（docs/46 §35）----------

        /// <summary>左腳 Primary（Reliable），右腳是尾腳；回傳跑完後的狀態。尾腳上限＝LegBound（髖 y=1、腿長 1）＝ <paramref name="trailingY"/>。</summary>
        private static MovingPelvisState TrailingStop(bool walk, float rootVy, MovingPelvisFoot trailing, int frames, MovingPelvisTuning t)
        {
            var s = Started(-0.1f);
            MovingPelvisContext c = Release(ref s, walk, rootVy, t);
            RunStop(ref s, Reliable(0.5f), trailing, c, frames, t);
            return s;
        }

        private static MovingPelvisFoot LandingAt(float targetY) => Foot(FootLockPhase.Unlocked, targetY, soleHeight: 0f);

        [Test]
        public void DescendingStop_LandingTrailingFoot_PullsThePelvisDown_AnyGait()
        {
            foreach (bool walk in new[] { false, true })
            {
                MovingPelvisState s = TrailingStop(walk, -0.8f, LandingAt(-0.3f), 120, StopTuning());
                Assert.AreEqual(1, s.PrimaryFoot, "左腳是 Primary、右腳是尾腳");
                Assert.IsTrue(s.StopDescending);
                Assert.AreEqual(-0.3f, s.Offset, 1e-3f, (walk ? "Walk" : "Run") + " 下樓停步：落地中的尾腳需求提前生效");
            }
        }

        [Test]
        public void DescendingStop_Pull_DoesNotSnap()
        {
            MovingPelvisTuning t = StopTuning();
            var s = Started(-0.1f);
            MovingPelvisContext c = Release(ref s, false, -0.8f, t);
            float prev = s.Offset, worst = 0f;
            for (int i = 0; i < 60; i++)
            {
                RunStop(ref s, Reliable(0.5f), LandingAt(-0.3f), c, 1, t);
                worst = Mathf.Max(worst, prev - s.Offset);
                prev = s.Offset;
            }
            Assert.AreEqual(-0.3f, s.Offset, 1e-3f);
            Assert.Less(worst, 0.05f, "20 cm 的需求以 DropOmega 跟上，不是一帧夾下去");
        }

        [Test]
        public void UpOrFlatStop_LandingTrailingFoot_StillDoesNotPull()
        {
            Assert.GreaterOrEqual(TrailingStop(false, 0f, LandingAt(-0.3f), 120, StopTuning()).Offset, -0.1f - 1e-4f, "平地停步：§19 原行為");
            MovingPelvisState up = TrailingStop(true, +0.8f, LandingAt(-0.3f), 120, StopTuning());
            Assert.IsFalse(up.StopDescending);
            Assert.GreaterOrEqual(up.Offset, -0.1f - 1e-4f, "上樓停步：§19 原行為");
        }

        [Test]
        public void DescendingStop_ReleasingTrailingFoot_StillDoesNotPull()
        {
            MovingPelvisState s = TrailingStop(false, -0.8f, Foot(FootLockPhase.Releasing, -0.3f), 120, StopTuning());
            Assert.IsFalse(s.DebugTrailingLandingPull);
            Assert.GreaterOrEqual(s.Offset, -0.1f - 1e-4f, "規則②仍排除 Releasing／上升中的擺盪");
        }

        [Test]
        public void DescendingStop_TrailingLandingPull_Off_IsSection19Behaviour()
        {
            MovingPelvisTuning t = StopTuning();
            t.DescendingTrailingLandingPulls = false;
            Assert.GreaterOrEqual(TrailingStop(false, -0.8f, LandingAt(-0.3f), 120, t).Offset, -0.1f - 1e-4f);
        }

        [Test]
        public void Inactive_OrZeroDt_DoesNothing()
        {
            var s = default(MovingPelvisState);
            s.Offset = -0.2f;
            MovingPelvisFoot l = Foot(FootLockPhase.Locked, -0.3f), r = Free();
            MovingPelvisSolver.Step(ref s, in l, in r, NoHint, Dt, in Tuning);
            Assert.AreEqual(-0.2f, s.Offset, Eps);

            var a = Started(-0.2f);
            MovingPelvisSolver.Step(ref a, in l, in r, NoHint, 0f, in Tuning);
            Assert.AreEqual(-0.2f, a.Offset, Eps);
        }
    }
}
