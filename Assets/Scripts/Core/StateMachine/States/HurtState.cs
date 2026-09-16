using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// 受擊硬直（`docs/26` Model B，2026-09-14 使用者裁決）。
    /// **被動反應，不是主動行動**——它的觸發者永遠是別人。
    ///
    /// <para><b>形狀照抄 <see cref="RollState"/>，但位移路徑刻意不同</b></para>
    /// <list type="bullet">
    /// <item>動畫鍵＝<c>BaseState.AnimationKey</c> 的預設（<c>Type.ToString()</c> ＝ <c>"Hurt"</c>）</item>
    /// <item><b>時長</b>＝<c>Config.GetBakeData(StateType.Hurt)</c> 的 <c>Duration</c>
    ///       （⇒ 秒數住在資產，不在程式；查無資料才退化為 <see cref="FallbackDuration"/>）</item>
    /// <item><b>位移</b>＝<c>MotionDriver.ExecuteVerticalOnlyMovement</c>——**只有重力與接地，沒有水平速度**</item>
    /// </list>
    ///
    /// <para><b>🔴 為什麼位移不走 <c>ExecuteBakedCurveMovement</c>（2026-09-14 實測後的決定）</b></para>
    /// `Fists_Hit_Right` 這支 clip **本身帶 0.84 m 的後退站穩位移**，
    /// `Bake_Fists_Hit_Right` 也忠實地把它烘了進去（峰值 2.27 m/s）。但那份資料**不能拿來驅動 gameplay**：
    /// <list type="number">
    /// <item><b>方向是錯的。</b> 烘焙器用 <c>Vector3.Distance</c> 取速度 ⇒ **無號**；
    ///       而 <c>ExecuteBakedCurveMovement</c> 沿 <c>transform.forward</c> 積分
    ///       ⇒ 一支「往後踉蹌」的 clip 會把角色**往前推**，以最高 2.27 m/s 撞進攻擊者。</item>
    /// <item><b>量級也是錯的。</b> 0.84 m 的後退屬 **Knockback**，而 knockback 明確不在本輪範圍
    ///       （使用者 2026-09-14 裁決）。受擊第一版是**原地硬直**。</item>
    /// </list>
    /// ⚠️ 這不是「在 state 裡補一個 offset」——恰恰相反，是**不消費**那份不適用的資料，
    /// 改用 `MotionDriver` 既有的「只結算垂直」能力來表達「硬直期間沒有水平位移」。
    /// clip 的視覺後退仍然存在（匯入設定已把 XZ **Bake Into Pose**，
    /// 見 dev-spec §0.4「執行期用不到的成分一律 Bake Into Pose」），只是不再推動膠囊。
    ///
    /// ⇒ **共用底層 animation/motion execution，但不共用 Action semantic。**
    /// 這正是本 repo 既有的作法（`RollState` 從來就不是 `ActionState`），不是新發明。
    ///
    /// <para><b>准入：黑板，不是 mailbox</b></para>
    /// 唯一來源是 <c>data.Survivability.JustTookDamage</c>（<c>CharacterHealth</c> 於順序 0.5 發布），
    /// 與 <see cref="DeathState"/> 讀 <c>IsDead</c> **完全對稱**。
    /// ⛔ 本類別**不認識** <c>ActionRequestTarget</c>／<c>ActionSlot</c>／傷害數值／攻擊者。
    ///
    /// <para><b>⚠️ 「能不能進」不由本類別決定</b></para>
    /// 本類別只回答「**有沒有事要做**」。「Roll／Traversal 期間不得被 Hurt 打斷」是
    /// **transition permission**，明確住在 config 的 <c>CanBeInterruptedBy</c> 清單裡，
    /// **不是**靠 priority 比大小得到的副作用（使用者 2026-09-14 明確要求兩者分離）。
    /// </summary>
    public sealed class HurtState : BaseState
    {
        /// <summary>查無烘焙資料時的安全退化時長（秒）。與 <see cref="RollState"/> 同一種防線。</summary>
        private const float FallbackDuration = 0.4f;

        private MotionBakeData _hurtBakeData;
        private float _timer;
        private bool _isFinished;

        public override StateType Type => StateType.Hurt;

        /// <summary>硬直結束前不得自然離開；被更高權限的狀態（Death）搶佔是另一條路徑。</summary>
        public override bool CanTransitionAway => _isFinished;

        public override void Initialize(StateMachineConfigSO config, IMovementModel movementModel)
        {
            base.Initialize(config, movementModel);
            _hurtBakeData = config != null ? config.GetBakeData(Type) : null;

#if UNITY_EDITOR
            // 比照 RollState 的斷鏈防線＋M2 Warning 治理：只在 Play 中鳴響。
            // EditMode 測試以最小拓撲 config（無 bakeMappings）組裝是合法輸入，不是斷鏈。
            if (_hurtBakeData == null && Application.isPlaying)
            {
                Debug.LogWarning(
                    $"[HurtState] 查無 {Type} 的 MotionBakeData（StateMachineConfig 的 bakeMappings 未綁定 State: Hurt）。" +
                    $"受擊將退化為固定 {FallbackDuration}s 計時、且不套用烘焙曲線位移（原地硬直）。" +
                    "請把 Bake_Fists_Hit_Right 綁到 bakeMappings 的 Hurt 項。");
            }

            if (_hurtBakeData != null && _hurtBakeData.Duration <= 0f && Application.isPlaying)
            {
                Debug.LogWarning(
                    $"[HurtState] {Type} 的 MotionBakeData（{_hurtBakeData.name}）BakedDuration 為 0——" +
                    $"受擊已安全退化為固定 {FallbackDuration}s 計時。請重跑一次烘焙寫入正確時長。");
            }
#endif
        }

        /// <summary>
        /// 只讀 <c>CharacterHealth</c> 已 commit 的單幀事件，**不重新推導**「血有沒有變少」。
        /// ⚠️ 純讀取、無副作用（對照 <c>JumpState.CanEnter</c> 會寫自己的 ungrounded 計時）。
        /// </summary>
        public override bool CanEnter(PlayerRuntimeData data)
            => data != null && data.Survivability.JustTookDamage;

        /// <summary>
        /// **硬直中再次受擊要再踉蹌一次**——那正是硬直的定義。
        /// 判準與 <see cref="CanEnter"/> **同源**（同一個欄位），不引入第二個准入權威。
        /// 對應 Animancer 官方 FSM 範例的 <c>FlinchState.CanInterruptSelf => true</c>。
        /// </summary>
        public override bool CanReenter(PlayerRuntimeData data)
            => data != null && data.Survivability.JustTookDamage;

        public override void OnEnter(PlayerRuntimeData data)
        {
#if UNITY_EDITOR
            // 富文本字串有 GC Alloc，比照 IdleState／RollState 慣例包進 UNITY_EDITOR。
            Debug.Log("<color=orange>[State] 進入 HURT 硬直</color>");
#endif
            // 與 RollState 同樣以「值本身」而非「引用」判定退化條件：
            // 資產存在但 Duration 為 0 時，_timer 會是 0、硬直第一幀就結束。
            float bakedDuration = _hurtBakeData != null ? _hurtBakeData.Duration : 0f;
            _timer = bakedDuration > 0f ? bakedDuration : FallbackDuration;
            _isFinished = false;
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (_isFinished) return;

            _timer -= deltaTime;
            if (_timer <= 0f) _isFinished = true;
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            _timer = 0f;
            _isFinished = false;
        }

        /// <summary>
        /// 硬直期間**只結算重力與接地，不施加任何水平速度**（理由見類別註解）。
        ///
        /// ⚠️ 刻意**不**呼叫 <c>ExecuteBaseMovement</c>：那會照 <c>MoveSpeed × MoveDirection</c> 繼續走，
        /// 玩家就能一邊踉蹌一邊正常移動，硬直等於不存在。
        /// ⚠️ 也刻意**不**呼叫 <c>ExecuteBakedCurveMovement</c>：那份 bake 的位移方向與量級都不適用。
        ///
        /// 本路徑與 <see cref="DeathState"/> 相同，且 <c>ExecuteVerticalOnlyMovement</c> 是
        /// `MotionDriver` **既有**能力（首個使用者是 `JumpState` 的 HardRecovery），不是為受擊新增的。
        /// </summary>
        public override void OnUpdateMotion(
            MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            motionDriver.ExecuteVerticalOnlyMovement(data);
        }
    }
}
