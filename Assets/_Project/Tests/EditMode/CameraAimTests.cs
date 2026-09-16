using NUnit.Framework;
using Project.Presentation.Actions;
using Project.Presentation.CameraControl;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// Camera／AimPoint／Throw 的決策核心純函數測試。Combat target 選擇與 facing 已移至
    /// CombatContextTests，避免 Presentation 再持有 gameplay target／方向權威。
    /// </summary>
    public sealed class CameraAimTests
    {
        private const float Epsilon = 1e-4f;

        [Test]
        public void T1_ComputeThrowRotation_ForwardPointsAtAimPoint()
        {
            Vector3 spawn = new Vector3(1f, 2f, 3f);
            Vector3 aimPoint = spawn + new Vector3(4f, 1f, 5f);

            Quaternion rotation = ThrowProjectileEmitter.ComputeThrowRotation(
                spawn, aimPoint, Quaternion.identity);

            Assert.Less(Vector3.Angle(rotation * Vector3.forward, aimPoint - spawn), Epsilon,
                "生成 rotation 的 forward 必須指向 AimPoint。");
        }

        [Test]
        public void T2_ComputeThrowRotation_DegenerateDistanceReturnsFallbackExactly()
        {
            Vector3 spawn = new Vector3(1f, 2f, 3f);
            Quaternion fallback = Quaternion.Euler(10f, 20f, 30f);

            Quaternion rotation = ThrowProjectileEmitter.ComputeThrowRotation(
                spawn, spawn + Vector3.one * 0.0001f, fallback);

            Assert.AreEqual(fallback, rotation,
                "AimPoint 幾乎等於生成點時必須逐字回 fallback，不得呼叫 LookRotation(zero)。");
        }

        [Test]
        public void T3_ComputeOrbitPosition_ChangesPositionButOffsetDoesNotChangeYawPitchForward()
        {
            Vector3 pivot = new Vector3(2f, 1f, -4f);
            Vector3 centeredOffset = new Vector3(0f, 2f, -3.5f);
            Vector3 behind = ThirdPersonCamera.ComputeOrbitPosition(pivot, 0f, 0f, centeredOffset);
            Vector3 yawed = ThirdPersonCamera.ComputeOrbitPosition(pivot, 90f, 0f, centeredOffset);

            Assert.AreEqual(pivot.z - 3.5f, behind.z, Epsilon, "yaw=0 時相機必須在 pivot 正後方。");
            Assert.AreEqual(pivot.x - 3.5f, yawed.x, Epsilon,
                "yaw=90 時，相機位於 forward 反向側，鏡頭 forward 指向世界右方。");

            const float yaw = 37f;
            const float pitch = 18f;
            Vector3 shoulderOffset = new Vector3(0.8f, centeredOffset.y, centeredOffset.z);
            Vector3 centeredPosition = ThirdPersonCamera.ComputeOrbitPosition(
                pivot, yaw, pitch, centeredOffset);
            Vector3 shoulderPosition = ThirdPersonCamera.ComputeOrbitPosition(
                pivot, yaw, pitch, shoulderOffset);

            Assert.AreNotEqual(centeredPosition, shoulderPosition,
                "不同 offset.x 必須改變相機位置，否則無法形成過肩構圖。");

            Vector3 centeredForward = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            Vector3 shoulderForward = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            centeredForward.y = 0f;
            shoulderForward.y = 0f;
            Assert.Less(Vector3.Angle(centeredForward, shoulderForward), Epsilon,
                "相同 yaw／pitch 下，offset.x 不得影響壓平後的 camera forward。");
        }

        [Test]
        public void T4_ClampPitch_ClampsOutsideAndPreservesBoundaries()
        {
            Assert.AreEqual(-20f, ThirdPersonCamera.ClampPitch(-30f, -20f, 60f));
            Assert.AreEqual(60f, ThirdPersonCamera.ClampPitch(70f, -20f, 60f));
            Assert.AreEqual(-20f, ThirdPersonCamera.ClampPitch(-20f, -20f, 60f));
            Assert.AreEqual(60f, ThirdPersonCamera.ClampPitch(60f, -20f, 60f));
        }

        [Test]
        public void T7_ResolveCameraDistance_UsesDesiredHitMinusSkinAndMinimum()
        {
            Assert.AreEqual(3.5f,
                ThirdPersonCamera.ResolveCameraDistance(3.5f, false, 0f, 0.1f, 0.6f), Epsilon);
            Assert.AreEqual(1.9f,
                ThirdPersonCamera.ResolveCameraDistance(3.5f, true, 2f, 0.1f, 0.6f), Epsilon);
            Assert.AreEqual(0.6f,
                ThirdPersonCamera.ResolveCameraDistance(3.5f, true, 0.2f, 0.1f, 0.6f), Epsilon,
                "近距離命中不得回負值或 0，必須夾在 minDistance。");
        }

        [Test]
        public void T8_AdvanceOccludedDistance_PullsInImmediatelyAndReturnsGradually()
        {
            Assert.AreEqual(1.2f,
                ThirdPersonCamera.AdvanceOccludedDistance(3f, 1.2f, 6f, 0.1f), Epsilon,
                "遮蔽拉近必須立即採 target，避免先穿牆再退出。");

            float first = ThirdPersonCamera.AdvanceOccludedDistance(1.2f, 3f, 6f, 0.1f);
            float second = ThirdPersonCamera.AdvanceOccludedDistance(first, 3f, 6f, 0.1f);
            Assert.Greater(first, 1.2f);
            Assert.Less(first, 3f);
            Assert.Greater(second, first);
            Assert.LessOrEqual(second, 3f, "推遠必須單調逼近且不得 overshoot。");
        }

        // T-9 —— 開場俯角的角度正規化（2026-08-31 補）
        //
        // 背景：`Transform.eulerAngles` 回傳 [0, 360)。舊寫法把它直接當 pitch 再 Clamp，
        //       「相機朝上擺」(-10°) 會被讀成 350，夾完變成 maxPitch ⇒ 開場鏡頭甩到最大俯角。
        // 這條把「先正規化再夾限」釘死；沒有它，回歸時只會表現成偶發的開場鏡頭跳動，極難歸因。
        [Test]
        public void T9_NormalizeAngle_MapsEulerRangeToSignedRange()
        {
            Assert.AreEqual(-10f, ThirdPersonCamera.NormalizeAngle(350f), Epsilon,
                "350° 必須還原為 -10°，否則 Clamp(minPitch, maxPitch) 會把朝上擺的相機壓成 maxPitch。");
            Assert.AreEqual(25.2f, ThirdPersonCamera.NormalizeAngle(25.2f), Epsilon,
                "已在 [-180, 180] 內的值必須逐字不變。");
            Assert.AreEqual(0f, ThirdPersonCamera.NormalizeAngle(360f), Epsilon);
            Assert.AreEqual(-90f, ThirdPersonCamera.NormalizeAngle(270f), Epsilon);
            Assert.AreEqual(-10f, ThirdPersonCamera.NormalizeAngle(-370f), Epsilon,
                "超出一圈的負值同樣要收斂回 [-180, 180]。");

            // 迴歸重點：正規化後再夾限，結果必須落在合法俯角內且**不是** maxPitch。
            float pitch = Mathf.Clamp(ThirdPersonCamera.NormalizeAngle(350f), -20f, 60f);
            Assert.AreEqual(-10f, pitch, Epsilon, "正規化後夾限不得把 -10° 變成 60°。");
        }

        [Test]
        public void T12_ComputeFacingTarget_FlattensVerticalDirection()
        {
            Vector3 targetDirection = new Vector3(1f, 5f, 1f);

            Quaternion target = MotionDriver.ComputeFacingTarget(
                Quaternion.identity, targetDirection, out bool hasTarget);
            Vector3 targetForward = target * Vector3.forward;

            Assert.IsTrue(hasTarget);
            Assert.AreEqual(0f, targetForward.y, Epsilon,
                "AimPoint 高低差只能影響瞄準，不得把角色 root 轉出水平面產生 pitch。");
            Assert.Less(Vector3.Angle(targetForward, new Vector3(1f, 0f, 1f)), Epsilon);
        }

        /// <summary>
        /// 🆕（2026-09-15）**貼身抑制的遲滯。**
        ///
        /// 2026-09-15 錄影（63s／71s／90s）：貼身纏鬥時相機被壓到 <c>minDistance</c>，
        /// 落在角色網格內部 ⇒ 畫面被角色背部填滿。防穿牆本身沒壞（它正確地拉近了），
        /// 壞的是「拉到極限之後沒有人負責讓角色讓開」。
        ///
        /// ⚠️ 遲滯是這個功能的**必要**部分，不是優化：門檻是一個距離、而相機距離每帧都在阻尼變化，
        /// 沒有遲滯就會在門檻附近逐帧顯示／隱藏 ⇒ 角色閃爍，比穿模更糟。
        /// </summary>
        [Test]
        public void T13_TargetProximityHide_UsesHysteresisSoItCannotFlicker()
        {
            const float threshold = 0.95f;
            const float hysteresis = 0.15f;

            Assert.IsFalse(ThirdPersonCamera.ResolveTargetHidden(false, 1.2f, threshold, hysteresis),
                "距離充足時不得隱藏角色");
            Assert.IsTrue(ThirdPersonCamera.ResolveTargetHidden(false, 0.9f, threshold, hysteresis),
                "跨過門檻必須隱藏");

            // 關鍵：已隱藏時要退出必須走比較寬的門檻，門檻正上方的距離不得立刻恢復顯示。
            Assert.IsTrue(ThirdPersonCamera.ResolveTargetHidden(true, 1.0f, threshold, hysteresis),
                "已隱藏時，僅略高於門檻不得恢復顯示——那正是閃爍的來源");
            Assert.IsFalse(ThirdPersonCamera.ResolveTargetHidden(true, 1.2f, threshold, hysteresis),
                "距離明確拉開（超過門檻＋遲滯）之後必須恢復顯示");
        }

        [Test]
        public void T14_TargetProximityHide_IsDisabledByZeroThreshold()
        {
            Assert.IsFalse(ThirdPersonCamera.ResolveTargetHidden(true, 0.01f, 0f, 0.15f),
                "門檻 0 ＝ 停用本功能，必須連『已隱藏』的狀態都能退出，否則關掉功能後角色會永遠消失");
        }
    }
}
