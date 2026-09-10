using UnityEngine;
using Project.Core.Blackboard;

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
    /// ⛔ **本元件不持有任何冷卻計時器**（使用者 2026-09-05 明確要求）。
    /// 「這一拳現在能不能出」的唯一回答者仍是 `ActionState`（ADR-004 D2）——
    /// 它握有 per-slot 冷卻、`RequiresGrounded`、以及 Action lifecycle。
    /// AI 只負責回答「我想不想打」，不負責回答「我可不可以打」。
    ///
    /// ⚠️ **已知的介面限制，見 <see cref="FetchRawInput"/> 的說明**。
    /// </summary>
    public sealed class AIInputSource : MonoBehaviour, IInputSource
    {
        [Tooltip("攻擊對象。留空 ⇒ 永不出手（不是錯誤，未接線的敵人應該安靜）。")]
        [SerializeField] private Transform target;

        [Tooltip("進入這個水平距離內就想出手。\n" +
                 "預設 2 對齊 AIMovementSource 的 maximumEngagementDistance——" +
                 "兩者不一致會產生「站在帶內卻打不到」或「邊退邊揮空」的怪異行為。")]
        [SerializeField, Min(0f)] private float attackRange = 2f;

        /// <summary>
        /// 只回答「攻擊鍵現在有沒有被按著」。**不碰移動輸入**——敵人的移動意圖由
        /// `AIMovementSource` 在順序 2.5 直接寫 `MovementIntent`，兩者職責不重疊。
        /// （`inputData` 由 Runner 每幀 `default` 初始化，因此這裡不必歸零其餘欄位。）
        ///
        /// 🔴 **已知限制：這是 level-triggered，不是 edge-triggered。**
        /// 只要目標在射程內，本方法**每一幀**都會回報 `Slot1ButtonDown = true`——
        /// 語意上等於「AI 一直按著攻擊鍵」，而真實裝置的 `WasPressedThisFrame` 不可能連續兩幀為真。
        ///
        /// **為什麼還是這樣做**：`IInputSource.FetchRawInput(ref InputData)` **拿不到黑板**，
        /// 因此輸入源無從得知「上一次按下有沒有被消化」「冷卻好了沒」。
        /// 要產生正確節奏的單幀脈衝，只剩兩條路——AI 自己計時（使用者已否決），
        /// 或回讀狀態機（違反依賴方向）。⇒ 在現有介面下，level-triggered 是唯一誠實的表達。
        ///
        /// **今天為什麼安全**：`EnemyPunchDefinition` 沒有 `Loop`（`WaitForTrigger` 用不到）
        /// 也沒有 `ChainSegments`（連段推進用不到），`ActionState` 裡兩條會讀 re-trigger 的路徑都走不到，
        /// 節流完全由 `Cooldown` 負責。
        ///
        /// **哪一天會咬人**：敵人的 Definition 一旦加上 `Loop`＋`WaitForTrigger` 或 `ChainSegments`，
        /// 持續為真的旗標會讓它**每一段都立刻自動推進**。屆時正解是替 `InputData` 補一組
        /// `Slot1ButtonHeld`（比照既有的 `SprintButtonHeld` ／ `SprintButtonDown` 並存先例），
        /// **不是**在本元件補計時器。`AIInputSourceTests` 已把這條語意釘住。
        /// </summary>
        public void FetchRawInput(ref InputData data)
        {
            data.Slot1ButtonDown = WantsToAttack();
        }

        /// <summary>目前是否想出手。<c>internal</c> 供 EditMode 驗證，production 只經 <see cref="FetchRawInput"/>。</summary>
        internal bool WantsToAttack()
        {
            if (target == null) return false;
            return IsWithinAttackRange(transform.position, target.position, attackRange);
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

        /// <summary>測試用的顯式接線；production 走 Inspector 指派。</summary>
        internal void ConfigureForTests(Transform attackTarget, float range)
        {
            target = attackTarget;
            attackRange = range;
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
