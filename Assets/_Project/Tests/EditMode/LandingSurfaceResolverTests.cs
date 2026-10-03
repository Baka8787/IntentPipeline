using NUnit.Framework;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🧪 T3 landing-surface slice Phase 2（docs/45 §17）：destination resolver 的承諾規則、可信度、即時驗證收斂，
    /// 以及擺盪抬升混合的「不穿透」與「權重 0 ⇒ 現行」。runtime outcome 屬 PlayMode 探針（LandingSurfaceProbe）。
    /// </summary>
    public class LandingSurfaceResolverTests
    {
        private const float Dt = 1f / 60f;
        private const float Epsilon = 1e-5f;

        private static readonly LandingSurfaceTuning Tuning = new LandingSurfaceTuning
        {
            VerifyBeta = 0.9f,
            ConfirmFrames = 2,
            ReviseFrames = 3,
            SurfaceTolerance = 0.03f,
            LowConfidenceWeight = 0.5f,
            WeightRate = 10f,
            ContradictionRate = 20f,
            PlantStillDistance = 0.01f,
        };

        private static LandingSurfaceInputs Plant(float y, bool final, float beta = 0f) => new LandingSurfaceInputs
        {
            Phase = FootLockPhase.Unlocked, Beta = beta, HasPlant = true, PlantSurfaceY = y, PlantIsFinal = final,
            HasLive = true, LiveSurfaceY = 0f,
        };

        private static LandingSurfaceInputs Live(float y, float beta, bool hasPlant = false, float plantY = 0f) => new LandingSurfaceInputs
        {
            Phase = FootLockPhase.Unlocked, Beta = beta, HasPlant = hasPlant, PlantSurfaceY = plantY,
            HasLive = true, LiveSurfaceY = y,
        };

        private static void Step(ref LandingSurfaceState s, LandingSurfaceInputs input, int frames = 1)
        {
            for (int i = 0; i < frames; i++) LandingSurfaceResolver.Step(ref s, in input, Dt, in Tuning);
        }

        // === 承諾規則（§11.2）===

        [Test]
        public void FirstCommit_RequiresAnchoredPlantThenTwoAgreeingFrames()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, true));
            Assert.IsFalse(s.Intent.Valid, "第一帧還不知道落點是否定住。");
            Step(ref s, Plant(0.3f, true));
            Assert.IsFalse(s.Intent.Valid, "單帧候選不承諾。");
            Step(ref s, Plant(0.3f, true));
            Assert.IsTrue(s.Intent.Valid);
            Assert.AreEqual(0.3f, s.Intent.SurfaceY, Epsilon);
            Assert.AreEqual(LandingSurfaceSource.Prediction, s.Intent.Source);
            Assert.AreEqual(0, s.Intent.Revisions);
        }

        [Test]
        public void Revision_RequiresThreeConsecutiveFrames_SingleFrameNeverChangesAnswer()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, true), 3);
            Step(ref s, Plant(0.15f, true));
            Step(ref s, Plant(0.3f, true));
            Assert.AreEqual(0.3f, s.Intent.SurfaceY, Epsilon, "單帧不同的候選不改答案。");
            Step(ref s, Plant(0.15f, true), 2);
            Assert.AreEqual(0.3f, s.Intent.SurfaceY, Epsilon);
            Step(ref s, Plant(0.15f, true));
            Assert.AreEqual(0.15f, s.Intent.SurfaceY, Epsilon);
            Assert.AreEqual(1, s.Intent.Revisions);
        }

        // === 可信度 ===

        [Test]
        public void FinalPlant_IsHighConfidence_FullWeight()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, true), 30);
            Assert.AreEqual(LandingSurfaceConfidence.High, s.Intent.Confidence);
            Assert.AreEqual(1f, s.Intent.Weight, Epsilon);
        }

        [Test]
        public void NonFinalPlant_IsLowConfidence_WeightCapped()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, false), 30);
            Assert.AreEqual(LandingSurfaceConfidence.Low, s.Intent.Confidence);
            Assert.AreEqual(Tuning.LowConfidenceWeight, s.Intent.Weight, Epsilon);
        }

        [Test]
        public void LiveAgreement_UpgradesLowConfidenceToHigh()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, false), 10);
            Step(ref s, Live(0.3f, 0.95f, true, 0.3f), 10);
            Assert.AreEqual(LandingSurfaceConfidence.High, s.Intent.Confidence);
            Assert.AreEqual(LandingSurfaceSource.Live, s.Intent.Source);
            Assert.AreEqual(1f, s.Intent.Weight, Epsilon);
        }

        // === 即時驗證的安全收斂 ===

        [Test]
        public void LiveContradiction_FadesWeightBeforeRevising_ThenConvergesToLive()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, false), 30);
            float before = s.Intent.Weight;

            Step(ref s, Live(0.15f, 0.95f, true, 0.3f));
            Assert.AreEqual(before, s.Intent.Weight, Epsilon, "單帧即時取樣既不改答案、也不觸發退出。");

            Step(ref s, Live(0.15f, 0.95f, true, 0.3f));
            Assert.AreEqual(0.3f, s.Intent.SurfaceY, Epsilon, "連續 2 帧矛盾還不改答案……");
            Assert.Less(s.Intent.Weight, before, "……但過渡開始退出。");
            Assert.LessOrEqual(before - s.Intent.Weight, Tuning.ContradictionRate * Dt + Epsilon, "退出速度受 ContradictionRate 限制。");

            Step(ref s, Live(0.15f, 0.95f, true, 0.3f));
            Assert.AreEqual(0.15f, s.Intent.SurfaceY, Epsilon);
            Assert.AreEqual(LandingSurfaceSource.Live, s.Intent.Source);
            Assert.AreEqual(1, s.Intent.Revisions);
        }

        [Test]
        public void Revision_FadesThroughZeroBeforeSwitchingSurface()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, true), 30);
            Step(ref s, Plant(0.0f, true), 3);
            Assert.AreEqual(0.0f, s.Intent.SurfaceY, Epsilon);
            Assert.AreEqual(0.3f, s.Intent.BlendSurfaceY, Epsilon, "過渡仍在舊踏面上時不換——先退到 0。");
            Assert.Less(s.Intent.Weight, 1f);
            for (int i = 0; i < 10 && s.Intent.BlendSurfaceY > 0.15f; i++)
            {
                float before = s.Intent.Weight;
                Step(ref s, Plant(0.0f, true));
                Assert.LessOrEqual(before - s.Intent.Weight, Tuning.WeightRate * Dt + Epsilon);
            }
            Assert.AreEqual(0f, s.Intent.BlendSurfaceY, Epsilon, "權重歸零那一帧才換成新答案。");
            Step(ref s, Plant(0.0f, true), 30);
            Assert.AreEqual(1f, s.Intent.Weight, Epsilon);
        }

        [Test]
        public void PlantMovingWithCharacter_IsNotACandidate()
        {
            // 停步 pending：落點以「目前 root ＋ clip 0 位移」發布，每帧跟著角色前進 3.8 cm（Walk 實測）。
            var s = default(LandingSurfaceState);
            for (int i = 0; i < 20; i++)
            {
                var input = Plant(0.3f, true);
                input.PlantXZ = new UnityEngine.Vector2(0.038f * i, 0f);
                Step(ref s, input);
            }
            Assert.IsFalse(s.Intent.Valid);
            Assert.AreEqual(0f, s.Intent.Weight);
        }

        [Test]
        public void VerifyWindow_NeedsRisingCrossing_HighBetaRightAfterReleaseDoesNotOpenIt()
        {
            // 剛釋放的腳 β 仍高：腳下是「上一個」支撐，不是目的地。
            var s = default(LandingSurfaceState);
            Step(ref s, Live(0.15f, 0.95f, true, 0.3f), 10);
            Assert.AreEqual(0.3f, s.Intent.SurfaceY, Epsilon);
            Assert.AreEqual(LandingSurfaceSource.Prediction, s.Intent.Source);
            Assert.AreEqual(Tuning.LowConfidenceWeight, s.Intent.Weight, Epsilon, "即時矛盾沒有讓過渡退出。");
        }

        [Test]
        public void BelowVerifyBeta_LiveDoesNotOverridePrediction()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Live(0.15f, 0.5f, true, 0.3f), 10);
            Assert.AreEqual(0.3f, s.Intent.SurfaceY, Epsilon, "擺盪中（β 低）腳下的踏面不是目的地。");
            Assert.AreEqual(LandingSurfaceSource.Prediction, s.Intent.Source);
        }

        [Test]
        public void WithoutPrediction_LiveNeverCreatesIntent()
        {
            // 一般行走：每一步落腳前 β 都會超過 VerifyBeta——沒有 next plant 時必須維持 no-intent（＝ T3 現行）。
            var s = default(LandingSurfaceState);
            Step(ref s, Live(0.15f, 0.95f), 30);
            Assert.IsFalse(s.Intent.Valid);
            Assert.AreEqual(0f, s.Intent.Weight);
        }

        // === 失效 ===

        [Test]
        public void PredictionDisappears_IntentInvalidates_WeightFadesToZero()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, true), 30);
            var none = new LandingSurfaceInputs { Phase = FootLockPhase.Unlocked };
            Step(ref s, none);
            Assert.IsFalse(s.Intent.Valid, "快照失效 ⇒ 不沿用舊答案（ADR-010：消費端不得跨帧快取）。");
            Assert.Greater(s.Intent.Weight, 0f, "過渡以有限速度退出，不跳。");
            Step(ref s, none, 30);
            Assert.AreEqual(0f, s.Intent.Weight);
        }

        [TestCase(FootLockPhase.Locked)]
        [TestCase(FootLockPhase.Releasing)]
        public void NotUnlocked_ResetsState(FootLockPhase phase)
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, true), 30);
            var input = Plant(0.3f, true);
            input.Phase = phase;
            Step(ref s, input);
            Assert.IsFalse(s.Intent.Valid);
            Assert.AreEqual(0f, s.Intent.Weight, "鎖定由 T3 自己的支撐決定（authority 不變）。");
        }

        [Test]
        public void VerifyWindow_IsLatched_BetaDipDoesNotRestoreWeight()
        {
            var s = default(LandingSurfaceState);
            Step(ref s, Plant(0.3f, true), 30);
            Step(ref s, Live(0.15f, 0.95f, true, 0.3f));     // 進入驗證窗，即時矛盾
            float afterContradiction = s.Intent.Weight;
            Step(ref s, Live(0.15f, 0.85f, true, 0.3f));     // β 掉回門檻下：仍在驗證窗
            Assert.Less(s.Intent.Weight, afterContradiction, "β 在門檻附近來回時，矛盾的退出不得中斷。");
        }

        // === 參考面抬高 ===

        [Test]
        public void ReferenceRaise_OnlyUpward()
        {
            Assert.AreEqual(0.1f, LandingSurfaceResolver.ReferenceRaise(0.2f, 0.3f, 1f, 1f), Epsilon, "上樓：參考面升到目的地踏面。");
            Assert.AreEqual(0f, LandingSurfaceResolver.ReferenceRaise(0.3f, 0.1f, 1f, 1f), "下樓：腳還在上一階上方，參考面不往下拉。");
        }

        [Test]
        public void ReferenceRaise_ScalesWithWeightAndProximity()
        {
            Assert.AreEqual(0.05f, LandingSurfaceResolver.ReferenceRaise(0.2f, 0.3f, 0.5f, 1f), Epsilon);
            Assert.AreEqual(0f, LandingSurfaceResolver.ReferenceRaise(0.2f, 0.3f, 1f, 0f));
        }

        [Test]
        public void Proximity_SmoothstepFalloff()
        {
            Assert.AreEqual(1f, LandingSurfaceResolver.Proximity(0f, 0.35f), Epsilon);
            Assert.AreEqual(0.5f, LandingSurfaceResolver.Proximity(0.175f, 0.35f), Epsilon);
            Assert.AreEqual(0f, LandingSurfaceResolver.Proximity(0.5f, 0.35f), Epsilon);
            Assert.AreEqual(1f, LandingSurfaceResolver.Proximity(5f, 0f), "radius 0 ⇒ 不限距離。");
        }

        // === 抬升混合 ===

        [Test]
        public void BlendLift_ZeroWeight_ReturnsLiveLift()
        {
            float live = FootContactSolver.ComputeMinimumLift(-0.1f, 0.5f);
            Assert.AreEqual(live, LandingSurfaceResolver.BlendLift(live, true, -0.1f, 0.2f, 0.5f, 0f));
        }

        [Test]
        public void BlendLift_FullWeight_PullsLowestSoleOntoDestination()
        {
            // 上樓：腳底在下一階踏面之下 0.15 m（目的地抬升 +0.15），目前腳下懸空 0.02 m。
            float lift = LandingSurfaceResolver.BlendLift(FootContactSolver.ComputeMinimumLift(-0.02f, 1f), true, -0.02f, 0.15f, 1f, 1f);
            Assert.AreEqual(0.15f, lift, Epsilon);
        }

        [Test]
        public void BlendLift_NeverPenetratesLiveGround()
        {
            // 下樓：目的地在下一階（−0.17），但腳還在上一階上方、已經穿透 0.01 ⇒ 至少抬 0.01。
            float lift = LandingSurfaceResolver.BlendLift(0.01f, true, 0.01f, -0.17f, 1f, 1f);
            Assert.AreEqual(0.01f, lift, Epsilon);
        }

        [Test]
        public void BlendLift_IsContinuousInWeight()
        {
            float live = FootContactSolver.ComputeMinimumLift(-0.05f, 0.8f);
            float a = LandingSurfaceResolver.BlendLift(live, true, -0.05f, 0.12f, 0.8f, 0.001f);
            Assert.AreEqual(live, a, 0.001f);
        }
    }
}
