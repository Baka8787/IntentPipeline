using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// `StateType.Hurt` ／ `StateType.Death` 的 FSM 行為（`docs/26` Model B ＋ ADR-009）。
    ///
    /// <para><b>本檔用程式建構的 config，刻意**複製正式資產應有的形狀**</b></para>
    /// 因此它同時是那份設定的規格。⚠️ 但**出貨資產本身**由
    /// `ArchitectureRegressionTests.A38_ShippingStateConfigs_MatchDecidedInterruptionPolicy` 守——
    /// 兩者缺一不可：本檔驗「機制對不對」，A38 驗「出貨資料真的這樣填」。
    ///
    /// <para><b>⚠️ permission 與 priority 是兩件事</b></para>
    /// 「Roll／Traversal 不得被 Hurt 打斷」一律由 `CanBeInterruptedBy` **明確表示**，
    /// **不是**靠 `Hurt priority &lt; Roll priority` 得到的副作用（使用者 2026-09-14 明確要求）。
    /// `HD8` 專門守這一條：即使把 Hurt 的 priority 拉到最高，Roll 仍然擋得住。
    /// </summary>
    public sealed class HurtAndDeathStateTests
    {
        private readonly List<Object> _created = new();

        private sealed class FakeMovementModel : IMovementModel
        {
            public bool IsProducingMotion { get; set; }
            public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime) { }
            public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data) { }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // =====================================================================
        // Hurt 的基本語意
        // =====================================================================

        [Test]
        public void HD1_JustTookDamage_EntersHurtFromLocomotion()
        {
            FullBodyStateMachine machine = BuildMachine(out PlayerRuntimeData data, out _);

            data.Survivability.JustTookDamage = true;
            machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Hurt, machine.CurrentState.Type);
            Assert.AreEqual("Hurt", machine.CurrentState.AnimationKey,
                "動畫鍵走 BaseState 的預設（Type.ToString()），不需要 Definition 資產");
        }

        /// <summary>硬直期間不得自然過渡離開；時間到才放行。</summary>
        [Test]
        public void HD2_HurtHoldsUntilFinished_ThenReturnsToLocomotion()
        {
            FullBodyStateMachine machine = BuildMachine(out PlayerRuntimeData data, out _);

            data.Survivability.JustTookDamage = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Hurt, machine.CurrentState.Type);

            // 事件是單幀的：下一幀起就沒有新的受擊。
            data.Survivability.JustTookDamage = false;
            machine.Tick(data, 0.1f);
            Assert.AreEqual(StateType.Hurt, machine.CurrentState.Type, "硬直未結束前不得離開");

            machine.Tick(data, 5f);
            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type, "硬直結束後回到 locomotion");
        }

        /// <summary>硬直中再次受擊要再踉蹌一次（Animancer 範例的 `CanInterruptSelf => true` 同形）。</summary>
        [Test]
        public void HD3_HurtCanReenterItself()
        {
            FullBodyStateMachine machine = BuildMachine(out PlayerRuntimeData data, out _);
            BaseState hurt = GetState(machine, StateType.Hurt);

            data.Survivability.JustTookDamage = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Hurt, machine.CurrentState.Type);

            Assert.IsTrue(hurt.CanReenter(data), "同一幀仍有受擊事件 ⇒ 可重入");

            data.Survivability.JustTookDamage = false;
            Assert.IsFalse(hurt.CanReenter(data), "沒有新的受擊事件就不得重入");
        }

        // =====================================================================
        // Action ↔ Hurt ↔ Death（使用者點名要驗的核心）
        // =====================================================================

        /// <summary>普通（可中斷）Action 被 Hurt 打斷。</summary>
        [Test]
        public void HD4_InterruptibleAction_IsInterruptedByHurt()
        {
            FullBodyStateMachine machine = BuildMachine(
                out PlayerRuntimeData data, out _, actionInterruptible: true);

            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Action, machine.CurrentState.Type);

            data.Intent.Reset();
            data.Survivability.JustTookDamage = true;
            machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Hurt, machine.CurrentState.Type,
                "可中斷 phase 應被受擊打斷");
        }

        /// <summary>
        /// 🔴 `Interruptible: false` 的 phase 擋住 Hurt——**但擋不住 Death**。
        /// 這兩件事走 `ActionState.CanBeInterruptedBy` 的不同分支，必須分別驗。
        /// </summary>
        [Test]
        public void HD5_UninterruptibleAction_BlocksHurtButNotDeath()
        {
            FullBodyStateMachine machine = BuildMachine(
                out PlayerRuntimeData data, out _, actionInterruptible: false);

            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Action, machine.CurrentState.Type);

            // ① 受擊打不斷
            data.Intent.Reset();
            data.Survivability.JustTookDamage = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Action, machine.CurrentState.Type,
                "Interruptible: false ⇒ 普通受擊不得打斷（現有 phase interruption gate）");

            // ② 死亡打得斷
            data.Survivability.JustTookDamage = false;
            data.Survivability.IsDead = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Death, machine.CurrentState.Type,
                "打不斷不等於打不死：Death 的授權來自 config 的 state-level 清單");
        }

        [Test]
        public void HD6_HurtCanTransitionToDeath()
        {
            FullBodyStateMachine machine = BuildMachine(out PlayerRuntimeData data, out _);

            data.Survivability.JustTookDamage = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Hurt, machine.CurrentState.Type);

            data.Survivability.JustTookDamage = false;
            data.Survivability.IsDead = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Death, machine.CurrentState.Type);
        }

        /// <summary>
        /// **HD7 — 死著的時候，什麼都拉不出 Death。**
        ///
        /// <para>🔄 2026-09-14 改寫（respawn，ADR-009 D3 修訂）。</para>
        /// 原本這條連「`IsDead` 變回 false」都一併斷言為出不去——理由是
        /// 「復活是一個尚未設計的流程，不能靠 Death 忘了鎖門而意外可行」。
        /// 復活現在**設計好了**，而且它正是唯一的出口。⇒ 那一半移到 <c>HD9</c>。
        ///
        /// **本條守的仍然是原本不變量的核心，一字未弱**：
        /// 跳躍、翻滾、出手、受擊、external request——**在角色還是死的時候**，
        /// 沒有任何一條能把他拉起來。`CanBeInterruptedBy` 留空這一層完全沒有被碰過。
        /// </summary>
        [Test]
        public void HD7_WhileStillDead_NothingCanPullTheCharacterOut()
        {
            FullBodyStateMachine machine = BuildMachine(out PlayerRuntimeData data, out _);

            data.Survivability.IsDead = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Death, machine.CurrentState.Type);

            // 仍然是死的；同時對每一條可能的出口施壓。
            data.Survivability.JustTookDamage = true;
            data.Intent.JumpRequested = true;
            data.Intent.RollRequested = true;
            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            for (int i = 0; i < 5; i++) machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Death, machine.CurrentState.Type,
                "死著的時候 Death 是吸收態：空的 CanBeInterruptedBy 擋掉所有中斷，" +
                "CanTransitionAway 也因為 IsDead 仍為 true 而不放行");
        }

        /// <summary>
        /// **HD9 — 唯一的出口是「已經不是死的」。**
        ///
        /// <para>🆕 2026-09-14（respawn；使用者裁決）。</para>
        /// <c>CanEnter => IsDead</c> 與 <c>CanTransitionAway => !IsDead</c> **對稱**：
        /// 進門與出門問的是同一個已 commit 的事實，只是方向相反。
        ///
        /// ⚠️ 這裡刻意**直接改黑板**來模擬「<c>CharacterHealth.Revive()</c> 已經 commit 並發布」。
        /// 誰有權改那個欄位由 `WriterRules` 守（唯一寫入者是 `CharacterHealth`）；
        /// 本條只驗 FSM 對「已經活了」的反應。
        ///
        /// <para><b>同一幀生效，不延遲一幀</b></para>
        /// `FullBodyStateMachine.Tick` 的順序是 `OnTick` → `EvaluateInterrupts` → `EvaluateTransitions`，
        /// 而 `DeathState` 在 `OnTick` 抄 `IsDead` ⇒ 同一次 Tick 就走得出去。
        /// </summary>
        [Test]
        public void HD9_RevivePublished_ReleasesDeathOnTheSameTick()
        {
            FullBodyStateMachine machine = BuildMachine(out PlayerRuntimeData data, out _);

            data.Survivability.IsDead = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Death, machine.CurrentState.Type, "測試前提：先真的死掉");

            // 模擬 CharacterHealth.Revive() 之後、順序 0.5 發布出來的那一幀。
            data.Survivability.IsDead = false;
            machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type,
                "IsDead 一旦變回 false，Death 必須在**同一次 Tick** 內放行到 Idle");
        }

        /// <summary>
        /// **HD10 — 重生後只回得到 Idle／Move，回不到 Action／Jump／Roll。**
        ///
        /// 使用者 2026-09-14 明確裁決：「Death 的正常出口保持最小，第一版只回 Idle/Move」。
        /// 直接開那些邊會讓「重生瞬間就能出手」變成一個沒人設計過的能力——
        /// 而且它會**看起來像 bug**：按著攻擊鍵等重生，角色一活過來就揮了一拳。
        ///
        /// 本條刻意在復活的**同一幀**把所有 request 都按著，證明它們不會被 Death 的出口消費。
        /// </summary>
        [Test]
        public void HD10_ReviveWithButtonsHeld_StillLandsInAmbientStateFirst()
        {
            FullBodyStateMachine machine = BuildMachine(out PlayerRuntimeData data, out _);

            data.Survivability.IsDead = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Death, machine.CurrentState.Type);

            // 復活那一幀，玩家正按著所有東西。
            data.Survivability.IsDead = false;
            data.Intent.JumpRequested = true;
            data.Intent.RollRequested = true;
            data.Intent.RequestedActionSlot = ActionSlot.Slot1;
            machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Idle, machine.CurrentState.Type,
                "Death 的 ValidTransitions 只有 Idle／Move ⇒ 離開 Death 的那一幀必定先落在 ambient state；" +
                "要跳要打要滾，從 Idle 走既有路徑");
        }

        // =====================================================================
        // ⭐ permission ≠ priority
        // =====================================================================

        /// <summary>
        /// 🔴 **使用者 2026-09-14 明確要求的護欄。**
        ///
        /// 「Roll 不被 Hurt 打斷」必須由 `CanBeInterruptedBy` 表示。
        /// 本測試把 **Hurt 的 priority 拉到比 Roll 還高**——若某天有人把政策改成靠 priority 比大小，
        /// 這條會立刻紅。permission 先過，priority 才輪得到上場。
        /// </summary>
        [Test]
        public void HD8_RollBlocksHurtByPermission_NotByPriority()
        {
            StateMachineConfigSO config = BuildConfig(
                actionInterruptible: true,
                hurtPriority: 999);            // ⚠️ 刻意高於 Roll(20) 與 Action(10)
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.RollRequested = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Roll, machine.CurrentState.Type);

            data.Intent.Reset();
            data.Survivability.JustTookDamage = true;
            machine.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Roll, machine.CurrentState.Type,
                "Roll 擋 Hurt 必須來自 config 的 CanBeInterruptedBy——" +
                "即使 Hurt priority 高於 Roll 也不得進入。priority 只在多個 transition 都合法時才決定誰贏。");

            // 對照組：同一個 Roll 仍然可以被 Death 打斷（Death 在 Roll 的清單裡）。
            data.Survivability.JustTookDamage = false;
            data.Survivability.IsDead = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Death, machine.CurrentState.Type,
                "permission 有給就進得去——證明擋住 Hurt 的不是 priority");
        }

        /// <summary>
        /// Roll 中受擊：**只扣血、不播 Hurt、也不補播**。
        /// 傷害成立（`CurrentHealth` 有變）、表現被丟棄（翻滾結束後不會抽動一下）。
        /// </summary>
        [Test]
        public void HD9_HitDuringRoll_DropsTheFlinchWithoutQueueing()
        {
            StateMachineConfigSO config = BuildConfig(actionInterruptible: true);
            var data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());

            data.Intent.RollRequested = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Roll, machine.CurrentState.Type);

            // 受擊事件只存在這一幀（CharacterHealth 發布後即清）。
            data.Intent.Reset();
            data.Survivability.JustTookDamage = true;
            machine.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Roll, machine.CurrentState.Type);

            // 之後的每一幀都沒有新的受擊事件 ⇒ 翻滾結束後不得補播。
            data.Survivability.JustTookDamage = false;
            for (int i = 0; i < 60; i++) machine.Tick(data, 0.05f);

            Assert.AreNotEqual(StateType.Hurt, machine.CurrentState.Type,
                "被無敵幀擋掉的受擊必須被丟棄——翻滾結束後補播一次踉蹌是錯的");
        }

        // =====================================================================
        // helpers
        // =====================================================================

        private FullBodyStateMachine BuildMachine(
            out PlayerRuntimeData data,
            out StateMachineConfigSO config,
            bool actionInterruptible = true)
        {
            config = BuildConfig(actionInterruptible);
            data = new PlayerRuntimeData { IsGrounded = true };
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel());
            return machine;
        }

        /// <summary>
        /// 複製正式 config 應有的形狀。Traversal 刻意省略——本檔驗的是 Hurt／Death 的機制，
        /// traversal 的 permission 由 `A38` 在**出貨資產**上守。
        /// </summary>
        private StateMachineConfigSO BuildConfig(bool actionInterruptible, int hurtPriority = 15)
        {
            ActionDefinitionSO attack = ScriptableObject.CreateInstance<ActionDefinitionSO>();
            _created.Add(attack);
            attack.Slot = ActionSlot.Slot1;
            attack.Cooldown = 0f;
            attack.RequiresGrounded = false;
            attack.Phases = new[]
            {
                new ActionPhaseEntry
                {
                    Phase = ActionPhase.Start,
                    AnimationKey = "TestAttack",
                    FallbackDuration = 10f,   // 夠長，測試期間不會自然結束
                    Interruptible = actionInterruptible
                }
            };

            var config = ScriptableObject.CreateInstance<StateMachineConfigSO>();
            _created.Add(config);
            SetPrivateField(config, "rules", new List<StateRule>
            {
                Rule(StateType.Idle, 0,
                     new[] { StateType.Roll, StateType.Jump, StateType.Action, StateType.Hurt, StateType.Death },
                     new[] { StateType.Move }),
                Rule(StateType.Move, 0,
                     new[] { StateType.Jump, StateType.Roll, StateType.Action, StateType.Hurt, StateType.Death },
                     new[] { StateType.Idle }),
                Rule(StateType.Jump, 10,
                     new[] { StateType.Hurt, StateType.Death },
                     new[] { StateType.Move, StateType.Idle }),
                // ⭐ Roll 的清單刻意**不含 Hurt**——這就是「不允許」的表示方式。
                Rule(StateType.Roll, 20,
                     new[] { StateType.Death },
                     new[] { StateType.Move, StateType.Idle }),
                Rule(StateType.Action, 10,
                     new[] { StateType.Roll, StateType.Jump, StateType.Hurt, StateType.Death },
                     new[] { StateType.Move, StateType.Idle }),
                Rule(StateType.Hurt, hurtPriority,
                     new[] { StateType.Hurt, StateType.Death },
                     new[] { StateType.Move, StateType.Idle }),
                // 🔄 2026-09-14（respawn，ADR-009 D3 修訂）：ValidTransitions 由空改為 Idle／Move。
                //    CanBeInterruptedBy **仍然留空**——沒有任何狀態「打斷」死亡，
                //    是死亡這個前提本身消失了，所以離開走的是自然過渡而不是中斷。
                Rule(StateType.Death, 100, new StateType[0], new[] { StateType.Idle, StateType.Move }),
            });
            SetPrivateField(config, "paramsMappings", new List<StateParamsMapping>());
            SetPrivateField(config, "actionDefinitions", new List<ActionDefinitionSO> { attack });
            return config;
        }

        private static StateRule Rule(
            StateType state, int priority, StateType[] interruptedBy, StateType[] transitions)
            => new StateRule
            {
                State = state,
                Priority = priority,
                CanBeInterruptedBy = new List<StateType>(interruptedBy),
                ValidTransitions = new List<StateType>(transitions)
            };

        private static BaseState GetState(FullBodyStateMachine machine, StateType type)
        {
            FieldInfo field = typeof(FullBodyStateMachine).GetField(
                "_stateRegistry", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "找不到 FullBodyStateMachine._stateRegistry");
            var registry = (Dictionary<StateType, BaseState>)field.GetValue(machine);
            Assert.IsTrue(registry.ContainsKey(type), $"registry 缺少 {type}");
            return registry[type];
        }

        private static void SetPrivateField<T>(StateMachineConfigSO config, string name, T value)
        {
            FieldInfo field = typeof(StateMachineConfigSO).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 StateMachineConfigSO.{name}");
            field.SetValue(config, value);
        }
    }
}
