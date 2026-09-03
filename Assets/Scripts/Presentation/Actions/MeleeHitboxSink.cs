using Project.Core.Actions;
using UnityEngine;

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

        // 固定容量只在元件建立時配置一次；命中窗內不用 HashSet／List，因此多 collider 命中也不產生 GC。
        // 若極端情況超過容量，直接關窗比失去去重能力更安全：寧可漏掉第 17 個目標，也不能重複結算。
        private readonly ActionRequestTarget[] _hitTargets = new ActionRequestTarget[MaxTargetsPerSwing];
        private int _hitTargetCount;
        private bool _releasedThisExecution;
        private bool _windowOpen;

        private void Awake()
        {
            if (hitbox == null) hitbox = GetComponent<Collider>();
            Cleanup();
        }

        public void Begin()
        {
            Cleanup();
            _releasedThisExecution = false;
            _hitTargetCount = 0;
        }

        public void Release()
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

            ActionRequestTarget target = other.GetComponentInParent<ActionRequestTarget>();
            if (target == null || target.transform.root == transform.root) return;
            TryRequestHit(target);
        }

        internal bool TryRequestHit(ActionRequestTarget target)
        {
            if (!_windowOpen || target == null) return false;

            for (int i = 0; i < _hitTargetCount; i++)
            {
                if (_hitTargets[i] == target) return false;
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

            _hitTargets[_hitTargetCount++] = target;
            target.RequestAction(ActionSlot.Reaction);
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
