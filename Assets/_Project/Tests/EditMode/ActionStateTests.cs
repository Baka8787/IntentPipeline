using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
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

            target.RequestAction(ActionSlot.Slot1);
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type, "離地條件拒絕 external request");

            data.IsGrounded = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type, "被拒絕的 request 不得排隊到下一幀");

            target.RequestAction(ActionSlot.Slot1);
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
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot1, sink));
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };
            data.Intent.RequestedActionSlot = ActionSlot.Slot1;

            state.OnEnter(data);
            Assert.AreEqual(1, sink.BeginCount);
            state.OnTick(data, 0.1f);
            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
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
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot1, sink));
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData();

            // 🆕（ADR-005）ActionState 不再於 Initialize 綁死 Definition，改為每次進入時依 request 的
            // slot 現查 ⇒ 直接呼叫 OnEnter 的測試**必須先表達 request**，否則解析不到任何 Definition。
            // 走 FSM 的測試不受影響（CanEnter 已先問過）。進入後復位，比照管線順序 7 的單幀語意。
            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            state.OnEnter(data);
            data.Intent.RequestedActionSlot = ActionSlot.None;

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
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot1, sink));
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            // 🆕（ADR-005）直接呼叫 OnEnter ⇒ 必須先表達 request（見上方 ReleaseNormalizedTime_Zero 註解）。
            // 進入後復位為 None，避免殘留的 request 在 Loop 期被誤判為 WaitForTrigger 的 re-trigger。
            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            state.OnEnter(data);
            data.Intent.RequestedActionSlot = ActionSlot.None;

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
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot1, sink));
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData();

            // 🆕（ADR-005）直接呼叫 OnEnter ⇒ 必須先表達 request（見上方 ReleaseNormalizedTime_Zero 註解）。
            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            state.OnEnter(data);
            data.Intent.RequestedActionSlot = ActionSlot.None;

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
            ActionDefinitionSO quick = CreateDefinition("QuickSpell", false, 0.1f, slot: ActionSlot.Slot2);
            ActionDefinitionSO ice = CreateDefinition("IceSpell", false, 0.1f, slot: ActionSlot.Slot3);
            StateMachineConfigSO config = BuildMultiActionConfig(quick, ice);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("QuickSpell", machine.CurrentState.AnimationKey);
            BaseState firstInstance = machine.CurrentState;

            data.Intent.RequestedActionSlot = ActionSlot.None;
            machine.Tick(data, 0.2f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type);

            data.Intent.RequestedActionSlot = ActionSlot.Slot3;
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
                CreateDefinition("QuickSpell", false, 0.05f, slot: ActionSlot.Slot2, cooldown: 5f);
            ActionDefinitionSO ice =
                CreateDefinition("IceSpell", false, 0.05f, slot: ActionSlot.Slot3, cooldown: 0f);
            StateMachineConfigSO config = BuildMultiActionConfig(quick, ice);
            config.Initialize();
            var state = new ActionState();
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            state.OnEnter(data);
            state.OnTick(data, 0.1f);
            state.OnExit(data);

            Assert.Greater(state.GetCooldownRemaining(ActionSlot.Slot2), 0f, "出手後該 slot 進入冷卻");
            Assert.AreEqual(0f, state.GetCooldownRemaining(ActionSlot.Slot3),
                "冷卻必須是 per-slot——另一個技能不得被連坐");

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            Assert.IsFalse(state.CanEnter(data), "冷卻中的 slot 不得再次進入");

            data.Intent.RequestedActionSlot = ActionSlot.Slot3;
            Assert.IsTrue(state.CanEnter(data), "未冷卻的 slot 必須仍可進入");

            Destroy(quick, ice, config);
        }

        [Test]
        public void T20_ActionToActionInterrupt_RequiresDifferentSlotAndInterruptiblePhase()
        {
            ActionDefinitionSO quick = CreateDefinition("QuickSpell", false, 1f, slot: ActionSlot.Slot2);
            ActionDefinitionSO ice = CreateDefinition("IceSpell", false, 1f, slot: ActionSlot.Slot3);
            StateMachineConfigSO config = BuildMultiActionConfig(quick, ice);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("QuickSpell", machine.CurrentState.AnimationKey);

            // 同一個 slot 再次請求：不得重入（否則按住鍵會無限重播 Start）
            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("QuickSpell", machine.CurrentState.AnimationKey);

            // 不同 slot：Interruptible phase 允許重入（FU-1）
            data.Intent.RequestedActionSlot = ActionSlot.Slot3;
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

            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            machine.Tick(data, 0.016f);

            Assert.AreEqual("Throw_Start", machine.CurrentState.AnimationKey,
                "既有資產不改一個欄位也必須能繼續運作");
            Destroy(throwDefinition, config);
        }

        [Test]
        public void T22_LegacySlot1Definition_DoesNotResolveReactionRequest()
        {
            // 2026-09-02 真實回歸：舊 DamageDefinition 沒有序列化 Slot，因此吃到初始值 Slot1；
            // projectile 提交 Reaction 後若仍讓它解析，反而會掩蓋身分填錯。相容退路保留資產結構，
            // 不保證錯誤身分也能工作——這裡的正確期望就是拒絕，並在 Editor 大聲指出接線問題。
            ActionDefinitionSO legacyDamage = CreateDefinition("Damage", false, 0.1f);
            StateMachineConfigSO config = BuildConfig(legacyDamage);
            var targetObject = new GameObject("Legacy-Damage-Target-Test");
            ActionRequestTarget target = targetObject.AddComponent<ActionRequestTarget>();
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel(), target);

            LogAssert.Expect(LogType.Warning,
                "[ActionState] ActionSlot.Reaction 沒有對應的 ActionDefinitionSO；" +
                "請檢查 StateMachineConfig 的 actionDefinitions 與該 Definition 的 Slot 欄位。");
            target.RequestAction(ActionSlot.Reaction);
            machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type,
                "Slot1 Definition 不得冒充 Reaction；應要求資產明確宣告正確身分");
            Destroy(legacyDamage, config, targetObject);
        }

        [Test]
        public void T23_LegacyReactionDefinition_ResolvesReactionRequest()
        {
            // 同樣不填 actionDefinitions，但把舊 Definition 的單一遷移欄位設對，即可沿用原資產與相容退路。
            ActionDefinitionSO legacyDamage = CreateDefinition(
                "Damage", false, 0.1f, slot: ActionSlot.Reaction);
            StateMachineConfigSO config = BuildConfig(legacyDamage);
            var targetObject = new GameObject("Legacy-Reaction-Target-Test");
            ActionRequestTarget target = targetObject.AddComponent<ActionRequestTarget>();
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel(), target);

            target.RequestAction(ActionSlot.Reaction);
            machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Action, machine.CurrentState.Type);
            Assert.AreEqual("Damage", machine.CurrentState.AnimationKey,
                "舊資產不必重建，但 Slot 必須明確遷移成 Reaction");
            Destroy(legacyDamage, config, targetObject);
        }

        [Test]
        public void T24_MeleeHitbox_OpensOnReleaseAndRequestsEachTargetOnce()
        {
            var hitboxObject = new GameObject("Melee-Hitbox-Test");
            Collider collider = hitboxObject.AddComponent<BoxCollider>();
            MeleeHitboxSink sink = hitboxObject.AddComponent<MeleeHitboxSink>();
            // ⚠️ 同上：EditMode 不呼叫 Awake ⇒ hitbox 快取為空、命中窗永遠開不了。
            sink.ResolveHitbox();
            var targetObject = new GameObject("Melee-Target-Test");
            ActionRequestTarget target = targetObject.AddComponent<ActionRequestTarget>();

            sink.Begin();
            Assert.IsFalse(sink.TryRequestHit(target), "Release 前命中窗必須保持關閉");
            sink.Release();
            Assert.IsTrue(collider.enabled);
            Assert.IsTrue(sink.TryRequestHit(target));
            Assert.IsFalse(sink.TryRequestHit(target), "同一次揮擊對同一目標只能提交一次 Reaction");
            sink.Cleanup();
            Assert.IsFalse(collider.enabled);
            Assert.IsFalse(sink.TryRequestHit(target), "Cleanup 後命中窗不得繼續提交");

            Destroy(hitboxObject, targetObject);
        }

        [Test]
        public void T25_Slot2Action_InvokesOnlySlot2LifecycleSink()
        {
            var slot1Sink = new CountingLifecycleSink();
            var slot2Sink = new CountingLifecycleSink();
            var slot3Sink = new CountingLifecycleSink();
            var sinks = new IActionLifecycleSink[ActionState.SlotCount];
            sinks[(int)ActionSlot.Slot1] = slot1Sink;
            sinks[(int)ActionSlot.Slot2] = slot2Sink;
            sinks[(int)ActionSlot.Slot3] = slot3Sink;

            ActionDefinitionSO quickSpell = CreateDefinition(
                "QuickSpell", true, 0.05f, slot: ActionSlot.Slot2);
            StateMachineConfigSO config = BuildMultiActionConfig(quickSpell);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel(), null, sinks);

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            machine.Tick(data, 0.016f);
            machine.Tick(data, 0.05f);

            Assert.AreEqual(0, slot1Sink.BeginCount);
            Assert.AreEqual(0, slot1Sink.ReleaseCount);
            Assert.AreEqual(0, slot1Sink.CleanupCount,
                "Slot2 出手不得誤觸 Slot1 melee sink");
            Assert.AreEqual(1, slot2Sink.BeginCount);
            Assert.AreEqual(1, slot2Sink.ReleaseCount);
            Assert.AreEqual(1, slot2Sink.CleanupCount);
            Assert.AreEqual(0, slot3Sink.BeginCount);
            Assert.AreEqual(0, slot3Sink.ReleaseCount);
            Assert.AreEqual(0, slot3Sink.CleanupCount,
                "Slot2 出手不得誤觸 Slot3 projectile sink");

            Destroy(quickSpell, config);
        }

        private static ActionDefinitionSO CreateDefinition(
            string key,
            bool emitsRelease,
            float duration,
            bool requiresGrounded = false,
            ActionSlot slot = ActionSlot.Slot1,
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

        private static IActionLifecycleSink[] CreateSinkMap(
            ActionSlot slot, IActionLifecycleSink sink)
        {
            var sinks = new IActionLifecycleSink[ActionState.SlotCount];
            sinks[(int)slot] = sink;
            return sinks;
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
