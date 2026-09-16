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
            Assert.AreEqual(controller.center, result.ControllerCenter);
            Assert.AreEqual(controller.height, result.ControllerHeight);
            Assert.AreEqual(controller.radius, result.ControllerRadius);
        }
    }
}
