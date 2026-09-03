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
        internal enum EngagementMovement
        {
            Hold,
            Approach,
            Retreat
        }

        [SerializeField] private Transform target;
        [SerializeField, Range(0f, 1f)] private float desiredSpeedNormalized = 1f;

        [Header("Melee Engagement Distance")]
        [SerializeField, Min(0f)] private float minimumEngagementDistance = 1.25f;
        [SerializeField, Min(0f)] private float maximumEngagementDistance = 2f;
        [SerializeField, Min(0f)] private float distanceHysteresis = 0.15f;

        private NavMeshAgent _agent;
        private TemporaryGameplayEffectState _effectState;
        private EngagementMovement _engagementMovement;

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
            if (_agent == null || !_agent.isOnNavMesh || target == null || data.CameraTransform == null)
            {
                _engagementMovement = EngagementMovement.Hold;
                return;
            }

            // Agent 只維護 path query 的內部位置；角色 Transform 只會被 MotionDriver 搬動。
            _agent.nextPosition = transform.position;
            _agent.SetDestination(target.position);

            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            _engagementMovement = ResolveEngagementMovement(
                _engagementMovement,
                Mathf.Sqrt(toTarget.sqrMagnitude),
                minimumEngagementDistance,
                maximumEngagementDistance,
                distanceHysteresis);

            if (_engagementMovement == EngagementMovement.Hold)
            {
                return;
            }

            Vector3 worldDirection;
            if (_engagementMovement == EngagementMovement.Retreat)
            {
                // 後退仍只是 producer 產生的方向意圖；真正位移繼續由 MotionDriver 獨佔。
                // 近距離時 path 可能因 stoppingDistance 而沒有有效 steeringTarget，因此以目標反方向
                // 作為局部脫離方向，不能讓 NavMeshAgent 自己搬 Transform 來規避這個情況。
                worldDirection = -toTarget;
            }
            else
            {
                if (_agent.pathPending || !_agent.hasPath ||
                    _agent.pathStatus == NavMeshPathStatus.PathInvalid)
                {
                    return;
                }

                worldDirection = _agent.steeringTarget - transform.position;
            }

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
        /// 交戰距離是 producer 私有的 intent 狀態，不回讀 FSM。外側門檻決定開始移動，內側門檻
        /// 決定何時停下；兩者間的 hysteresis 讓微小距離誤差不會逐幀切換前進／停止或後退／停止。
        /// </summary>
        internal static EngagementMovement ResolveEngagementMovement(
            EngagementMovement current,
            float distance,
            float minimumDistance,
            float maximumDistance,
            float hysteresis)
        {
            float minimum = Mathf.Max(0f, minimumDistance);
            float maximum = Mathf.Max(minimum, maximumDistance);
            float deadZone = Mathf.Min(Mathf.Max(0f, hysteresis), (maximum - minimum) * 0.5f);

            if (current == EngagementMovement.Retreat && distance < minimum + deadZone)
                return EngagementMovement.Retreat;
            if (current == EngagementMovement.Approach && distance > maximum - deadZone)
                return EngagementMovement.Approach;

            if (distance < minimum) return EngagementMovement.Retreat;
            if (distance > maximum) return EngagementMovement.Approach;
            return EngagementMovement.Hold;
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
