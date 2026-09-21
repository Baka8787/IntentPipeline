using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    public class MotionDriverCommittedMovementPlayModeTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator VerticalCurveDeltaMovesCharacterWithoutGravityAndPublishesState()
        {
            GameObject host = new GameObject("Committed Curve Host");
            _created.Add(host);
            host.transform.position = new Vector3(0f, 10f, 0f);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            MotionDriver driver = host.AddComponent<MotionDriver>();
            MotionBakeData bake = ScriptableObject.CreateInstance<MotionBakeData>();
            _created.Add(bake);
            bake.BakedDuration = 1f;
            bake.SpeedCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0f));
            bake.VerticalCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
            var data = new PlayerRuntimeData();
            yield return null;

            float beforeY = host.transform.position.y;
            driver.ExecuteCommittedCurveMovement(bake, 0.5f, 0f, data);

            Assert.AreEqual(0.5f, host.transform.position.y - beforeY, 0.001f,
                "垂直位移必須直接等於 VerticalCurve 的播放頭差值，不得疊加重力");
            Assert.AreEqual(0.5f / Time.deltaTime, data.VerticalVelocity, 0.01f);
            Assert.AreEqual(controller.isGrounded, data.IsGrounded);
        }

        [UnityTest]
        public IEnumerator MissingVerticalCurve_DegradesToZeroVerticalDisplacement()
        {
            GameObject host = new GameObject("Legacy Bake Host");
            _created.Add(host);
            host.transform.position = new Vector3(0f, 10f, 0f);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            MotionDriver driver = host.AddComponent<MotionDriver>();
            MotionBakeData bake = ScriptableObject.CreateInstance<MotionBakeData>();
            _created.Add(bake);
            bake.BakedDuration = 1f;
            var data = new PlayerRuntimeData();
            yield return null;

            float beforeY = host.transform.position.y;
            driver.ExecuteCommittedCurveMovement(bake, 0.5f, 0f, data);

            Assert.AreEqual(beforeY, host.transform.position.y, 0.0001f,
                "舊資產 VerticalCurve=null 時必須退化成 0，而不是積分重力或丟例外");
            Assert.AreEqual(0f, data.VerticalVelocity, 0.0001f);
        }

        [UnityTest]
        public IEnumerator CP1_CP2_CP4_CenterProfileAppliesAndRestoresIdempotently()
        {
            GameObject host = new GameObject("Traversal Collision Profile Host");
            _created.Add(host);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 1f, 0f);
            controller.height = 2f;
            controller.radius = 0.5f;
            MotionDriver driver = host.AddComponent<MotionDriver>();
            yield return null;

            Vector3 originalCenter = controller.center;
            float originalHeight = controller.height;
            float originalRadius = controller.radius;
            var profile = new TraversalCollisionProfile(
                true,
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0.1f)),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0.2f)),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0.3f)));

            driver.BeginTraversalCollisionProfile(in profile);
            driver.ApplyTraversalCollisionProfile(0.5f);
            Assert.IsTrue(driver.IsTraversalCollisionProfileActive);
            Assert.That(Vector3.Distance(
                    originalCenter + new Vector3(0.05f, 0.1f, 0.15f), controller.center),
                Is.LessThan(0.0001f));
            Assert.AreEqual(originalHeight, controller.height);
            Assert.AreEqual(originalRadius, controller.radius);

            driver.RestoreTraversalCollisionProfile();
            driver.RestoreTraversalCollisionProfile();
            Assert.IsFalse(driver.IsTraversalCollisionProfileActive);
            Assert.That(Vector3.Distance(originalCenter, controller.center), Is.LessThan(0.0001f));
            Assert.AreEqual(originalHeight, controller.height);
            Assert.AreEqual(originalRadius, controller.radius);
        }

        /// <summary>
        /// 🔒 **committed traversal 期間，膠囊擦碰不得被回報成落地。**
        ///
        /// 真實 bug（2026-09-21 使用者回報「Climb2m 爬上去一瞬間很多聲」，探針定案）：
        /// 爬牆時膠囊擦到牆面／邊緣會讓 `CharacterController.isGrounded` 瞬間變 true，
        /// 而 `JustLanded` 是**無去抖動的純邊沿偵測** ⇒ 每擦一次就是一聲落地。
        /// 實測一次 Climb2m 出現 2 次假落地（n=0.059 抓握瞬間、n=0.535 爬到一半）。
        ///
        /// ⚠️ 本測試**必須保留陽性對照**：沒有對照就分不出「成功擋掉」與「本來就不會觸發」。
        /// </summary>
        [UnityTest]
        public IEnumerator C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(floor);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);

            GameObject host = new GameObject("Traversal Ground Edge Host");
            _created.Add(host);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 1f, 0f);
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.minMoveDistance = 0f;
            MotionDriver driver = host.AddComponent<MotionDriver>();
            yield return null;
            Physics.SyncTransforms();

            // 真的踩到地上，讓 isGrounded 由物理回答而不是由測試假造。
            controller.Move(Vector3.down * 0.2f);
            yield return null;
            Assert.IsTrue(controller.isGrounded, "前提不成立：角色必須真的站在地上");

            var syncMethod = typeof(MotionDriver).GetMethod(
                "SyncGroundedState",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(syncMethod);
            System.Reflection.FieldInfo wasGrounded = typeof(MotionDriver).GetField(
                "_wasGrounded",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(wasGrounded);
            System.Reflection.MethodInfo setActivePlan = typeof(MotionDriver)
                .GetProperty("HasActiveTraversalPlan").GetSetMethod(true);
            Assert.IsNotNull(setActivePlan);

            // ── 陽性對照：不在 traversal 中，同樣的邊沿必須照常回報 ──
            var control = new PlayerRuntimeData();
            wasGrounded.SetValue(driver, false);
            setActivePlan.Invoke(driver, new object[] { false });
            syncMethod.Invoke(driver, new object[] { control });
            Assert.IsTrue(control.JustLanded,
                "陽性對照失敗：非 traversal 的 false→true 邊沿本來就該回報落地");

            // ── 受測路徑：committed traversal 期間同一個邊沿不得回報 ──
            var traversal = new PlayerRuntimeData();
            wasGrounded.SetValue(driver, false);
            setActivePlan.Invoke(driver, new object[] { true });
            syncMethod.Invoke(driver, new object[] { traversal });
            Assert.IsFalse(traversal.JustLanded,
                "traversal 期間擦碰牆面不是落地，不得回報 JustLanded");
            Assert.IsTrue(traversal.IsGrounded,
                "⚠️ 只擋事件語意，不得連 IsGrounded 一起擋——" +
                "那是 Foot IK／腳步偵測的閘門，也是 exit 貼地力刻意要達成的效果");
        }

        [UnityTest]
        public IEnumerator C0_ReboundGroundingBlockIsNotReportedAsWarpBlock()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(floor);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);

            GameObject host = new GameObject("Traversal Rebound Evidence Host");
            _created.Add(host);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 1f, 0f);
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.minMoveDistance = 0f;
            MotionDriver driver = host.AddComponent<MotionDriver>();
            yield return null;
            Physics.SyncTransforms();

            driver.BeginTraversalPresentation(default, default);
            var move = typeof(MotionDriver).GetMethod(
                "MoveTraversalAndRecord",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(move);
            move.Invoke(driver, new object[]
            {
                Vector3.zero,
                Vector3.down * 0.1f,
                0.9f,
            });

            Assert.That(driver.LastTraversalMaxWarpBlockedDisplacement, Is.LessThan(0.005f),
                "grounding-only Move must not contaminate traversal warp blocking");
            Assert.That(driver.LastTraversalMaxReboundBlockedDisplacement, Is.GreaterThan(0.05f),
                "the blocked downward grounding request must remain observable separately");
        }

        [UnityTest]
        public IEnumerator C0_ExecutionRecordsRequestedActualFlagsAndCapsule()
        {
            GameObject host = new GameObject("Traversal Execution Evidence Host");
            _created.Add(host);
            host.transform.position = new Vector3(0f, 5f, 0f);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            MotionDriver driver = host.AddComponent<MotionDriver>();
            MotionBakeData bake = ScriptableObject.CreateInstance<MotionBakeData>();
            _created.Add(bake);
            bake.BakedDuration = 1f;
            bake.SpeedCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
            bake.VerticalCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0f));
            var data = new PlayerRuntimeData();
            yield return null;

            driver.ExecuteCommittedCurveMovement(bake, 0.25f, 0f, data);
            TraversalExecutionResult result = driver.LastTraversalExecutionResult;
            Assert.IsTrue(result.HasResult);
            Assert.AreEqual(0.25f, result.RequestedDisplacement.z, 0.001f);
            Assert.That(Vector3.Distance(result.RequestedDisplacement, result.ActualDisplacement),
                Is.LessThan(0.0001f));
            Assert.That(result.BlockedDisplacement.magnitude, Is.LessThan(0.0001f));
            Assert.That(driver.LastTraversalMaxWarpBlockedDisplacement, Is.LessThan(0.0001f));
            Assert.That(driver.LastTraversalMaxReboundBlockedDisplacement, Is.LessThan(0.0001f));
            Assert.AreEqual(controller.center, result.ControllerCenter);
            Assert.AreEqual(controller.height, result.ControllerHeight);
            Assert.AreEqual(controller.radius, result.ControllerRadius);
        }
    }
}
