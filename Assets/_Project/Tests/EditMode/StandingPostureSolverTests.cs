using NUnit.Framework;
using UnityEngine;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🧪 站直原型（docs/05 §3.5.4.6）純函數：單腳可達上限與 Moving → Settling → Upright → Return → Moving 狀態機。
    /// 視覺（站直高度、回到移動的下沉）屬 PlayMode 探針量測。
    /// </summary>
    public class StandingPostureSolverTests
    {
        private const float Dt = 1f / 60f;
        private const float Eps = 1e-5f;

        // === 單腳可達上限 ===

        [Test]
        public void LegBound_StraightDown_IsReachMinusHeight()
        {
            // 髖在腳踝正上方 1.0 m，腿長 0.9 ⇒ 骨盆最多只能在 −0.1（再高就搆不到）。
            float p = StandingPostureSolver.LegBound(new Vector3(0f, 1f, 0f), Vector3.zero, 0.9f);
            Assert.AreEqual(-0.1f, p, Eps);
        }

        [Test]
        public void LegBound_IsContinuousAtHorizontalReach()
        {
            Vector3 hip = new Vector3(0f, 1f, 0f);
            float inside = StandingPostureSolver.LegBound(hip, new Vector3(0.9f - 1e-4f, 0f, 0f), 0.9f);
            float outside = StandingPostureSolver.LegBound(hip, new Vector3(0.9f + 1e-4f, 0f, 0f), 0.9f);
            Assert.Less(Mathf.Abs(inside - outside), 0.02f, "水平搆不到時延續到垂直最近的姿勢，不得跳到固定下限。");
            Assert.AreEqual(-1f, outside, Eps, "水平搆不到 ⇒ 髖與腳踝同高。");
        }

        // === 狀態機 ===

        private static PostureInputs Still(float e, float r, float c) =>
            new PostureInputs { Envelope = e, Preferred = r, HardLimit = c, Still = true };

        private static PostureInputs Moving(float e, float c = 0f) =>
            new PostureInputs { Envelope = e, Preferred = 0f, HardLimit = c, Still = false };

        private static void StepN(ref PostureState s, PostureInputs input, ref float p, int n)
        {
            for (int i = 0; i < n; i++)
                StandingPostureSolver.Step(ref s, in input, Dt, ref p, 5f, 6, 6f, 15f, 0.01f, 0.05f);
        }

        [Test]
        public void ZeroDeltaTime_DoesNothing()
        {
            var s = default(PostureState);
            float p = -0.2f;
            var input = Still(-0.35f, -0.1f, 0f);
            StandingPostureSolver.Step(ref s, in input, 0f, ref p, 5f, 6, 6f, 15f, 0.01f, 0.05f);
            Assert.AreEqual(-0.2f, p);
            Assert.AreEqual(0, s.StillFrames);
            Assert.AreEqual(PosturePhase.Moving, s.Phase);
        }

        [Test]
        public void Moving_IsExactlyTodaysMoveTowards()
        {
            var s = default(PostureState);
            float p = 0f;
            float expected = 0f;
            for (int i = 0; i < 20; i++)
            {
                var input = Moving(-0.35f);
                StandingPostureSolver.Step(ref s, in input, Dt, ref p, 5f, 6, 6f, 15f, 0.01f, 0.05f);
                expected = Mathf.MoveTowards(expected, -0.35f, 5f * Dt);
                Assert.AreEqual(expected, p, "移動中必須與既有 MoveTowards 逐位元相同。");
            }
        }

        [Test]
        public void Settling_RequiresConsecutiveStillFrames()
        {
            var s = default(PostureState);
            float p = -0.35f;
            StepN(ref s, Still(-0.35f, -0.1f, 0f), ref p, 5);
            Assert.AreEqual(PosturePhase.Moving, s.Phase);
            StepN(ref s, Moving(-0.35f), ref p, 1);
            StepN(ref s, Still(-0.35f, -0.1f, 0f), ref p, 5);
            Assert.AreEqual(PosturePhase.Moving, s.Phase, "中斷後重新計數。");
            StepN(ref s, Still(-0.35f, -0.1f, 0f), ref p, 1);
            Assert.AreEqual(PosturePhase.Settling, s.Phase);
        }

        [Test]
        public void Standing_RisesSmoothlyToPreferred_ThenUpright()
        {
            var s = default(PostureState);
            float p = -0.35f;
            float prev = p;
            int frames = 0;
            while (s.Phase != PosturePhase.Upright && frames < 200)
            {
                StepN(ref s, Still(-0.35f, -0.1f, 0f), ref p, 1);
                Assert.GreaterOrEqual(p, prev - Eps, "臨界阻尼：單調上升、不下沉。");
                Assert.Less(p - prev, 0.02f, "每帧上升有限（彈簧，不是瞬移）。");
                Assert.LessOrEqual(p, -0.1f + Eps, "不超過偏好上限。");
                prev = p;
                frames++;
            }
            Assert.AreEqual(PosturePhase.Upright, s.Phase);
            Assert.AreEqual(-0.1f, p, 0.01f);
        }

        [Test]
        public void Standing_NeverBelowEnvelope_EvenWhenPreferredIsLower()
        {
            // Codex High：平地 E = −0.029、R = −0.033、C = −0.028 ⇒ 目標 E，而且到得了（舊版「p ≤ R」會永遠到不了）。
            var s = default(PostureState);
            float p = -0.029f;
            StepN(ref s, Still(-0.029f, -0.033f, -0.028f), ref p, 120);
            Assert.AreEqual(-0.029f, p, 1e-3f, "平地不變。");
            Assert.AreEqual(PosturePhase.Upright, s.Phase);
        }

        [Test]
        public void HardLimit_BelowEnvelope_Wins()
        {
            var s = default(PostureState);
            float p = -0.2f;
            StepN(ref s, Still(-0.2f, -0.1f, -0.25f), ref p, 120);
            Assert.AreEqual(-0.25f, p, 1e-3f, "搆得到優先於「不低於包絡」。");
        }

        [Test]
        public void HardLimitClamp_IsImmediate_AndKillsUpwardVelocity()
        {
            var s = default(PostureState);
            float p = -0.35f;
            StepN(ref s, Still(-0.35f, 0f, 0f), ref p, 20); // 上升中，速度向上
            Assert.Greater(s.Velocity, 0f);
            float before = p;
            StepN(ref s, Still(-0.35f, 0f, before - 0.05f), ref p, 1);
            Assert.AreEqual(before - 0.05f, p, Eps, "硬性上限是唯一允許立即下降的安全夾。");
            Assert.LessOrEqual(s.Velocity, 0f, "夾住時把向外（向上）的速度歸零，不留彈回。");
        }

        [Test]
        public void Exit_GoesToReturn_CarryingVelocity_ThenHandsBack()
        {
            var s = default(PostureState);
            float p = -0.35f;
            StepN(ref s, Still(-0.35f, -0.1f, 0f), ref p, 25);
            float v = s.Velocity;
            float before = p;
            StepN(ref s, Moving(-0.35f), ref p, 1);
            Assert.AreEqual(PosturePhase.Return, s.Phase, "一有移動就立即離開站姿。");
            Assert.Less(Mathf.Abs(p - before), 0.02f, "轉移當帧位置連續。");

            float prev = p;
            int frames = 0;
            while (s.Phase == PosturePhase.Return && frames < 200)
            {
                StepN(ref s, Moving(-0.35f), ref p, 1);
                Assert.Less(Mathf.Abs(p - prev), 0.06f, "回到包絡是彈簧，不是瞬移。");
                prev = p;
                frames++;
            }
            Assert.AreEqual(PosturePhase.Moving, s.Phase);
            Assert.AreEqual(-0.35f, p, 0.011f);
            Assert.Less(frames, 40, "ω15 在 0.6 s 內交回。");
            Assert.IsFalse(float.IsNaN(v));
        }

        [Test]
        public void Return_ClampedByHardLimit_DropsImmediately()
        {
            var s = default(PostureState);
            float p = -0.35f;
            StepN(ref s, Still(-0.35f, -0.05f, 0f), ref p, 90);
            StepN(ref s, Moving(-0.35f, -0.3f), ref p, 1);
            Assert.LessOrEqual(p, -0.3f + Eps, "第一步需要的深度立即滿足（腳要搆得到）。");
        }

        [Test]
        public void Return_CanResettle_WithoutPassingThroughMoving()
        {
            var s = default(PostureState);
            float p = -0.35f;
            StepN(ref s, Still(-0.35f, -0.1f, 0f), ref p, 60);
            StepN(ref s, Moving(-0.35f), ref p, 2);
            Assert.AreEqual(PosturePhase.Return, s.Phase);
            StepN(ref s, Still(-0.35f, -0.1f, 0f), ref p, 6);
            Assert.AreEqual(PosturePhase.Settling, s.Phase, "短暫的意圖（點一下）之後又靜止：直接回到站起來。");
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var s = default(PostureState);
            float p = -0.35f;
            StepN(ref s, Still(-0.35f, -0.1f, 0f), ref p, 30);
            s.Reset();
            Assert.AreEqual(PosturePhase.Moving, s.Phase);
            Assert.AreEqual(0f, s.Velocity);
            Assert.AreEqual(0, s.StillFrames);
            Assert.IsFalse(s.HasPreviousOutput);
        }
    }
}
