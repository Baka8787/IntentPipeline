using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Survivability;
using UnityEngine;

namespace Project.Core.Combat
{
    /// <summary>
    /// 玩家 CombatContext 的唯一 producer。候選只在進入、目前目標失效時重選；合法目標
    /// 會一直保留，避免另一個更近的敵人逐幀奪取朝向。
    /// </summary>
    public sealed class PlayerCombatContextSource : MonoBehaviour
    {
        private const int CandidateBufferSize = 32;
        private const float DirectionSqrEpsilon = 0.000001f;
        private const float RadiusEpsilon = 0.01f;

        [Header("Combat Context")]
        [SerializeField, Min(0f)] private float enterRadius = 6f;
        [SerializeField, Min(0f)] private float leaveRadius = 9f;
        [SerializeField, Min(0f)] private float disengageSeconds = 5f;

        [Header("Target Selection")]
        [SerializeField] private LayerMask targetMask = ~0;
        [SerializeField, Range(0f, 180f)] private float selectionConeAngle = 25f;

        [Header("Action Soft Target (Playtest Tuning)")]
        [SerializeField, Min(0f)] private float softTargetRange = 12f;
        [SerializeField, Range(0f, 180f)] private float softTargetConeHalfAngle = 25f;

        // 固定容量只在元件建構時配置一次；Update 熱路徑不建立 List／LINQ 結果。
        private readonly Collider[] _candidateBuffer = new Collider[CandidateBufferSize];
        // ⚠️ `ActionRequestTarget` 在本類別**只剩一個角色：敵對目標的身分標記**
        //    （`_targetMarker`／候選掃描）。它不再被用來偵測「我被打了」——那條 seam 已於
        //    2026-09-14 遷移到 `Survivability.JustTookDamage`（見 Tick 內的說明）。
        //    原本的 `_selfRequestTarget` 欄位與 `Awake` 因此一併移除，不留無人讀取的快取。
        private ActionRequestTarget _targetMarker;
        private Collider _targetCollider;
        private float _lastInteractionTime;
        private bool _hasInteractionTime;

        /// <summary>
        /// 管線順序 2.6。時間由 Runner 注入，讓距離／時間的 AND 離場規則可確定性測試。
        /// </summary>
        public void Tick(PlayerRuntimeData data, float currentTime)
        {
            if (data == null) return;

            float effectiveEnterRadius = Mathf.Max(0f, enterRadius);
            float effectiveLeaveRadius = Mathf.Max(leaveRadius, effectiveEnterRadius + RadiusEpsilon);
            // 🔄（`docs/26` §I，2026-09-14）**seam migration，不是行為變更。**
            //
            // 「受到敵對互動」原本讀 `_selfRequestTarget.HasPendingRequest`——因為當時受擊是一個
            // Action，會經 `ActionRequestTarget` 投遞（`docs/15` §4.1 明文記載這條契約）。
            // 受擊改為 `StateType.Hurt` 之後那個 mailbox 不再承載受擊，**若不一併遷移，
            // 「被打」會安靜地停止刷新交戰計時 ⇒ 被圍毆時反而提早脫離戰鬥語境，且沒有任何錯誤訊息。**
            //
            // 新來源是同一份已 commit 的生存真相（順序 0.5 由 `CharacterHealth` 發布，
            // 本步在順序 2.6 讀得到），而且**涵蓋面更廣**：任何來源的傷害都算互動，
            // 不再限於「記得戳 mailbox」的那些。
            //
            // ⚠️ 本輪**只遷移這一條 seam**。`docs/15` §4.2 的「目標合法性改讀 `IsDead`」
            //    （§16-7）**刻意不做**——那是 targeting scope，使用者明確要求不拉進本輪。
            bool interactionThisFrame = data.Intent.RequestedActionSlot != ActionSlot.None ||
                                        data.Survivability.JustTookDamage;

            if (interactionThisFrame)
            {
                _lastInteractionTime = currentTime;
                _hasInteractionTime = true;
            }

            bool wasInCombat = data.CombatContext.InCombat;
            if (!IsRetainedTargetLegal(transform.root, _targetMarker, effectiveLeaveRadius))
            {
                ClearTarget();
            }

            // 黏性核心：目前目標仍合法就完全不掃描，不讓每幀的距離／相機變化改寫目標。
            if (_targetMarker == null)
            {
                float searchRadius = wasInCombat ? effectiveLeaveRadius : effectiveEnterRadius;
                TrySelectTarget(data.CameraTransform, searchRadius);
            }

            bool hasTarget = _targetMarker != null;
            bool hasSoftTarget = TrySelectSoftTarget(
                data.CameraTransform,
                softTargetRange,
                softTargetConeHalfAngle,
                out Vector3 softTargetPosition);
            bool inCombat = wasInCombat || interactionThisFrame || hasTarget;
            if (inCombat && !wasInCombat && !_hasInteractionTime)
            {
                // 純半徑進場沒有「上次互動」可量；以進場時刻作為 disengage 計時基準，
                // 否則第一次走近再離開會永久卡在 InCombat。
                _lastInteractionTime = currentTime;
                _hasInteractionTime = true;
            }

            if (inCombat && ShouldDisengage(
                    hasTarget, _hasInteractionTime, currentTime, _lastInteractionTime, disengageSeconds))
            {
                inCombat = false;
                _hasInteractionTime = false;
                ClearTarget();
            }

            data.CombatContext = new CombatContextData
            {
                InCombat = inCombat,
                HasTarget = inCombat && _targetMarker != null,
                TargetPosition = inCombat && _targetMarker != null
                    ? ResolveTargetPosition()
                    : Vector3.zero,
                HasSoftTarget = hasSoftTarget,
                SoftTargetPosition = hasSoftTarget ? softTargetPosition : Vector3.zero
            };
        }

