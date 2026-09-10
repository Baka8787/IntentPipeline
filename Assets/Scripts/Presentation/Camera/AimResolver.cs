using System.Runtime.CompilerServices;
using UnityEngine;
using Project.Core.Actions;

[assembly: InternalsVisibleTo("Project.Tests.EditMode")]

namespace Project.Presentation.CameraControl
{
    /// <summary>
    /// 將螢幕中心射線解析成當幀世界座標 AimPoint。
    /// 這是 Presentation 私有查詢：不保存 gameplay target、不寫黑板，也不加入角色管線。
    /// </summary>
    public sealed class AimResolver : MonoBehaviour, IAimSource
    {
        [Header("Aim Source")]
        [SerializeField] private UnityEngine.Camera sourceCamera;

        [Header("Aim Ray")]
        [SerializeField, Min(0f)] private float maxAimDistance = 60f;
        [SerializeField] private LayerMask aimRayMask = ~0;

        private int _cachedFrame = -1;
        private Vector3 _cachedPoint;
        private bool _hasAim;

        /// <summary>
        /// 目前 AimResolver 與角色 Root 同物件，因此它能忠實提供方向承諾的角色世界起點。
        /// 這是既有組裝關係的窄版轉送，不代表相機式瞄準理應擁有角色位置。
        /// </summary>
        public Vector3 CommitmentOrigin => transform.position;

        private void OnEnable()
        {
            _cachedFrame = -1;
        }

        private void OnDisable()
        {
            _cachedFrame = -1;
            _hasAim = false;
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

            return true;
        }
    }
}
