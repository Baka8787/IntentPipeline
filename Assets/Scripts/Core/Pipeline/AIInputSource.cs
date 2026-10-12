using UnityEngine;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Survivability;

namespace Project.Core.Pipeline
{
    /// <summary>
    /// 敵人的攻擊「按鍵」來源。**AI 就是另一種 <see cref="IInputSource"/>**——
    /// 它產生的輸入走與玩家完全相同的一條路：順序 1 取得輸入 → 順序 2 `ProcessIntents`
    /// 翻成 `RequestedActionSlot` → `ActionState` 決定能不能出手。
    ///
    /// 🎯 **為什麼攻擊決策必須住在這裡，不能塞回 <c>AIMovementSource</c>**：
    /// `CharacterPipelineRunner` 的順序是 **2 `ProcessIntents` → 2.5 `ProduceIntent`**。
    /// 在 `ProduceIntent` 裡寫 `Slot1ButtonDown` 時，本幀的 `ProcessIntents` **已經跑完**；
    /// 而下一幀開頭 `InputData inputData = default` 會把整份輸入歸零重取
    /// ⇒ 那個旗標**永遠不會被任何人讀到**。這不是風格偏好，是時序事實。
    ///
    /// 本元件只持有「多久重新送一次請求」的 producer cadence；它不是攻擊冷卻。
    /// 「這一拳現在能不能出」的唯一回答者仍是 `ActionState`（ADR-004 D2）——
    /// 它握有 per-slot 冷卻、`RequiresGrounded`、以及 Action lifecycle。
    /// AI 只負責回答「我想不想打」，不負責回答「我可不可以打」。
    ///
    /// </summary>
    public sealed class AIInputSource : MonoBehaviour, IInputSource
    {
        private const float MinimumRetryInterval = 0.01f;

        // 🔄（2026-10-12，`docs/54` §3.3.2-① T1）**不再持有自己的 target。**
        // 攻擊對象與移動／交戰對象必須是同一個（W15 以前只在 prefab 層約束兩份序列化欄位相同，
        // 場景 override 照樣可以分岔）。現在整隻敵人只有 `AIMovementSource.target` 一份，
        // 本元件經它的唯讀屬性讀取——參照資料依賴，不是呼叫移動的決策邏輯。
        // 同物件沒有 AIMovementSource（或其 target 為空）⇒ 永不出手（未接線的敵人應該安靜）。
        private AIMovementSource _targetSource;

        [Tooltip("進入這個水平距離內就想出手。\n" +
                 "⚠️ 契約（W10 守）：必須**等於** AIMovementSource 的 maximumEngagementDistance。\n" +
                 "太大 ⇒ 還在 Approach 就揮拳（邊滑步邊出拳）；\n" +
                 "太小 ⇒ 兩者之間出現一段「站得住但打不到」的死區，敵人會在那裡繞圈永不出手。\n" +
                 "要改就兩個一起改，並以 MeleeHitbox 的實際觸及範圍為準。")]
        [SerializeField, Min(0f)] private float attackRange = 2f;

        [Tooltip("仍在攻擊距離內時，隔多久重新嘗試送一次 Slot1 request。\n" +
                 "這只是 producer 的 request cadence，不是 attack cooldown；" +
                 "真正能否執行仍由 ActionState 的 Cooldown、CooldownVariance、著地條件與 lifecycle 決定。")]
        [SerializeField, Min(MinimumRetryInterval)] private float attackRequestRetryInterval = 0.5f;

        private bool _wasWithinAttackRange;
        private float _nextAttackRequestTime;

        // 🆕（2026-10-12，`docs/54` §3.3）目標的生命值元件快取。找到後 target 不變就不再重找；
        // 缺席（null）＝目標沒有生命值，視為活著。
        private Transform _healthResolvedFor;
        private CharacterHealth _targetHealth;

        /// <summary>
        /// 把持續的 attack desire 轉成單幀 request pulse。**不碰移動輸入**——敵人的移動意圖由
        /// `AIMovementSource` 在順序 2.5 直接寫 `MovementIntent`，兩者職責不重疊。
        /// （`inputData` 由 Runner 每幀 `default` 初始化，因此這裡不必歸零其餘欄位。）
        ///
        /// 第一次進入射程立即送一次；持續留在射程內時，每隔 request retry interval 再送一次。
        /// 中間幀必為 false，讓 external request 有機會進入既有仲裁。離開射程會重新武裝，
        /// 下次進入可立即再送。此處不讀黑板、FSM 或 ActionState，也不知道上次請求是否成功。
        /// </summary>
        public void FetchRawInput(ref InputData data)
        {
            FetchRawInput(ref data, Time.time);
        }

        private void FetchRawInput(ref InputData data, float currentTime)
        {
            bool wantsToAttack = WantsToAttack();
            if (!wantsToAttack)
            {
                RearmAttackRequest();
                data.Slot1ButtonDown = false;
                return;
            }

            if (!_wasWithinAttackRange || currentTime >= _nextAttackRequestTime)
            {
                _wasWithinAttackRange = true;
                _nextAttackRequestTime = currentTime
                    + Mathf.Max(MinimumRetryInterval, attackRequestRetryInterval);
                data.Slot1ButtonDown = true;
                return;
            }

            data.Slot1ButtonDown = false;
        }