        /// <summary>
        /// 每幀、無記憶的 Action soft-target candidate。與黏性的 combat target 共用同一 producer／mask／buffer，
        /// 但語意刻意分開：combat target 回答「戰鬥語境中的對象」，soft target 回答「鏡頭中心現在大致瞄誰」。
        /// </summary>
        private bool TrySelectSoftTarget(
            Transform cameraTransform,
            float range,
            float coneHalfAngle,
            out Vector3 targetPosition)
        {
            targetPosition = default;
            if (cameraTransform == null || range <= 0f) return false;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                range,
                _candidateBuffer,
                targetMask,
                QueryTriggerInteraction.Ignore);

            Vector3 origin = transform.position;
            Vector3 cameraForward = cameraTransform.forward;
            bool found = false;
            float bestAlignment = -1f;
            float bestSqrDistance = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                Collider candidateCollider = _candidateBuffer[i];
                if (candidateCollider == null) continue;

                ActionRequestTarget candidate = candidateCollider.GetComponentInParent<ActionRequestTarget>();
                if (!IsHostileCandidateLegal(candidate, transform.root)) continue;

                Vector3 candidatePoint = candidateCollider.bounds.center;
                if (!TryScoreSoftTarget(
                        origin,
                        cameraForward,
                        candidatePoint,
                        range,
                        coneHalfAngle,
                        out float alignment,
                        out float sqrDistance))
                {
                    continue;
                }

                if (found && !IsBetterSoftTarget(
                        alignment,
                        sqrDistance,
                        bestAlignment,
                        bestSqrDistance))
                {
                    continue;
                }

                found = true;
                bestAlignment = alignment;
                bestSqrDistance = sqrDistance;
                targetPosition = candidatePoint;
            }

