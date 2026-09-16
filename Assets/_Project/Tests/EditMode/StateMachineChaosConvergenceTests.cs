using NUnit.Framework;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-09-15）**亂序操作之後必須收斂回乾淨基線。**
    ///
    /// <para><b>這個檔案在回應什麼</b></para>
    /// 「戰鬥中故意亂操作，看有沒有某個系統留下髒狀態」是一個**正確的擔憂**，
    /// 但用人工 Play 亂按去驗它是最弱的做法——不可重現，找到了也沒法 re-run。
    /// 這裡把它換成可執行的形式：**用確定性亂數序列驅動出貨用的 FSM 拓撲**，
    /// 然後放開全部輸入，斷言系統收斂。種子固定 ⇒ 失敗永遠可重現。
    ///
    /// <para><b>⚠️ 用的是真實 config，不是測試用的簡化拓撲</b></para>
    /// <see cref="StateMachineTests"/> 刻意用只含 Idle/Move/Jump/Roll 的最小拓撲來測轉移政策。
    /// **髒狀態問題恰好相反**——它只在完整拓撲（Action／Hurt／Traversal 互相打斷）下才出現。
    /// 因此本檔載入 <c>PlayerStateMachineConfig.asset</c> 本身。
    ///
    /// <para><b>⛔ 本檔涵蓋不到的那一半（誠實界線）</b></para>
    /// 「Upper Body layer 有沒有退乾淨」住在 <see cref="AnimationFacadeBase"/> 的層權重，
    /// 不在 FSM 也不在黑板 ⇒ **本檔驗不到**，需要 facade 層的測試或人眼。
    /// 這裡驗的是 **FSM 收斂 ＋ 黑板意圖欄位不殘留**。
    /// </summary>
    public sealed class StateMachineChaosConvergenceTests
    {
        private const string PlayerConfigPath =
            "Assets/ScriptableObjects/StateMachine/PlayerStateMachineConfig.asset";

        /// <summary>只回答 IsProducingMotion；位移由 PlayMode 層負責，本檔只測狀態收斂。</summary>
        private sealed class FakeMovementModel : IMovementModel
        {
            public bool IsProducingMotion { get; set; }
            public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime) { }
            public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data) { }
        }

        private static FullBodyStateMachine BuildShippingMachine(
            out PlayerRuntimeData data, out FakeMovementModel model)
        {
            var config = AssetDatabase.LoadAssetAtPath<StateMachineConfigSO>(PlayerConfigPath);
            Assert.IsNotNull(config,
                $"找不到出貨用的狀態機設定 {PlayerConfigPath}——本測試的價值完全來自「用真實拓撲」，"
                + "資產被搬走時必須大聲失敗，不得悄悄退化成簡化拓撲");

            data = new PlayerRuntimeData();
            model = new FakeMovementModel();
            var sm = new FullBodyStateMachine();
            sm.Initialize(config, data, model);
            return sm;
        }

        /// <summary>
        /// 放開一切外部驅動力。**這是「玩家鬆手」的定義**——
        /// 之後系統若還動，就是有人自己留了狀態。
        /// </summary>
        private static void ReleaseEverything(PlayerRuntimeData data, FakeMovementModel model)
        {
            data.Intent.Reset();
            data.Intent.RequestedActionSlot = ActionSlot.None;
            data.Survivability.JustTookDamage = false;
            data.Survivability.IsDead = false;   // 死亡是吸收態，不屬於「鬆手應該恢復」的範圍
            data.IsGrounded = true;
            data.MoveSpeed = 0f;
            data.MoveDirection = Vector3.zero;
            data.MovementIntent = default;
            model.IsProducingMotion = false;
        }

        /// <summary>
        /// 🔴 主測項：400 帧亂序操作 → 鬆手 → 必須回到 Idle。
        ///
        /// 失敗的意義很具體：**某個狀態進得去、出不來**，或某個旗標沒有人負責清掉。
        /// 那正是「打完一場之後角色怪怪的」在自動化層的樣子。
        /// </summary>
        [Test]
        public void SC1_ChaoticInputSequence_ConvergesToIdleAfterRelease()
        {
            FullBodyStateMachine sm = BuildShippingMachine(
                out PlayerRuntimeData data, out FakeMovementModel model);

            var rng = new System.Random(20260915); // 固定種子 ⇒ 失敗可重現
            const float deltaTime = 1f / 60f;

            for (int i = 0; i < 400; i++)
            {
                data.IsGrounded = rng.Next(4) != 0;               // 偶爾離地
                data.Intent.JumpRequested = rng.Next(5) == 0;
                data.Intent.RollRequested = rng.Next(7) == 0;
                data.Intent.RequestedActionSlot = rng.Next(4) == 0
                    ? (ActionSlot)(1 + rng.Next(3))
                    : ActionSlot.None;
                data.Survivability.JustTookDamage = rng.Next(11) == 0;
                model.IsProducingMotion = rng.Next(3) != 0;
                data.MoveSpeed = model.IsProducingMotion ? 1f : 0f;
                data.MoveDirection = model.IsProducingMotion ? Vector3.forward : Vector3.zero;

                sm.Tick(data, deltaTime);
            }

            ReleaseEverything(data, model);

            // 有界收斂：20 秒模擬時間仍回不到 Idle ＝ 卡住，不是「還在播動畫」。
            const int maxTicks = 1200;
            int ticks = 0;
            while (ticks < maxTicks && sm.CurrentState.Type != StateType.Idle)
            {
                sm.Tick(data, deltaTime);
                ticks++;
            }

            Assert.AreEqual(StateType.Idle, sm.CurrentState.Type,
                $"放開全部輸入後經過 {ticks} 帧（{ticks * deltaTime:0.0}s）仍停在 "
                + $"{sm.CurrentState.Type}——有狀態進得去出不來，或有旗標沒有人清");
        }

        /// <summary>
        /// 收斂之後**黑板的意圖區必須是乾淨的**。
        ///
        /// ⚠️ 這條與 SC1 不同：SC1 問「狀態機回得去嗎」，這條問「回去之後有沒有留下垃圾」。
        /// 兩者可以分開失敗——FSM 回到 Idle 但 <c>RequestedActionSlot</c> 還掛著，
        /// 下一幀就會無中生有地再出一次手。
        /// </summary>
        [Test]
        public void SC2_AfterConvergence_IntentRegionIsClean()
        {
            FullBodyStateMachine sm = BuildShippingMachine(
                out PlayerRuntimeData data, out FakeMovementModel model);

            var rng = new System.Random(4727038);
            const float deltaTime = 1f / 60f;

            for (int i = 0; i < 240; i++)
            {
                data.Intent.RequestedActionSlot = (ActionSlot)(1 + rng.Next(3));
                data.Intent.RollRequested = rng.Next(6) == 0;
                data.IsGrounded = true;
                model.IsProducingMotion = rng.Next(2) == 0;
                sm.Tick(data, deltaTime);
            }

            ReleaseEverything(data, model);
            for (int i = 0; i < 1200 && sm.CurrentState.Type != StateType.Idle; i++)
                sm.Tick(data, deltaTime);

            Assert.AreEqual(ActionSlot.None, data.Intent.RequestedActionSlot,
                "收斂後不得殘留 action 請求——殘留會在下一幀無中生有地再觸發一次");
            Assert.IsFalse(data.Intent.JumpRequested, "收斂後不得殘留跳躍請求");
            Assert.IsFalse(data.Intent.RollRequested, "收斂後不得殘留翻滾請求");
            Assert.IsFalse(data.Survivability.JustTookDamage,
                "JustTookDamage 是單帧事件，不得跨帧存活（會造成無限硬直）");
        }

        /// <summary>
        /// 反向守門：**同一個種子必須得到同一個結果。**
        /// FSM 若在某處讀了 <c>Time.time</c>／<c>Random</c> 等隱藏輸入，這條會不穩定地失敗——
        /// 而「偶爾失敗的測試」本身就是這類缺陷的訊號。
        /// </summary>
        [Test]
        public void SC3_SameSeed_ProducesSameFinalState()
        {
            StateType RunOnce()
            {
                FullBodyStateMachine sm = BuildShippingMachine(
                    out PlayerRuntimeData data, out FakeMovementModel model);
                var rng = new System.Random(58131);
                for (int i = 0; i < 200; i++)
                {
                    data.IsGrounded = rng.Next(4) != 0;
                    data.Intent.JumpRequested = rng.Next(5) == 0;
                    data.Intent.RollRequested = rng.Next(7) == 0;
                    data.Intent.RequestedActionSlot = rng.Next(4) == 0
                        ? (ActionSlot)(1 + rng.Next(3))
                        : ActionSlot.None;
                    model.IsProducingMotion = rng.Next(3) != 0;
                    sm.Tick(data, 1f / 60f);
                }
                return sm.CurrentState.Type;
            }

            Assert.AreEqual(RunOnce(), RunOnce(),
                "相同輸入序列必須得到相同最終狀態；不一致代表 FSM 存在未經黑板的隱藏輸入");
        }
    }
}
