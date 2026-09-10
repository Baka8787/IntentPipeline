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
using Project.Presentation.CameraControl;
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
            public ActionReleaseContext LastReleaseContext { get; private set; }

            public void Begin() => BeginCount++;
            public void Release(in ActionReleaseContext context)
            {
                ReleaseCount++;
                LastReleaseContext = context;
            }
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
            ActionDefinitionSO fireball = CreateDefinition("Fireball", false, 0.1f, slot: ActionSlot.Slot2);
            ActionDefinitionSO ice = CreateDefinition("IceSpell", false, 0.1f, slot: ActionSlot.Slot3);
            StateMachineConfigSO config = BuildMultiActionConfig(fireball, ice);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("Fireball", machine.CurrentState.AnimationKey);
            BaseState firstInstance = machine.CurrentState;

            data.Intent.RequestedActionSlot = ActionSlot.None;
            machine.Tick(data, 0.2f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type);

            data.Intent.RequestedActionSlot = ActionSlot.Slot3;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("IceSpell", machine.CurrentState.AnimationKey);
            Assert.AreSame(firstInstance, machine.CurrentState,
                "兩個技能必須共用同一顆 ActionState 實例（ADR-004 §5.2：不得一個動作一個 State）");

            Destroy(fireball, ice, config);
        }

        [Test]
        public void T19_CooldownIsPerSlot_AndDoesNotBlockOtherSlots()
        {
            ActionDefinitionSO fireball =
                CreateDefinition("Fireball", false, 0.05f, slot: ActionSlot.Slot2, cooldown: 5f);
            ActionDefinitionSO ice =
                CreateDefinition("IceSpell", false, 0.05f, slot: ActionSlot.Slot3, cooldown: 0f);
            StateMachineConfigSO config = BuildMultiActionConfig(fireball, ice);
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

            Destroy(fireball, ice, config);
        }

        [Test]
        public void T20_ActionToActionInterrupt_RequiresDifferentSlotAndInterruptiblePhase()
        {
            ActionDefinitionSO fireball = CreateDefinition("Fireball", false, 1f, slot: ActionSlot.Slot2);
            ActionDefinitionSO ice = CreateDefinition("IceSpell", false, 1f, slot: ActionSlot.Slot3);
            StateMachineConfigSO config = BuildMultiActionConfig(fireball, ice);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("Fireball", machine.CurrentState.AnimationKey);

            // 同一個 slot 再次請求：不得重入（否則按住鍵會無限重播 Start）
            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("Fireball", machine.CurrentState.AnimationKey);

            // 不同 slot：Interruptible phase 允許重入（FU-1）
            data.Intent.RequestedActionSlot = ActionSlot.Slot3;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("IceSpell", machine.CurrentState.AnimationKey,
                "不同身分的 Action 必須能互相打斷（FU-1）");

            Destroy(fireball, ice, config);
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
            ActionReleaseContext context = default;
            sink.Release(in context);
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
                "Fireball", true, 0.05f, slot: ActionSlot.Slot2);
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

        // =====================================================================
        // 冷卻變異（2026-09-06）
        // =====================================================================

        /// <summary>
        /// 變異只加不減——authored 的 `Cooldown` 永遠是**下限**。
        /// 這條守的是「調整手感不會意外讓技能變快」：填變異是為了讓節奏不可預測，
        /// 不是為了讓它偶爾比設計值更早可用。
        /// </summary>
        [Test]
        public void T34_CooldownVariance_OnlyExtends_NeverShortens()
        {
            Assert.AreEqual(12f, ActionState.ComputeCooldownEndTime(10f, 2f, 1.2f, 0f), 1e-4f,
                "random01 = 0 ⇒ 剛好是 authored cooldown");
            Assert.AreEqual(13.2f, ActionState.ComputeCooldownEndTime(10f, 2f, 1.2f, 1f), 1e-4f,
                "random01 = 1 ⇒ cooldown + 完整變異量");
            Assert.AreEqual(12.6f, ActionState.ComputeCooldownEndTime(10f, 2f, 1.2f, 0.5f), 1e-4f);
        }

        /// <summary>
        /// 變異 0 ＝ 舊行為一字不差。既有資產沒有這個欄位（反序列化取 C# 初始值 0），
        /// 因此這條同時是「加欄位不得改變既有技能手感」的回歸守衛。
        /// </summary>
        [Test]
        public void T34b_ZeroVariance_ReproducesLegacyCooldown()
        {
            Assert.AreEqual(11.5f, ActionState.ComputeCooldownEndTime(10f, 1.5f, 0f, 0.9f), 1e-4f,
                "沒填變異的 Definition 不得因為隨機來源而改變冷卻");
        }

        /// <summary>負值與越界的隨機值不得產生比 authored 更短的冷卻。</summary>
        [Test]
        public void T34c_CooldownVariance_ClampsDegenerateInputs()
        {
            Assert.AreEqual(10f, ActionState.ComputeCooldownEndTime(10f, -5f, 2f, 0f), 1e-4f,
                "負的 cooldown 夾成 0");
            Assert.AreEqual(12f, ActionState.ComputeCooldownEndTime(10f, 2f, -3f, 1f), 1e-4f,
                "負的變異夾成 0");
            Assert.AreEqual(12f, ActionState.ComputeCooldownEndTime(10f, 2f, 1f, -0.5f), 1e-4f,
                "random01 夾進 [0,1]");
        }

        // =====================================================================
        // 同 slot 重入：Reaction 特例（2026-09-06）
        // =====================================================================

        /// <summary>
        /// 硬直中再被打一次要能再踉蹌一次。**這是 Reaction 專屬的例外**，
        /// 因為「受擊」沒有「被自己打斷」的問題——觸發者本來就是別人。
        /// </summary>
        [Test]
        public void T32_Reaction_AllowsSameSlotReentry_WhenInterruptible()
        {
            var targetObject = new GameObject("Reaction-Reentry-Test");
            ActionRequestTarget target = targetObject.AddComponent<ActionRequestTarget>();

            ActionDefinitionSO damage = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            damage.Slot = ActionSlot.Reaction;
            damage.Cooldown = 0f;
            damage.RequiresGrounded = false;
            damage.Phases = new[] { Entry(ActionPhase.Start, "Damage", 1f) }; // Entry 的 Interruptible 是 true
            StateMachineConfigSO config = BuildMultiActionConfig(damage);
            config.Initialize();

            var state = new ActionState(target);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData();

            target.RequestAction(ActionSlot.Reaction);
            state.OnEnter(data);
            Assert.AreEqual("Damage", state.AnimationKey, "第一次受擊應進入 Reaction");

            target.RequestAction(ActionSlot.Reaction);
            Assert.IsTrue(state.CanReenter(data), "硬直中再被打一次應該可以重新觸發受擊");

            Destroy(damage, config, targetObject);
        }

        /// <summary>Reaction 仍然要看 `Interruptible`——無敵的倒地不該被輕拳打斷。</summary>
        [Test]
        public void T32b_Reaction_RejectsSameSlotReentry_WhenNotInterruptible()
        {
            var targetObject = new GameObject("Reaction-NoReentry-Test");
            ActionRequestTarget target = targetObject.AddComponent<ActionRequestTarget>();

            ActionDefinitionSO damage = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            damage.Slot = ActionSlot.Reaction;
            damage.Cooldown = 0f;
            damage.RequiresGrounded = false;
            damage.Phases = new[]
            {
                new ActionPhaseEntry
                {
                    Phase = ActionPhase.Start,
                    AnimationKey = "Damage",
                    FallbackDuration = 1f,
                    Interruptible = false
                }
            };
            StateMachineConfigSO config = BuildMultiActionConfig(damage);
            config.Initialize();

            var state = new ActionState(target);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData();

            target.RequestAction(ActionSlot.Reaction);
            state.OnEnter(data);

            target.RequestAction(ActionSlot.Reaction);
            Assert.IsFalse(state.CanReenter(data), "Interruptible 為 false 時不得重入，即使是 Reaction");

            Destroy(damage, config, targetObject);
        }

        /// <summary>
        /// 🔴 **這條是本次改動的護欄**：普通 Action 的同 slot 重入**仍然禁止**。
        ///
        /// ADR-005 給 `CanReenter` 的原始語意是「只多了『身分不同』這個條件」。
        /// 2026-09-06 加入的是 **Reaction 特例**，不是把限制整條放寬——
        /// 若哪天有人為了別的需求把 `AllowsSameSlotReentry` 改成「Interruptible 就好」，
        /// 這條會紅，並提醒代價是連段第 2 段會被第 3 段的請求吃掉。
        /// </summary>
        [Test]
        public void T33_NonReactionSlots_StillForbidSameSlotReentry()
        {
            Assert.IsTrue(ActionState.AllowsSameSlotReentry(ActionSlot.Reaction),
                "Reaction 是唯一的同 slot 重入例外");

            Assert.IsFalse(ActionState.AllowsSameSlotReentry(ActionSlot.Slot1));
            Assert.IsFalse(ActionState.AllowsSameSlotReentry(ActionSlot.Slot2));
            Assert.IsFalse(ActionState.AllowsSameSlotReentry(ActionSlot.Slot3));
            Assert.IsFalse(ActionState.AllowsSameSlotReentry(ActionSlot.None));
        }

        /// <summary>行為層面的同一條：可中斷的 Slot2 被自己再請求一次,仍然不得重入。</summary>
        [Test]
        public void T33b_InterruptibleAction_IsNotReenteredByItsOwnSlot()
        {
            ActionDefinitionSO fireball = CreateDefinition("Fireball", false, 1f, slot: ActionSlot.Slot2);
            StateMachineConfigSO config = BuildMultiActionConfig(fireball);
            config.Initialize();

            var state = new ActionState();
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            state.OnEnter(data);
            Assert.AreEqual("Fireball", state.AnimationKey);

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            Assert.IsFalse(state.CanReenter(data),
                "普通 Action 不得被同一個 slot 重入——那正是連段機制要保護的東西");

            Destroy(fireball, config);
        }

        // =====================================================================
        // Action-time facing（docs/11 §8.3）
        // =====================================================================

        /// <summary>
        /// 轉向規則是**全域的**：所有出手一律轉向，`Reaction` 例外。
        ///
        /// 這條看起來瑣碎，但它守的是 §8.3 的核心論證——**不得下放成 authored 欄位**。
        /// 只要有一個 Action 忘了勾，玩家就無法預測命中方向，「miss 可歸因於自己」當場破功。
        /// 哪天有人為了某個技能想「這招不要轉」而加欄位，這條測試會提醒他代價是什麼。
        ///
        /// `Reaction` 例外的理由是語意的：**受擊不是出手**，被打的人不該自己轉去面對攻擊者。
        /// </summary>
        [Test]
        public void T30_FacingRule_AppliesToEveryActionExceptReaction()
        {
            Assert.IsTrue(ActionState.ShouldFaceTargetOnEnter(ActionSlot.Slot1), "近戰要轉向");
            Assert.IsTrue(ActionState.ShouldFaceTargetOnEnter(ActionSlot.Slot2), "Fireball 要轉向");
            Assert.IsTrue(ActionState.ShouldFaceTargetOnEnter(ActionSlot.Slot3), "Ice 要轉向");

            Assert.IsFalse(ActionState.ShouldFaceTargetOnEnter(ActionSlot.Reaction),
                "受擊不是出手——被打的人不該自己轉去面對攻擊者");
            Assert.IsFalse(ActionState.ShouldFaceTargetOnEnter(ActionSlot.None));
        }

        [Test]
        public void TD7_Reaction_DoesNotAcquireDirectionCommitment()
        {
            AimResolver resolver = CreateAimResolverRig(out GameObject root, out MotionDriver motionDriver);
            CacheAim(resolver, root.transform.position + new Vector3(3f, 2f, 5f));

            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateDefinition(
                "Reaction", true, 0.1f, slot: ActionSlot.Reaction);
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();

            var state = new ActionState(null, CreateSinkMap(ActionSlot.Reaction, sink), resolver);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };
            data.Intent.RequestedActionSlot = ActionSlot.Reaction;

            state.OnEnter(data);
            state.OnUpdateMotion(motionDriver, null, data);
            state.OnTick(data, 0.2f);

            Assert.AreEqual(1, sink.ReleaseCount);
            Assert.IsFalse(sink.LastReleaseContext.HasAim,
                "Reaction 是受擊而非出手；即使角色有可用 AimResolver，也不得取得方向承諾");
            Assert.AreEqual(-1, GetPrivateInstanceField<int>(motionDriver, "_facingRequestFrame"),
                "Reaction 不得透過方向承諾送出 facing request");

            Destroy(definition, config, root);
        }

        /// <summary>
        /// 沒有 `AimResolver` 的角色（例如敵人）走完整條 Action 不得爆掉。
        /// 朝向是**可選的增益**，不是 Action 的前置條件。
        /// </summary>
        [Test]
        public void T31_TD6_ActionWithoutAimResolver_ReleasesNoAimContextAndCompletes()
        {
            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateDefinition("Enemy_Punch_R", true, 0.1f, slot: ActionSlot.Slot1);
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();

            // 第三個參數刻意留 null＝沒有 AimResolver。
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot1, sink), null);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };
            data.Intent.RequestedActionSlot = ActionSlot.Slot1;

            state.OnEnter(data);
            Assert.AreEqual(1, sink.BeginCount);

            state.OnTick(data, 0.2f);
            Assert.AreEqual(ActionPhase.None, state.CurrentPhase, "沒有 AimResolver 也要能正常收尾");
            Assert.AreEqual(1, sink.ReleaseCount);
            Assert.IsFalse(sink.LastReleaseContext.HasAim,
                "無 AimResolver 時 lifecycle 必須交付 HasAim=false 的 context，而不是中斷流程");

            Destroy(definition, config);
        }

        // =====================================================================
        // 連段（docs/11 §4.3）
        // =====================================================================

        [Test]
        public void TD5_SecondChainSegment_ReleaseAndFacingUseSameCommittedDirection()
        {
            AimResolver resolver = CreateAimResolverRig(out GameObject root, out MotionDriver motionDriver);
            root.transform.position = new Vector3(2f, 1f, -3f);

            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateChainDefinition();
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot2, sink), resolver);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            Vector3 firstAimPoint = root.transform.position + new Vector3(0f, 2f, 8f);
            CacheAim(resolver, firstAimPoint);
            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            state.OnEnter(data);
            data.Intent.RequestedActionSlot = ActionSlot.None;

            Vector3 secondAimPoint = root.transform.position + new Vector3(6f, 3f, 2f);
            CacheAim(resolver, secondAimPoint);
            AdvanceSegment(state, data, "Chain_2");
            Assert.AreEqual(1, sink.ReleaseCount, "切入第 2 段時只有第 1 段完成 release");

            // 模擬敵人在第 2 段承諾後移動；release 不得重新讀這個新位置。
            CacheAim(resolver, root.transform.position + new Vector3(-5f, 4f, 7f));
            Assert.IsTrue(state.TryGetFacingCommitment(out Vector3 submittedFacing),
                "第 2 段承諾應由 facing source 可讀取");

            state.OnTick(data, 0.11f);

            Assert.AreEqual(2, sink.ReleaseCount);
            Assert.IsTrue(sink.LastReleaseContext.HasAim);
            Assert.AreEqual(secondAimPoint, sink.LastReleaseContext.AimPoint,
                "第 2 段 release 必須使用該段邊界取得的 AimPoint，不得在 release 時重解");

            Vector3 releaseFacing = sink.LastReleaseContext.Direction;
            releaseFacing.y = 0f;
            releaseFacing.Normalize();
            Assert.Less((releaseFacing - submittedFacing).sqrMagnitude, 1e-6f,
                "同一段的身體 facing 與世界效果必須是同一份承諾的水平／完整投影");

            Destroy(definition, config, root);
        }

        /// <summary>
        /// 三段連段的骨幹：每段在窗口開啟後被再按一次就接下去，每段各發一次 Release。
        /// **整條只提交一次冷卻**——冷卻屬於「這次出手」，不屬於「每一段」。
        /// </summary>
        [Test]
        public void T26_ChainedAction_AdvancesPerRepressAndReleasesOncePerSegment()
        {
            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateChainDefinition(cooldown: 1.5f);
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot2, sink));
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            state.OnEnter(data);
            data.Intent.RequestedActionSlot = ActionSlot.None; // trigger 旗標當幀生、當幀死
            Assert.AreEqual("Chain_1", state.AnimationKey);
            Assert.AreEqual(1, sink.BeginCount);

            AdvanceSegment(state, data, "Chain_2");
            Assert.AreEqual(1, sink.ReleaseCount, "此時只有第 1 段發過 Release");

            AdvanceSegment(state, data, "Chain_3");
            Assert.AreEqual(2, sink.ReleaseCount, "第 2 段發出屬於自己的 Release");

            state.OnTick(data, 0.2f); // 最後一段播完且沒有再按 ⇒ 收尾
            Assert.AreEqual(3, sink.ReleaseCount, "第 3 段發出屬於自己的 Release ⇒ 三段共三次");
            Assert.AreEqual(ActionPhase.None, state.CurrentPhase);
            Assert.AreEqual(1, sink.BeginCount, "整條連段只算一次出手，Begin 不得每段各來一次");
            Assert.AreEqual(1, sink.CleanupCount);
            Assert.Greater(state.GetCooldownRemaining(ActionSlot.Slot2), 0f,
                "冷卻應在整條連段結束後提交一次");

            Destroy(definition, config);
        }

        /// <summary>沒有連按 ⇒ 第 1 段播完就結束，不得自己往下接。</summary>
        [Test]
        public void T27_ChainedAction_WithoutRepress_StopsAfterFirstSegment()
        {
            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateChainDefinition();
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot2, sink));
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            state.OnEnter(data);
            data.Intent.RequestedActionSlot = ActionSlot.None;

            state.OnTick(data, 0.25f);

            Assert.AreEqual(ActionPhase.None, state.CurrentPhase, "沒接到連按就該收尾");
            Assert.AreEqual(1, sink.ReleaseCount, "只播了一段，就只該有一次 Release");

            Destroy(definition, config);
        }

        /// <summary>
        /// 窗口開啟前的按壓不算連按。這條守的是 <c>ChainInputOpenNormalized</c> 真的有作用——
        /// 否則「進場那一幀順手多按的一下」會直接把第 2 段排進去。
        /// </summary>
        [Test]
        public void T28_ChainedAction_RepressBeforeWindowOpens_DoesNotAdvance()
        {
            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateChainDefinition(); // 窗口在 0.25 × 0.2 ＝ 0.05s 開
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot2, sink));
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            state.OnEnter(data);

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            state.OnTick(data, 0.02f); // 0.02s < 0.05s ⇒ 太早，不排隊
            data.Intent.RequestedActionSlot = ActionSlot.None;

            state.OnTick(data, 0.25f);

            Assert.AreEqual(ActionPhase.None, state.CurrentPhase, "窗口未開的按壓不得推進連段");
            Assert.AreEqual(1, sink.ReleaseCount);

            Destroy(definition, config);
        }

        /// <summary>連段用完就是用完；最後一段期間再按不會憑空長出第 4 段。</summary>
        [Test]
        public void T29_ChainedAction_RepressOnLastSegment_DoesNotExtendChain()
        {
            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateChainDefinition();
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot2, sink));
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            state.OnEnter(data);
            data.Intent.RequestedActionSlot = ActionSlot.None;

            AdvanceSegment(state, data, "Chain_2");
            AdvanceSegment(state, data, "Chain_3");

            data.Intent.RequestedActionSlot = ActionSlot.Slot2; // 最後一段期間再按
            state.OnTick(data, 0.11f);
            data.Intent.RequestedActionSlot = ActionSlot.None;
            state.OnTick(data, 0.11f);

            Assert.AreEqual(ActionPhase.None, state.CurrentPhase, "陣列已耗盡，不得再延伸");
            Assert.AreEqual(3, sink.ReleaseCount, "三段就是三次 Release");

            Destroy(definition, config);
        }

        /// <summary>
        /// 在窗口內按一次，再讓當前段播完，斷言已切到 <paramref name="expectedKey"/>。
        /// 段長 0.2s、窗口 0.05s 開、release 點 0.1s——三個數字都來自 <see cref="CreateChainDefinition"/>。
        /// </summary>
        private static void AdvanceSegment(ActionState state, PlayerRuntimeData data, string expectedKey)
        {
            data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            state.OnTick(data, 0.11f); // 越過窗口起點與 release 點，並排下一段
            data.Intent.RequestedActionSlot = ActionSlot.None;

            state.OnTick(data, 0.1f);  // 當前段播滿 ⇒ 切段
            Assert.AreEqual(expectedKey, state.AnimationKey);
            Assert.AreEqual(ActionPhase.Start, state.CurrentPhase,
                "連段是同一個 Start 換素材重播，不得變成別的 phase");
        }

        private static ActionDefinitionSO CreateChainDefinition(
            float segmentDuration = 0.2f,
            float chainInputOpenNormalized = 0.25f,
            float cooldown = 0f)
        {
            var definition = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            definition.Slot = ActionSlot.Slot2;
            definition.Cooldown = cooldown;
            definition.RequiresGrounded = true;
            definition.ChainInputOpenNormalized = chainInputOpenNormalized;
            definition.Phases = new[]
            {
                Entry(ActionPhase.Start, "Chain_1", segmentDuration,
                    emitsRelease: true, releaseNormalizedTime: 0.5f)
            };
            definition.ChainSegments = new[]
            {
                Entry(ActionPhase.None, "Chain_2", segmentDuration,
                    emitsRelease: true, releaseNormalizedTime: 0.5f),
                Entry(ActionPhase.None, "Chain_3", segmentDuration,
                    emitsRelease: true, releaseNormalizedTime: 0.5f)
            };
            return definition;
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

        private static AimResolver CreateAimResolverRig(
            out GameObject root,
            out MotionDriver motionDriver)
        {
            root = new GameObject("Action-Commitment-Test");
            CharacterController controller = root.AddComponent<CharacterController>();
            motionDriver = root.AddComponent<MotionDriver>();
            AimResolver resolver = root.AddComponent<AimResolver>();

            SetPrivateInstanceField(motionDriver, "characterController", controller);
            return resolver;
        }

        private static void CacheAim(AimResolver resolver, Vector3 aimPoint)
        {
            SetPrivateInstanceField(resolver, "_cachedFrame", Time.frameCount);
            SetPrivateInstanceField(resolver, "_cachedPoint", aimPoint);
            SetPrivateInstanceField(resolver, "_hasAim", true);
        }

        private static void SetPrivateInstanceField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }

        private static T GetPrivateInstanceField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{name}");
            return (T)field.GetValue(target);
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
