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
            bool firedFootstepBeforeExit = false;
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
                    // ⭐ 判準的邊界是 **committed Exit**，不是「plan 還活著」。
                    //
                    // Exit 之前角色是離地的：`isGrounded == false`、沒有地面觀測，任何腳步聲
                    // 都是攀爬動畫的腳掃過偵測帶造成的幻覺（`FootstepDetector` 的 Airborne 抑制
                    // 就是為此而存在）。
                    //
                    // Exit **之後** plan 仍然活到 Recovery（Climb2m：0.833 → 0.870），
                    // 而此時角色已經被貼地力壓在落腳面上、Foot IK 已接手、人也開始走。
                    // 那一步是**真的踩在平台上**（實測 `hasGround=True / phase=Planted /
                    // 水平速度 1.70 m/s`）⇒ 它該出聲。
                    // ⛔ 不要把它算進來——那會把「正確的落地腳步」當成缺陷，
                    //    正是 `docs/39` F1 那一類「累加器沒有被窗口限制」的錯誤。
                    if (driver.ActiveTraversalNormalizedTime <
                        driver.ActiveTraversalPlan.ExitKnot.NormalizedTime)
                        firedFootstepBeforeExit |= HasFiredFootstep(footsteps);
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
            Assert.IsFalse(firedFootstepBeforeExit,
                "A footstep fired before the committed Exit — the character is airborne there, " +
                "so that sound has no real foot contact behind it.");
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

        private static bool HasFiredFootstep(FootstepDetector footsteps)
        {
            for (int i = 0; i < footsteps.TraceCount; i++)
            {
                if (footsteps.GetTrace(i).Reason == FootstepSuppressReason.Fired)
                    return true;
            }
            return false;
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
