using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Core.StateMachine.Actions;
using Project.Core.Survivability;
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

        /// <remarks>
        /// ⚰️ 原 `T14_ProjectileHit_RequestsEnemyStartOnlyDamageAction` 已隨 `ActionSlot.Reaction`
        /// 退役而移除（`docs/26` Model B）。投射物 → 受擊的完整鏈路改由
        /// `SurvivabilityTests.H10`（傷害 → `JustTookDamage`）與 `HurtStateTests`（→ `StateType.Hurt`）
        /// 覆蓋——那才是它現在真正經過的地方。
        /// </remarks>
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
        public void T22_LegacySlot1Definition_DoesNotResolveAnotherSlotRequest()
        {
            // 2026-09-02 真實回歸：舊 Definition 沒有序列化 Slot，因此吃到初始值 Slot1；
            // 外部提交**別的** slot 後若仍讓它解析，反而會掩蓋身分填錯。相容退路保留資產結構，
            // 不保證錯誤身分也能工作——這裡的正確期望就是拒絕，並在 Editor 大聲指出接線問題。
            // 🔄（docs/26）原本用已退役的 `ActionSlot.Reaction` 當「另一個 slot」，改用 `Slot2`；
            //    斷言的行為一字未改。
            ActionDefinitionSO legacyDamage = CreateDefinition("Damage", false, 0.1f);
            StateMachineConfigSO config = BuildConfig(legacyDamage);
            var targetObject = new GameObject("Legacy-Damage-Target-Test");
            ActionRequestTarget target = targetObject.AddComponent<ActionRequestTarget>();
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel(), target);

            LogAssert.Expect(LogType.Warning,
                "[ActionState] ActionSlot.Slot2 沒有對應的 ActionDefinitionSO；" +
                "請檢查 StateMachineConfig 的 actionDefinitions 與該 Definition 的 Slot 欄位。");
            target.RequestAction(ActionSlot.Slot2);
            machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type,
                "Slot1 Definition 不得冒充 Slot2；應要求資產明確宣告正確身分");
            Destroy(legacyDamage, config, targetObject);
        }

        // ⚰️ 原 `T23_LegacyReactionDefinition_ResolvesReactionRequest` 已移除（`docs/26` Model B）。
        //    它驗的是「舊 Definition 把 Slot 改成 Reaction 就能沿用」——那條遷移路徑本身已退役。
        //    現在由 `A37_RetiredReactionSlot_HasNoRuntimeCallerOrAsset` 反向守住：
        //    **不得有任何資產使用 Reaction**。

        [Test]
        public void T24_MeleeHitbox_OpensOnReleaseAndRequestsEachTargetOnce()
        {
            var hitboxObject = new GameObject("Melee-Hitbox-Test");
            Collider collider = hitboxObject.AddComponent<BoxCollider>();
            MeleeHitboxSink sink = hitboxObject.AddComponent<MeleeHitboxSink>();
            // ⚠️ 同上：EditMode 不呼叫 Awake ⇒ hitbox 快取為空、命中窗永遠開不了。
            sink.ResolveHitbox();
            var targetObject = new GameObject("Melee-Target-Test");
            targetObject.AddComponent<ActionRequestTarget>();
            // 🔄（ADR-009 D2）去重鍵改為 CharacterHealth——近戰送的是傷害，不是 Reaction。
            CharacterHealth health = targetObject.AddComponent<CharacterHealth>();

            sink.Begin();
            Assert.IsFalse(sink.TryRequestHit(health), "Release 前命中窗必須保持關閉");
            ActionReleaseContext context = default;
            sink.Release(in context);
            Assert.IsTrue(collider.enabled);
            Assert.IsTrue(sink.TryRequestHit(health));
            Assert.IsFalse(sink.TryRequestHit(health), "同一次揮擊對同一目標只能結算一次傷害");
            sink.Cleanup();
            Assert.IsFalse(collider.enabled);
            Assert.IsFalse(sink.TryRequestHit(health), "Cleanup 後命中窗不得繼續提交");

            Destroy(hitboxObject, targetObject);
        }

        [Test]
        public void T25_Slot2Action_InvokesOnlySlot2LifecycleSink()
        {
            var slot1Sink = new CountingLifecycleSink();
            var slot2Sink = new CountingLifecycleSink();
            var slot3Sink = new CountingLifecycleSink();
            IActionLifecycleSink[][] sinks = CreateEmptySinkMap();
            sinks[(int)ActionSlot.Slot1] = new IActionLifecycleSink[] { slot1Sink };
            sinks[(int)ActionSlot.Slot2] = new IActionLifecycleSink[] { slot2Sink };
            sinks[(int)ActionSlot.Slot3] = new IActionLifecycleSink[] { slot3Sink };

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

        /// <summary>
        /// 🆕 **T26 —— 同一個 slot 的多顆 sink 全部收到通知，順序 ＝ 清單順序。**
        ///
        /// <para>2026-09-14 使用者裁決：`IActionLifecycleSink` 由 per-slot 單顆改為 per-slot 多顆。</para>
        /// 理由是一個 Action 天生可以有好幾個彼此獨立的副作用——命中判定、武器顯隱、刀光、VFX、音效
        /// ——它們共用同一組 `Begin` → `Release` → `Cleanup` 時點，卻沒有理由互相認識。
        ///
        /// <para><b>為什麼順序也要釘</b></para>
        /// sink 之間**不應該**互相依賴順序，但順序必須**可預期**：除錯時人會對照 Inspector 上的
        /// 清單順序去讀 log，順序若不穩定（例如某天改用字典）那份對照就失效，而且不會有任何測試變紅。
        ///
        /// <para><b>⚠️ 這條測的是派送，不是「幾顆 sink 才對」</b></para>
        /// 該掛幾顆是每個角色的接線決定，由 `PrefabWiringTests.W3` 在資產層檢查。
        /// </summary>
        [Test]
        public void T26_MultipleSinksOnOneSlot_AllReceiveLifecycleInBindingOrder()
        {
            var order = new List<string>();
            var first = new OrderRecordingLifecycleSink("first", order);
            var second = new OrderRecordingLifecycleSink("second", order);

            ActionDefinitionSO definition = CreateDefinition("Throw_Start", true, 0.05f);
            StateMachineConfigSO config = BuildConfig(definition);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(
                config, data, new FakeMovementModel(),
                actionLifecycleSinks: CreateSinkMap(ActionSlot.Slot1, first, second));

            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            machine.Tick(data, 0.016f);
            machine.Tick(data, 0.05f);   // 推完整段 ⇒ Release 與 Cleanup 都會走到（比照 T25）

            Assert.AreEqual(1, first.BeginCount, "第一顆 sink 必須收到 Begin");
            Assert.AreEqual(1, second.BeginCount,
                "第二顆 sink 也必須收到 Begin——舊版的『一 slot 一 sink』會讓它靜默地什麼都收不到");
            Assert.AreEqual(1, first.ReleaseCount);
            Assert.AreEqual(1, second.ReleaseCount);
            Assert.AreEqual(1, first.CleanupCount);
            Assert.AreEqual(1, second.CleanupCount,
                "Cleanup 必須送到每一顆——漏掉任何一顆都會留下沒收回去的狀態（例如武器卡在手上）");

            CollectionAssert.AreEqual(
                new[]
                {
                    "first.Begin", "second.Begin",
                    "first.Release", "second.Release",
                    "first.Cleanup", "second.Cleanup",
                },
                order,
                "每個時點都必須走完所有 sink 才進入下一個時點，且順序恆為清單順序");

            Destroy(definition, config);
        }

        /// <summary>
        /// 🆕 **T27 —— 冷卻進度的分母是「這一次實際用掉的長度」，不是 authored 的 `Cooldown`。**
        ///
        /// <para><b>為什麼這條非寫不可</b></para>
        /// `CooldownVariance` 讓每一次冷卻的實際長度都不同。HUD 若自己拿
        /// `Definition.Cooldown` 當分母，進度條會在**變異非零時**失準——
        /// 而那是一種只在特定資產設定下才發作、看起來只是「進度條有點怪」的靜默錯誤。
        /// ⇒ 由**擁有冷卻的人**（`ActionState`，ADR-004 D2）回答進度，HUD 只消費結果。
        ///
        /// <para>本測項用 `CooldownVariance` 明顯大於 0 的設定，讓「拿 authored 值當分母」
        /// 這個寫法無法通過：實際長度落在 [2, 4]，只有真的記下用掉的長度才算得出 1.0。</para>
        /// </summary>
        [Test]
        public void T27_CooldownNormalized_UsesTheActualDurationOfThisCooldown()
        {
            ActionDefinitionSO definition = CreateDefinition(
                "Throw_Start", false, 0.05f, cooldown: 2f);
            definition.CooldownVariance = 2f;   // 實際長度 ∈ [2, 4]

            StateMachineConfigSO config = BuildConfig(definition);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            var actionState = (ActionState)GetRegisteredState(machine, StateType.Action);
            Assert.AreEqual(0f, actionState.GetCooldownNormalized(ActionSlot.Slot1), 0.0001f,
                "還沒進過冷卻 ⇒ 進度 0（可用），且不得除以零");

            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            machine.Tick(data, 0.016f);
            machine.Tick(data, 0.05f);   // 播完 ⇒ Complete ⇒ CommitCooldown

            // 剛 commit 完、還沒經過任何時間 ⇒ 剩餘 ≈ 總長 ⇒ 進度 ≈ 1。
            // 若分母寫成 authored 的 2f 而實際長度是 3.5f，這裡會得到 1.75 被 clamp 成 1——
            // 所以**額外**檢查剩餘秒數確實落在 [2, 4]，讓「分母取錯」無處可藏。
            Assert.AreEqual(1f, actionState.GetCooldownNormalized(ActionSlot.Slot1), 0.02f,
                "剛進冷卻 ⇒ 進度 ≈ 1");

            float remaining = actionState.GetCooldownRemaining(ActionSlot.Slot1);
            Assert.GreaterOrEqual(remaining, 2f - 0.02f, "實際長度下限是 authored 的 Cooldown");
            Assert.LessOrEqual(remaining, 4f + 0.02f, "上限是 Cooldown + Variance");

            Assert.AreEqual(0f, actionState.GetCooldownNormalized(ActionSlot.Slot2), 0.0001f,
                "冷卻是 per-slot 的：Slot1 進冷卻不得讓 Slot2 也變暗");

            Destroy(definition, config);
        }

        /// <summary>從 FSM 的私有 registry 取出一顆 state，供直接驗證它的唯讀查詢。</summary>
        private static BaseState GetRegisteredState(FullBodyStateMachine machine, StateType type)
        {
            System.Reflection.FieldInfo field = typeof(FullBodyStateMachine).GetField(
                "_stateRegistry",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "找不到 FullBodyStateMachine._stateRegistry（欄位名稱可能已變更）");
            var registry = (Dictionary<StateType, BaseState>)field.GetValue(machine);
            Assert.IsTrue(registry.TryGetValue(type, out BaseState state), $"registry 裡沒有 {type}");
            return state;
        }

        /// <summary>把每次回呼記進共用序列，用來驗證派送順序而不只是次數。</summary>
        private sealed class OrderRecordingLifecycleSink : IActionLifecycleSink
        {
            private readonly string _name;
            private readonly List<string> _log;

            public OrderRecordingLifecycleSink(string name, List<string> log)
            {
                _name = name;
                _log = log;
            }

            public int BeginCount { get; private set; }
            public int ReleaseCount { get; private set; }
            public int CleanupCount { get; private set; }

            public void Begin()
            {
                BeginCount++;
                _log.Add($"{_name}.Begin");
            }

            public void Release(in ActionReleaseContext context)
            {
                ReleaseCount++;
                _log.Add($"{_name}.Release");
            }

            public void Cleanup()
            {
                CleanupCount++;
                _log.Add($"{_name}.Cleanup");
            }
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

        /// <summary>
        /// Enemy 的 Slot1 只在 request pulse 幀存在；下一幀為 false 時，**external request
        /// 必須能走進既有仲裁**（`docs/20` §2.6 的 starvation 回歸）。
        /// 這裡不改優先序——若兩者真的同幀，仍維持 intent 優先。
        ///
        /// 🔄（`docs/26` Model B）原本用 `ActionSlot.Reaction` 當那個 external request。
        /// 受擊已不再走 mailbox，但**「level 型 producer 會餓死 external request」這條風險沒有消失**
        /// （`docs/20` §2.7 明文登記為未解），所以這條測試改用 `Slot2` 繼續守住它。
        /// </summary>
        [Test]
        public void T35_EnemyAttackPulse_LeavesNextFrameForExternalRequest()
        {
            var enemy = new GameObject("Enemy-Attack-Reaction-Arbitration-Test");
            var attackTargetObject = new GameObject("Enemy-Attack-Target-Test");
            attackTargetObject.transform.position = Vector3.forward;

            AIInputSource inputSource = enemy.AddComponent<AIInputSource>();
            inputSource.ConfigureForTests(attackTargetObject.transform, 2f, 0.5f);
            ActionRequestTarget externalRequest = enemy.AddComponent<ActionRequestTarget>();

            ActionDefinitionSO punch = CreateDefinition(
                "Enemy_Punch_R", false, 1f, slot: ActionSlot.Slot1);
            ActionDefinitionSO external = CreateDefinition(
                "Fireball", false, 1f, slot: ActionSlot.Slot2);
            StateMachineConfigSO config = BuildMultiActionConfig(punch, external);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel(), externalRequest);

            InputData firstFrame = default;
            inputSource.FetchRawInputAtTimeForTests(ref firstFrame, 10f);
            data.Intent.RequestedActionSlot = firstFrame.Slot1ButtonDown
                ? ActionSlot.Slot1
                : ActionSlot.None;
            machine.Tick(data, 0.016f);
            Assert.AreEqual("Enemy_Punch_R", machine.CurrentState.AnimationKey,
                "測試前提：第一次 pulse 應先讓敵人出拳");

            data.ResetTransientState();
            externalRequest.RequestAction(ActionSlot.Slot2);
            InputData nextFrame = default;
            inputSource.FetchRawInputAtTimeForTests(ref nextFrame, 10.016f);
            data.Intent.RequestedActionSlot = nextFrame.Slot1ButtonDown
                ? ActionSlot.Slot1
                : ActionSlot.None;
            machine.Tick(data, 0.016f);

            Assert.IsFalse(nextFrame.Slot1ButtonDown,
                "retry interval 內的下一幀必須沒有 Slot1，否則 external request 仍會 starvation");
            Assert.AreEqual("Fireball", machine.CurrentState.AnimationKey,
                "沒有 Slot1 的幀必須讓 external request 被既有 ActionState 仲裁消費");

            Destroy(punch, external, config, enemy, attackTargetObject);
        }

        // =====================================================================
        // 同 slot 重入：**一律禁止**（docs/26 Model B 移除了 Reaction 特例）
        // =====================================================================

        /// <summary>
        /// 🔴 **`CanReenter` 的護欄**：同一個 Action 身分**永遠**不得自我重入。
        ///
        /// ADR-005 給 `CanReenter` 的語意是「只多了『身分不同』這個條件」；
        /// 2026-09-06 曾為 `Reaction` 開一個特例（硬直中再被打要再踉蹌），
        /// 該特例已於 `docs/26`（Model B）**連同 Reaction 一起移除**——
        /// 「再次受擊」現在由 `HurtState.CanReenter` 回答，不再是 Action 的事。
        ///
        /// 若哪天有人把這條放寬成「Interruptible 就好」，代價是**連段第 2 段會被第 3 段的請求吃掉**。
        /// </summary>
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
        public void T30_FacingRule_AppliesToEveryAction_WithoutException()
        {
            Assert.IsTrue(ActionState.ShouldFaceTargetOnEnter(ActionSlot.Slot1), "近戰要轉向");
            Assert.IsTrue(ActionState.ShouldFaceTargetOnEnter(ActionSlot.Slot2), "Fireball 要轉向");
            Assert.IsTrue(ActionState.ShouldFaceTargetOnEnter(ActionSlot.Slot3), "Ice 要轉向");

            // 🔄（docs/26 Model B）唯一的例外 `Reaction` 已隨「受擊不是 Action」一起移除
            // ⇒ 這條規則現在**沒有例外**。`None` 不是一個身分，不算例外。
            Assert.IsFalse(ActionState.ShouldFaceTargetOnEnter(ActionSlot.None),
                "None 代表沒有請求，不是一個 Action 身分");
        }

        // ⚰️ 原 `TD7_Reaction_DoesNotAcquireDirectionCommitment` 已移除（`docs/26` Model B）。
        //    它驗的是「受擊不得取得方向承諾」——那個保證現在是**結構性的**，不再需要斷言：
        //    `HurtState` 根本不認識 `IAimSource`／`ActionReleaseContext`／facing commitment，
        //    也沒有任何路徑能讓它送出 facing request。
        //    「被打的人不該自己轉去面對攻擊者」從一條 slot 例外變成了型別邊界。

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

            // 模擬敵人在第 2 段承諾後移動——release 必須跟上這個新位置。
            Vector3 movedAimPoint = root.transform.position + new Vector3(-5f, 4f, 7f);
            CacheAim(resolver, movedAimPoint);
            Assert.IsTrue(state.TryGetFacingCommitment(out Vector3 submittedFacing),
                "第 2 段承諾應由 facing source 可讀取");

            state.OnTick(data, 0.11f);

            Assert.AreEqual(2, sink.ReleaseCount);
            Assert.IsTrue(sink.LastReleaseContext.HasAim);
            // 🔄 **2026-09-15 invariant change（使用者明確裁決）。**
            // 舊 baseline：「release 必須使用該段邊界的 AimPoint，不得重解」。
            // 那條是缺陷本體——Fireball 的 release 在抬手 0.42 秒後，用舊座標發射
            // ＋約 0.6 秒飛行、敵人側移 1.644 m/s ⇒ 累積 ≈1.7 m，而膠囊只有 0.64 m 寬
            // ⇒ 側移中的敵人必定打不到（2026-09-15 使用者 Play 回報 ＋ 逐帧錄影確認）。
            // ⇒ **效果落點改為 release 當下解算**；facing 承諾維持段落快照（見下一段斷言）。
            Assert.AreEqual(movedAimPoint, sink.LastReleaseContext.AimPoint,
                "效果落點必須是 release 當下的目標位置——用段落邊界的舊座標就是那 1.7m 落差的來源");

            // ⚠️ **反向的一半必須同時成立**：facing 承諾**不得**跟著每帧重取，
            //    否則角色在抬手期間會持續扭向目標——那正是 ADR-007 一次承諾要消除的抽搐。
            Assert.IsTrue(state.TryGetFacingCommitment(out Vector3 facingAfterMove));
            Assert.Less((facingAfterMove - submittedFacing).sqrMagnitude, 1e-6f,
                "facing 仍必須是該段邊界的快照（ADR-007 的這一半未變）");

            Destroy(definition, config, root);
        }

        /// <summary>
        /// Soft target 的**facing 承諾**只在 commitment boundary 取一次快照；
        /// 段落開始後即使候選被清掉、camera aim 改變，**facing 仍維持該段承諾**。
        /// 🔄 2026-09-15：**效果落點不再受此限**（見 TD5）——它改為 release 當下解算，
        /// 因為打移動目標時舊座標必定落空。本測項因此只守 facing 那一半。
        /// </summary>
        [Test]
        public void TD5B_SoftTargetSnapshot_IsStableForTheCommittedSegment()
        {
            AimResolver resolver = CreateAimResolverRig(out GameObject root, out _);
            root.transform.position = new Vector3(1f, 0f, -2f);

            var sink = new CountingLifecycleSink();
            ActionDefinitionSO definition = CreateDefinition(
                "Spell_Ice", true, 0.1f, slot: ActionSlot.Slot3);
            definition.Targeting = ActionTargetingPolicy.CameraConeSoftTarget;
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();

            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot3, sink), resolver);
            state.Initialize(config, new FakeMovementModel());
            var data = new PlayerRuntimeData { IsGrounded = true };

            Vector3 cameraAim = root.transform.position + new Vector3(0f, 2f, 10f);
            Vector3 committedTarget = root.transform.position + new Vector3(5f, 1f, 7f);
            CacheAim(resolver, cameraAim);
            data.CombatContext = new CombatContextData
            {
                HasSoftTarget = true,
                SoftTargetPosition = committedTarget
            };
            data.Intent.RequestedActionSlot = ActionSlot.Slot3;

            state.OnEnter(data);
            Assert.IsTrue(state.TryGetFacingCommitment(out Vector3 committedFacing));

            // 模擬下一幀 target 已失效且 camera aim 已轉向；本段承諾不得被這些 live 資料改寫。
            data.CombatContext = default;
            CacheAim(resolver, root.transform.position + new Vector3(-8f, 4f, 1f));
            state.OnTick(data, 0.2f);

            Assert.AreEqual(1, sink.ReleaseCount);
            Assert.IsTrue(sink.LastReleaseContext.HasAim);
            Assert.AreEqual(committedTarget, sink.LastReleaseContext.AimPoint,
                "失效後仍須保留段落開始時承諾的 soft-target point");

            Vector3 releaseFacing = sink.LastReleaseContext.Direction;
            releaseFacing.y = 0f;
            releaseFacing.Normalize();
            Assert.Less((releaseFacing - committedFacing).sqrMagnitude, 1e-6f,
                "facing 與 projectile release 必須共享同一次 commitment");

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

        /// <summary>
        /// 建一份「只有指定 slot 有 sink」的派送表。
        /// 🆕 2026-09-14：外層索引＝slot、內層＝該 slot 的 sink 清單（一個 slot 可以有多顆）。
        /// </summary>
        private static IActionLifecycleSink[][] CreateSinkMap(
            ActionSlot slot, params IActionLifecycleSink[] sinks)
        {
            IActionLifecycleSink[][] map = CreateEmptySinkMap();
            map[(int)slot] = sinks;
            return map;
        }

        private static IActionLifecycleSink[][] CreateEmptySinkMap()
        {
            var map = new IActionLifecycleSink[ActionState.SlotCount][];
            for (int i = 0; i < map.Length; i++) map[i] = System.Array.Empty<IActionLifecycleSink>();
            return map;
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

        // ═══════════════════════════════════════════════════════════════════
        // 🆕 2026-09-15：facing 承諾（段落邊界快照）vs 效果落點（release 當下）
        // ═══════════════════════════════════════════════════════════════════

        private sealed class MovingAimSource : IAimSource
        {
            public Vector3 Origin = Vector3.zero;
            public Vector3 Point = new Vector3(0f, 0f, 5f);
            public Vector3 CommitmentOrigin => Origin;
            public bool TryGetAimPoint(out Vector3 worldPoint)
            {
                worldPoint = Point;
                return true;
            }
        }

        /// <summary>
        /// 🔴 **效果落點必須跟上目標；facing 承諾必須不動。**
        ///
        /// <para><b>這條守的是一個實測過的缺陷</b></para>
        /// Fireball 的 release 在抬手 <c>1.2 × 0.35 = 0.42 秒</c>後。舊實作用段落邊界的舊座標發射，
        /// 等於打「敵人 0.42 秒前的位置」；加上約 0.6 秒飛行、敵人側移 1.644 m/s
        /// ⇒ 累積 <b>≈1.7 m</b>，而敵人膠囊直徑只有 <b>0.64 m</b>
        /// ⇒ **側移中的敵人打不中是預期行為**（2026-09-15 使用者 Play 回報 ＋ 逐帧錄影確認）。
        ///
        /// ⚠️ 反向的一半同樣重要：**facing 不得跟著每帧重取**，否則角色在抬手期間會持續扭向目標，
        /// 那正是 ADR-007 一次承諾當初要消除的抽搐。兩個斷言必須同時成立。
        /// </summary>
        [Test]
        public void AC1_EffectContextTracksTarget_WhileFacingCommitmentStaysAtEntry()
        {
            var aim = new MovingAimSource { Point = new Vector3(0f, 0f, 5f) };
            var sink = new CountingLifecycleSink();

            ActionDefinitionSO definition = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            definition.Slot = ActionSlot.Slot1;
            definition.Phases = new[]
            {
                Entry(ActionPhase.Start, "Spell", 1f, emitsRelease: true, releaseNormalizedTime: 0.5f)
            };
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();

            var data = new PlayerRuntimeData { IsGrounded = true };
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot1, sink), aim);
            state.Initialize(config, new FakeMovementModel());

            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            state.OnEnter(data);

            Assert.IsTrue(state.TryGetFacingCommitment(out Vector3 facingAtEntry),
                "前提：進入時必須取得 facing 承諾");

            // 目標在抬手期間橫向移開——這正是實機上敵人側移的情形。
            aim.Point = new Vector3(4f, 0f, 5f);

            for (int i = 0; i < 40 && sink.ReleaseCount == 0; i++) state.OnTick(data, 0.02f);

            Assert.AreEqual(1, sink.ReleaseCount, "前提：必須真的 release 過一次");

            Assert.AreEqual(aim.Point, sink.LastReleaseContext.AimPoint,
                "效果落點必須是 release 當下的目標位置，不是段落邊界的舊座標——"
                + "用舊座標發射就是那 1.7m 落差的來源");

            Assert.IsTrue(state.TryGetFacingCommitment(out Vector3 facingAtRelease));
            Assert.AreEqual(facingAtEntry, facingAtRelease,
                "facing 承諾必須維持段落邊界的快照（ADR-007）——跟著每帧重取會讓角色抬手時抽搐");

            Destroy(definition, config);
        }

        /// <summary>
        /// 反向守門：瞄準中途失效（目標消失／aim source 解不出）時，
        /// 效果落點必須**保留上一次可用的值**，不得退化成 <c>default</c>——那會讓法術朝世界原點飛。
        /// </summary>
        [Test]
        public void AC2_EffectContext_KeepsLastUsablePoint_WhenAimBecomesUnavailable()
        {
            var aim = new MovingAimSource { Point = new Vector3(0f, 0f, 5f) };
            var sink = new CountingLifecycleSink();

            ActionDefinitionSO definition = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            definition.Slot = ActionSlot.Slot1;
            definition.Phases = new[]
            {
                Entry(ActionPhase.Start, "Spell", 1f, emitsRelease: true, releaseNormalizedTime: 0.5f)
            };
            StateMachineConfigSO config = BuildMultiActionConfig(definition);
            config.Initialize();

            var data = new PlayerRuntimeData { IsGrounded = true };
            var state = new ActionState(null, CreateSinkMap(ActionSlot.Slot1, sink), aim);
            state.Initialize(config, new FakeMovementModel());

            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            state.OnEnter(data);

            Vector3 lastGood = new Vector3(2f, 0f, 5f);
            aim.Point = lastGood;
            state.OnTick(data, 0.02f);

            for (int i = 0; i < 40 && sink.ReleaseCount == 0; i++) state.OnTick(data, 0.02f);

            Assert.AreEqual(1, sink.ReleaseCount);
            Assert.IsTrue(sink.LastReleaseContext.HasAim,
                "release 必須帶著可用的落點，不得是 default——default 會讓效果生在世界原點");

            Destroy(definition, config);
        }

    }
}
