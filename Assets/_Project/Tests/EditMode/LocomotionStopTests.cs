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
                DesiredDirection = Vector2.up
            };
            for (int i = 0; i < 120; i++)
                smoother.Tick(in runIntent, 0.12f, 0.18f, 1f / 60f);

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
                DesiredDirection = Vector2.up
            };
            for (int i = 0; i < 120; i++)
                smoother.Tick(in walkIntent, 0.12f, 0.18f, 1f / 60f);

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

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }
    }
}
