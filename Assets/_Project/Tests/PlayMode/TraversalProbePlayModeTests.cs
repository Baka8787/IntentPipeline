using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Environment;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    public class TraversalProbePlayModeTests
    {
        private readonly List<Object> _created = new List<Object>();

        private static readonly TraversalProbeSettings ProbeSettings = new TraversalProbeSettings(
            obstacleMask: 1 << 0,
            scanStep: 0.1f,
            scanCeiling: 2.7f,
            forwardScanDistance: 1.5f,
            vault1mMaxDepth: 0.6f,
            climb1mMaxHeight: 1.25f,
            climb2mMaxHeight: 2.1f,
            heightHysteresis: 0.1f,
            depthHysteresis: 0.08f,
            probeMinSpeed: 0.1f,
            destinationClearanceMargin: 0.05f);

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator ThinOneMetreWall_ClassifiesVault1m()
        {
            CreateGround();
            TraversalProbe probe = CreateProbe();
            CreateObstacle(height: 1f, depth: 0.3f);
            Physics.SyncTransforms();
            yield return null;

            probe.Tick(ValidData());

            Assert.AreEqual(TraversalKind.Vault1m, probe.Candidate.Kind);
        }

        [UnityTest]
        public IEnumerator StationaryGroundedFacingWall_StillProducesCandidateFromFacing()
        {
            CreateGround();
            TraversalProbe probe = CreateProbe();
            CreateObstacle(height: 1f, depth: 1f);
            Physics.SyncTransforms();
            yield return null;

            PlayerRuntimeData data = ValidData();
            data.MoveSpeed = 0f;
            data.MoveDirection = Vector3.zero;
            probe.Tick(data);

            Assert.AreEqual(TraversalKind.Climb1m, probe.Candidate.Kind);
            Assert.AreEqual(TraversalDetectionDirectionSource.Facing, probe.Candidate.DirectionSource);
            Assert.That(Vector3.Angle(probe.Candidate.Forward, Vector3.forward), Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator ThickOneMetrePlatform_ClassifiesClimb1m()
        {
            CreateGround();
            TraversalProbe probe = CreateProbe();
            CreateObstacle(height: 1f, depth: 1f);
            Physics.SyncTransforms();
            yield return null;

            probe.Tick(ValidData());

            Assert.AreEqual(TraversalKind.Climb1m, probe.Candidate.Kind);
        }

        [UnityTest]
        public IEnumerator DepthNearBoundary_DoesNotFlipEveryFrame()
        {
            CreateGround();
            TraversalProbe probe = CreateProbe();
            GameObject obstacle = CreateObstacle(height: 1f, depth: 0.58f);
            Physics.SyncTransforms();
            yield return null;

            PlayerRuntimeData data = ValidData();
            probe.Tick(data);
            Assert.AreEqual(TraversalKind.Vault1m, probe.Candidate.Kind);

            SetObstacleDepthKeepingFrontFace(obstacle, 0.62f);
            Physics.SyncTransforms();
            probe.Tick(data);
            Assert.AreEqual(TraversalKind.Vault1m, probe.Candidate.Kind,
                "厚度在閾值另一側但仍位於 hysteresis band 時，上一幀 Vault 不得翻類別");

            SetObstacleDepthKeepingFrontFace(obstacle, 0.58f);
            Physics.SyncTransforms();
            probe.Tick(data);
            Assert.AreEqual(TraversalKind.Vault1m, probe.Candidate.Kind);
        }

        [UnityTest]
        public IEnumerator TwoMetreObstacle_ClassifiesClimb2m()
        {
            CreateGround();
            TraversalProbe probe = CreateProbe();
            CreateObstacle(height: 2f, depth: 1f);
            Physics.SyncTransforms();
            yield return null;

            probe.Tick(ValidData());

            Assert.AreEqual(TraversalKind.Climb2m, probe.Candidate.Kind);
        }

        [UnityTest]
        public IEnumerator TwoPointFiveMetreObstacle_IsTooHigh()
        {
            CreateGround();
            TraversalProbe probe = CreateProbe();
            CreateObstacle(height: 2.5f, depth: 1f);
            Physics.SyncTransforms();
            yield return null;

            probe.Tick(ValidData());

            Assert.AreEqual(TraversalKind.None, probe.Candidate.Kind);
            Assert.AreEqual(TraversalRejectReason.TooHigh, probe.Candidate.RejectReason);
        }

        [UnityTest]
        public IEnumerator CeilingOverDestination_IsBlocked()
        {
            CreateGround();
            TraversalProbe probe = CreateProbe();
            CreateObstacle(height: 1f, depth: 1f);
            GameObject ceiling = CreateCube("Ceiling");
            ceiling.transform.position = new Vector3(0f, 1.5f, 1.4f);
            ceiling.transform.localScale = new Vector3(2f, 0.2f, 0.7f);
            Physics.SyncTransforms();
            yield return null;

            probe.Tick(ValidData());

            Assert.AreEqual(TraversalKind.None, probe.Candidate.Kind);
            Assert.AreEqual(TraversalRejectReason.DestinationBlocked, probe.Candidate.RejectReason);
        }

        [UnityTest]
        public IEnumerator DestinationClearButEntryToTransferCorridorBlocked_IsRejected()
        {
            CreateGround();
            TraversalProbe probe = CreateProbe();
            CreateObstacle(height: 1f, depth: 1f);

            GameObject beam = CreateCube("Entry Corridor Beam");
            beam.transform.position = new Vector3(0f, 1.75f, 0.25f);
            beam.transform.localScale = new Vector3(2f, 0.2f, 0.3f);
            Physics.SyncTransforms();
            yield return null;

            probe.Tick(ValidData());

            Assert.AreEqual(TraversalKind.None, probe.Candidate.Kind);
            Assert.AreEqual(TraversalRejectReason.CorridorBlocked, probe.Candidate.RejectReason);
            Assert.AreNotEqual(TraversalCorridorRejectReason.None, probe.Candidate.Corridor.RejectReason);
        }

        [UnityTest]
        public IEnumerator FortyFiveDegreeTop_RespectsControllerSlopeLimit()
        {
            CreateGround();
            TraversalProbe probe = CreateProbe(slopeLimit: 35f);
            GameObject slope = CreateCube("Slope");
            slope.transform.position = new Vector3(0f, 0.55f, 1.25f);
            slope.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            slope.transform.localScale = new Vector3(2f, 0.25f, 1.4f);
            Physics.SyncTransforms();
            yield return null;

            probe.Tick(ValidData());

            Assert.AreNotEqual(TraversalKind.Climb1m, probe.Candidate.Kind);
            Assert.AreNotEqual(TraversalKind.Climb2m, probe.Candidate.Kind);
            Assert.AreEqual(TraversalRejectReason.TopTooSteep, probe.Candidate.RejectReason);
        }

        [UnityTest]
        public IEnumerator ProbePresence_DoesNotChangeJumpApexAirTimeOrLandingEdge()
        {
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                CreateGround();
                Physics.SyncTransforms();

                JumpResult withoutProbe = default;
                yield return RunJump(includeProbe: false, result => withoutProbe = result);
                JumpResult withProbe = default;
                yield return RunJump(includeProbe: true, result => withProbe = result);

                Assert.That(withProbe.ApexHeight, Is.EqualTo(withoutProbe.ApexHeight).Within(0.03f));
                Assert.That(withProbe.AirTime, Is.EqualTo(withoutProbe.AirTime).Within(0.05f));
                Assert.That(withProbe.LandingFrame, Is.EqualTo(withoutProbe.LandingFrame).Within(1),
                    "掛上只讀 probe 不得改變 JustLanded 的發生時點");
            }
            finally
            {
                Time.captureDeltaTime = previousCaptureDeltaTime;
            }
        }

        private IEnumerator RunJump(bool includeProbe, System.Action<JumpResult> completed)
        {
            GameObject host = new GameObject(includeProbe ? "Jump With Probe" : "Jump Without Probe");
            _created.Add(host);
            host.layer = 2;
            host.transform.position = new Vector3(4f, 0.02f, 0f);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 1f, 0f);
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.stepOffset = 0.3f;
            controller.minMoveDistance = 0f;
            MotionDriver driver = host.AddComponent<MotionDriver>();
            TraversalProbe probe = includeProbe ? host.AddComponent<TraversalProbe>() : null;
            if (probe != null) ConfigureProbe(probe);

            var data = new PlayerRuntimeData
            {
                MoveDirection = Vector3.forward,
                MoveSpeed = 1f,
            };

            // batchmode 的首幀 deltaTime 可能極短，先用一次 collision-only 位移讓
            // CharacterController 建立穩定接觸；這只是治具前置，不屬於被比較的跳躍區間。
            controller.Move(Vector3.down * 0.1f);
            for (int i = 0; i < 60 && !data.IsGrounded; i++)
            {
                data.ResetTransientState();
                driver.ExecuteVerticalOnlyMovement(data);
                yield return null;
            }
            Assert.IsTrue(data.IsGrounded, "跳躍對照開始前角色必須先穩定著地");

            float startY = host.transform.position.y;
            float apexY = startY;
            float elapsed = 0f;
            int landingFrame = -1;
            driver.ApplyJumpLaunch(new JumpLaunchData(5f, 9.81f));

            for (int frame = 0; frame < 240; frame++)
            {
                // MotionDriver 的 production owner 在 Update；固定 capture delta 可讓有／無 probe
                // 都得到相同 render-step，同時確保 Time.frameCount 每次積分都前進。
                yield return null;
                data.ResetTransientState();
                probe?.Tick(data);
                driver.ExecuteVerticalOnlyMovement(data);
                elapsed += Time.deltaTime;
                apexY = Mathf.Max(apexY, host.transform.position.y);

                if (frame > 0 && data.JustLanded)
                {
                    landingFrame = frame;
                    break;
                }
            }

            Assert.GreaterOrEqual(landingFrame, 0, "跳躍應在測試上限內重新著地");
            completed(new JumpResult(apexY - startY, elapsed, landingFrame));
            Object.DestroyImmediate(host);
            _created.Remove(host);
        }

        private TraversalProbe CreateProbe(float slopeLimit = 45f)
        {
            GameObject host = new GameObject("Traversal Probe Host");
            _created.Add(host);
            host.layer = 2;
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 1f, 0f);
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.stepOffset = 0.3f;
            controller.slopeLimit = slopeLimit;
            TraversalProbe probe = host.AddComponent<TraversalProbe>();
            ConfigureProbe(probe);
            return probe;
        }

        private static void ConfigureProbe(TraversalProbe probe)
        {
            SetPrivateField(probe, "settings", ProbeSettings);
            SetPrivateField(probe, "drawTraversalRuntimeLines", false);
        }

        private void CreateGround()
        {
            GameObject ground = CreateCube("Ground");
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
        }

        private GameObject CreateObstacle(float height, float depth)
        {
            GameObject obstacle = CreateCube("Obstacle");
            obstacle.transform.position = new Vector3(0f, height * 0.5f, 0.75f + depth * 0.5f);
            obstacle.transform.localScale = new Vector3(2f, height, depth);
            return obstacle;
        }

        private GameObject CreateCube(string name)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.layer = 0;
            _created.Add(cube);
            return cube;
        }

        private static void SetObstacleDepthKeepingFrontFace(GameObject obstacle, float depth)
        {
            Vector3 scale = obstacle.transform.localScale;
            scale.z = depth;
            obstacle.transform.localScale = scale;
            Vector3 position = obstacle.transform.position;
            position.z = 0.75f + depth * 0.5f;
            obstacle.transform.position = position;
        }

        private static PlayerRuntimeData ValidData()
        {
            return new PlayerRuntimeData
            {
                IsGrounded = true,
                MoveSpeed = 1f,
                MoveDirection = Vector3.forward,
            };
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到測試設定欄位 {fieldName}");
            field.SetValue(target, value);
        }

        private readonly struct JumpResult
        {
            public readonly float ApexHeight;
            public readonly float AirTime;
            public readonly int LandingFrame;

            public JumpResult(float apexHeight, float airTime, int landingFrame)
            {
                ApexHeight = apexHeight;
                AirTime = airTime;
                LandingFrame = landingFrame;
            }
        }
    }
}
