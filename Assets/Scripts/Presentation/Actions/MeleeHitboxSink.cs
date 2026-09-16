using UnityEngine;
using Project.Core.Actions;
using Project.Core.Survivability;

namespace Project.Presentation.Actions
{
    /// <summary>
    /// 近戰 Action lifecycle 的 Unity side-effect adapter。命中窗只由 ActionState 的 Release 開啟，
    /// 不觀察動畫、VFX 或 particle collision；Cleanup 則是所有結束／中斷路徑共用的關窗點。
    /// </summary>
    public sealed class MeleeHitboxSink : MonoBehaviour, IActionLifecycleSink
    {
        private const int MaxTargetsPerSwing = 16;

        [SerializeField] private Collider hitbox;

        // 🆕（ADR-009 D2）揮擊傷害。數值在資產上，不在程式裡（同 ThrownProjectile.damage）。
        [SerializeField, Min(0f)] private float damage = 10f;

        // 固定容量只在元件建立時配置一次；命中窗內不用 HashSet／List，因此多 collider 命中也不產生 GC。
        // 若極端情況超過容量，直接關窗比失去去重能力更安全：寧可漏掉第 17 個目標，也不能重複結算。
        // 🔄（ADR-009 D2）去重鍵由 ActionRequestTarget 改為 CharacterHealth——收件者換了，鍵就得跟著換。
        private readonly CharacterHealth[] _hitTargets = new CharacterHealth[MaxTargetsPerSwing];
        private int _hitTargetCount;
        private bool _releasedThisExecution;
        private bool _windowOpen;

        private void Awake()
        {
            ResolveHitbox();
            Cleanup();
        }

        /// <summary>
        /// 補齊未在 Inspector 指派的 <c>hitbox</c>（退回同物件上的 Collider）。
        /// **與 <see cref="Awake"/> 分開，是為了 EditMode 測試**——EditMode 不在 Play mode，
        /// `AddComponent` **不會呼叫 `Awake`** ⇒ `hitbox` 恆為 null ⇒ `Release(in context)` 只會噴
        /// 「未綁定 Collider」警告、命中窗永遠開不了（2026-09-04 首跑實際踩到，T24 因此紅）。
        ///
        /// ⚠️ 不改變 production 行為：正式流程仍走 <see cref="Awake"/>，且 Inspector 已指派時本方法不覆寫。
        /// </summary>
        internal void ResolveHitbox()
        {
            if (hitbox == null) hitbox = GetComponent<Collider>();
        }

        public void Begin()
        {
            Cleanup();
            _releasedThisExecution = false;
            _hitTargetCount = 0;
        }

        public void Release(in ActionReleaseContext context)
        {
            if (_releasedThisExecution) return;
            _releasedThisExecution = true;
            if (hitbox == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{gameObject.name}] MeleeHitboxSink 未綁定 Collider；命中窗無法開啟。", this);
#endif
                return;
            }

            _windowOpen = true;
            hitbox.enabled = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_windowOpen || other == null) return;

            CharacterHealth health = other.GetComponentInParent<CharacterHealth>();
            if (health == null || health.transform.root == transform.root) return;
            TryRequestHit(health);
        }

        internal bool TryRequestHit(CharacterHealth health)
        {
            if (!_windowOpen || health == null) return false;

            for (int i = 0; i < _hitTargetCount; i++)
            {
                if (_hitTargets[i] == health) return false;
            }

            if (_hitTargetCount >= _hitTargets.Length)
            {
                Cleanup();
#if UNITY_EDITOR
                Debug.LogWarning($"[{gameObject.name}] 單次揮擊超過 {MaxTargetsPerSwing} 個目標；" +
                                 "命中窗已提前關閉以維持每目標只結算一次。", this);
#endif
                return false;
            }

            _hitTargets[_hitTargetCount++] = health;
            // 🆕（ADR-009 D2）近戰與投射物、地面 AoE 走同一條新 seam：只送傷害，
            // 「要播受擊還是要死」由持有生命值的一方決定。
            health.ApplyDamage(damage);
            return true;
        }

        public void Cleanup()
        {
            _windowOpen = false;
            if (hitbox != null) hitbox.enabled = false;
        }

        private void OnDisable() => Cleanup();
    }
}
