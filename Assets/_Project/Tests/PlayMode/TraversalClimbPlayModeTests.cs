using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Pipeline;
using Project.Presentation.Footstep;
using Project.Presentation.IK;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Project.Tests.PlayMode
{
    public sealed class TraversalClimbPlayModeTests : InputTestFixture
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/X Bot.prefab";
        private const float WallFrontZ = 1.6f;
        private const int MaximumFrames = 600;

        private readonly List<Object> _created = new List<Object>();
        private Keyboard _keyboard;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [TearDown]
        public void DestroyCreatedObjects()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator Climb2m_RealPrefab_CompletesWithinAuthoredBudgets(
            [Values(2.05f, 1.80f)] float wallHeight)
        {
            CreateGround();
            CreateWall(wallHeight);
            Transform cameraTransform = CreateCamera();

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(source, $"Missing player prefab at {PlayerPrefabPath}.");

            var inactiveRoot = new GameObject("TraversalClimbPlayMode-InactiveRoot");
            _created.Add(inactiveRoot);
            inactiveRoot.SetActive(false);

            GameObject player = Object.Instantiate(source, inactiveRoot.transform);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            CharacterPipelineRunner runner = player.GetComponent<CharacterPipelineRunner>();
            MotionDriver driver = player.GetComponent<MotionDriver>();
            TraversalHandIKController handIK = player.GetComponent<TraversalHandIKController>();
            FootstepDetector footsteps = player.GetComponent<FootstepDetector>();
            Assert.IsNotNull(runner);
            Assert.IsNotNull(driver);
            Assert.IsNotNull(handIK);
            Assert.IsNotNull(footsteps);

            // A prefab cannot own the scene camera used to project movement input. Keep the real
            // runner/input path and provide that one bare-scene dependency before Awake runs.
            SetPrivateField(runner, "playerCamera", cameraTransform);
            inactiveRoot.SetActive(true);

            Physics.SyncTransforms();
            yield return null;
            yield return null;
            yield return null;

            bool jumpPressed = false;
            bool releaseJump = false;
            bool traversalStarted = false;
            bool segmentedExecutionObserved = false;
            bool fullWeightSampleObserved = false;
            bool firedFootstepWhileAirborne = false;
            int previousFiredFootstepCount = 0;
            float maximumFullWeightPostIKResidual = 0f;
            float maximumFullWeightArmReachRatio = 0f;
            float maximumActualFrameDisplacement = 0f;

            Press(_keyboard.wKey);
            for (int frame = 0; frame < MaximumFrames; frame++)
            {
                yield return null;

                if (releaseJump)
                {
                    Release(_keyboard.spaceKey);
                    releaseJump = false;
                }

                float distanceToWall = WallFrontZ - player.transform.position.z;
                if (!jumpPressed && distanceToWall >= 0.35f && distanceToWall <= 0.55f)
                {
                    footsteps.ClearTrace();
                    Press(_keyboard.spaceKey);
                    releaseJump = true;
                    jumpPressed = true;
                }

                if (driver.HasActiveTraversalPlan)
                {
                    traversalStarted = true;
                    segmentedExecutionObserved |= driver.IsSegmentedExecutionActive;

                    TraversalExecutionResult execution = driver.LastTraversalExecutionResult;
                    if (execution.HasResult)
                    {
                        maximumActualFrameDisplacement = Mathf.Max(
                            maximumActualFrameDisplacement,
                            execution.ActualDisplacement.magnitude);
                    }

                    CaptureFullWeightHandMetrics(
                        handIK,
                        ref fullWeightSampleObserved,
                        ref maximumFullWeightPostIKResidual,
                        ref maximumFullWeightArmReachRatio);
                    // ⭐ 判準是「**這一步底下有沒有真的地面**」，不是任何 normalized time。
                    //
                    // 缺陷的本體是**離地時出聲**：攀爬途中角色沒有地面觀測，腳掃過偵測帶
                    // 產生的都是幻覺（`FootstepDetector` 的 Airborne 抑制就是為此存在）。
                    // 只要腳真的踩在落腳面上，那一步**就該出聲**，不管此刻 normalized time 是多少。
                    //
                    // ⛔ 不要拿 Exit 當界線。實測：修好 root 過衝之後角色在 n≈0.51 就真的
                    //    站上平台，右腳在 n≈0.68 落地（`hasGround=True / phase=Planted /
                    //    水平速度 1.12 m/s`）——那是正確的腳步，卻會被 Exit(0.833) 判成缺陷。
                    //    綁在時間上的判準會隨著 root 軌跡變好而誤報，那就不是判準，是噪音。
                    int firedFootstepCount = CountFiredFootsteps(footsteps);
                    if (firedFootstepCount > previousFiredFootstepCount &&
                        !runner.RuntimeData.IsGrounded)
                        firedFootstepWhileAirborne = true;
                    previousFiredFootstepCount = firedFootstepCount;
                }
                else if (traversalStarted)
                {
                    break;
                }
            }
            Release(_keyboard.wKey);
            if (releaseJump) Release(_keyboard.spaceKey);

            Assert.IsTrue(jumpPressed,
                $"Player never entered the 0.35-0.55 m jump window for wall {wallHeight:F2} m.");
            Assert.IsTrue(traversalStarted,
                $"Real input never started traversal for wall {wallHeight:F2} m.");
            Assert.IsTrue(driver.LastTraversalCompleted,
                $"Traversal did not reach its committed Exit for wall {wallHeight:F2} m.");

            TraversalWarpPlan plan = driver.LastCommittedTraversalPlan;
            Assert.IsTrue(plan.IsPiecewise, "Committed climb plan must use authored landmarks.");
            Assert.IsTrue(segmentedExecutionObserved,
                "The authored Segmented execution model was never active.");
            Assert.IsTrue(fullWeightSampleObserved,
                "The climb never produced a full-weight hand-IK sample.");

            TraversalRootReachConstraint reachConstraint = plan.Bake.Traversal.RootReachConstraint;
            Assert.IsTrue(reachConstraint.IsValid, "Climb2m must carry its authored reach budget.");
            Assert.LessOrEqual(
                maximumFullWeightArmReachRatio,
                reachConstraint.MaximumReachRatio + Mathf.Epsilon,
                "A full-weight hand goal exceeded the authored reach ratio.");

            Assert.Greater(maximumActualFrameDisplacement, 0f,
                "Traversal produced no actual frame displacement to define the latency budget.");
            Assert.LessOrEqual(
                maximumFullWeightPostIKResidual,
                maximumActualFrameDisplacement * 2f,
                "Full-weight post-IK residual exceeded two actual committed frame displacements.");

            float committedDisplacement = Vector3.Distance(plan.StartPosition, plan.TargetPosition);
            Assert.Greater(committedDisplacement, 0f);
            Assert.LessOrEqual(
                driver.LastTraversalMaxWarpBlockedDisplacement,
                committedDisplacement * 0.01f,
                "Root warp was materially fighting collision during the climb.");
            Assert.IsFalse(firedFootstepWhileAirborne,
                "A footstep fired on a frame where the character was not grounded — that sound " +
                "has no real foot contact behind it.");
        }

        private static void CaptureFullWeightHandMetrics(
            TraversalHandIKController handIK,
            ref bool sampleObserved,
            ref float maximumPostIKResidual,
            ref float maximumArmReachRatio)
        {
            TraversalHandIKTargetData targets = handIK.TargetData;
            if (targets == null) return;

            if (Mathf.Approximately(targets.LeftHandPositionWeight, 1f))
            {
                sampleObserved = true;
                maximumPostIKResidual = Mathf.Max(
                    maximumPostIKResidual, handIK.LeftSolvedResidualError);
                maximumArmReachRatio = Mathf.Max(
                    maximumArmReachRatio, handIK.LeftArmReachRatio);
            }
            if (Mathf.Approximately(targets.RightHandPositionWeight, 1f))
            {
                sampleObserved = true;
                maximumPostIKResidual = Mathf.Max(
                    maximumPostIKResidual, handIK.RightSolvedResidualError);
                maximumArmReachRatio = Mathf.Max(
                    maximumArmReachRatio, handIK.RightArmReachRatio);
            }
        }

        private static int CountFiredFootsteps(FootstepDetector footsteps)
        {
            int fired = 0;
            for (int i = 0; i < footsteps.TraceCount; i++)
            {
                if (footsteps.GetTrace(i).Reason == FootstepSuppressReason.Fired) fired++;
            }
            return fired;
        }

        private void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TraversalClimbPlayMode-Ground";
            ground.layer = 0;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            _created.Add(ground);
        }

        private void CreateWall(float height)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = $"TraversalClimbPlayMode-Wall-{height:F2}";
            wall.layer = 0;
            wall.transform.position = new Vector3(0f, height * 0.5f, WallFrontZ + 0.5f);
            wall.transform.localScale = new Vector3(4f, height, 1f);
            _created.Add(wall);
        }

        private Transform CreateCamera()
        {
            var cameraObject = new GameObject("TraversalClimbPlayMode-Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 3f, -5f);
            cameraObject.transform.rotation = Quaternion.identity;
            cameraObject.AddComponent<Camera>();
            _created.Add(cameraObject);
            return cameraObject.transform;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Missing {target.GetType().Name}.{fieldName}.");
            field.SetValue(target, value);
        }
    }
}