        private void OnDisable()
        {
            RearmAttackRequest();
        }

        private void RearmAttackRequest()
        {
            _wasWithinAttackRange = false;
            _nextAttackRequestTime = 0f;
        }

        /// <summary>目前是否想出手。<c>internal</c> 供 EditMode 驗證，production 只經 <see cref="FetchRawInput"/>。</summary>
        internal bool WantsToAttack()
        {
            Transform target = ResolveTarget();
            if (target == null) return false;

            // 🆕（2026-10-12，`docs/54` §3.3，Q3 Target Eligibility）**死亡的目標不是攻擊目標。**
            // 玩家死亡後（重生是手動鍵，可以無限期保持死亡），敵人原本會每 2–3.2 s 對屍體揮一拳。
            // 判準與玩家選目標同一個（CharacterHealth.IsAliveOrUntracked）：沒有生命值的目標視為活著。
            // ⚠️ 讀的是**目標的**已 commit 判定，不是自己的黑板，也不讀 FSM（A27 不變）。
            if (!CharacterHealth.IsAliveOrUntracked(ResolveTargetHealth(target))) return false;

            return IsWithinAttackRange(transform.position, target.position, attackRange);
        }

        private Transform ResolveTarget()
        {
            // EditMode 不呼叫 Awake ⇒ 惰性解析；找到後沿用（同物件上的元件不會換人）。
            if (_targetSource == null) _targetSource = GetComponent<AIMovementSource>();
            return _targetSource != null ? _targetSource.Target : null;
        }

        private CharacterHealth ResolveTargetHealth(Transform target)
        {
            // 找不到（null）時每次重找：目標可能稍後才掛上生命值元件；找到後只要 target 不變就沿用。
            if (_healthResolvedFor != target || _targetHealth == null)
            {
                _healthResolvedFor = target;
                _targetHealth = target != null ? target.GetComponentInParent<CharacterHealth>() : null;
            }
            return _targetHealth;
        }

        /// <summary>
        /// 純函數版本，供不需要 GameObject 的測試使用。
        /// **只比水平距離**：高低差不該讓敵人在斜坡或台階上突然停手——這與
        /// `AIMovementSource.ResolveEngagementMovement` 的距離定義刻意一致（兩者都先把 y 歸零）。
        /// </summary>
        internal static bool IsWithinAttackRange(Vector3 self, Vector3 targetPosition, float range)
        {
            Vector3 offset = targetPosition - self;
            offset.y = 0f;

            float clampedRange = Mathf.Max(0f, range);
            return offset.sqrMagnitude <= clampedRange * clampedRange;
        }

        /// <summary>
        /// 測試用的顯式接線；production 走 Inspector 指派。
        /// target 刻意寫進同物件的 <see cref="AIMovementSource"/>（必要時補掛）——測試走的就是
        /// production 的單一權威路徑，不在本元件留後門。
        /// </summary>
        internal void ConfigureForTests(Transform attackTarget, float range, float retryInterval = 0.5f)
        {
            AIMovementSource targetSource = GetComponent<AIMovementSource>();
            if (targetSource == null) targetSource = gameObject.AddComponent<AIMovementSource>();
            targetSource.SetTargetForTests(attackTarget);
            _targetSource = targetSource;
            attackRange = range;
            attackRequestRetryInterval = retryInterval;
            RearmAttackRequest();
        }

        /// <summary>測試以顯式時間驗證 cadence，production 仍使用 Unity 的 scaled game time。</summary>
        internal void FetchRawInputAtTimeForTests(ref InputData data, float currentTime)
        {
            FetchRawInput(ref data, currentTime);
        }

#if UNITY_EDITOR
        /// <summary>
        /// **攻擊決策的 Scene 視窗可視化**（2026-09-06）。與 `AIMovementSource` 的接戰帶畫在一起
        /// （兩者同在角色 Root，選中角色時會同時顯示），可以一眼看出
        /// 「攻擊圈」與「接戰帶」對不對得上——2026-09-05 的「敵人亂動」就是
        /// `attackRange` 等於 `maximumEngagementDistance`、在邊界上一邊滑步一邊揮拳。
        ///
        /// ⚠️ 只讀不寫：gizmo 不得成為第二個決策來源。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position;
            bool wants = WantsToAttack();

            // 想出手＝實心暖色，不想＝暗灰。一眼分辨「決策層現在要不要打」。
            UnityEditor.Handles.color = wants
                ? new Color(1f, 0.55f, 0.15f, 1f)
                : new Color(0.5f, 0.5f, 0.55f, 0.6f);
            UnityEditor.Handles.DrawWireDisc(origin, Vector3.up, Mathf.Max(0f, attackRange));

            Vector3 labelAnchor = origin + Vector3.up * 2.0f;

            Transform target = ResolveTarget();
            if (target == null)
            {
                UnityEditor.Handles.color = Color.red;
                UnityEditor.Handles.Label(labelAnchor, "AI attack: target = <none> ⇒ 永遠不出手");
                return;
            }

            Vector3 offset = target.position - origin;
            offset.y = 0f;

            UnityEditor.Handles.Label(labelAnchor,
                $"AI attack: desire = {(wants ? "TRUE" : "false")}\n" +
                $"distance {offset.magnitude:0.00}  range {attackRange:0.00}");
        }
#endif
    }
}
