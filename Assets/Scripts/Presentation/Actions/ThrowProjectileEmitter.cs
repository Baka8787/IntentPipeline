using UnityEngine;
using Project.Core.Actions;

namespace Project.Presentation.Actions
{
    /// <summary>
    /// Throw lifecycle 的 Unity side-effect adapter。只管理 held visual 與生成 projectile，
    /// 不決定 phase、release 時點或 Action lifecycle。
    /// </summary>
    public sealed class ThrowProjectileEmitter : MonoBehaviour, IActionLifecycleSink
    {
        [SerializeField] private ThrownProjectile projectilePrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private GameObject heldVisual;
        [SerializeField, Min(0f)] private float projectileSpeed = 5f;
        [SerializeField, Min(0.01f)] private float projectileLifetime = 5f;

        private bool _releasedThisExecution;

        public void Begin()
        {
            _releasedThisExecution = false;
            if (heldVisual != null) heldVisual.SetActive(true);
        }

        public void Release()
        {
            Cleanup();
            if (_releasedThisExecution) return;
            _releasedThisExecution = true;

            if (projectilePrefab == null)
            {
                Debug.LogWarning($"[{gameObject.name}] ThrowProjectileEmitter 未綁定 projectile prefab。", this);
                return;
            }

            Transform origin = spawnPoint != null ? spawnPoint : transform;
            ThrownProjectile projectile = Instantiate(projectilePrefab, origin.position, transform.rotation);
            projectile.Initialize(projectileSpeed, projectileLifetime, transform.root);
        }

        public void Cleanup()
        {
            if (heldVisual != null) heldVisual.SetActive(false);
        }

        private void OnDisable() => Cleanup();
    }
}
