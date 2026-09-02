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

            data.Intent.RequestedActionSlot = ActionSlot.Primary;
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

            target.RequestAction(ActionSlot.Primary);
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type, "離地條件拒絕 external request");

            data.IsGrounded = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type, "被拒絕的 request 不得排隊到下一幀");

            target.RequestAction(ActionSlot.Primary);
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

            ActionDefinitionSO definition = CreateDefinition("Damage", false, 0.1f, slot: ActionSlot.Reaction);
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
            data.Intent.RequestedActionSlot = ActionSlot.Primary;

            state.OnEnter(data);
            Assert.AreEqual(1, sink.BeginCount);
            state.OnTick(data, 0.1f);
            data.Intent.RequestedActionSlot = ActionSlot.Primary;
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

        // =====================================================================
        // ADR-005（Trial）—— 多 Action 身分。以下四項是本次 Trial 的核心驗證。
        // =====================================================================

        [Test]
        public void T18_TwoDefinitions_TriggerIndependentlyOnOneActionState()
        {
            ActionDefinitionSO quick = CreateDefinition("QuickSpell", false, 0.1f, slot: ActionSlot.Secondary);
            ActionDefinitionSO ice = CreateDefinition("IceSpell", false, 0.1f, slot: ActionSlot.Tertiary);
            StateMachineConfigSO config = BuildMultiActionConfig(quick, ice);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.RequestedActionSlot = ActionSlot.Secondary;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("QuickSpell", machine.CurrentState.AnimationKey);
            BaseState firstInstance = machine.CurrentState;

            data.Intent.RequestedActionSlot = ActionSlot.None;
            machine.Tick(data, 0.2f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type);

            data.Intent.RequestedActionSlot = ActionSlot.Tertiary;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("IceSpell", machine.CurrentState.AnimationKey);
            Assert.AreSame(firstInstance, machine.CurrentState,
                "兩個技能必須共用同一顆 ActionState 實例（ADR-004 §5.2：不得一個動作一個 State）");

            Destroy(quick, ice, config);
        }

        [Test]
        public void T19_CooldownIsPerSlot_AndDoesNotBlockOtherSlots()
        {
            ActionDefinitionSO quick =
                CreateDefinition("QuickSpell", false, 0.05f, slot: ActionSlot.Secondary, cooldown: 5f);
            ActionDefinitionSO ice =
                CreateDefinition("IceSpell", false, 0.05f, slot: ActionSlot.Tertiary, cooldown: 0f);
            StateMachineConfigSO config = BuildMultiActionConfig(quick, ice);
            config.Initialize();
            var state = new ActionState();
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            data.Intent.RequestedActionSlot = ActionSlot.Secondary;
            state.OnEnter(data);
            state.OnTick(data, 0.1f);
            state.OnExit(data);

            Assert.Greater(state.GetCooldownRemaining(ActionSlot.Secondary), 0f, "出手後該 slot 進入冷卻");
            Assert.AreEqual(0f, state.GetCooldownRemaining(ActionSlot.Tertiary),
                "冷卻必須是 per-slot——另一個技能不得被連坐");

            data.Intent.RequestedActionSlot = ActionSlot.Secondary;
            Assert.IsFalse(state.CanEnter(data), "冷卻中的 slot 不得再次進入");

            data.Intent.RequestedActionSlot = ActionSlot.Tertiary;
            Assert.IsTrue(state.CanEnter(data), "未冷卻的 slot 必須仍可進入");

            Destroy(quick, ice, config);
        }

        [Test]
        public void T20_ActionToActionInterrupt_RequiresDifferentSlotAndInterruptiblePhase()
        {
            ActionDefinitionSO quick = CreateDefinition("QuickSpell", false, 1f, slot: ActionSlot.Secondary);
            ActionDefinitionSO ice = CreateDefinition("IceSpell", false, 1f, slot: ActionSlot.Tertiary);
            StateMachineConfigSO config = BuildMultiActionConfig(quick, ice);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.RequestedActionSlot = ActionSlot.Secondary;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("QuickSpell", machine.CurrentState.AnimationKey);

            // 同一個 slot 再次請求：不得重入（否則按住鍵會無限重播 Start）
            data.Intent.RequestedActionSlot = ActionSlot.Secondary;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("QuickSpell", machine.CurrentState.AnimationKey);

            // 不同 slot：Interruptible phase 允許重入（FU-1）
            data.Intent.RequestedActionSlot = ActionSlot.Tertiary;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("IceSpell", machine.CurrentState.AnimationKey,
                "不同身分的 Action 必須能互相打斷（FU-1）");

            Destroy(quick, ice, config);
        }

        [Test]
        public void T21_LegacySingleDefinitionConfig_StillResolves()
        {
            // 相容路徑：ADR-004 期的資產只在 paramsMappings 綁一份 Definition，未填 actionDefinitions。
            ActionDefinitionSO throwDefinition = CreateDefinition("Throw_Start", false, 0.1f);
            StateMachineConfigSO config = BuildConfig(throwDefinition);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.RequestedActionSlot = ActionSlot.Primary;
            machine.Tick(data, 0.016f);

            Assert.AreEqual("Throw_Start", machine.CurrentState.AnimationKey,
                "既有資產不改一個欄位也必須能繼續運作");
            Destroy(throwDefinition, config);
        }

        private static ActionDefinitionSO CreateDefinition(
            string key,
            bool emitsRelease,
            float duration,
            bool requiresGrounded = false,
            ActionSlot slot = ActionSlot.Primary,
            float cooldown = 0f)
        {
            var definition = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            definition.Slot = slot;
            definition.Cooldown = cooldown;
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
            SetPrivateField(config, "paramsMappings", definition == null
                ? new List<StateParamsMapping>()
                : new List<StateParamsMapping>
                {
                    new StateParamsMapping { State = StateType.Action, Params = definition }
                });
            return config;
        }

        /// <summary>
        /// 多份 Definition 版本（ADR-005）。刻意走 <c>actionDefinitions</c> 這條**新索引**，
        /// 與上方單份版本走 <c>paramsMappings</c> 相容路徑形成對照——兩條路都必須有效。
        /// </summary>
        private static StateMachineConfigSO BuildMultiActionConfig(params ActionDefinitionSO[] definitions)
        {
            StateMachineConfigSO config = BuildConfig(null);
            SetPrivateField(config, "paramsMappings", new List<StateParamsMapping>());
            SetPrivateField(config, "actionDefinitions", new List<ActionDefinitionSO>(definitions));
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
