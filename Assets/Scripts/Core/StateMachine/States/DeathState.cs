using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// 死亡（ADR-009 D3）。**死著的時候是吸收態；唯一的出口是「不再是死的」。**
    ///
    /// <para><b>三層保證</b></para>
    /// <list type="number">
    /// <item><b>進得去</b>：<see cref="CanEnter"/> 只看黑板已 commit 的 <c>IsDead</c>，
    /// 且 config 給它最高 <c>Priority</c> ⇒ <c>EvaluateInterrupts</c> 一定選中它。</item>
    /// <item><b>出不來（自然過渡）</b>：<see cref="CanTransitionAway"/> **只在
    /// <c>IsDead == false</c> 時放行**，且 config 的 <c>ValidTransitions</c> 只列 Idle／Move。</item>
    /// <item><b>出不來（被中斷）</b>：config 資產的 <c>CanBeInterruptedBy</c> **仍然留空**
    /// ⇒ <c>base.CanBeInterruptedBy</c> 對任何來源都回 false。</item>
    /// </list>
    /// ⚠️ 三層分別擋掉三條**不同**的路徑。只做其中一層會留下另外兩個缺口——
    /// 例如只靠 Priority，`EvaluateTransitions` 仍會在下一幀把角色換回 Idle。
    ///
    /// <para><b>🔄 2026-09-14 修訂（respawn；ADR-009 D3 修訂紀錄）</b></para>
    /// 第 ② 層由「恆 false」改為「<c>!IsDead</c>」，第 ①③ 層**一字未動**。
    ///
    /// <b>不變量的原意完整保留</b>：跳躍、翻滾、出手、受擊、任何 external request——
    /// 全都**仍然**拉不出 Death。能把角色救出來的只有
    /// <c>CharacterHealth.Revive()</c>（`IsDead` 的唯一寫入者）。
    /// ⇒ 這不是「把門打開」，是**讓門的條件與進門的條件對稱**：
    /// <c>CanEnter => IsDead</c>／<c>CanTransitionAway => !IsDead</c>。
    ///
    /// <b>為什麼離開走「自然過渡」而不是「中斷」</b>：中斷的語意是「別的狀態比我更該發生、
    /// 把我打斷」。沒有任何狀態「打斷」死亡——是死亡這個**前提本身**消失了。
    /// 所以 <c>CanBeInterruptedBy</c> 繼續留空，這一層的鎖沒有被碰過。
    ///
    /// <b>第一版只回 Idle／Move</b>：⛔ 不直接允許 Death → Action／Jump／Roll
    /// （使用者裁決）。重生後要跳要打，從 Idle 走既有路徑即可；
    /// 直接開那些邊會讓「重生瞬間就能出手」變成一個沒人設計過的能力。
    ///
    /// <para><b>位移：只有重力，沒有水平速度</b></para>
    /// 死亡不等於凍結在半空 ⇒ 重力仍要結算，屍體要落到地面。
    /// 但**水平速度必須立刻消失**。
    ///
    /// 🔴 **2026-09-14 修正**：原本沿用 <c>BaseState</c> 的預設（<c>ExecuteBaseMovement</c>），
    /// 它讀 <c>MoveSpeed × MoveDirection</c>。死亡雖然會經 <c>DeathArbiterSource</c> 歸零輸入，
    /// 但 <c>BlockInput</c> 有**一幀延遲**，而且 `LocomotionModel` 的 B9 減速還要花時間把速度收到 0
    /// ⇒ **全速奔跑時中彈會滑行一段距離才停**。
    /// 改用 <c>ExecuteVerticalOnlyMovement</c>：那是 `MotionDriver` **既有**的能力
    /// （首個使用者是 `JumpState` 的 HardRecovery），語意正好是「只結算朝向、重力、接地，
    /// 不施加 Movement Output 的水平速度」。
    /// ⇒ 水平速度在進入 Death 的**那一幀**就消失，不需要任何 magic correction，也沒有動到 locomotion。
    ///
    /// ⛔ 刻意**不**呼叫 <c>MovementModel.UpdateMotion</c>：那是 ambient locomotion 的路徑，
    /// 死亡不是 locomotion 的一種。
    /// </summary>
    public sealed class DeathState : BaseState
    {
        public override StateType Type => StateType.Death;

        /// <summary>
        /// 只讀已 commit 的結果，**不重新推導** <c>CurrentHealth &lt;= 0</c>
        /// （`docs/16` 點名的加重缺陷：下游重新推導已 commit 的狀態）。
        /// </summary>
        public override bool CanEnter(PlayerRuntimeData data) => data != null && data.Survivability.IsDead;

        /// <summary>
        /// **只有「已經不是死的」才放行。**
        ///
        /// ⚠️ <c>CanTransitionAway</c> 是無參數屬性（`BaseState` 的既有形狀，六個狀態共用），
        /// 拿不到黑板 ⇒ 旗標在 <see cref="OnTick"/> 內從已 commit 的 <c>IsDead</c> 抄一次。
        /// 這與 <c>HurtState._isFinished</c> **是同一個既有慣例**，不是為了 respawn 發明的機制；
        /// 也因此**不需要**為了取得 data 去改 `BaseState` 的介面形狀（那會動到六個 state）。
        ///
        /// 時序安全：`FullBodyStateMachine.Tick` 的順序是
        /// <c>OnTick</c> → <c>EvaluateInterrupts</c> → <c>EvaluateTransitions</c>
        /// ⇒ 同一幀抄到的值同一幀就生效，沒有延遲一幀的問題。
        ///
        /// ⛔ **抄的是已 commit 的結果，不是重新推導** <c>CurrentHealth &gt; 0</c>
        /// （`docs/16` 點名的加重缺陷）。
        /// </summary>
        public override bool CanTransitionAway => _reviveCommitted;

        // 本幀觀察到的「已經不是死的」。⚠️ 不是跨幀狀態的累積，每幀由 OnTick 整體覆寫。
        private bool _reviveCommitted;

        public override void OnEnter(PlayerRuntimeData data)
        {
            // 進場時一律視為仍死著：避免沿用上一次死亡週期的殘值，在進入的那一幀就放行。
            _reviveCommitted = false;

#if UNITY_EDITOR
            // 富文本字串有 GC Alloc，比照 IdleState／JumpState 慣例包進 UNITY_EDITOR。
            // 死亡一次只會發生一次，不是熱路徑。
            Debug.Log("<color=red>[State] 進入 DEATH 狀態（吸收態）</color>");
#endif
        }

        /// <summary>抄一次已 commit 的死亡判定。⛔ 不重新推導、不寫任何黑板欄位。</summary>
        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            _reviveCommitted = data != null && !data.Survivability.IsDead;
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            _reviveCommitted = false;
        }

        public override void OnUpdateMotion(
            MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            // 只有重力與接地。水平速度在進入 Death 的那一幀就消失——不等 BlockInput 的一幀延遲，
            // 也不等 B9 減速把 MoveSpeed 收到 0（見類別註解）。
            motionDriver.ExecuteVerticalOnlyMovement(data);
        }
    }
}
