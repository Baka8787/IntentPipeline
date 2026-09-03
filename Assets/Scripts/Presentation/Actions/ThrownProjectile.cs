using UnityEngine;
using Project.Core.Actions;
using Project.Core.Effects;

namespace Project.Presentation.Actions
{
    /// <summary>
    /// 最小 Throw projectile：直線飛行、命中提交 external Action request、逾時銷毀。
    /// 不認識 FSM、ActionDefinition 或 AnimationFacade。
    /// </summary>
    public sealed class ThrownProjectile : MonoBehaviour
    {
        /// <summary>Slow 的倍率＝「速度剩原本的 30%」（`docs/11` §7.4，使用者裁決，不得自行更動）。</summary>
        private const float SlowMovementSpeedMultiplier = 0.3f;

        // ⚠️ `ThrownProjectile` 是**共用**的投射物元件——Throw、Quick Spell、Ice Spell 走同一支程式、
        //    只換 prefab。因此 Slow **必須由資產決定，不能寫死在程式裡**：
        //    `docs/11` §4 明列只有 Ice Spell 投遞 Slow，Throw 與 Quick Spell 命中不減速。
        //    預設 false ⇒ 既有 Throw prefab 不改一個欄位，行為與加 Slow 之前完全相同。
        [SerializeField] private bool appliesSlow;
        [SerializeField, Min(0.01f)] private float slowDuration = 3f;

        private float _speed;
        private float _remainingLifetime;
        private Transform _ownerRoot;
        private bool _completed;

        public void Initialize(float speed, float lifetime, Transform ownerRoot)
        {
            _speed = Mathf.Max(0f, speed);
            _remainingLifetime = Mathf.Max(0.01f, lifetime);
            _ownerRoot = ownerRoot;
            _completed = false;
        }

        private void Update()
        {
            if (_completed || Time.deltaTime <= 0f) return;

            transform.position += transform.forward * (_speed * Time.deltaTime);
            _remainingLifetime -= Time.deltaTime;
            if (_remainingLifetime <= 0f) Complete();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_completed || other == null) return;
            if (_ownerRoot != null && other.transform.IsChildOf(_ownerRoot)) return;

            ActionRequestTarget target = other.GetComponentInParent<ActionRequestTarget>();
            TemporaryGameplayEffectState effectState = other.GetComponentInParent<TemporaryGameplayEffectState>();
            if (target != null || effectState != null)
            {
                TryRequestHit(target, effectState);
                return;
            }

            Complete();
        }

        /// <summary>供確定性測試與碰撞入口共用；成功後同一 projectile 不會再次提交。</summary>
        public bool TryRequestHit(ActionRequestTarget target, TemporaryGameplayEffectState effectState = null)
        {
            if (_completed || (target == null && effectState == null)) return false;

            _completed = true;
            // 🆕（ADR-005 D1）命中提交的是 Reaction 身分——受擊與「我要出手」自此可區分（FU-3）。
            if (target != null) target.RequestAction(ActionSlot.Reaction);

            // Projectile 只投遞「Effect.Slow／0.3 倍／有限時長」，完全不認識目標的移動系統。
            // `appliesSlow` 由 prefab 決定 ⇒ 只有 Ice Spell 的那顆會減速（docs/11 §4）。
            if (appliesSlow && effectState != null)
                effectState.ApplySlow(SlowMovementSpeedMultiplier, slowDuration);
            if (Application.isPlaying) Destroy(gameObject);
            return true;
        }

        private void Complete()
        {
            if (_completed) return;
            _completed = true;
            Destroy(gameObject);
        }
    }
}
