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

        private const float MinAimSqrDistance = 0.000001f;

        public void Begin()
        {
            if (heldVisual != null) heldVisual.SetActive(true);
        }

        /// <summary>
        /// 🐞 **2026-09-05 移除本方法原有的 <c>_releasedThisExecution</c> 去重**。
        ///
        /// 它是 ADR-004 期的第二道保險：`ActionState` 已經以 `_releaseEmittedThisExecution`
        /// 保證「一次執行只發一次 Release」，這裡再擋一次是多餘的。
        ///
        /// **連段（docs/11 §4.3）讓這層多餘變成 bug**：連段刻意每段各發一次 Release
        /// （每段各出一顆投射物），但本地旗標只在 `Begin()` 重置、而 `Begin()` 一次執行只呼叫一次
        /// ⇒ 第 2、3 段的 Release **被這裡靜默吞掉**，畫面上只會看到第一顆。
        ///
        /// ⚖️ 修法選擇：不是「加一個 per-segment 重置的回呼」，而是**移除這層去重**——
        /// release 時點的唯一權威是 `ActionState`（ADR-004 D2）。sink 自帶第二套判斷正是
        /// 該條決策要防的「第二個權威」，它在這裡具體害了一次。
        /// </summary>
        public void Release(in ActionReleaseContext context)
        {
            Cleanup();

            if (projectilePrefab == null)
            {
                Debug.LogWarning($"[{gameObject.name}] ThrowProjectileEmitter 未綁定 projectile prefab。", this);
                return;
            }

            Transform origin = spawnPoint != null ? spawnPoint : transform;
            Quaternion fallback = transform.rotation;
            Quaternion rotation = context.HasAim
                ? ComputeThrowRotation(origin.position, context.AimPoint, fallback)
                : fallback;
            ThrownProjectile projectile = Instantiate(projectilePrefab, origin.position, rotation);
            projectile.Initialize(projectileSpeed, projectileLifetime, transform.root);
        }

        internal static Quaternion ComputeThrowRotation(Vector3 spawn, Vector3 aimPoint, Quaternion fallback)
        {
            Vector3 direction = aimPoint - spawn;
            return direction.sqrMagnitude > MinAimSqrDistance
                ? Quaternion.LookRotation(direction.normalized)
                : fallback;
        }

        public void Cleanup()
        {
            if (heldVisual != null) heldVisual.SetActive(false);
        }

        private void OnDisable() => Cleanup();
    }
}
