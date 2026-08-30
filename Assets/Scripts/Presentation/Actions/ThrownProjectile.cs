using UnityEngine;
using Project.Core.Actions;

namespace Project.Presentation.Actions
{
    /// <summary>
    /// 最小 Throw projectile：直線飛行、命中提交 external Action request、逾時銷毀。
    /// 不認識 FSM、ActionDefinition 或 AnimationFacade。
    /// </summary>
    public sealed class ThrownProjectile : MonoBehaviour
    {
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
            if (target != null)
            {
                TryRequestHit(target);
                return;
            }

            Complete();
        }

        /// <summary>供確定性測試與碰撞入口共用；成功後同一 projectile 不會再次提交。</summary>
        public bool TryRequestHit(ActionRequestTarget target)
        {
            if (_completed || target == null) return false;

            _completed = true;
            target.RequestAction();
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
