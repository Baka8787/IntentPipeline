using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Actions;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    public class ActionStateTests
    {
        private sealed class FakeMovementModel : IMovementModel
        {
            public bool IsProducingMotion { get; set; }
            public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime) { }
            public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data) { }
        }

        private sealed class CountingLifecycleSink : IActionLifecycleSink
        {
            public int BeginCount { get; private set; }
            public int ReleaseCount { get; private set; }
            public int CleanupCount { get; private set; }

            public void Begin() => BeginCount++;
            public void Release() => ReleaseCount++;
            public void Cleanup() => CleanupCount++;
        }

        [Test]
        public void PlayerFireRequest_TransitionsThroughFsmToThrow()
        {
            ActionDefinitionSO definition = CreateDefinition("Throw_Start", false, 0.1f);
            StateMachineConfigSO config = BuildConfig(definition);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.FireRequested = true;
            machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Action, machine.CurrentState.Type);
            Assert.AreEqual("Throw_Start", machine.CurrentState.AnimationKey);
            Destroy(definition, config);
        }

        [Test]
        public void T13_ExternalRequest_GetsOneFsmEvaluationAndDoesNotQueue()
        {
            var targetObject = new GameObject("ActionRequestTarget-Test");
            ActionRequestTarget target = targetObject.AddComponent<ActionRequestTarget>();
            ActionDefinitionSO definition = CreateDefinition("Damage", false, 0.1f, true);
            StateMachineConfigSO config = BuildConfig(definition);
            var data = new PlayerRuntimeData { IsGrounded = false };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel(), target);

            target.RequestAction();
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type, "離地條件拒絕 external request");

            data.IsGrounded = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type, "被拒絕的 request 不得排隊到下一幀");

            target.RequestAction();
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Action, machine.CurrentState.Type, "新 request 應重新取得一次 FSM 仲裁機會");

            Destroy(definition, config, targetObject);
        }

        [Test]
        public void T14_ProjectileHit_RequestsEnemyStartOnlyDamageAction()
        {
            var targetObject = new GameObject("Enemy-ActionTarget-Test");
            ActionRequestTarget target = targetObject.AddComponent<ActionRequestTarget>();
            var projectileObject = new GameObject("Projectile-Test");
            ThrownProjectile projectile = projectileObject.AddComponent<ThrownProjectile>();

            ActionDefinitionSO definition = CreateDefinition("Damage", false, 0.1f);
            StateMachineConfigSO config = BuildConfig(definition);
            var data = new PlayerRuntimeData();
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel(), target);

            Assert.IsTrue(projectile.TryRequestHit(target));
            Assert.IsFalse(projectile.TryRequestHit(target), "同一 projectile 不得重複提交 hit request");
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Action, machine.CurrentState.Type);
            Assert.AreEqual("Damage", machine.CurrentState.AnimationKey);

            machine.Tick(data, 0.1f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type, "Start-only Damage 到時應自然完成");

            Destroy(definition, config, targetObject, projectileObject);
        }

        [Test]
        public void T15_ReleasePhase_EmitsExactlyOncePerExecution()
        {
            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            definition.Cooldown = 0f;
            definition.RequiresGrounded = true;
            definition.Phases = new[]
            {
                Entry(ActionPhase.Start, "Throw_Start", 0.1f),
                Entry(ActionPhase.Loop, "Throw_Loop", 1f, waitForTrigger: true),
                Entry(
                    ActionPhase.End,
                    "Throw_End",
                    0.2f,
                    emitsRelease: true,
                    releaseNormalizedTime: 0.5f)
            };
            StateMachineConfigSO config = BuildConfig(definition);
            config.Initialize();
            var state = new ActionState(null, sink);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            state.OnEnter(data);
            Assert.AreEqual(1, sink.BeginCount);
            state.OnTick(data, 0.1f);
            data.Intent.FireRequested = true;
            state.OnTick(data, 0.016f);
            Assert.AreEqual(ActionPhase.End, state.CurrentPhase);
            Assert.AreEqual(0, sink.ReleaseCount, "進入 End 時手上 visual 應繼續存在");

            state.OnTick(data, 0.099f);
            Assert.AreEqual(0, sink.ReleaseCount, "尚未到 authored release point 不得提早 release");
            state.OnTick(data, 0.002f);
            Assert.AreEqual(1, sink.ReleaseCount, "跨過 authored release point 時應 release");
            state.OnTick(data, 0.05f);
            Assert.AreEqual(1, sink.ReleaseCount, "同一次 execution 的後續 Tick 不得重送 release");

            state.OnExit(data);
            Assert.AreEqual(1, sink.CleanupCount);
            state.OnEnter(data);
            Assert.AreEqual(2, sink.BeginCount);
            state.OnTick(data, 0.1f);
            state.OnTick(data, 0.016f);
            state.OnTick(data, 0.1f);
            Assert.AreEqual(2, sink.ReleaseCount, "下一次 execution 應有自己的一次 release");

            state.OnExit(data);

            Destroy(definition, config);
        }

        [Test]
        public void ReleaseNormalizedTime_Zero_PreservesPhaseEntryRelease()
        {
            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateDefinition("Immediate", true, 0.2f);
            StateMachineConfigSO config = BuildConfig(definition);
            config.Initialize();
            var state = new ActionState(null, sink);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData();

            state.OnEnter(data);

            Assert.AreEqual(1, sink.ReleaseCount, "預設值 0 必須維持既有 phase-entry release 行為");

            state.OnExit(data);
            Destroy(definition, config);
        }

        [Test]
        public void T16_CancelPhase_CleansUpHeldVisualWithoutRelease()
        {
            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            definition.Cooldown = 0f;
            definition.RequiresGrounded = true;
            definition.CancelMoveIntentThreshold = 0.1f;
            definition.Phases = new[]
            {
                Entry(ActionPhase.Start, "Throw_Start", 0.1f),
                Entry(ActionPhase.Loop, "Throw_Loop", 1f, waitForTrigger: true),
                Entry(ActionPhase.Cancel, "Throw_Cancel", 0.2f)
            };
            StateMachineConfigSO config = BuildConfig(definition);
            config.Initialize();
            var state = new ActionState(null, sink);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            state.OnEnter(data);
            state.OnTick(data, 0.1f);
            data.MovementIntent.DesiredSpeedNormalized = 1f;
            state.OnTick(data, 0.016f);

            Assert.AreEqual(ActionPhase.Cancel, state.CurrentPhase);
            Assert.AreEqual(1, sink.BeginCount);
            Assert.AreEqual(1, sink.CleanupCount);
            Assert.AreEqual(0, sink.ReleaseCount);

            state.OnExit(data);
            Destroy(definition, config);
        }

        [Test]
        public void T17_Complete_ProvidesCleanupSafetyNet()
        {
            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateDefinition("StartOnly", false, 0.1f);
            StateMachineConfigSO config = BuildConfig(definition);
            config.Initialize();
            var state = new ActionState(null, sink);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData();

            state.OnEnter(data);
            state.OnTick(data, 0.1f);

            Assert.AreEqual(ActionPhase.None, state.CurrentPhase);
            Assert.AreEqual(1, sink.BeginCount);
            Assert.AreEqual(1, sink.CleanupCount);
            Assert.AreEqual(0, sink.ReleaseCount);

            state.OnExit(data);
            Destroy(definition, config);
        }

        private static ActionDefinitionSO CreateDefinition(
            string key, bool emitsRelease, float duration, bool requiresGrounded = false)
        {
            var definition = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            definition.Cooldown = 0f;
            definition.RequiresGrounded = requiresGrounded;
            definition.Phases = new[] { Entry(ActionPhase.Start, key, duration, emitsRelease: emitsRelease) };
            return definition;
        }

        private static ActionPhaseEntry Entry(
            ActionPhase phase,
            string key,
            float duration,
            bool waitForTrigger = false,
            bool emitsRelease = false,
            float releaseNormalizedTime = 0f)
        {
            return new ActionPhaseEntry
            {
                Phase = phase,
                AnimationKey = key,
                FallbackDuration = duration,
                Interruptible = true,
                WaitForTrigger = waitForTrigger,
                EmitsRelease = emitsRelease,
                ReleaseNormalizedTime = releaseNormalizedTime
            };
        }

        private static StateMachineConfigSO BuildConfig(ActionDefinitionSO definition)
        {
            var config = ScriptableObject.CreateInstance<StateMachineConfigSO>();
            SetPrivateField(config, "rules", new List<StateRule>
            {
                new StateRule
                {
                    State = StateType.Idle,
                    CanBeInterruptedBy = new List<StateType> { StateType.Action },
                    ValidTransitions = new List<StateType>()
                },
                new StateRule
                {
                    State = StateType.Action,
                    Priority = 10,
                    CanBeInterruptedBy = new List<StateType>(),
                    ValidTransitions = new List<StateType> { StateType.Idle }
                }
            });
            SetPrivateField(config, "paramsMappings", new List<StateParamsMapping>
            {
                new StateParamsMapping { State = StateType.Action, Params = definition }
            });
            return config;
        }

        private static void SetPrivateField<T>(StateMachineConfigSO config, string name, T value)
        {
            FieldInfo field = typeof(StateMachineConfigSO).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 StateMachineConfigSO.{name}");
            field.SetValue(config, value);
        }

        private static void Destroy(params Object[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
        }
    }
}
