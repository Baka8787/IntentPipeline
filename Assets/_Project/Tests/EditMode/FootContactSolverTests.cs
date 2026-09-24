using NUnit.Framework;
using UnityEngine;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🧪 T3 腳部接觸原型（docs/45 §6–§7）純函數：一帧外插、落腳相位 β、帶朝向的最小抬升、鎖腳狀態機。
    /// 視覺效果（穿透、鎖腳穩定、腳步對齊）屬 PlayMode 探針量測，不在此涵蓋。
    /// </summary>
    public class FootContactSolverTests
    {
        private const float Epsilon = 1e-5f;
        private const float Dt = 1f / 60f;

        // === 一帧外插 ===

        [Test]
        public void PredictPose_WithoutHistory_DoesNotExtrapolateAndRecordsHistory()
        {
            var state = default(FootContactState);

            bool continuous = FootContactSolver.PredictPose(ref state, new Vector3(1f, 0.1f, 2f), 0.1f, Dt, 0.5f,
                out Vector3 predicted, out float height, out float speed);

            Assert.IsFalse(continuous);
            Assert.AreEqual(new Vector3(1f, 0.1f, 2f), predicted);
            Assert.AreEqual(0.1f, height, Epsilon);
            Assert.AreEqual(0f, speed);
            Assert.IsTrue(state.HasHistory, "第一帧也要寫入歷史，下一帧才能外插。");
        }

        [Test]
        public void PredictPose_ConstantVelocity_ExtrapolatesOneFrameAhead()
        {
            var state = default(FootContactState);
            FootContactSolver.PredictPose(ref state, new Vector3(0f, 0.2f, 0f), 0.2f, Dt, 0.5f, out _, out _, out _);

            bool continuous = FootContactSolver.PredictPose(ref state, new Vector3(0.05f, 0.25f, 0f), 0.25f, Dt, 0.5f,
                out Vector3 predicted, out float height, out float speed);

            Assert.IsTrue(continuous);
            Assert.AreEqual(0.10f, predicted.x, Epsilon, "水平位置外插到下一帧（目標下一帧才套用）。");
            Assert.AreEqual(0.30f, height, Epsilon, "動畫離地高度同樣外插。");
            Assert.AreEqual(0.05f / Dt, speed, 1e-3f, "速度只算水平分量。");
        }

        [Test]
        public void PredictPose_Teleport_TreatedAsDiscontinuity()
        {
            var state = default(FootContactState);
            FootContactSolver.PredictPose(ref state, Vector3.zero, 0.1f, Dt, 0.5f, out _, out _, out _);

            bool continuous = FootContactSolver.PredictPose(ref state, new Vector3(3f, 0f, 0f), 0.1f, Dt, 0.5f,
                out Vector3 predicted, out _, out float speed);

            Assert.IsFalse(continuous, "一帧位移超過上限 ⇒ 不連續，呼叫端必須重設鎖腳。");
            Assert.AreEqual(new Vector3(3f, 0f, 0f), predicted, "不連續時不外插（否則會把瞬移再推一次）。");
            Assert.AreEqual(0f, speed);
            Assert.AreEqual(new Vector3(3f, 0f, 0f), state.PrevPosePosition, "歷史從新位置重新開始。");
        }

        // === 落腳相位 β ===

        [Test]
        public void ContactBeta_IsStanceTimesSmoothSpeedPhase()
        {
            Assert.AreEqual(1f, FootContactSolver.ComputeContactBeta(1f, 0.5f, 1f, 4f), Epsilon);
            Assert.AreEqual(0f, FootContactSolver.ComputeContactBeta(1f, 5f, 1f, 4f), Epsilon);
            Assert.AreEqual(0.5f, FootContactSolver.ComputeContactBeta(1f, 2.5f, 1f, 4f), Epsilon, "smoothstep 中點。");
            Assert.AreEqual(0.25f, FootContactSolver.ComputeContactBeta(0.5f, 2.5f, 1f, 4f), Epsilon);
            Assert.AreEqual(0f, FootContactSolver.ComputeContactBeta(0f, 0f, 1f, 4f), Epsilon, "擺盪高度 ⇒ 0。");
        }

        [Test]
        public void ContactBeta_IsContinuousInSpeed()
        {
            float previous = FootContactSolver.ComputeContactBeta(1f, 0f, 1f, 4f);
            for (float v = 0.01f; v <= 5f; v += 0.01f)
            {
                float current = FootContactSolver.ComputeContactBeta(1f, v, 1f, 4f);
                Assert.Less(Mathf.Abs(current - previous), 0.01f, $"β 在 v={v} 不連續");
                previous = current;
            }
        }

        // === 帶朝向的最小抬升 ===

        [Test]
        public void MinimumLift_Penetration_IsAlwaysFullyLifted()
        {
            Assert.AreEqual(0.04f, FootContactSolver.ComputeMinimumLift(0.04f, 0f), Epsilon,
                "擺盪中（β=0）也不允許腳底穿進地面。");
            Assert.AreEqual(0.04f, FootContactSolver.ComputeMinimumLift(0.04f, 1f), Epsilon);
        }

        [Test]
        public void MinimumLift_Floating_PulledDownOnlyByBeta()
        {
            Assert.AreEqual(0f, FootContactSolver.ComputeMinimumLift(-0.10f, 0f), Epsilon, "擺盪：保留動畫離地高度。");
            Assert.AreEqual(-0.05f, FootContactSolver.ComputeMinimumLift(-0.10f, 0.5f), Epsilon);
            Assert.AreEqual(-0.10f, FootContactSolver.ComputeMinimumLift(-0.10f, 1f), Epsilon, "落腳：貼上接觸面。");
        }

        [Test]
        public void MinimumLift_IsContinuousAcrossZero()
        {
            for (float beta = 0f; beta <= 1f; beta += 0.25f)
            {
                float below = FootContactSolver.ComputeMinimumLift(-1e-4f, beta);
                float above = FootContactSolver.ComputeMinimumLift(1e-4f, beta);
                Assert.Less(Mathf.Abs(above - below), 3e-4f, $"β={beta}：懸空與穿透兩支在 0 必須相接");
            }
        }

        [Test]
        public void SolePoint_PreservesFootOrientation()
        {
            // 腳尖朝下 30°（擺盪時的腳）：腳尖端點比腳跟低，最小抬升只需讓最低點不穿透——不把腳壓平。
            Quaternion toeDown = Quaternion.AngleAxis(30f, Vector3.right);
            Vector3 ankle = new Vector3(0f, 0.2f, 0f);

            Vector3 heel = FootContactSolver.SolePoint(ankle, toeDown, 0.08f, -0.1f);
            Vector3 toe = FootContactSolver.SolePoint(ankle, toeDown, 0.08f, 0.15f);

            Assert.Less(toe.y, heel.y, "朝向保留：腳尖端點低於腳跟端點。");
            Assert.AreEqual(ankle + toeDown * new Vector3(0f, -0.08f, 0.15f), toe);
        }

        // === 鎖腳狀態機 ===

        private static FootContactState ReadyToLock()
        {
            var state = default(FootContactState);
            state.PrevContactValid = true;
            return state;
        }

        [Test]
        public void Lock_EngagesAtLockBeta_WithContact_AtCurrentAnimationPosition()
        {
            var state = ReadyToLock();
            Vector3 anim = new Vector3(1f, 0f, 2f);

            FootContactSolver.StepLock(ref state, 0.96f, anim, Dt, 0.95f, 0.7f, 0.2f, 0.12f);

            Assert.AreEqual(FootLockPhase.Locked, state.Phase);
            Assert.AreEqual(anim, FootContactSolver.ResolveHorizontal(in state, anim), "鎖定當帧目標不跳。");
        }

        [Test]
        public void Lock_DoesNotEngage_WithoutContactOrBelowLockBeta()
        {
            var noContact = default(FootContactState);
            FootContactSolver.StepLock(ref noContact, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            Assert.AreEqual(FootLockPhase.Unlocked, noContact.Phase, "沒有接觸不得鎖。");

            var lowBeta = ReadyToLock();
            FootContactSolver.StepLock(ref lowBeta, 0.9f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            Assert.AreEqual(FootLockPhase.Unlocked, lowBeta.Phase);
        }

        [Test]
        public void Lock_HoldsHorizontalPosition_WithHysteresis()
        {
            var state = ReadyToLock();
            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);

            // β 掉到兩個門檻之間：仍鎖住（滯後）；動畫腳滑動 5 cm：目標仍在鎖定點。
            Vector3 anim = new Vector3(0.05f, 0.1f, 0f);
            FootContactSolver.StepLock(ref state, 0.8f, anim, Dt, 0.95f, 0.7f, 0.2f, 0.12f);

            Assert.AreEqual(FootLockPhase.Locked, state.Phase);
            Vector3 target = FootContactSolver.ResolveHorizontal(in state, anim);
            Assert.AreEqual(0f, target.x, Epsilon, "鎖住水平位置。");
            Assert.AreEqual(0.1f, target.y, Epsilon, "高度不由鎖定決定（每帧由接觸解重算）。");
        }

        [TestCase(0.6f, 0f, true, TestName = "Lock_Releases_WhenBetaDropsBelowUnlockBeta")]
        [TestCase(1f, 0.25f, true, TestName = "Lock_Releases_WhenAnimationLeavesUnlockRadius")]
        [TestCase(1f, 0f, false, TestName = "Lock_Releases_WhenContactIsLost")]
        public void Lock_ReleaseTriggers(float beta, float animOffset, bool contact)
        {
            var state = ReadyToLock();
            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            state.PrevContactValid = contact;

            FootContactSolver.StepLock(ref state, beta, new Vector3(animOffset, 0f, 0f), Dt, 0.95f, 0.7f, 0.2f, 0.12f);

            Assert.AreEqual(FootLockPhase.Releasing, state.Phase);
            Assert.AreEqual(1f, state.ReleaseBlend, "釋放當帧仍在鎖定點（不跳）。");
        }

        [Test]
        public void Release_FollowsMovingAnimation_WithDecayingFrozenOffset()
        {
            // 鎖在 0；動畫腳離開 0.25 m 觸發釋放，之後以每帧 0.1 m（6 m/s 擺盪）繼續前進。
            var state = ReadyToLock();
            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            Vector3 anim = new Vector3(0.25f, 0f, 0f);
            FootContactSolver.StepLock(ref state, 1f, anim, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            Assert.AreEqual(FootLockPhase.Releasing, state.Phase);
            Assert.AreEqual(0f, FootContactSolver.ResolveHorizontal(in state, anim).x, Epsilon,
                "轉移當帧目標正好是鎖定點（偏移與傳進 StepLock 的動畫腳位同一個參數凍結）。");

            float previousTrail = anim.x - FootContactSolver.ResolveHorizontal(in state, anim).x;
            float previousX = 0f;
            int frames = 0;
            while (state.Phase == FootLockPhase.Releasing && frames < 100)
            {
                anim.x += 0.1f;
                FootContactSolver.StepLock(ref state, 0f, anim, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
                float x = FootContactSolver.ResolveHorizontal(in state, anim).x;
                float trail = anim.x - x;
                Assert.LessOrEqual(trail, previousTrail + Epsilon,
                    "落後動畫的距離只會縮小：不再朝留在原地的鎖定點插值（舊寫法會越拖越遠）。");
                Assert.LessOrEqual(trail, 0.25f + Epsilon, "落後量不超過釋放開始時的差距。");
                Assert.Less(x - previousX, 0.1f + 0.25f * 0.25f, "每帧位移 ≤ 動畫位移 ＋ 偏移的平滑衰減，不是瞬移。");
                previousTrail = trail;
                previousX = x;
                frames++;
            }

            Assert.AreEqual(FootLockPhase.Unlocked, state.Phase);
            Assert.AreEqual(anim.x, previousX, Epsilon, "釋放完成時正好回到動畫腳位。");
            Assert.AreEqual(Mathf.CeilToInt(0.12f / Dt), frames, 1, "釋放時間 ≈ ReleaseTime。");
        }

        [Test]
        public void Release_OffsetIsClearedByReset()
        {
            var state = ReadyToLock();
            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            FootContactSolver.StepLock(ref state, 1f, new Vector3(0.3f, 0f, 0f), Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            Assert.AreNotEqual(Vector3.zero, state.ReleaseOffset);

            state.Reset();

            Assert.AreEqual(Vector3.zero, state.ReleaseOffset);
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), FootContactSolver.ResolveHorizontal(in state, new Vector3(0.5f, 0f, 0f)));
        }

        [Test]
        public void Release_CannotRelockUntilComplete()
        {
            var state = ReadyToLock();
            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            FootContactSolver.StepLock(ref state, 1f, new Vector3(0.3f, 0f, 0f), Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            Assert.AreEqual(FootLockPhase.Releasing, state.Phase, "超出半徑 ⇒ 釋放。");

            FootContactSolver.StepLock(ref state, 1f, new Vector3(0.3f, 0f, 0f), Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            Assert.AreEqual(FootLockPhase.Releasing, state.Phase, "β 仍高也要先完成釋放（避免門檻附近抖動）。");
        }

        [Test]
        public void ReleaseHeight_CrossingATreadEdge_BlendsInsteadOfJumping()
        {
            // 鎖在上一階（腳踝 0.30），釋放時即時解已經跨到下一階（0.10）：高度必須隨釋放進度過渡，不是跳 20 cm。
            var state = ReadyToLock();
            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            state.LockedHeight = 0.30f;
            FootContactSolver.StepLock(ref state, 0f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);

            float previous = FootContactSolver.ResolveHeight(in state, 0.10f, true, 0.05f);
            Assert.AreEqual(0.30f, previous, Epsilon, "釋放當帧仍在鎖定高度。");
            while (state.Phase == FootLockPhase.Releasing)
            {
                FootContactSolver.StepLock(ref state, 0f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
                float y = FootContactSolver.ResolveHeight(in state, 0.10f, true, 0.05f);
                Assert.Less(Mathf.Abs(y - previous), 0.20f * 0.25f, "釋放期的高度是平滑過渡。");
                previous = y;
            }
            Assert.AreEqual(0.10f, previous, Epsilon, "釋放完成時回到即時解。");
        }

        [Test]
        public void ReleaseHeight_NeverBelowContactFloor()
        {
            // 鎖在下一階、即時解在上一階：過渡不得讓腳底穿進上一階（接觸底線 0.28）。
            var state = ReadyToLock();
            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            state.LockedHeight = 0.10f;
            FootContactSolver.StepLock(ref state, 0f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);

            Assert.AreEqual(0.28f, FootContactSolver.ResolveHeight(in state, 0.30f, true, 0.28f), Epsilon);
            Assert.AreEqual(0.10f, FootContactSolver.ResolveHeight(in state, 0.30f, false, 0.28f), Epsilon,
                "沒有接觸時沒有底線可用，照過渡。");
        }

        [Test]
        public void Height_OutsideRelease_IsTheLiveSolution()
        {
            var state = ReadyToLock();
            state.LockedHeight = 0.5f;
            Assert.AreEqual(0.12f, FootContactSolver.ResolveHeight(in state, 0.12f, true, 0f), Epsilon);
            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            Assert.AreEqual(0.12f, FootContactSolver.ResolveHeight(in state, 0.12f, true, 0f), Epsilon,
                "鎖定只鎖水平；高度仍是即時解。");
        }

        [Test]
        public void ContactGround_WhileLocked_HoldsSupportOnlyAtTheSelectedPoint()
        {
            var state = ReadyToLock();
            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            state.LockedSupportY = 0.39f; // 鎖定當帧：腳尖（2）站在上一階
            state.LockedSupportPoint = 2;

            Assert.IsTrue(FootContactSolver.ContactGround(in state, true, 2, true, 0.195f, out float toeLower));
            Assert.AreEqual(0.39f, toeLower, Epsilon, "選定端點滑出上一階：不掉下去（不跳一個級高）。");
            Assert.IsTrue(FootContactSolver.ContactGround(in state, true, 2, true, 0.585f, out float toeHigher));
            Assert.AreEqual(0.585f, toeHigher, Epsilon, "選定端點滑進更高的面：仍然不得穿透。");
            Assert.IsTrue(FootContactSolver.ContactGround(in state, true, 2, false, 0f, out float toeMiss));
            Assert.AreEqual(0.39f, toeMiss, Epsilon, "選定端點即時沒命中：用鎖定支撐。");

            // Codex High：其他端點維持即時地面——較低的腳跟不得也被墊到上一階（那會讓鎖定後一帧再跳一次）。
            Assert.IsTrue(FootContactSolver.ContactGround(in state, true, 0, true, 0.195f, out float heel));
            Assert.AreEqual(0.195f, heel, Epsilon);
            Assert.IsFalse(FootContactSolver.ContactGround(in state, true, 0, false, 0f, out _), "其他端點沒命中就是沒有地面。");
        }

        [Test]
        public void ContactGround_BeforeSupportSelectedOrUnlocked_IsLive()
        {
            var state = ReadyToLock();
            state.LockedSupportY = 0.39f;
            state.LockedSupportPoint = 1;
            Assert.IsTrue(FootContactSolver.ContactGround(in state, true, 1, true, 0.195f, out float unlocked));
            Assert.AreEqual(0.195f, unlocked, Epsilon, "未鎖：即時地面。");
            Assert.IsFalse(FootContactSolver.ContactGround(in state, true, 1, false, 0f, out _), "未鎖且沒命中：沒有地面。");

            FootContactSolver.StepLock(ref state, 1f, Vector3.zero, Dt, 0.95f, 0.7f, 0.2f, 0.12f);
            Assert.IsTrue(FootContactSolver.ContactGround(in state, false, 1, true, 0.195f, out float lockFrame));
            Assert.AreEqual(0.195f, lockFrame, Epsilon, "鎖定當帧支撐尚未選定：即時地面。");
        }

        [Test]
        public void Reset_ClearsLockAndHistory()
        {
            var state = ReadyToLock();
            FootContactSolver.PredictPose(ref state, Vector3.one, 0.1f, Dt, 0.5f, out _, out _, out _);
            FootContactSolver.StepLock(ref state, 1f, Vector3.one, Dt, 0.95f, 0.7f, 0.2f, 0.12f);

            state.Reset();

            Assert.AreEqual(FootLockPhase.Unlocked, state.Phase);
            Assert.IsFalse(state.HasHistory, "失效時速度歷史一併清除（Codex Medium ②）。");
            Assert.IsFalse(state.PrevContactValid);
        }
    }
}
