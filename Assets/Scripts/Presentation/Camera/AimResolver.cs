using System.Runtime.CompilerServices;
using Project.Core.Actions;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: InternalsVisibleTo("Project.Tests.EditMode")]

namespace Project.Presentation.CameraControl
{
    /// <summary>
    /// 將螢幕中心射線解析成當幀世界座標 AimPoint。
    /// 這是 Presentation 私有查詢：不保存 gameplay target、不寫黑板，也不加入角色管線。
    /// </summary>
    public sealed class AimResolver : MonoBehaviour
    {
        private const int HitBufferSize = 8;
        private const float DirectionSqrEpsilon = 0.000001f;

        [Header("Aim Source")]
        [SerializeField] private UnityEngine.Camera sourceCamera;
        [SerializeField] private InputAction aimAction;

        [Header("Aim Ray")]
        [SerializeField, Min(0f)] private float maxAimDistance = 60f;
        [SerializeField] private LayerMask aimRayMask = ~0;

        [Header("Soft Target")]
        [SerializeField] private LayerMask softTargetMask = ~0;
        [SerializeField, Min(0f)] private float softTargetRadius = 1.2f;
        [SerializeField, Range(0f, 180f)] private float softTargetMaxAngle = 8f;

        // 固定容量是刻意的：soft target 只需要當幀最優候選，不保存列表；溢出的候選直接忽略，
        // 避免為罕見的密集重疊目標把熱路徑改成會擴容的集合。
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];

        private int _cachedFrame = -1;
        private Vector3 _cachedPoint;
        private bool _hasAim;
        private MotionDriver _motionDriver;

        /// <summary>
        /// 取景切換意圖。未綁 action 時安靜退化為探索取景，不建立第二套狀態機。
        /// </summary>
        public bool IsAiming => aimAction != null && aimAction.IsPressed();

        private void Awake()
        {
            _motionDriver = GetComponent<MotionDriver>();
        }

        private void OnEnable()
        {
            aimAction?.Enable();
            _cachedFrame = -1;
        }

        private void OnDisable()
        {
            aimAction?.Disable();
            _cachedFrame = -1;
            _hasAim = false;
        }

        private void Update()
        {
            if (!IsAiming || _motionDriver == null || !TryGetAimPoint(out Vector3 aimPoint)) return;

            // AimResolver 與 MotionDriver 同在角色 Root；在 Update 提交可保證當帧的 LateUpdate
            // 位移結算看得到 request，且不需要新增黑板欄位或讓 sink 自己輪詢相機。
            Vector3 aimDirection = aimPoint - transform.position;
            aimDirection.y = 0f;
            _motionDriver.RequestFacing(aimDirection);
        }

        /// <summary>
        /// 被詢問時才解算，並以 frameCount 去重。同幀所有消費者因此共享同一個快照，
        /// 而不必依賴 AimResolver、ActionState、Camera 的 Unity 執行順序。
        /// </summary>
        public bool TryGetAimPoint(out Vector3 point)
        {
            int frame = Time.frameCount;
            if (_cachedFrame != frame)
            {
                _hasAim = Resolve(out _cachedPoint);
                _cachedFrame = frame;
            }

            point = _cachedPoint;
            return _hasAim;
        }

        private bool Resolve(out Vector3 point)
        {
            UnityEngine.Camera camera = sourceCamera != null ? sourceCamera : UnityEngine.Camera.main;
            if (camera == null)
            {
                point = default;
                return false;
            }

            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float distance = Mathf.Max(0f, maxAimDistance);

            if (Physics.Raycast(ray, out RaycastHit geometryHit, distance, aimRayMask,
                    QueryTriggerInteraction.Ignore))
            {
                point = geometryHit.point;
            }
            else
            {
                point = ray.origin + ray.direction * distance;
            }

            int hitCount = Physics.SphereCastNonAlloc(ray, Mathf.Max(0f, softTargetRadius),
                _hitBuffer, distance, softTargetMask, QueryTriggerInteraction.Ignore);

            bool hasSoftTarget = false;
            Vector3 bestPoint = point;

            for (int i = 0; i < hitCount; i++)
            {
                Collider candidateCollider = _hitBuffer[i].collider;
                if (candidateCollider == null) continue;

                ActionRequestTarget candidate = candidateCollider.GetComponentInParent<ActionRequestTarget>();
                if (candidate == null || candidate.transform.root == transform.root) continue;

                Vector3 candidatePoint = candidateCollider.bounds.center;
                if (!IsWithinSoftTargetCone(ray.origin, ray.direction, candidatePoint,
                        softTargetMaxAngle, distance))
                {
                    continue;
                }

                if (hasSoftTarget && !IsBetterSoftTarget(
                        ray.origin, ray.direction, candidatePoint, bestPoint))
                {
                    continue;
                }

                hasSoftTarget = true;
                bestPoint = candidatePoint;
            }

            if (hasSoftTarget) point = bestPoint;
            return true;
        }

        /// <summary>
        /// 角錐判定只比較 dot 與 cos，不做 Acos；除了更便宜，也避免退化輸入把 NaN 帶進選擇。
        /// </summary>
        internal static bool IsWithinSoftTargetCone(Vector3 origin, Vector3 direction,
            Vector3 candidate, float maxAngle, float maxDistance)
        {
            Vector3 toCandidate = candidate - origin;
            float candidateSqrDistance = toCandidate.sqrMagnitude;
            float directionSqrMagnitude = direction.sqrMagnitude;
            if (candidateSqrDistance <= DirectionSqrEpsilon ||
                directionSqrMagnitude <= DirectionSqrEpsilon || maxDistance < 0f ||
                candidateSqrDistance > maxDistance * maxDistance)
            {
                return false;
            }

            float clampedAngle = Mathf.Clamp(maxAngle, 0f, 180f);
            float alignment = Vector3.Dot(direction, toCandidate) /
                Mathf.Sqrt(directionSqrMagnitude * candidateSqrDistance);
            return alignment >= Mathf.Cos(clampedAngle * Mathf.Deg2Rad);
        }

        /// <summary>
        /// soft target 的唯一排序鍵：角度偏差。距離只負責是否超出搜尋範圍，不參與排名。
        /// </summary>
        internal static bool IsBetterSoftTarget(Vector3 origin, Vector3 direction,
            Vector3 candidate, Vector3 currentBest)
        {
            return ComputeAlignment(origin, direction, candidate) >
                ComputeAlignment(origin, direction, currentBest);
        }

        private static float ComputeAlignment(Vector3 origin, Vector3 direction, Vector3 candidate)
        {
            Vector3 toCandidate = candidate - origin;
            float denominatorSqr = direction.sqrMagnitude * toCandidate.sqrMagnitude;
            if (denominatorSqr <= DirectionSqrEpsilon) return -1f;

            return Vector3.Dot(direction, toCandidate) / Mathf.Sqrt(denominatorSqr);
        }
    }
}
