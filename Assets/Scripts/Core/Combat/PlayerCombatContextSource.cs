using Project.Core.Actions;
using Project.Core.Blackboard;
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

        // 固定容量只在元件建構時配置一次；Update 熱路徑不建立 List／LINQ 結果。
        private readonly Collider[] _candidateBuffer = new Collider[CandidateBufferSize];
        private ActionRequestTarget _selfRequestTarget;
        private ActionRequestTarget _targetMarker;
        private Collider _targetCollider;
        private float _lastInteractionTime;
        private bool _hasInteractionTime;

        private void Awake()
        {
            _selfRequestTarget = GetComponent<ActionRequestTarget>();
        }

        /// <summary>
        /// 管線順序 2.6。時間由 Runner 注入，讓距離／時間的 AND 離場規則可確定性測試。
        /// </summary>
        public void Tick(PlayerRuntimeData data, float currentTime)
        {
            if (data == null) return;

            float effectiveEnterRadius = Mathf.Max(0f, enterRadius);
            float effectiveLeaveRadius = Mathf.Max(leaveRadius, effectiveEnterRadius + RadiusEpsilon);
            bool interactionThisFrame = data.Intent.RequestedActionSlot != ActionSlot.None ||
                                        (_selfRequestTarget != null && _selfRequestTarget.HasPendingRequest);

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
                    : Vector3.zero
            };
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
                if (candidate == null || !candidate.isActiveAndEnabled ||
                    candidate.transform.root == transform.root)
                {
                    continue;
                }

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

        private bool IsRetainedTargetLegal(Transform selfRoot, ActionRequestTarget target, float radius)
        {
            if (target == null || !target.isActiveAndEnabled || target.transform.root == selfRoot) return false;
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
    }
}
