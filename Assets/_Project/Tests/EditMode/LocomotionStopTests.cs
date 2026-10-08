using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    public sealed class LocomotionStopTestFacade : AnimationFacadeBase
    {
        public string LastPlayedKey { get; private set; }
        public float RootNormalizedTime { get; set; }
        public float DominantChildNormalizedTime { get; set; }
        public bool HasDominantChildTime { get; set; } = true;

        public override void Play(string stateKey) => LastPlayedKey = stateKey;
        public override void PlayWithCallback(string stateKey, System.Action onComplete) => LastPlayedKey = stateKey;
        public override void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f) { }
        public override void SetFloat(string key, float value) { }
        public override void SetBool(string key, bool value) { }
        public override bool IsPlaying(string stateKey) => LastPlayedKey == stateKey;
        public override bool TryGetDominantChildNormalizedTime(string stateKey, out float normalizedTime)
        {
            normalizedTime = DominantChildNormalizedTime;
            return HasDominantChildTime && IsPlaying(stateKey);
        }
        public override float GetNormalizedTime() => RootNormalizedTime;
    }

    public class LocomotionStopTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        private LocomotionStopVariant Variant(float entryPhase, string key)
        {
            var bake = ScriptableObject.CreateInstance<MotionBakeData>();
            _created.Add(bake);
            bake.BakedDuration = 1f;
            bake.FootPhaseCurve = new AnimationCurve(new Keyframe(0f, entryPhase), new Keyframe(1f, entryPhase));
            return new LocomotionStopVariant(bake, key);
        }

        [Test]
        public void Selection_MatchesAuthoredEntryPhase()
        {
            LocomotionStopVariant[] variants = { Variant(0.2f, "Positive"), Variant(-0.2f, "Negative") };
            Assert.AreEqual(1, LocomotionStopSelector.SelectByEntryPhase(variants, FootPhase.LeftFootDown));
            Assert.AreEqual(0, LocomotionStopSelector.SelectByEntryPhase(variants, FootPhase.RightFootDown));
        }

        [Test]
        public void Selection_SafelyFallsBackAcrossIncompleteVariants()
        {
            Assert.AreEqual(-1, LocomotionStopSelector.SelectByEntryPhase(null, FootPhase.LeftFootDown));
            LocomotionStopVariant[] variants = { default, Variant(-0.2f, "OnlyValid") };
            Assert.AreEqual(1, LocomotionStopSelector.SelectByEntryPhase(variants, FootPhase.RightFootDown));
            Assert.AreEqual(1, LocomotionStopSelector.SelectNextValid(variants, 0));
        }

        [Test]
        public void Selection_WaitsForNearestAuthoredEntryPhase()
        {
            var loop = ScriptableObject.CreateInstance<MotionBakeData>();
            _created.Add(loop);
            loop.BakedDuration = 1f;
            loop.FootPhaseCurve = new AnimationCurve(
                new Keyframe(0f, -0.2f),
                new Keyframe(0.25f, 0f),
                new Keyframe(0.5f, 0.2f),
                new Keyframe(0.75f, 0f),
                new Keyframe(1f, -0.2f));
            LocomotionStopVariant[] variants =
            {
                Variant(0.2f, "PositiveStop"),
                Variant(-0.2f, "NegativeStop")
            };

            Assert.IsTrue(LocomotionStopSelector.TrySelectNearestFuturePhaseMatch(
                loop, variants, 0.2f, 0.02f, out int firstIndex, out float firstTarget));
            Assert.AreEqual(0, firstIndex);
            Assert.AreEqual(0.5f, firstTarget, 0.0001f);

            Assert.IsTrue(LocomotionStopSelector.TrySelectNearestFuturePhaseMatch(
                loop, variants, 0.6f, 0.02f, out int secondIndex, out float secondTarget));
            Assert.AreEqual(1, secondIndex);
            Assert.AreEqual(1f, secondTarget, 0.0001f);
        }

        [Test]
        public void RealAssets_LockLuRuEntrySemantics()
        {
            MotionBakeData lu = AssetDatabase.LoadAssetAtPath<MotionBakeData>(
                "Assets/ScriptableObjects/Motion/Bake_WalkFwdStop_LU.asset");
            MotionBakeData ru = AssetDatabase.LoadAssetAtPath<MotionBakeData>(
                "Assets/ScriptableObjects/Motion/Bake_WalkFwdStop_RU.asset");
            Assert.IsNotNull(lu);
            Assert.IsNotNull(ru);
            Assert.AreEqual(FootPhase.RightFootDown, lu.GetFootPhaseAt(0f));
            Assert.AreEqual(FootPhase.LeftFootDown, ru.GetFootPhaseAt(0f));

            MotionBakeData runLu = AssetDatabase.LoadAssetAtPath<MotionBakeData>(
                "Assets/ScriptableObjects/Motion/Bake_RunFwdStop_LU.asset");
            MotionBakeData runRu = AssetDatabase.LoadAssetAtPath<MotionBakeData>(
                "Assets/ScriptableObjects/Motion/Bake_RunFwdStop_RU.asset");
            Assert.IsNotNull(runLu);
            Assert.IsNotNull(runRu);
            Assert.AreEqual(FootPhase.RightFootDown, runLu.GetFootPhaseAt(0f));
            Assert.AreEqual(FootPhase.LeftFootDown, runRu.GetFootPhaseAt(0f));
            Assert.AreEqual(60f, runLu.SampleRate);
            Assert.AreEqual(60f, runRu.SampleRate);
            Assert.IsFalse(runLu.SourceClip.isLooping);
            Assert.IsFalse(runRu.SourceClip.isLooping);
        }

        /// <summary>
        /// docs/48 M1.0 characterization（出貨資產）：停步 tier 由**實際速度**決定，與 control gait 無關。
        /// 搖桿半推 Run ⇒ 強度落在 Walk 帶 ⇒ 播 Walk Stop，而 `Gait` 仍是 Run；Sprint 放開 ⇒ 沒有停步 clip。
        /// 這兩件事是 D1-b（規則⑦跟 committed stop tier、不跟 Gait）的事實前提；M1.3 不會改變它們。
        /// </summary>
        [Test]
        public void Characterization_StopTier_FollowsReleaseSpeed_NotControlGait()
        {
            var gait = AssetDatabase.LoadAssetAtPath<GaitProfileSO>("Assets/ScriptableObjects/Movement/Gait_ActionRPG.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/X Bot.prefab");
            Assert.IsNotNull(gait, "Gait_ActionRPG.asset");
            Assert.IsNotNull(prefab, "X Bot.prefab");
            var model = prefab.GetComponentInChildren<LocomotionModel>(true);
            Assert.IsNotNull(model, "X Bot 必須有 LocomotionModel");

            float Band(string field) => (float)typeof(LocomotionModel)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(model);
            float walkMin = Band("walkStopMinIntensity"), walkMax = Band("walkStopMaxIntensity");
            float runMin = Band("runStopMinIntensity"), runMax = Band("runStopMaxIntensity");

            // 半推（0.5）、未按 Walk／Sprint ⇒ policy 寫 Gait = Run，但強度 = defaultIntensity × 0.5。
            float halfStickRun = gait.ResolveIntensity(0.5f, sprintHeld: false, walkActive: false);
            Assert.AreEqual(LocomotionStopTier.Walk,
                LocomotionStopSelector.SelectTier(halfStickRun, walkMin, walkMax, runMin, runMax),
                $"半推 Run（強度 {halfStickRun:F4}）放開時應播 Walk Stop——停步 tier 跟速度，不跟 Gait");

            float sprint = gait.ResolveIntensity(1f, sprintHeld: true, walkActive: false);
            Assert.AreEqual(LocomotionStopTier.None,
                LocomotionStopSelector.SelectTier(sprint, walkMin, walkMax, runMin, runMax),
                $"Sprint（強度 {sprint:F4}）沒有 Stop 資產 ⇒ 不承諾任何停步（骨盆仍以意圖放開進 StopHold，見 MovingPelvisSolverTests）");
        }

        [Test]
        public void TierSelection_UsesEntryIntensityBeforePhaseSelection()
        {
            Assert.AreEqual(LocomotionStopTier.Walk,
                LocomotionStopSelector.SelectTier(0.3651f, 0.35f, 0.50f, 0.75f, 0.875f));
            Assert.AreEqual(LocomotionStopTier.Run,
                LocomotionStopSelector.SelectTier(0.75f, 0.35f, 0.50f, 0.75f, 0.875f));
            Assert.AreEqual(LocomotionStopTier.Run,
                LocomotionStopSelector.SelectTier(0.74999994f, 0.35f, 0.50f, 0.75f, 0.875f));
            Assert.AreEqual(LocomotionStopTier.None,
                LocomotionStopSelector.SelectTier(0.70f, 0.35f, 0.50f, 0.75f, 0.875f));
            Assert.AreEqual(LocomotionStopTier.None,
                LocomotionStopSelector.SelectTier(0.748f, 0.35f, 0.50f, 0.75f, 0.875f));
            Assert.AreEqual(LocomotionStopTier.None,
                LocomotionStopSelector.SelectTier(0.75f, 0.35f, 0.80f, 0.75f, 0.875f));
        }

        [Test]
        public void Request_RequiresSingleReleaseEdgeAndWalkBand()
        {
            Assert.IsTrue(LocomotionStopSelector.ShouldRequest(
                true, 0f, false, true, true, 1f / 60f, 0.3651f, 0.35f, 0.50f));
            Assert.IsFalse(LocomotionStopSelector.ShouldRequest(
                false, 0f, false, true, true, 1f / 60f, 0.3651f, 0.35f, 0.50f));
            Assert.IsFalse(LocomotionStopSelector.ShouldRequest(
                true, 0f, false, true, true, 0f, 0.3651f, 0.35f, 0.50f));
            Assert.IsFalse(LocomotionStopSelector.ShouldRequest(
                true, 0f, false, true, true, 1f / 60f, 0.75f, 0.35f, 0.50f));
            Assert.IsFalse(LocomotionStopSelector.ShouldRequest(
                true, 0f, false, true, true, 1f / 60f, 0.34f, 0.35f, 0.50f));
        }

        [Test]
        public void Runtime_RejectsRepeatedAndStaleCallbacks()
        {
            var runtime = new LocomotionStopRuntime();
            int first = runtime.Begin(LocomotionStopTier.Walk, 0);
            Assert.IsTrue(runtime.TryRequestCompletion(first));
            runtime.Invalidate();
            Assert.IsFalse(runtime.TryRequestCompletion(first));
            int second = runtime.Begin(LocomotionStopTier.Run, 1);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(LocomotionStopTier.Run, runtime.Tier);
            Assert.IsFalse(runtime.TryRequestCompletion(first));
            Assert.IsTrue(runtime.TryRequestCompletion(second));
        }

        [Test]
        public void Runtime_TimesOutWithoutEndEvent()
        {
            var runtime = new LocomotionStopRuntime();
            runtime.Begin(LocomotionStopTier.Walk, 0);
            runtime.Advance(0.5f, 1.26f);
            Assert.IsTrue(runtime.HasTimedOut(1f, 0.25f));
        }

        [Test]
        public void Runtime_PendingDoesNotAdvancePlaybackUntilPromoted()
        {
            var runtime = new LocomotionStopRuntime();
            int generation = runtime.BeginPending(LocomotionStopTier.Walk, 1, 0.5f);
            Assert.IsTrue(runtime.IsActive);
            Assert.IsTrue(runtime.IsPending);
            Assert.IsFalse(runtime.IsPlaying);
            Assert.AreEqual(0.5f, runtime.TargetNormalizedTime);

            runtime.Advance(0.4f, 0.1f);
            Assert.AreEqual(0f, runtime.NormalizedTime);
            Assert.IsFalse(runtime.TryRequestCompletion(generation));

            runtime.AdvancePending(0.1f);
            Assert.IsTrue(runtime.StartPlaying());
            Assert.AreEqual(generation, runtime.Generation);
            Assert.IsTrue(runtime.IsPlaying);
            runtime.Advance(0.4f, 0.1f);
            Assert.AreEqual(0.4f, runtime.NormalizedTime);
        }

        [Test]
        public void Runtime_HoldsEntryMotionUntilPlayheadAdvances()
        {
            var runtime = new LocomotionStopRuntime();
            runtime.Begin(LocomotionStopTier.Run, 0);
            Assert.IsTrue(runtime.IsPlaying);
            Assert.IsTrue(runtime.IsAwaitingPlayhead, "Play 當帧播放頭仍是 0。");
            Assert.IsTrue(runtime.HoldsEntryMotion);

            runtime.Advance(0f, 1f / 60f);
            Assert.IsTrue(runtime.IsAwaitingPlayhead, "新 state 的第一次評估不推進時間（docs/07 R8 實測第 2 帧）。");

            runtime.Advance(0.017f, 1f / 60f);
            Assert.IsFalse(runtime.IsAwaitingPlayhead);
            Assert.IsFalse(runtime.HoldsEntryMotion, "播放頭一前進就交給曲線。");
            Assert.AreEqual(0f, runtime.PreviousNormalizedTime, "曲線從 t=0 起積分——減速形狀不被跳過。");

            var pending = new LocomotionStopRuntime();
            pending.BeginPending(LocomotionStopTier.Walk, 0, 0.5f);
            Assert.IsTrue(pending.HoldsEntryMotion);
            Assert.IsFalse(pending.IsAwaitingPlayhead, "Pending 不是 Playing。");
        }

        [Test]
        public void Model_HoldsReleaseEntrySpeedUntilStopPlayheadAdvances()
        {
            var go = new GameObject("LocomotionPlayheadHoldUnderTest");
            _created.Add(go);
            LocomotionModel model = go.AddComponent<LocomotionModel>();
            LocomotionStopTestFacade facade = go.AddComponent<LocomotionStopTestFacade>();

            LocomotionStopVariant runVariant = Variant(-0.2f, "RunStop");
            SetPrivateField(model, "runLoopBakeData", runVariant.BakeData);
            SetPrivateField(model, "runStopVariants", new[] { runVariant });

            var smoother = default(LocomotionSpeedSmoother);
            var runIntent = new MovementIntentData { DesiredSpeedNormalized = 0.75f, DesiredDirection = Vector3.forward };
            for (int i = 0; i < 120; i++)
                smoother.Tick(in runIntent, 0.12f, 0.18f, 720f, 1f / 60f);
            float entrySpeed = smoother.Speed;

            SetPrivateField(model, "_smoother", smoother);
            SetPrivateField(model, "_wasIntending", true);
            facade.Play("Idle");
            facade.RootNormalizedTime = 0f;
            var data = new PlayerRuntimeData { IsGrounded = true };
            data.MovementIntent = default;

            // 放開帧（Play）與下一帧：播放頭 0 ⇒ 維持 release-entry 速度，不先 B9 衰減。
            for (int frame = 0; frame < 2; frame++)
            {
                SetPrivateField(model, "_lastMotionFrame", Time.frameCount - 1);
                model.Tick(data, facade, 1f / 60f);
                Assert.AreEqual("RunStop", facade.LastPlayedKey);
                Assert.AreEqual(entrySpeed, data.MoveSpeed, 0.000001f, $"frame {frame}：播放頭未前進期間不得開始減速。");
            }

            // 播放頭前進 ⇒ 曲線接手，smoother 恢復既有 B9（Stop 期間 MoveSpeed 不寫曲線速度，docs/07 §10.2）。
            facade.RootNormalizedTime = 0.017f;
            SetPrivateField(model, "_lastMotionFrame", Time.frameCount - 1);
            model.Tick(data, facade, 1f / 60f);
            Assert.Less(data.MoveSpeed, entrySpeed);
        }

        [Test]
        public void Model_ReleaseUsesPreDecaySpeedForRunTier()
        {
            var go = new GameObject("LocomotionReleaseUnderTest");
            _created.Add(go);
            LocomotionModel model = go.AddComponent<LocomotionModel>();
            LocomotionStopTestFacade facade = go.AddComponent<LocomotionStopTestFacade>();

            LocomotionStopVariant runVariant = Variant(-0.2f, "RunStop");
            MotionBakeData runBake = runVariant.BakeData;
            SetPrivateField(model, "runLoopBakeData", runBake);
            SetPrivateField(model, "runStopVariants", new[] { runVariant });

            var smoother = default(LocomotionSpeedSmoother);
            var runIntent = new MovementIntentData
            {
                DesiredSpeedNormalized = 0.75f,
                DesiredDirection = Vector3.forward
            };
            for (int i = 0; i < 120; i++)
                smoother.Tick(in runIntent, 0.12f, 0.18f, 720f, 1f / 60f);

            Assert.Less(smoother.Speed, 0.75f,
                "SmoothDamp 以漸近方式收斂，真實穩態不應被測試假造為精確 0.75。");
            Assert.GreaterOrEqual(smoother.Speed, 0.749f);

            SetPrivateField(model, "_smoother", smoother);
            SetPrivateField(model, "_wasIntending", true);
            SetPrivateField(model, "_lastMotionFrame", Time.frameCount - 1);

            facade.Play("Idle");
            var data = new PlayerRuntimeData { IsGrounded = true };
            data.MovementIntent = default;
            model.Tick(data, facade, 1f / 60f);

            Assert.Less(data.MoveSpeed, 0.75f, "測試前提：放開首幀的輸出速度已被 SmoothDamp 衰減。");
            Assert.AreEqual("RunStop", facade.LastPlayedKey,
                "tier 必須使用 SmoothDamp 前的 0.75 入場速度，不能使用本幀已衰減的輸出速度。");
        }

        [Test]
        public void Model_StopPhaseUsesDominantChildClockInsteadOfMixerRoot()
        {
            var go = new GameObject("LocomotionPhaseClockUnderTest");
            _created.Add(go);
            LocomotionModel model = go.AddComponent<LocomotionModel>();
            LocomotionStopTestFacade facade = go.AddComponent<LocomotionStopTestFacade>();

            var loopBake = ScriptableObject.CreateInstance<MotionBakeData>();
            _created.Add(loopBake);
            loopBake.BakedDuration = 1f;
            loopBake.FootPhaseCurve = new AnimationCurve(
                new Keyframe(0f, -0.2f),
                new Keyframe(0.25f, 0f),
                new Keyframe(0.5f, 0.2f),
                new Keyframe(0.75f, 0f),
                new Keyframe(1f, -0.2f));

            LocomotionStopVariant positive = Variant(0.2f, "PositiveStop");
            LocomotionStopVariant negative = Variant(-0.2f, "NegativeStop");
            SetPrivateField(model, "walkLoopBakeData", loopBake);
            SetPrivateField(model, "walkStopVariants", new[] { positive, negative });

            var smoother = default(LocomotionSpeedSmoother);
            var walkIntent = new MovementIntentData
            {
                DesiredSpeedNormalized = 0.3651f,
                DesiredDirection = Vector3.forward
            };
            for (int i = 0; i < 120; i++)
                smoother.Tick(in walkIntent, 0.12f, 0.18f, 720f, 1f / 60f);

            SetPrivateField(model, "_smoother", smoother);
            SetPrivateField(model, "_wasIntending", true);
            SetPrivateField(model, "_lastMotionFrame", Time.frameCount - 1);

            facade.RootNormalizedTime = 0.5f;
            facade.DominantChildNormalizedTime = 0.75f;
            facade.Play("Idle");

            var data = new PlayerRuntimeData { IsGrounded = true };
            data.MovementIntent = default;
            model.Tick(data, facade, 1f / 60f);

            Assert.AreEqual("Idle", facade.LastPlayedKey,
                "放開時不在 authored 入場點，必須維持 Locomotion 等待，不能立即硬切 Stop。");
            Assert.AreEqual(smoother.Speed, data.MoveSpeed, 0.000001f,
                "Pending 期間必須維持 release-entry 速度，避免先減速再被 Stop 曲線重新推動。");

            facade.DominantChildNormalizedTime = 1f;
            SetPrivateField(model, "_lastMotionFrame", Time.frameCount - 1);
            model.Tick(data, facade, 1f / 60f);

            Assert.AreEqual("NegativeStop", facade.LastPlayedKey,
                "抵達最近的 authored 入場相位後，才播放對應 Stop。");
        }

        [Test]
        public void Model_ReportsMotionWhileStopIsActive()
        {
            var go = new GameObject("LocomotionModelUnderTest");
            _created.Add(go);
            LocomotionModel model = go.AddComponent<LocomotionModel>();
            FieldInfo field = typeof(LocomotionModel).GetField("_stop", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            var runtime = new LocomotionStopRuntime();
            runtime.Begin(LocomotionStopTier.Walk, 0);
            field.SetValue(model, runtime);
            Assert.IsTrue(model.IsProducingMotion);
        }

        [Test]
        public void Model_PublishesAndClearsNumericStopPrediction()
        {
            var go = new GameObject("LocomotionPredictionUnderTest");
            _created.Add(go);
            LocomotionModel model = go.AddComponent<LocomotionModel>();
            LocomotionStopTestFacade facade = go.AddComponent<LocomotionStopTestFacade>();
            LocomotionStopVariant variant = Variant(-0.2f, "RunStop");
            MotionBakeData bake = variant.BakeData;
            bake.SpeedCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f / 60f, 1f), new Keyframe(1f, 0f));
            var schedule = new FootPlantSchedule
            {
                EventCount = 1,
                EndPose = new FootPlantEndPose
                {
                    LeftAnkleInRoot = new Vector3(-0.1f, 0f, 0.2f),
                    RightAnkleInRoot = new Vector3(0.1f, 0f, 0.2f),
                    ClipDuration = 1f,
                }
            };
            var plant = new FootPlantScheduleEvent { Side = FootPlantSide.Left, ClipTime = 0.5f, AnkleInRoot = new Vector3(-0.1f, 0f, 0.2f) };
            schedule.SetEvent(0, in plant);
            bake.FootPlantSchedule = schedule;
            SetPrivateField(model, "runLoopBakeData", bake);
            SetPrivateField(model, "runStopVariants", new[] { variant });

            var smoother = default(LocomotionSpeedSmoother);
            var runIntent = new MovementIntentData { DesiredSpeedNormalized = 0.75f, DesiredDirection = Vector3.forward };
            for (int i = 0; i < 120; i++) smoother.Tick(in runIntent, 0.12f, 0.18f, 720f, 1f / 60f);
            SetPrivateField(model, "_smoother", smoother);
            SetPrivateField(model, "_wasIntending", true);
            facade.Play("Idle");
            var data = new PlayerRuntimeData { IsGrounded = true };

            SetPrivateField(model, "_lastMotionFrame", Time.frameCount - 1);
            model.Tick(data, facade, 1f / 60f);
            Assert.IsFalse(data.LocomotionStopPrediction.Active, "播放頭尚未前進，倍率未知時不猜結束站姿。");
            Assert.IsTrue(data.LocomotionStopPrediction.HasNextPlant, "ADR-010 D1-R1：variant committed 就發布 next plant，不等播放頭。");
            Assert.AreEqual(CommittedStopTier.Run, data.LocomotionStopPrediction.StopTier, "ADR-010 D6：committed 的當帧就發布 tier。");

            facade.RootNormalizedTime = 0.1f;
            SetPrivateField(model, "_lastMotionFrame", Time.frameCount - 1);
            model.Tick(data, facade, 1f / 60f);
            Assert.IsTrue(data.LocomotionStopPrediction.Active);
            Assert.Greater(data.LocomotionStopPrediction.RemainingForwardDisplacement, 0f);
            Assert.AreEqual(new Vector3(-0.1f, 0f, 0.2f), data.LocomotionStopPrediction.EndLeftAnkleInRoot);
            Assert.AreEqual(new Vector3(0.1f, 0f, 0.2f), data.LocomotionStopPrediction.EndRightAnkleInRoot);
            // ADR-010 D5：下一次 planned plant（clip 0.5 s，尚未發生）——publish root 座標系，含前進量 ⇒ z 大於 ankle-in-root 的 0.2
            Assert.IsTrue(data.LocomotionStopPrediction.HasNextPlant);
            Assert.IsTrue(data.LocomotionStopPrediction.NextPlantIsLeft);
            Assert.IsTrue(data.LocomotionStopPrediction.NextPlantIsFinal, "排程只有一個事件 ⇒ 它就是最後一次 planned plant");
            Assert.AreEqual(-0.1f, data.LocomotionStopPrediction.NextPlantAnkleInPublishRoot.x, 1e-5f);
            Assert.Greater(data.LocomotionStopPrediction.NextPlantAnkleInPublishRoot.z, 0.2f);

            data.MovementIntent = runIntent;
            SetPrivateField(model, "_lastMotionFrame", Time.frameCount - 1);
            model.Tick(data, facade, 1f / 60f);
            Assert.IsFalse(data.LocomotionStopPrediction.Active, "移動意圖中斷 Stop 的同一 Tick 必須清除預測。");
            Assert.AreEqual(Vector3.zero, data.LocomotionStopPrediction.EndLeftAnkleInRoot, "失效時整份快照是 default（ADR-010 D1）。");
            Assert.IsFalse(data.LocomotionStopPrediction.HasNextPlant);
            Assert.AreEqual(CommittedStopTier.None, data.LocomotionStopPrediction.StopTier, "停步中斷的同一 Tick，tier 也一併清除。");
        }

        /// <summary>
        /// ADR-010 D6：tier 的有效條件只看「有沒有 committed 的停步」——Bake 沒有排程（沒有結束站姿、沒有 next plant）時，
        /// 其他欄位維持 default，tier 照樣發布。⇒ 規則⑦ 不會因為某支 clip 少烘焙了排程就誤判成「不是停步」。
        /// </summary>
        [Test]
        public void Model_PublishesCommittedStopTier_EvenWithoutSchedule()
        {
            var go = new GameObject("LocomotionStopTierUnderTest");
            _created.Add(go);
            LocomotionModel model = go.AddComponent<LocomotionModel>();
            LocomotionStopTestFacade facade = go.AddComponent<LocomotionStopTestFacade>();
            LocomotionStopVariant variant = Variant(-0.2f, "RunStop");
            variant.BakeData.FootPlantSchedule = default;   // 沒有排程
            SetPrivateField(model, "runLoopBakeData", variant.BakeData);
            SetPrivateField(model, "runStopVariants", new[] { variant });

            var smoother = default(LocomotionSpeedSmoother);
            var runIntent = new MovementIntentData { DesiredSpeedNormalized = 0.75f, DesiredDirection = Vector3.forward };
            for (int i = 0; i < 120; i++) smoother.Tick(in runIntent, 0.12f, 0.18f, 720f, 1f / 60f);
            SetPrivateField(model, "_smoother", smoother);
            SetPrivateField(model, "_wasIntending", true);
            facade.Play("Idle");
            var data = new PlayerRuntimeData { IsGrounded = true };

            SetPrivateField(model, "_lastMotionFrame", Time.frameCount - 1);
            model.Tick(data, facade, 1f / 60f);
            Assert.AreEqual(CommittedStopTier.Run, data.LocomotionStopPrediction.StopTier);
            Assert.IsFalse(data.LocomotionStopPrediction.Active);
            Assert.IsFalse(data.LocomotionStopPrediction.HasNextPlant);
            Assert.AreEqual(Vector3.zero, data.LocomotionStopPrediction.RootWorldPositionAtPublish,
                "只有 tier 時，其他區塊仍是 default——D6 只覆寫自己那一欄");
        }

        [TestCase(LocomotionStopTier.None, CommittedStopTier.None)]
        [TestCase(LocomotionStopTier.Walk, CommittedStopTier.Walk)]
        [TestCase(LocomotionStopTier.Run, CommittedStopTier.Run)]
        public void CommittedStopTier_MirrorsMovementTier(LocomotionStopTier tier, CommittedStopTier expected)
        {
            Assert.AreEqual(expected, LocomotionModel.ToCommittedStopTier(tier));
            Assert.AreEqual((int)tier, (int)expected, "數值保持一致，避免兩個 enum 漂移");
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }
    }
}
