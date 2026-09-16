using Project.Core.Blackboard;
using Project.Core.StateMachine;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.Facing
{
    /// <summary>
    /// 每個角色唯一的 facing request 送出者。優先序與「本幀是否需要轉」都在此一次解完，
    /// MotionDriver 只消費方向並維持 rotation 單一寫入者。
    /// 持續 combat target 佔第二順位，是否啟用由每個 actor 的序列化政策決定。
    /// </summary>
    public sealed class CharacterFacingSource : MonoBehaviour
    {
        private const float DirectionSqrEpsilon = 0.000001f;

        [SerializeField] private float facingAngleDeadzone = 8f;
        // Actor policy 留在 prefab；預設 false 保證玩家的 facing 路徑不變，敵人再顯式啟用。
        [SerializeField] private bool usePersistentCombatFacing = false;

        private MotionDriver _motionDriver;
        private FullBodyStateMachine _stateMachine;

        public void Initialize(MotionDriver motionDriver, FullBodyStateMachine stateMachine)
        {
            _motionDriver = motionDriver;
            _stateMachine = stateMachine;
        }

        /// <summary>管線順序 4.6：FSM 已確定本幀 Action 承諾，LateUpdate 尚未消費 request。</summary>
        public void Tick(PlayerRuntimeData data)
        {
            if (_motionDriver == null || data == null) return;

            // 死亡是 facing 的**否決層**，與下面三段優先序正交——不是「第 0 順位」，
            // 而是「不論哪一順位贏，都不送 request」。
            //
            // ⚠️ **為什麼 `DeathArbiterSource` 擋不住這件事**（2026-09-14 使用者 Play 回報：
            //    屍體會不斷面向玩家）：它封鎖的是 `BlockInput`，也就是**輸入產生的意圖**。
            //    但 facing 的第二順位讀的是 `CombatContext`，由 `AIMovementSource`
            //    在順序 2.5 直接寫入黑板，**根本不經過輸入** ⇒ 封鎖輸入對它毫無作用。
            //
            // 不送 request ⇒ MotionDriver 維持既有 rotation，這正是 Priority 4 既有的語意，
            // 因此**不需要**新增任何「凍結朝向」機制或 MotionDriver 側的旗標。
            //
            // 這裡只讀 `CharacterHealth` 已 commit 的判定，⛔ 不重算 `CurrentHealth <= 0`
            // （`SurvivabilityData.IsDead` 的註解點名那是本專案的加重缺陷）。
            bool isDead = data.Survivability.IsDead;

            Vector3 commitment = default;
            bool hasCommitment = _stateMachine != null &&
                                 _stateMachine.TryGetActiveFacingCommitment(out commitment);

            bool hasCombatFacing = usePersistentCombatFacing &&
                                   data.CombatContext.InCombat &&
                                   data.CombatContext.HasTarget;
            Vector3 combatFacing = hasCombatFacing
                ? data.CombatContext.TargetPosition - transform.position
                : default;

            bool resolved = TryResolveFacing(
                hasCommitment,
                commitment,
                hasCombatFacing,
                combatFacing,
                data.MoveDirection,
                out Vector3 direction,
                out bool fromActionCommitment);

            // 死區只保護站定出手的 Action 姿勢，避免半蹲腳掌被連續微轉拖著扭。
            // Combat target 與 MovementDirection 都是 continuous dynamics：即使角差很小也必須
            // 逐幀送出，否則會累積成「停住 → 飄出死區 → 猛轉」的極限環。
            // ⚠️ `&&` 短路：未解出方向時不評估死區，與先前兩段 early-return 的版本逐位元等價。
            //    改成單一出口是為了讓 O-1 的快照有唯一的記錄點（見下方 debug 區塊紀律 3）。
            bool requestSent = !isDead &&
                               resolved &&
                               ShouldRequestFacing(
                                   transform.forward,
                                   direction,
                                   fromActionCommitment,
                                   facingAngleDeadzone);

#if UNITY_EDITOR
            RecordFacingDebug(data, transform.forward, direction, resolved, fromActionCommitment, requestSent, isDead);
#endif

            if (!requestSent) return;

            _motionDriver.RequestFacing(direction);
        }

        internal static bool TryResolveFacing(
            bool hasActionCommitment,
            Vector3 actionCommitment,
            bool hasCombatFacing,
            Vector3 combatFacing,
            Vector3 moveDirection,
            out Vector3 worldDirection,
            out bool fromActionCommitment)
        {
            if (hasActionCommitment && TryNormalizeHorizontal(actionCommitment, out worldDirection))
            {
                fromActionCommitment = true;
                return true;
            }

            // Priority 2：持續 combat facing 不是站定姿勢，因此與 movement 同列不吃死區。
            // 零向量（target 與自身重合）沒有可用朝向，必須繼續退回 Priority 3。
            if (hasCombatFacing && TryNormalizeHorizontal(combatFacing, out worldDirection))
            {
                fromActionCommitment = false;
                return true;
            }

            // Priority 3：沒有有效的 Action／combat 方向時，才跟隨 movement dynamics。
            if (TryNormalizeHorizontal(moveDirection, out worldDirection))
            {
                fromActionCommitment = false;
                return true;
            }

            // Priority 4：維持當前朝向＝不送 request，交給 MotionDriver 保持既有 rotation。
            worldDirection = default;
            fromActionCommitment = false;
            return false;
        }

        /// <summary>
        /// 只回答兩個水平世界方向是否落在同一個死區內，不決定哪一種 facing 來源該使用死區。
        /// 來源政策留在 <see cref="Tick"/>，避免 movement 與站定 Action 再次共享一套錯誤規則。
        /// </summary>
        internal static bool IsWithinFacingDeadzone(
            Vector3 currentForward,
            Vector3 desiredDirection,
            float deadzoneDegrees)
        {
            if (!TryNormalizeHorizontal(currentForward, out Vector3 current) ||
                !TryNormalizeHorizontal(desiredDirection, out Vector3 desired))
            {
                return false;
            }

            return Vector3.Angle(current, desired) <= Mathf.Max(0f, deadzoneDegrees);
        }

        /// <summary>
        /// 將「死區只屬於 Action commitment」表達成可測的來源政策。
        /// combat target／movement 即使落在同一角度範圍內也永遠回傳 true，
        /// 避免小幅航向修正被週期性吞掉。
        /// </summary>
        internal static bool ShouldRequestFacing(
            Vector3 currentForward,
            Vector3 desiredDirection,
            bool fromActionCommitment,
            float deadzoneDegrees)
        {
            return !fromActionCommitment ||
                   !IsWithinFacingDeadzone(currentForward, desiredDirection, deadzoneDegrees);
        }

        private static bool TryNormalizeHorizontal(Vector3 direction, out Vector3 normalized)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= DirectionSqrEpsilon)
            {
                normalized = default;
                return false;
            }

            normalized = direction.normalized;
            return true;
        }

