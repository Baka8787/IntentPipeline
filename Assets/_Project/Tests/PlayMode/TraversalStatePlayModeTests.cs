using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Environment;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    public class TraversalStatePlayModeTests
    {
        private sealed class FakeMovementModel : IMovementModel
        {
            public bool IsProducingMotion { get; set; }
            public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime) { }
            public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data) { }
        }

        private sealed class FakeAnimationFacade : AnimationFacadeBase
        {
            public bool Playing;
            public float NormalizedTime;
            public int NormalizedTimeReadCount;

            public override void Play(string stateKey) { }
            public override void PlayWithCallback(string stateKey, Action onComplete) { }
            public override void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f) { }
            public override void SetFloat(string key, float value) { }
            public override void SetBool(string key, bool value) { }
            public override bool IsPlaying(string stateKey) => Playing;
            public override float GetNormalizedTime()
            {
                NormalizedTimeReadCount++;
                return NormalizedTime;
            }
        }

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [Test]
        public void T1_NoneCandidate_FallsBackToJump()
        {
            TestRig rig = BuildRig();
            SetCandidate(rig.Probe, default);
            RequestGroundedJump(rig.Data);

            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreEqual(StateType.Jump, rig.Machine.CurrentState.Type);
        }

        [Test]
        public void T2_Vault1m_EntersSharedTraversalState()
        {
            AssertTraversalEntry(TraversalKind.Vault1m, "Traversal.Vault1m");
        }

        [Test]
        public void T3_Climb1m_EntersSharedTraversalState()
        {
            AssertTraversalEntry(TraversalKind.Climb1m, "Traversal.Climb1m");
        }

        [Test]
        public void T4_Climb2m_EntersSharedTraversalState()
        {
            AssertTraversalEntry(TraversalKind.Climb2m, "Traversal.Climb2m");
        }

        [Test]
        public void T5_CommittedCandidateAndBinding_IgnoreLaterProbeChanges()
        {
            TestRig rig = BuildRig();
            SetCandidate(rig.Probe, Candidate(TraversalKind.Vault1m));
            RequestGroundedJump(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);

            var traversal = rig.Machine.CurrentState as TraversalState;
            Assert.IsNotNull(traversal);
            MotionBakeData committedBake = traversal.CurrentBake;
            string committedKey = traversal.AnimationKey;

            SetCandidate(rig.Probe, default);
            rig.Data.Intent.JumpRequested = false;
            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreSame(traversal, rig.Machine.CurrentState);
            Assert.AreEqual(TraversalKind.Vault1m, traversal.CommittedKind);
            Assert.AreSame(committedBake, traversal.CurrentBake);
            Assert.AreEqual(committedKey, traversal.AnimationKey);
        }

        [Test]
        public void T6_AirborneCharacter_CannotEnterTraversal()
        {
            TestRig rig = BuildRig();
            SetCandidate(rig.Probe, Candidate(TraversalKind.Climb2m));
            rig.Data.IsGrounded = false;
            rig.Data.Intent.JumpRequested = true;

            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreNotEqual(StateType.Traversal, rig.Machine.CurrentState.Type);
        }

        [UnityTest]
        public IEnumerator T7_TraversalConsumesCommittedVerticalCurveWithoutGravityAndSyncsGroundedState()
        {
            TestRig rig = BuildRig();
            SetCandidate(rig.Probe, Candidate(TraversalKind.Vault1m));
            RequestGroundedJump(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);
            rig.Data.Intent.JumpRequested = false;

            GameObject motionHost = new GameObject("Traversal Motion Host");
            _created.Add(motionHost);
            motionHost.transform.position = new Vector3(0f, 10f, 0f);
            CharacterController controller = motionHost.AddComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            MotionDriver driver = motionHost.AddComponent<MotionDriver>();
            FakeAnimationFacade facade = motionHost.AddComponent<FakeAnimationFacade>();
            facade.Playing = true;
            facade.NormalizedTime = 0.5f;
            yield return null;

            float beforeY = motionHost.transform.position.y;
            rig.Machine.CurrentState.OnUpdateMotion(driver, facade, rig.Data);

            Assert.AreEqual(1f, motionHost.transform.position.y - beforeY, 0.001f,
                "TraversalState 必須實際消費 0→0.5 的 VerticalCurve delta，不得疊加普通 gravity");
            Assert.AreEqual(1f / Time.deltaTime, rig.Data.VerticalVelocity, 0.1f);
            Assert.AreEqual(controller.isGrounded, rig.Data.IsGrounded,
                "committed motion 仍須經 MotionDriver 的單一 grounded 同步路徑");
        }

        [Test]
        public void PlaybackNotStarted_DoesNotTrustStaleNormalizedTime()
        {
            TestRig rig = BuildRig();
            SetCandidate(rig.Probe, Candidate(TraversalKind.Vault1m));
            RequestGroundedJump(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);

            GameObject motionHost = new GameObject("Traversal Playback Guard Host");
            _created.Add(motionHost);
            motionHost.AddComponent<CharacterController>();
            MotionDriver driver = motionHost.AddComponent<MotionDriver>();
            FakeAnimationFacade facade = motionHost.AddComponent<FakeAnimationFacade>();
            facade.Playing = false;
            facade.NormalizedTime = 1f;

            var traversal = (TraversalState)rig.Machine.CurrentState;
            traversal.OnUpdateMotion(driver, facade, rig.Data);

            Assert.AreEqual(0, facade.NormalizedTimeReadCount,
                "本 traversal animation 未播放時不得讀到上一支動畫的 normalized time");
            Assert.IsFalse(traversal.IsFinished);
        }

        [Test]
        public void T8_FinishedAndGrounded_TransitionsThroughConfiguredRulesToIdle()
        {
            TestRig rig = BuildRig();
            SetCandidate(rig.Probe, Candidate(TraversalKind.Climb1m));
            RequestGroundedJump(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);
            rig.Data.Intent.JumpRequested = false;
            rig.Data.IsGrounded = true;

            rig.Machine.Tick(rig.Data, 2f);

            Assert.AreEqual(StateType.Idle, rig.Machine.CurrentState.Type,
                "TraversalState 不應硬寫 Idle；grounded 結果由 ValidTransitions 選回 ambient state");
        }

        [UnityTest]
        public IEnumerator T9_FinishedWhileAirborne_TransitionsToExistingJumpFallEntry()
        {
            TestRig rig = BuildRig();
            SetCandidate(rig.Probe, Candidate(TraversalKind.Vault1m));
            RequestGroundedJump(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);
            rig.Data.Intent.JumpRequested = false;
            rig.Data.IsGrounded = false;

            // committed 期間 EvaluateInterrupts 仍會讓 JumpState 觀察失地，但 Traversal 的空 interrupt
            // 清單會阻止它提前切走。超過既有 fall-entry grace 後才讓 traversal 結束。
            rig.Machine.Tick(rig.Data, 0.01f);
            yield return new WaitForSeconds(0.12f);
            rig.Machine.Tick(rig.Data, 0.01f);
            Assert.AreEqual(StateType.Traversal, rig.Machine.CurrentState.Type);

            rig.Machine.Tick(rig.Data, 2f);

            Assert.AreEqual(StateType.Jump, rig.Machine.CurrentState.Type);
            Assert.IsTrue(GetPrivateField<bool>(rig.Machine.CurrentState, "_enteredFromFall"),
                "空中完成後必須由既有 Jump fall-entry 接手，不得退化成會注入起跳速度的主動 Jump");
        }

        [Test]
        public void V2_LowClimbWithinNormalJumpCapability_EntersJump()
        {
            V2TestRig rig = BuildV2Rig(1.2f);
            SetCandidate(rig.Probe, Candidate(
                TraversalKind.Climb1m, 0.8f, new Vector3(0f, 0.8f, 1f)));
            RequestGroundedJump(rig.Data);

            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreEqual(StateType.Jump, rig.Machine.CurrentState.Type);
        }

        [Test]
        public void V2_HighClimbOutsideNormalJumpCapability_EntersTraversal()
        {
            V2TestRig rig = BuildV2Rig(1.2f);
            SetCandidate(rig.Probe, Candidate(
                TraversalKind.Climb1m, 1.2f, new Vector3(0f, 1.2f, 1f)));
            RequestGroundedJump(rig.Data);

            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreEqual(StateType.Traversal, rig.Machine.CurrentState.Type);
        }

        [Test]
        public void V2_VaultRemainsContextualTraversalEvenWhenJumpCanReach()
        {
            V2TestRig rig = BuildV2Rig(2f);
            SetCandidate(rig.Probe, Candidate(
                TraversalKind.Vault1m, 0.5f, new Vector3(0f, 0.5f, 1f)));
            RequestGroundedJump(rig.Data);

            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreEqual(StateType.Traversal, rig.Machine.CurrentState.Type);
        }

        [UnityTest]
        public IEnumerator V2_CommittedWarpEndsAtCandidateAndIgnoresLaterProbeTarget()
        {
            V2TestRig rig = BuildV2Rig(0.5f);
            Vector3 committedTarget = new Vector3(0.25f, 1.2f, 1.35f);
            SetCandidate(rig.Probe, Candidate(
                TraversalKind.Climb1m, 1.2f, committedTarget));
            RequestGroundedJump(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);
            rig.Data.Intent.JumpRequested = false;

            var traversal = (TraversalState)rig.Machine.CurrentState;
            Assert.IsTrue(traversal.WarpPlan.IsValid);
            Assert.Less(Vector3.Distance(traversal.WarpPlan.TargetPosition, committedTarget), 0.0001f);

            SetCandidate(rig.Probe, Candidate(
                TraversalKind.Climb2m, 2f, new Vector3(4f, 2f, 4f)));
            rig.Facade.Playing = true;
            // 🔄（docs/23 §R1／Phase 1）語義更新：warp window（此 rig 為 0.1–0.7）只控制
            //    correction 融入的快慢，不再把整條 baked trajectory 壓進窗內。
            //    baked 位移依動畫時間播放，因此 committed destination 在動畫結束時抵達。
            rig.Facade.NormalizedTime = 1f;
            yield return null;
            traversal.OnUpdateMotion(rig.Driver, rig.Facade, rig.Data);

            Assert.Less(Vector3.Distance(rig.Host.transform.position, committedTarget), 0.02f);
            Assert.Less(Vector3.Distance(traversal.WarpPlan.TargetPosition, committedTarget), 0.0001f,
                "OnEnter committed target A 後，Probe 變成 B 不得改寫 warp plan");
        }

        [TestCase(TraversalKind.Vault1m, 0.72f)]
        [TestCase(TraversalKind.Climb1m, 0.8f)]
        [TestCase(TraversalKind.Climb2m, 0.9f)]
        public void R1_R3_EachTraversalBindingRecoversBeforeAnimationEnd(
            TraversalKind kind,
            float recoverTime)
        {
            V2TestRig rig = BuildV2Rig(0.5f);
            float height = kind == TraversalKind.Climb2m ? 2f : 1f;
            SetCandidate(rig.Probe, Candidate(kind, height, new Vector3(0f, height, 1f)));
            RequestGroundedJump(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);

            var traversal = (TraversalState)rig.Machine.CurrentState;
            rig.Facade.Playing = true;
            rig.Facade.NormalizedTime = recoverTime;
            traversal.OnUpdateMotion(rig.Driver, rig.Facade, rig.Data);

            Assert.IsTrue(traversal.CanRecover);
            Assert.IsTrue(traversal.CanTransitionAway);
            Assert.IsFalse(traversal.IsFinished, "Recovery 與 animation finished 必須是兩個概念");
            Assert.IsFalse(rig.Driver.IsTraversalCollisionProfileActive,
                "early recovery 必須先 restore controller profile，才能交回 locomotion");
        }

        [Test]
        public void R2_BeforeRecovery_RemainsCommitted()
        {
            V2TestRig rig = BuildV2Rig(0.5f);
            SetCandidate(rig.Probe, Candidate(
                TraversalKind.Climb1m, 1f, new Vector3(0f, 1f, 1f)));
            RequestGroundedJump(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);

            var traversal = (TraversalState)rig.Machine.CurrentState;
            rig.Facade.Playing = true;
            rig.Facade.NormalizedTime = 0.79f;
            traversal.OnUpdateMotion(rig.Driver, rig.Facade, rig.Data);

            Assert.IsFalse(traversal.CanRecover);
            Assert.IsFalse(traversal.CanTransitionAway);
            Assert.IsTrue(rig.Driver.IsTraversalCollisionProfileActive,
                "recovery marker 前仍應消費 committed collision profile");
        }

        [Test]
        public void R4_RecoveryWithMoveIntent_TransitionsToMoveWithoutWaitingForOne()
        {
            V2TestRig rig = EnterRecoverableTraversal(TraversalKind.Vault1m, 1f, 0.72f);
            rig.Model.IsProducingMotion = true;

            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreEqual(StateType.Move, rig.Machine.CurrentState.Type);
            Assert.IsFalse(rig.Driver.IsTraversalCollisionProfileActive);
        }

        [Test]
        public void R5_RecoveryWithoutMoveIntent_TransitionsToIdleWithoutWaitingForOne()
        {
            V2TestRig rig = EnterRecoverableTraversal(TraversalKind.Climb1m, 1f, 0.8f);
            rig.Model.IsProducingMotion = false;

            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreEqual(StateType.Idle, rig.Machine.CurrentState.Type);
            Assert.IsFalse(rig.Driver.IsTraversalCollisionProfileActive);
        }

        [Test]
        public void E1_StationaryGroundedCandidateAndJump_EntersTraversal()
        {
            TestRig rig = BuildRig();
            SetCandidate(rig.Probe, Candidate(TraversalKind.Climb1m));
            rig.Data.MoveSpeed = 0f;
            rig.Data.MoveDirection = Vector3.zero;
            RequestGroundedJump(rig.Data);

            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreEqual(StateType.Traversal, rig.Machine.CurrentState.Type);
        }

        private void AssertTraversalEntry(TraversalKind kind, string expectedAnimationKey)
        {
            TestRig rig = BuildRig();
            SetCandidate(rig.Probe, Candidate(kind));
            RequestGroundedJump(rig.Data);

            rig.Machine.Tick(rig.Data, 0.016f);

            Assert.AreEqual(StateType.Traversal, rig.Machine.CurrentState.Type,
                "Traversal priority 必須高於同幀也成立的 Jump");
            var traversal = rig.Machine.CurrentState as TraversalState;
            Assert.IsNotNull(traversal, "三種 traversal 必須共用同一個 TraversalState class");
            Assert.AreEqual(kind, traversal.CommittedKind);
            Assert.AreEqual(expectedAnimationKey, traversal.AnimationKey);
            Assert.AreSame(rig.BakeFor(kind), traversal.CurrentBake);
        }

        private TestRig BuildRig()
        {
            GameObject probeHost = new GameObject("Traversal FSM Probe");
            _created.Add(probeHost);
            probeHost.AddComponent<CharacterController>();
            TraversalProbe probe = probeHost.AddComponent<TraversalProbe>();
            SetPrivateField(probe, "drawTraversalRuntimeLines", false);

            MotionBakeData vaultBake = CreateBake("Vault Bake");
            MotionBakeData climb1Bake = CreateBake("Climb 1m Bake");
            MotionBakeData climb2Bake = CreateBake("Climb 2m Bake");
            MotionBakeData rollBake = CreateBake("Roll Test Bake");

            TraversalStateParamsSO traversalParams = ScriptableObject.CreateInstance<TraversalStateParamsSO>();
            traversalParams.name = "Traversal Test Params";
            _created.Add(traversalParams);
            JumpStateParams jumpParams = ScriptableObject.CreateInstance<JumpStateParams>();
            jumpParams.name = "Jump Test Params";
            _created.Add(jumpParams);
            SetPrivateField(traversalParams, "vault1m",
                new TraversalMotionBinding("Traversal.Vault1m", vaultBake));
            SetPrivateField(traversalParams, "climb1m",
                new TraversalMotionBinding("Traversal.Climb1m", climb1Bake));
            SetPrivateField(traversalParams, "climb2m",
                new TraversalMotionBinding("Traversal.Climb2m", climb2Bake));

            StateMachineConfigSO config = ScriptableObject.CreateInstance<StateMachineConfigSO>();
            config.name = "Traversal Test Config";
            _created.Add(config);
            SetPrivateField(config, "rules", BuildRules());
            SetPrivateField(config, "bakeMappings", new List<StateBakeMapping>
            {
                new StateBakeMapping { State = StateType.Roll, BakeData = rollBake }
            });
            SetPrivateField(config, "paramsMappings", new List<StateParamsMapping>
            {
                new StateParamsMapping { State = StateType.Jump, Params = jumpParams },
                new StateParamsMapping { State = StateType.Traversal, Params = traversalParams }
            });

            var data = new PlayerRuntimeData();
            var model = new FakeMovementModel();
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, model, traversalProbe: probe);
            return new TestRig(machine, data, model, probe, vaultBake, climb1Bake, climb2Bake);
        }

        private V2TestRig BuildV2Rig(float normalJumpApex)
        {
            GameObject host = new GameObject("Traversal V2 Rig");
            _created.Add(host);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            TraversalProbe probe = host.AddComponent<TraversalProbe>();
            SetPrivateField(probe, "drawTraversalRuntimeLines", false);
            MotionDriver driver = host.AddComponent<MotionDriver>();
            FakeAnimationFacade facade = host.AddComponent<FakeAnimationFacade>();

            MotionBakeData traversalBake = CreateWarpBake("Traversal V2 Bake");
            MotionBakeData jumpBake = CreateWarpBake("Jump Capability Bake");
            jumpBake.AutoApexHeight = normalJumpApex;
            jumpBake.AutoCalculatedGravity = 10f;

            JumpStateParams jumpParams = ScriptableObject.CreateInstance<JumpStateParams>();
            jumpParams.name = "Traversal V2 Jump Params";
            jumpParams.Stages.Add(new JumpStage { Bake = jumpBake });
            _created.Add(jumpParams);

            TraversalStateParamsSO traversalParams = ScriptableObject.CreateInstance<TraversalStateParamsSO>();
            traversalParams.name = "Traversal V2 Params";
            _created.Add(traversalParams);
            SetPrivateField(traversalParams, "vault1m", new TraversalMotionBinding(
                "Traversal.Vault1m", traversalBake, 0.1f, 0.7f, 0.72f, 2f, 2f));
            SetPrivateField(traversalParams, "climb1m", new TraversalMotionBinding(
                "Traversal.Climb1m", traversalBake, 0.1f, 0.7f, 0.8f, 2f, 2f));
            SetPrivateField(traversalParams, "climb2m", new TraversalMotionBinding(
                "Traversal.Climb2m", traversalBake, 0.1f, 0.7f, 0.9f, 2f, 2f));

            StateMachineConfigSO config = ScriptableObject.CreateInstance<StateMachineConfigSO>();
            config.name = "Traversal V2 Config";
            _created.Add(config);
            SetPrivateField(config, "rules", BuildRules());
            SetPrivateField(config, "bakeMappings", new List<StateBakeMapping>());
            SetPrivateField(config, "paramsMappings", new List<StateParamsMapping>
            {
                new StateParamsMapping { State = StateType.Jump, Params = jumpParams },
                new StateParamsMapping { State = StateType.Traversal, Params = traversalParams }
            });

            var data = new PlayerRuntimeData();
            var model = new FakeMovementModel();
            var machine = new FullBodyStateMachine();
            machine.Initialize(
                config, data, model, traversalProbe: probe, motionDriver: driver);
            return new V2TestRig(host, driver, facade, machine, data, model, probe);
        }

        private V2TestRig EnterRecoverableTraversal(
            TraversalKind kind,
            float height,
            float recoverTime)
        {
            V2TestRig rig = BuildV2Rig(0.5f);
            SetCandidate(rig.Probe, Candidate(kind, height, new Vector3(0f, height, 1f)));
            RequestGroundedJump(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);
            rig.Data.Intent.JumpRequested = false;
            rig.Data.IsGrounded = true;
            rig.Facade.Playing = true;
            rig.Facade.NormalizedTime = recoverTime;
            rig.Machine.CurrentState.OnUpdateMotion(rig.Driver, rig.Facade, rig.Data);
            return rig;
        }

        private static List<StateRule> BuildRules()
        {
            return new List<StateRule>
            {
                new StateRule
                {
                    State = StateType.Idle,
                    Priority = 0,
                    CanBeInterruptedBy = new List<StateType>
                    {
                        StateType.Move, StateType.Jump, StateType.Roll, StateType.Action, StateType.Traversal
                    },
                    ValidTransitions = new List<StateType> { StateType.Move }
                },
                new StateRule
                {
                    State = StateType.Move,
                    Priority = 0,
                    CanBeInterruptedBy = new List<StateType>
                    {
                        StateType.Jump, StateType.Roll, StateType.Action, StateType.Traversal
                    },
                    ValidTransitions = new List<StateType> { StateType.Idle }
                },
                new StateRule
                {
                    State = StateType.Jump,
                    Priority = 10,
                    CanBeInterruptedBy = new List<StateType>(),
                    ValidTransitions = new List<StateType> { StateType.Move, StateType.Idle }
                },
                new StateRule
                {
                    State = StateType.Roll,
                    Priority = 10,
                    CanBeInterruptedBy = new List<StateType>(),
                    ValidTransitions = new List<StateType> { StateType.Move, StateType.Idle }
                },
                new StateRule
                {
                    State = StateType.Action,
                    Priority = 10,
                    CanBeInterruptedBy = new List<StateType>(),
                    ValidTransitions = new List<StateType> { StateType.Move, StateType.Idle }
                },
                new StateRule
                {
                    State = StateType.Traversal,
                    Priority = 20,
                    CanBeInterruptedBy = new List<StateType>(),
                    // Jump 必須先評估：空中完成時由既有 fall-entry 接手；grounded 時它回 false，
                    // 再由現有 ambient rules 選 Move／Idle。
                    ValidTransitions = new List<StateType> { StateType.Jump, StateType.Move, StateType.Idle }
                }
            };
        }

        private MotionBakeData CreateBake(string name)
        {
            MotionBakeData bake = ScriptableObject.CreateInstance<MotionBakeData>();
            bake.name = name;
            bake.BakedDuration = 1f;
            bake.SpeedCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 0f), new Keyframe(1f, 0f));
            bake.VerticalCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
            _created.Add(bake);
            return bake;
        }

        private MotionBakeData CreateWarpBake(string name)
        {
            MotionBakeData bake = ScriptableObject.CreateInstance<MotionBakeData>();
            bake.name = name;
            bake.BakedDuration = 1f;
            bake.SpeedCurve = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 1f));
            bake.VerticalCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 1.4f), new Keyframe(1f, 1f));
            bake.RotationCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(1f, 0f));
            _created.Add(bake);
            return bake;
        }

        private static TraversalCandidate Candidate(TraversalKind kind)
        {
            return new TraversalCandidate(
                kind,
                TraversalRejectReason.None,
                new Vector3(0f, 0.5f, 0.75f),
                Vector3.back,
                new Vector3(0f, 1f, 1f),
                Vector3.up,
                kind == TraversalKind.Climb2m ? 2f : 1f,
                kind == TraversalKind.Vault1m ? 0.3f : 1f,
                new Vector3(0f, 1f, 1.5f),
                Vector3.forward);
        }

        private static TraversalCandidate Candidate(
            TraversalKind kind,
            float height,
            Vector3 destination,
            Vector3? forward = null)
        {
            return new TraversalCandidate(
                kind,
                TraversalRejectReason.None,
                new Vector3(0f, height * 0.5f, 0.75f),
                Vector3.back,
                new Vector3(0f, height, 1f),
                Vector3.up,
                height,
                kind == TraversalKind.Vault1m ? 0.3f : 1f,
                destination,
                forward ?? Vector3.forward);
        }

        private static void RequestGroundedJump(PlayerRuntimeData data)
        {
            data.IsGrounded = true;
            data.Intent.JumpRequested = true;
        }

        private static void SetCandidate(TraversalProbe probe, TraversalCandidate candidate)
        {
            SetPrivateField(probe, "<Candidate>k__BackingField", candidate);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位");
            return (T)field.GetValue(target);
        }

        private sealed class TestRig
        {
            public readonly FullBodyStateMachine Machine;
            public readonly PlayerRuntimeData Data;
            public readonly FakeMovementModel Model;
            public readonly TraversalProbe Probe;
            private readonly MotionBakeData _vaultBake;
            private readonly MotionBakeData _climb1Bake;
            private readonly MotionBakeData _climb2Bake;

            public TestRig(
                FullBodyStateMachine machine,
                PlayerRuntimeData data,
                FakeMovementModel model,
                TraversalProbe probe,
                MotionBakeData vaultBake,
                MotionBakeData climb1Bake,
                MotionBakeData climb2Bake)
            {
                Machine = machine;
                Data = data;
                Model = model;
                Probe = probe;
                _vaultBake = vaultBake;
                _climb1Bake = climb1Bake;
                _climb2Bake = climb2Bake;
            }

            public MotionBakeData BakeFor(TraversalKind kind)
            {
                switch (kind)
                {
                    case TraversalKind.Vault1m:
                        return _vaultBake;
                    case TraversalKind.Climb1m:
                        return _climb1Bake;
                    case TraversalKind.Climb2m:
                        return _climb2Bake;
                    default:
                        return null;
                }
            }
        }

        private sealed class V2TestRig
        {
            public readonly GameObject Host;
            public readonly MotionDriver Driver;
            public readonly FakeAnimationFacade Facade;
            public readonly FullBodyStateMachine Machine;
            public readonly PlayerRuntimeData Data;
            public readonly FakeMovementModel Model;
            public readonly TraversalProbe Probe;

            public V2TestRig(
                GameObject host,
                MotionDriver driver,
                FakeAnimationFacade facade,
                FullBodyStateMachine machine,
                PlayerRuntimeData data,
                FakeMovementModel model,
                TraversalProbe probe)
            {
                Host = host;
                Driver = driver;
                Facade = facade;
                Machine = machine;
                Data = data;
                Model = model;
                Probe = probe;
            }
        }
    }
}
