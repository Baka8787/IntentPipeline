using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>記錄 ADR-011 對齊請求的測試 Facade。</summary>
    public sealed class EntryPhaseRecordingFacade : AnimationFacadeBase
    {
        public string AlignedKey; public float AlignedPhase = float.NaN; public int AlignCalls;
        public override void Play(string stateKey) { }
        public override void PlayWithCallback(string stateKey, System.Action onComplete) { }
        public override void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f) { }
        public override void SetFloat(string key, float value) { }
        public override void SetBool(string key, bool value) { }
        public override bool IsPlaying(string stateKey) => false;
        public override float GetNormalizedTime() => 0f;
        public override bool TryAlignEntryPhase(string stateKey, float normalizedTime)
        {
            AlignedKey = stateKey; AlignedPhase = normalizedTime; AlignCalls++;
            return true;
        }
    }

    /// <summary>
    /// 🧪 ADR-011（Trial）起跑入場相位對齊：Bake 腳相極值、觸發條件（純函數）、LocomotionModel 接線。
    /// 實際效果（起跑貼地滑動）屬 PlayMode 探針（PlantSettleProbe 的平地／樓梯起跑情境）。
    /// </summary>
    public class RunEntryPhaseTests
    {
        private MotionBakeData _bake;

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_bake != null) Object.DestroyImmediate(_bake);
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static void Set<T>(object target, string field, T value)
        {
            FieldInfo fi = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, field);
            fi.SetValue(target, value);
        }

        private EntryPhaseRecordingFacade TickRestart(bool enabled, GaitTier gait)
        {
            _go = new GameObject("RunEntryPhaseUnderTest");
            LocomotionModel model = _go.AddComponent<LocomotionModel>();
            var facade = _go.AddComponent<EntryPhaseRecordingFacade>();
            Set(model, "runLoopBakeData", Bake(1f, new Keyframe(0f, 0f), new Keyframe(0.4f, 0.4f), new Keyframe(0.9f, -0.4f), new Keyframe(1f, 0f)));
            Set(model, "alignRunEntryPhase", enabled);
            Set(model, "_wasIntending", false);
            var data = new PlayerRuntimeData { IsGrounded = true, Gait = gait };
            data.MovementIntent = new MovementIntentData { DesiredSpeedNormalized = 0.75f, DesiredDirection = Vector3.forward };
            model.Tick(data, facade, 1f / 60f);
            return facade;
        }

        // === LocomotionModel 接線 ===

        [Test]
        public void RunRestart_RequestsEntryPhaseFromBakeLandmark()
        {
            var facade = TickRestart(true, GaitTier.Run);
            Assert.AreEqual(1, facade.AlignCalls);
            Assert.AreEqual("Idle", facade.AlignedKey, "locomotion 狀態鍵沿用 locomotionAnimationKey。");
            Assert.AreEqual(0.9f, facade.AlignedPhase, 0.01f, "相位來自 run loop 的左腳觸地極值，不是手填常數。");
        }

        [Test]
        public void Disabled_NeverRequests()
            => Assert.AreEqual(0, TickRestart(false, GaitTier.Run).AlignCalls, "預設關 ＝ 現行。");

        [Test]
        public void WalkRestart_NeverRequests()
            => Assert.AreEqual(0, TickRestart(true, GaitTier.Walk).AlignCalls);

        private MotionBakeData Bake(float duration, params Keyframe[] keys)
        {
            _bake = ScriptableObject.CreateInstance<MotionBakeData>();
            _bake.BakedDuration = duration;
            _bake.FootPhaseCurve = new AnimationCurve(keys);
            return _bake;
        }

        // === Bake 腳相極值 ===

        [Test]
        public void LeftFootDownExtreme_IsCurveMinimum()
        {
            // 曲線在 0.9 s（共 1 s）達到最小值 −0.4（左腳觸地最深）。
            var b = Bake(1f, new Keyframe(0f, 0f), new Keyframe(0.4f, 0.4f), new Keyframe(0.9f, -0.4f), new Keyframe(1f, 0f));
            Assert.IsTrue(b.TryGetFootPhaseExtremeNormalizedTime(FootPhase.LeftFootDown, out float nt));
            Assert.AreEqual(0.9f, nt, 0.01f);
        }

        [Test]
        public void RightFootDownExtreme_IsCurveMaximum()
        {
            var b = Bake(1f, new Keyframe(0f, 0f), new Keyframe(0.4f, 0.4f), new Keyframe(0.9f, -0.4f), new Keyframe(1f, 0f));
            Assert.IsTrue(b.TryGetFootPhaseExtremeNormalizedTime(FootPhase.RightFootDown, out float nt));
            Assert.AreEqual(0.4f, nt, 0.01f);
        }

        [Test]
        public void Extreme_IsNormalizedByDuration()
        {
            // 時間軸是 clip 秒數：0.767 s 的 loop、極值在 0.69 s ⇒ normalized 0.9。
            var b = Bake(0.767f, new Keyframe(0f, 0f), new Keyframe(0.69f, -0.44f), new Keyframe(0.767f, 0f));
            Assert.IsTrue(b.TryGetFootPhaseExtremeNormalizedTime(FootPhase.LeftFootDown, out float nt));
            Assert.AreEqual(0.69f / 0.767f, nt, 0.01f);
        }

        [Test]
        public void MissingCurve_ReturnsFalse()
        {
            _bake = ScriptableObject.CreateInstance<MotionBakeData>();
            _bake.BakedDuration = 1f;
            Assert.IsFalse(_bake.TryGetFootPhaseExtremeNormalizedTime(FootPhase.LeftFootDown, out _));
        }

        // === 觸發條件 ===

        private static bool Should(bool was, bool now, float prevSpeed, bool run, Vector3 dir, Vector3 facing)
            => LocomotionModel.ShouldAlignRunEntryPhase(was, now, prevSpeed, LocomotionModel.MoveThreshold, run, dir, facing, 30f);

        [Test]
        public void RunRestartFromRest_SameDirection_Aligns()
            => Assert.IsTrue(Should(false, true, 0f, true, Vector3.forward, Vector3.forward));

        [Test]
        public void AlreadyIntending_DoesNotAlign()
            => Assert.IsFalse(Should(true, true, 0f, true, Vector3.forward, Vector3.forward), "只在意圖的上升緣。");

        [Test]
        public void StillMoving_DoesNotAlign()
            => Assert.IsFalse(Should(false, true, 0.5f, true, Vector3.forward, Vector3.forward), "移動中重新按 ≠ 靜止起跑。");

        [Test]
        public void WalkGait_DoesNotAlign()
            => Assert.IsFalse(Should(false, true, 0f, false, Vector3.forward, Vector3.forward), "Trial 範圍只有 Run。");

        [Test]
        public void DirectionChange_DoesNotAlign()
            => Assert.IsFalse(Should(false, true, 0f, true, Vector3.right, Vector3.forward), "轉向起跑不在範圍內。");

        [Test]
        public void SmallHeadingDifference_Aligns()
            => Assert.IsTrue(Should(false, true, 0f, true, Quaternion.Euler(0f, 20f, 0f) * Vector3.forward, Vector3.forward));
    }
}