            return found;
        }

        private void TrySelectTarget(Transform cameraTransform, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, radius, _candidateBuffer, targetMask, QueryTriggerInteraction.Ignore);

            Vector3 origin = transform.position;
            Vector3 selectionDirection = cameraTransform != null ? cameraTransform.forward : Vector3.zero;
            selectionDirection.y = 0f;
            bool hasSelectionDirection = selectionDirection.sqrMagnitude > DirectionSqrEpsilon;
            if (hasSelectionDirection) selectionDirection.Normalize();

            ActionRequestTarget bestMarker = null;
            Collider bestCollider = null;
            bool bestInCone = false;
            float bestAlignment = -1f;
            float bestSqrDistance = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                Collider candidateCollider = _candidateBuffer[i];
                if (candidateCollider == null) continue;

                ActionRequestTarget candidate = candidateCollider.GetComponentInParent<ActionRequestTarget>();
                if (!IsHostileCandidateLegal(candidate, transform.root)) continue;

                Vector3 candidatePoint = candidateCollider.bounds.center;
                Vector3 toCandidate = candidatePoint - origin;
                float sqrDistance = toCandidate.sqrMagnitude;
                if (sqrDistance <= DirectionSqrEpsilon) continue;

                float alignment = hasSelectionDirection
                    ? Vector3.Dot(selectionDirection, toCandidate) / Mathf.Sqrt(sqrDistance)
                    : -1f;
                bool inCone = hasSelectionDirection &&
                              alignment >= Mathf.Cos(Mathf.Clamp(selectionConeAngle, 0f, 180f) * Mathf.Deg2Rad);

                if (bestMarker != null && !IsBetterCandidate(
                        inCone, alignment, sqrDistance, bestInCone, bestAlignment, bestSqrDistance))
                {
                    continue;
                }

                bestMarker = candidate;
                bestCollider = candidateCollider;
                bestInCone = inCone;
                bestAlignment = alignment;
                bestSqrDistance = sqrDistance;
            }

            if (bestMarker == null) return;
            _targetMarker = bestMarker;
            _targetCollider = bestCollider;
        }

        /// <summary>
        /// 🆕（2026-09-15，`docs/15` §16-7 結案）**單一的敵對目標合法性判準**。
        /// 進入掃描（combat／soft）與保留檢查三處共用，避免「三個地方各自記得要檢查什麼」。
        ///
        /// <para><b>為什麼死亡必須在這一層擋掉</b></para>
        /// 舊版只檢查 <c>isActiveAndEnabled</c>。而 <c>DeathState</c>／<c>CharacterHealth</c>
        /// **都不會**停用 collider 或 <see cref="ActionRequestTarget"/>（2026-09-15 查證）
        /// ⇒ 屍體通過全部檢查、仍是合法目標 ⇒ 玩家會朝屍體轉、戰鬥語境不脫離。
        /// <c>docs/15</c> §4.2 當年寫「沒有 Health 契約 ⇒ 判斷不出死亡」，
        /// 該契約已於 ADR-009 建立（<c>Survivability.IsDead</c>），這裡把它接上。
        ///
        /// ⚠️ **沒有 <see cref="CharacterHealth"/> 的目標視為合法**——訓練樁、可互動物件
        /// 「沒有生命值」不等於「死了」。不得把缺席當成死亡。
        /// </summary>
        private bool IsHostileCandidateLegal(ActionRequestTarget candidate, Transform selfRoot)
        {
            if (candidate == null || !candidate.isActiveAndEnabled ||
                candidate.transform.root == selfRoot)
            {
                return false;
            }

            CharacterHealth health = candidate.GetComponentInParent<CharacterHealth>();
            return health == null || !health.IsDead;
        }

        private bool IsRetainedTargetLegal(Transform selfRoot, ActionRequestTarget target, float radius)
        {
            if (!IsHostileCandidateLegal(target, selfRoot)) return false;
            Vector3 delta = target.transform.position - transform.position;
            return delta.sqrMagnitude <= radius * radius;
        }

        private Vector3 ResolveTargetPosition()
        {
            return _targetCollider != null && _targetCollider.enabled && _targetCollider.gameObject.activeInHierarchy
                ? _targetCollider.bounds.center
                : _targetMarker.transform.position;
        }

        private void ClearTarget()
        {
            _targetMarker = null;
            _targetCollider = null;
        }

        internal static bool ShouldDisengage(
            bool hasLegalTarget,
            bool hasInteractionTime,
            float currentTime,
            float lastInteractionTime,
            float seconds)
        {
            return !hasLegalTarget && hasInteractionTime &&
                   currentTime - lastInteractionTime > Mathf.Max(0f, seconds);
        }

        /// <summary>相機錐只作選擇消歧；錐外候選仍可依距離成為威脅目標。</summary>
        internal static bool IsBetterCandidate(
            bool candidateInCone,
            float candidateAlignment,
            float candidateSqrDistance,
            bool currentInCone,
            float currentAlignment,
            float currentSqrDistance)
        {
            if (candidateInCone != currentInCone) return candidateInCone;
            if (candidateInCone && !Mathf.Approximately(candidateAlignment, currentAlignment))
                return candidateAlignment > currentAlignment;
            return candidateSqrDistance < currentSqrDistance;
        }

        internal static bool TryScoreSoftTarget(
            Vector3 origin,
            Vector3 cameraForward,
            Vector3 candidatePoint,
            float range,
            float coneHalfAngle,
            out float alignment,
            out float sqrDistance)
        {
            alignment = -1f;
            Vector3 delta = candidatePoint - origin;
            sqrDistance = delta.sqrMagnitude;
            float effectiveRange = Mathf.Max(0f, range);
            if (sqrDistance <= DirectionSqrEpsilon ||
                sqrDistance > effectiveRange * effectiveRange)
            {
                return false;
            }

            cameraForward.y = 0f;
            delta.y = 0f;
            if (cameraForward.sqrMagnitude <= DirectionSqrEpsilon ||
                delta.sqrMagnitude <= DirectionSqrEpsilon)
            {
                return false;
            }

            cameraForward.Normalize();
            delta.Normalize();
            alignment = Vector3.Dot(cameraForward, delta);
            float minimumAlignment = Mathf.Cos(
                Mathf.Clamp(coneHalfAngle, 0f, 180f) * Mathf.Deg2Rad);
            return alignment >= minimumAlignment;
        }

        /// <summary>鏡頭中心的角度優先；只有角度等價時才用距離消歧。</summary>
        internal static bool IsBetterSoftTarget(
            float candidateAlignment,
            float candidateSqrDistance,
            float currentAlignment,
            float currentSqrDistance)
        {
            if (!Mathf.Approximately(candidateAlignment, currentAlignment))
                return candidateAlignment > currentAlignment;
            return candidateSqrDistance < currentSqrDistance;
        }
    }
}
