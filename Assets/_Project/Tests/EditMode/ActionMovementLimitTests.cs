using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-10-12，`docs/54` §3.5.6／§3.5.7，WP-3a）**Action 宣告的方向限速：時序、撤銷、stale。**
    ///
    /// <para><b>為什麼不直接用 <c>LocomotionModel</c> 跑每一條</b></para>
    /// EditMode 的 <c>Time.frameCount</c> 不前進 ⇒ 真 model 分不出「本帧／上一帧／兩帧前」。
    /// 所以這裡的 <see cref="FrameSteppedModel"/> 自己數帧，但**判斷一律呼叫正式程式的純函數**
    /// （<see cref="LocomotionModel.ShouldApplyDirectionalSpeedLimit"/>）；宣告方是**真的**
    /// <see cref="ActionState"/>，經**真的** <see cref="FullBodyStateMachine"/>（含 Hurt／Death／Roll 中斷）。
    /// 每一帧的呼叫順序與 Runner 相同：順序 3 model → 順序 4 FSM。
    /// 真 <c>LocomotionModel</c> 的閘門接線另由 <c>RealLocomotionModel_*</c> 驗證。
    /// </summary>
    public sealed class ActionMovementLimitTests
    {
        private readonly List<Object> _created = new();

        /// <summary>
        /// 自己數帧的 model：<see cref="Tick"/>（順序 3）用正式純函數決定這一帧是否套用限速，
        /// <see cref="SetActionMovementLimit"/> 蓋上當下帧號（與 <c>LocomotionModel</c> 同語意）。
        /// </summary>
        private sealed class FrameSteppedModel : IMovementModel, IActionMovementLimitTarget
        {
            public int Frame;
            public ActionMovementLimit Declared;
            public int DeclaredFrame = int.MinValue;
            public bool AppliedThisFrame;
            public DirectionalSpeedLimitActivation Activation = DirectionalSpeedLimitActivation.WhileActionDeclares;

            public bool IsProducingMotion { get; set; }

            public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime)
            {
                AppliedThisFrame = LocomotionModel.ShouldApplyDirectionalSpeedLimit(
                    Activation, data.CombatContext.InCombat, Declared, DeclaredFrame, Frame);
            }

            public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data) { }

            public void SetActionMovementLimit(ActionMovementLimit limit)
            {
                Declared = limit;
                DeclaredFrame = Frame;
            }
        }

        private sealed class Rig
        {
            public FullBodyStateMachine Machine;
            public FrameSteppedModel Model;
            public PlayerRuntimeData Data;

            /// <summary>一帧：順序 3（model）→ 順序 4（FSM）。回傳本帧 model 是否套用限速。</summary>
            public bool Frame(float deltaTime = 0.05f, bool tickMachine = true)
            {
                Model.Frame++;
                Model.Tick(Data, null, deltaTime);
                if (tickMachine)
                {
                    Machine.Tick(Data, deltaTime);
                    Data.ResetTransientState();
                }
                return Model.AppliedThisFrame;
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // =====================================================================
        // AC-3a.2 首帧延遲（明文斷言）＋ 每帧續約
        // =====================================================================

        [Test]
        public void AC2_DeclaredPhase_NotLimitedOnEntryFrame_LimitedFromNextFrame()
        {
            Rig rig = BuildRig(spellDuration: 1f);

            Assert.IsFalse(rig.Frame(), "前提：Idle 不受限");

            rig.Data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            bool entryFrame = rig.Frame();
            Assert.AreEqual(StateType.Action, rig.Machine.CurrentState.Type, "前提：本帧進入施法");
            Assert.IsFalse(entryFrame,
                "首帧延遲（docs/54 §3.5.7-①，Z4）：宣告在順序 4，本帧的順序 3 已經跑完 ⇒ 進入帧 N 不受限");

            for (int i = 0; i < 10; i++)
            {
                Assert.IsTrue(rig.Frame(), $"N+{i + 1}：宣告每帧續約 ⇒ 持續受限");
                Assert.AreEqual(StateType.Action, rig.Machine.CurrentState.Type);
            }
        }

        // =====================================================================
        // AC-3a.3 撤銷：自然完成、Hurt、Death、Roll、Action 切換
        // =====================================================================

        [Test]
        public void AC3_NaturalCompletion_RevokesOnTheNextFrame()
        {
            Rig rig = BuildRig(spellDuration: 0.2f);
            EnterSpell(rig);

            int guard = 0;
            while (rig.Machine.CurrentState.Type == StateType.Action && guard++ < 20) rig.Frame();
            Assert.AreNotEqual(StateType.Action, rig.Machine.CurrentState.Type, "前提：施法自然結束");

            Assert.IsFalse(rig.Frame(), "自然完成（Complete → ResetExecutionState）後的下一帧 model 讀到 None");
        }

        [TestCase("Hurt")]
        [TestCase("Death")]
        [TestCase("Roll")]
        public void AC3_InterruptedByHurtDeathOrRoll_RevokesOnTheNextFrame(string interrupter)
        {
            Rig rig = BuildRig(spellDuration: 10f);
            EnterSpell(rig);
            Assert.IsTrue(rig.Frame(), "前提：施法中受限");

            switch (interrupter)
            {
                case "Hurt": rig.Data.Survivability.JustTookDamage = true; break;
                case "Death": rig.Data.Survivability.IsDead = true; break;
                case "Roll": rig.Data.Intent.RollRequested = true; break;
            }

            bool interruptFrame = rig.Frame();
            Assert.AreEqual(interrupter, rig.Machine.CurrentState.Type.ToString(), "前提：被打斷");
            Assert.IsTrue(interruptFrame,
                "被打斷的帧 N：順序 3 已在 FSM 之前跑完，套用的是上一帧的宣告（已執行完的輸出不變）");

            Assert.IsFalse(rig.Frame(), $"{interrupter} 不宣告；OnExit → ResetExecutionState 撤銷 ⇒ N+1 讀到 None");
            Assert.IsFalse(rig.Frame(), "之後持續 None（沒有殘留）");
        }

        [Test]
        public void AC3_ActionToActionSwitch_FinalDeclarationIsTheNewAction()
        {
            Rig rig = BuildRig(spellDuration: 10f, meleeDuration: 10f);

            // 施法（宣告）→ 切到近戰（None）：OnExit 撤銷、OnEnter 宣告 None，同一個 Tick 內後寫者贏。
            EnterSpell(rig);
            Assert.IsTrue(rig.Frame());
            rig.Data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            rig.Frame();
            Assert.AreEqual("TestMelee", rig.Machine.CurrentState.AnimationKey, "前提：Action → Action 重入");
            Assert.IsFalse(rig.Frame(), "新 Action 宣告 None ⇒ 下一帧不受限（舊施法的限制沒有殘留）");

            // 反向：近戰（None）→ 施法（宣告）：最終值是施法的宣告。
            rig.Data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            rig.Frame();
            Assert.AreEqual("TestSpell", rig.Machine.CurrentState.AnimationKey);
            Assert.IsTrue(rig.Frame(), "最終值＝新 Action 的宣告");
        }

        // =====================================================================
        // AC-3a.4 stale：停止續約兩帧後自動失效；暫停期間續約不中斷
        // =====================================================================

        [Test]
        public void AC4_MissingRenewal_ExpiresAutomatically_WithoutAnyRevocation()
        {
            Rig rig = BuildRig(spellDuration: 10f);
            EnterSpell(rig);
            Assert.IsTrue(rig.Frame());
            Assert.AreEqual(StateType.Action, rig.Machine.CurrentState.Type);

            // 模擬「某條路徑忘了撤銷」：FSM 完全不再被 Tick（沒有續約、也沒有 ResetExecutionState）。
            Assert.IsTrue(rig.Frame(tickMachine: false), "停止續約後第一帧：上一帧的宣告仍有效");
            Assert.IsFalse(rig.Frame(tickMachine: false), "第二帧：宣告已過期 ⇒ 自動視為 None");
            Assert.AreEqual(ActionMovementLimit.DirectionalSpeed, rig.Model.Declared,
                "前提：沒有任何人撤銷——失效完全來自帧號過期");
        }

        [Test]
        public void AC4_Pause_KeepsRenewing_AndResumeIsStillLimited()
        {
            Rig rig = BuildRig(spellDuration: 1f);
            EnterSpell(rig);
            Assert.IsTrue(rig.Frame());

            for (int i = 0; i < 30; i++)
            {
                Assert.IsTrue(rig.Frame(deltaTime: 0f),
                    $"暫停第 {i} 帧：OnTick 的 deltaTime<=0 early return 之前已續約 ⇒ 限制不過期");
            }
            Assert.AreEqual(StateType.Action, rig.Machine.CurrentState.Type, "暫停不推進施法時間");

            Assert.IsTrue(rig.Frame(), "解除暫停的第一帧仍受限（宣告沒有在暫停中遺失）");
        }

        // =====================================================================
        // AC-3a.5 未宣告的 Definition：逐位元等同今天（不受限）
        // =====================================================================

        [Test]
        public void AC5_UndeclaredAction_NeverLimits()
        {
            Rig rig = BuildRig(spellDuration: 10f, meleeDuration: 0.5f);
            rig.Data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            rig.Frame();
            Assert.AreEqual("TestMelee", rig.Machine.CurrentState.AnimationKey);
            for (int i = 0; i < 20; i++) Assert.IsFalse(rig.Frame(), $"None 宣告的 Action 第 {i} 帧不得受限");
        }

        // =====================================================================
        // 純函數：啟用政策（驗收⑤⑥）與 stale 邊界
        // =====================================================================

        [Test]
        public void Policy_WhileInCombat_IgnoresActionDeclarations_YBotUnchanged()
        {
            foreach (ActionMovementLimit declared in new[] { ActionMovementLimit.None, ActionMovementLimit.DirectionalSpeed })
            {
                Assert.IsTrue(LocomotionModel.ShouldApplyDirectionalSpeedLimit(
                    DirectionalSpeedLimitActivation.WhileInCombat, true, declared, 5, 5),
                    "Y Bot：交戰中一律套用（與導入前相同的閘門）");
                Assert.IsFalse(LocomotionModel.ShouldApplyDirectionalSpeedLimit(
                    DirectionalSpeedLimitActivation.WhileInCombat, false, declared, 5, 5),
                    "Y Bot：非交戰一律不套用，Action 宣告不影響");
            }
        }

        [Test]
        public void Policy_WhileActionDeclares_IgnoresInCombat_XBotNotLimitedWhenNotCasting()
        {
            Assert.IsFalse(LocomotionModel.ShouldApplyDirectionalSpeedLimit(
                DirectionalSpeedLimitActivation.WhileActionDeclares, true, ActionMovementLimit.None, 5, 5),
                "驗收⑤：X Bot 交戰中但未施法 ⇒ 不再因 InCombat 套用施法限速");
            Assert.IsTrue(LocomotionModel.ShouldApplyDirectionalSpeedLimit(
                DirectionalSpeedLimitActivation.WhileActionDeclares, false, ActionMovementLimit.DirectionalSpeed, 5, 6),
                "X Bot 非交戰施法仍受限（不再依賴「出手即交戰」）");
        }

        [Test]
        public void Stale_DeclarationIsLiveOnlyForThisOrPreviousFrame()
        {
            Assert.IsTrue(LocomotionModel.IsActionDeclarationLive(ActionMovementLimit.DirectionalSpeed, 10, 10));
            Assert.IsTrue(LocomotionModel.IsActionDeclarationLive(ActionMovementLimit.DirectionalSpeed, 10, 11));
            Assert.IsFalse(LocomotionModel.IsActionDeclarationLive(ActionMovementLimit.DirectionalSpeed, 10, 12));
            Assert.IsFalse(LocomotionModel.IsActionDeclarationLive(ActionMovementLimit.DirectionalSpeed, 10, 9),
                "未來帧的宣告不成立（帧號倒退＝資料錯誤，不得當成有效）");
            Assert.IsFalse(LocomotionModel.IsActionDeclarationLive(ActionMovementLimit.DirectionalSpeed, int.MinValue, int.MaxValue),
                "從未宣告（哨兵帧號）不得溢位成有效");
            Assert.IsFalse(LocomotionModel.IsActionDeclarationLive(ActionMovementLimit.None, 10, 10));
        }

        // =====================================================================
        // 真 LocomotionModel：閘門接線（數值仍是 profile 的 Bake 推導）
        // =====================================================================

        /// <summary>
        /// 同一份側移意圖，三顆真 model 並跑：未宣告／已宣告／沒有 profile。
        /// 只有「已宣告」的那顆速度被壓低；未宣告的與沒有 profile 的**逐位元相同**（AC-3a.5 的 model 半邊）。
        /// 交戰中（InCombat＝true）刻意打開——證明 X Bot 的政策下 InCombat 不再觸發限速。
        /// </summary>
        [Test]
        public void RealLocomotionModel_WhileActionDeclares_LimitsOnlyWhenDeclared()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CombatDirectionalSpeedProfileSO>(
                "Assets/ScriptableObjects/Motion/XBotSpellDirectionalSpeedProfile.asset");
            Assert.IsNotNull(profile, "找不到 XBotSpellDirectionalSpeedProfile");

            LocomotionModel undeclared = CreateModel(profile, DirectionalSpeedLimitActivation.WhileActionDeclares);
            LocomotionModel declared = CreateModel(profile, DirectionalSpeedLimitActivation.WhileActionDeclares);
            LocomotionModel unprofiled = CreateModel(null, DirectionalSpeedLimitActivation.WhileActionDeclares);
            LocomotionModel yBotStyle = CreateModel(profile, DirectionalSpeedLimitActivation.WhileInCombat);

            PlayerRuntimeData Data() => new PlayerRuntimeData
            {
                IsGrounded = true,
                MovementIntent = new MovementIntentData { DesiredDirection = Vector3.right, DesiredSpeedNormalized = 1f },
                CombatContext = new CombatContextData { InCombat = true },
            };
            PlayerRuntimeData a = Data(), b = Data(), c = Data(), d = Data();

            for (int i = 0; i < 60; i++)
            {
                declared.SetActionMovementLimit(ActionMovementLimit.DirectionalSpeed);   // EditMode 帧號不動 ⇒ 每帧都是「本帧」宣告
                undeclared.Tick(a, null, 0.02f);
                declared.Tick(b, null, 0.02f);
                unprofiled.Tick(c, null, 0.02f);
                yBotStyle.Tick(d, null, 0.02f);
            }

            float limit = profile.ResolveNormalizedSpeed(new Vector2(1f, 0f));
            Assert.Less(limit, 0.99f, "前提：X Bot 的側向 Bake 上限必須小於滿速，否則本測項無法分辨");
            Assert.AreEqual(c.MoveSpeed, a.MoveSpeed, "未宣告 ⇒ 與沒有 profile 逐位元相同（交戰中也不限速）");
            Assert.AreEqual(limit, b.MoveSpeed, 0.01f, "已宣告 ⇒ 收斂到 profile 的 Bake 推導上限");
            Assert.AreEqual(limit, d.MoveSpeed, 0.01f, "WhileInCombat 政策 ⇒ 交戰中照舊受限（Y Bot 路徑不變）");
        }

        // =====================================================================
        // helpers
        // =====================================================================

        private LocomotionModel CreateModel(CombatDirectionalSpeedProfileSO profile, DirectionalSpeedLimitActivation activation)
        {
            var go = new GameObject("ActionMovementLimit-Model");
            _created.Add(go);
            LocomotionModel model = go.AddComponent<LocomotionModel>();
            SetField(model, "combatDirectionalSpeedProfile", profile);
            SetField(model, "directionalSpeedLimitActivation", activation);
            return model;
        }

        private static void EnterSpell(Rig rig)
        {
            rig.Data.Intent.RequestedActionSlot = ActionSlot.Slot2;
            rig.Frame();
            Assert.AreEqual("TestSpell", rig.Machine.CurrentState.AnimationKey, "前提：進入施法");
        }

        private Rig BuildRig(float spellDuration, float meleeDuration = 0.5f)
        {
            ActionDefinitionSO spell = Definition(ActionSlot.Slot2, "TestSpell", spellDuration, ActionMovementLimit.DirectionalSpeed);
            ActionDefinitionSO melee = Definition(ActionSlot.Slot1, "TestMelee", meleeDuration, ActionMovementLimit.None);

            var config = ScriptableObject.CreateInstance<StateMachineConfigSO>();
            _created.Add(config);
            SetConfigField(config, "rules", new List<StateRule>
            {
                Rule(StateType.Idle, 0, new[] { StateType.Roll, StateType.Action, StateType.Hurt, StateType.Death }, new[] { StateType.Move }),
                Rule(StateType.Move, 0, new[] { StateType.Roll, StateType.Action, StateType.Hurt, StateType.Death }, new[] { StateType.Idle }),
                Rule(StateType.Roll, 20, new[] { StateType.Death }, new[] { StateType.Move, StateType.Idle }),
                Rule(StateType.Action, 10, new[] { StateType.Roll, StateType.Hurt, StateType.Death }, new[] { StateType.Move, StateType.Idle }),
                Rule(StateType.Hurt, 15, new[] { StateType.Death }, new[] { StateType.Move, StateType.Idle }),
                Rule(StateType.Death, 100, new StateType[0], new[] { StateType.Idle, StateType.Move }),
            });
            SetConfigField(config, "paramsMappings", new List<StateParamsMapping>());
            SetConfigField(config, "actionDefinitions", new List<ActionDefinitionSO> { melee, spell });

            var rig = new Rig
            {
                Machine = new FullBodyStateMachine(),
                Model = new FrameSteppedModel(),
                Data = new PlayerRuntimeData { IsGrounded = true },
            };
            rig.Machine.Initialize(config, rig.Data, rig.Model);
            return rig;
        }

        private ActionDefinitionSO Definition(ActionSlot slot, string key, float duration, ActionMovementLimit limit)
        {
            var definition = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            _created.Add(definition);
            definition.Slot = slot;
            definition.Cooldown = 0f;
            definition.RequiresGrounded = false;
            definition.CancelMoveIntentThreshold = 0f;
            definition.Phases = new[]
            {
                new ActionPhaseEntry
                {
                    Phase = ActionPhase.Start,
                    AnimationKey = key,
                    FallbackDuration = duration,
                    Interruptible = true,
                    MovementLimit = limit,
                }
            };
            return definition;
        }

        private static StateRule Rule(StateType state, int priority, StateType[] interruptedBy, StateType[] transitions)
            => new StateRule
            {
                State = state,
                Priority = priority,
                CanBeInterruptedBy = new List<StateType>(interruptedBy),
                ValidTransitions = new List<StateType>(transitions)
            };

        private static void SetConfigField<T>(StateMachineConfigSO config, string name, T value)
        {
            FieldInfo field = typeof(StateMachineConfigSO).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 StateMachineConfigSO.{name}");
            field.SetValue(config, value);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }
    }
}
