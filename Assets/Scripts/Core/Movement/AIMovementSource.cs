using UnityEngine;
using UnityEngine.AI;
using Project.Core.Blackboard;
using Project.Core.Effects;

namespace Project.Core.Movement
{
    /// <summary>
    /// 以 NavMesh 查詢下一段路徑方向，再把結果寫成模型無關的 MovementIntent。
    /// NavMeshAgent 不擁有 Transform；實際位移仍由 LocomotionModel → MotionDriver 結算。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AIMovementSource : MonoBehaviour, IMovementIntentSource
    {
        [SerializeField] private Transform target;
        [SerializeField, Range(0f, 1f)] private float desiredSpeedNormalized = 1f;

        private NavMeshAgent _agent;
        private TemporaryGameplayEffectState _effectState;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _effectState = GetComponent<TemporaryGameplayEffectState>();
            _agent.updatePosition = false;
            _agent.updateRotation = false;
        }

        public void ProduceIntent(ref InputData input, PlayerRuntimeData data)
        {
            if (data == null) return;

            data.MovementIntent = default;
            if (_agent == null || !_agent.isOnNavMesh || target == null || data.CameraTransform == null) return;

            // Agent 只維護 path query 的內部位置；角色 Transform 只會被 MotionDriver 搬動。
            _agent.nextPosition = transform.position;
            _agent.SetDestination(target.position);

            if (_agent.pathPending || !_agent.hasPath ||
                _agent.pathStatus == NavMeshPathStatus.PathInvalid ||
                _agent.remainingDistance <= _agent.stoppingDistance)
            {
                return;
            }

            Vector3 worldDirection = _agent.steeringTarget - transform.position;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) return;
            worldDirection.Normalize();

            Vector3 cameraForward = data.CameraTransform.forward;
            Vector3 cameraRight = data.CameraTransform.right;
            cameraForward.y = 0f;
            cameraRight.y = 0f;
            cameraForward.Normalize();
            cameraRight.Normalize();

            data.MovementIntent.DesiredDirection = new Vector2(
                Vector3.Dot(worldDirection, cameraRight),
                Vector3.Dot(worldDirection, cameraForward));
            data.MovementIntent.DesiredSpeedNormalized = ResolveDesiredSpeedNormalized(Time.time);
        }

        /// <summary>
        /// Slow 在 intent producer 的最後一道輸出上生效；黑板仍只由 AIMovementSource 寫入，
        /// 下游速度平滑、gait、停步、Foot IK 與音效都只會看到自然縮小後的同一份意圖。
        /// </summary>
        internal float ResolveDesiredSpeedNormalized(float currentTime)
        {
            float multiplier = _effectState != null
                ? _effectState.GetMovementSpeedMultiplierAt(currentTime)
                : 1f;
            return Mathf.Clamp01(desiredSpeedNormalized) * multiplier;
        }
    }
}