#if UNITY_EDITOR
        // Direction Authority 快照保留為 Editor-only 被動診斷資料，但不再有 world-space presentation。
        // 目前只把「玩家看角色行為時無法得知系統採樣了什麼世界幾何」的資料畫進 Game View：
        // traversal 與 Foot IK。Facing／MoveDirection／Authority 若日後 combat 8-way 實際出現問題再重開。
        // 快照繼續遵守「記錄，不重算」與單向紀律，並由 A33 機器化守護。
        /// <summary>本幀 facing 決策的結果分類。⚠️ 純粹是兩個既有回傳值的編碼，不是重新判定。</summary>
        private enum FacingDebugOutcome
        {
            NotTicked,          // 還沒跑過 Tick：Edit mode，或 Runner 沒有排到這顆元件
            NoDirection,        // TryResolveFacing == false ⇒ Priority 4：不送 request，維持現有朝向
            DeadzoneSuppressed, // 解出方向了，但落在 Action commitment 死區內 ⇒ 本幀刻意不送
            DeathSuppressed,    // 角色已死 ⇒ 否決層生效，不論哪個優先序解出什麼都不送
            RequestSent,        // 已送出 facing request
        }

        private Vector3 _debugDesiredDirection;   // ① MovementIntent.DesiredDirection（意圖）
        private Vector3 _debugMoveDirection;      // ② data.MoveDirection（model 輸出，世界座標）
        private Vector3 _debugForwardAtDecision;  // ③ 決策當下的 transform.forward
        private Vector3 _debugResolvedDirection;  // ④ 本幀解出／送出的 facing 方向
        private bool _debugFromActionCommitment;  // ⑤ 贏的是哪個優先序（true＝Action commitment）
        private FacingDebugOutcome _debugOutcome = FacingDebugOutcome.NotTicked;

        /// <summary>
        /// 在管線順序 4.6 的決策點記錄本幀結果。**只賦值，不判斷**（紀律 3）。
        /// 零 GC：全部是 struct／primitive 賦值，無配置、無字串。
        /// </summary>
        private void RecordFacingDebug(
            PlayerRuntimeData data,
            Vector3 forwardAtDecision,
            Vector3 resolvedDirection,
            bool resolved,
            bool fromActionCommitment,
            bool requestSent,
            bool isDead)
        {
            _debugDesiredDirection = data.MovementIntent.DesiredDirection;
            _debugMoveDirection = data.MoveDirection;
            _debugForwardAtDecision = forwardAtDecision;
            _debugResolvedDirection = resolvedDirection;
            _debugFromActionCommitment = fromActionCommitment;
            // 編碼，不判定：resolved、requestSent 與 isDead 都是 Tick 已經算出來的值。
            // 死亡先判，因為它是否決層——否則會把屍體誤報成「落在死區內」。
            _debugOutcome = isDead ? FacingDebugOutcome.DeathSuppressed
                : !resolved ? FacingDebugOutcome.NoDirection
                : requestSent ? FacingDebugOutcome.RequestSent
                : FacingDebugOutcome.DeadzoneSuppressed;
        }

#endif
    }
}
