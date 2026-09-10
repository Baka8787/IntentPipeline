using Project.Core.Actions;
using Project.Core.Effects;
using UnityEngine;

namespace Project.Presentation.Actions
{
    /// <summary>
    /// 地面 AoE Action lifecycle 的 Unity side-effect adapter：Release 時在地面生成一次性效果，
    /// 並對半徑內的目標投遞既有的 Reaction ＋ Slow。
    ///
    /// 🎯 **存在的理由是證明 execution shape 可以不同**（`docs/11` §4.4）。Fireball 走 projectile，
    /// Ice 走地面爆發——兩者是**同一個 `IActionLifecycleSink` 的兩個實作**，
    /// `ActionState`／`ActionDefinitionSO`／輸入層一行都沒有為此改動。
    ///
    /// ⛔ **這不是 AoE framework，也不是 Targeting framework。**
    /// 沒有形狀抽象（只有球）、沒有效果管線（只有既有的 Slow）、沒有目標選擇策略
    /// （只有「瞄準點夾距離後往下探地」）。第二個地面技能出現前不擴充。
    /// </summary>
    public sealed class GroundEffectSink : MonoBehaviour, IActionLifecycleSink
    {
        /// <summary>固定容量在元件建立時配置一次；施放期間不用 List／HashSet，因此多目標命中不產生 GC。</summary>
        private const int MaxTargetsPerCast = 16;

        [Tooltip("在地面生成的一次性視覺（例如 Human_Spell_Ice）。粒子需自帶 Play On Awake——" +
                 "本元件刻意不觸碰任何播放 API（外部 seam 不得持有動畫權威）。")]
        [SerializeField] private GameObject effectPrefab;

        [Tooltip("落點沿段落邊界承諾的瞄準方向、距施法者多遠。無瞄準承諾時才退回角色正前方。")]
        [SerializeField, Min(0f)] private float castDistance = 0.4f;
        [SerializeField, Min(0f)] private float effectRadius = 3f;
        [SerializeField, Min(0.01f)] private float slowDuration = 3f;
        [SerializeField, Min(0.01f)] private float effectLifetime = 3f;

        [Tooltip("往下探地時視為「地面」的層。")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("AoE 取樣的層。實際是否受影響仍取決於對方身上有沒有 ActionRequestTarget／效果元件。")]
        [SerializeField] private LayerMask targetMask = ~0;

        [Tooltip("探地射線的起點高度（自水平落點往上抬多少）。")]
        [SerializeField, Min(0.01f)] private float groundProbeHeight = 5f;

        private readonly Collider[] _overlapBuffer = new Collider[MaxTargetsPerCast];
        private readonly RaycastHit[] _groundProbeBuffer = new RaycastHit[MaxTargetsPerCast];
        private readonly Transform[] _hitRoots = new Transform[MaxTargetsPerCast];
        private int _hitRootCount;

        public void Begin() => _hitRootCount = 0;

        /// <summary>
        /// 一次 Release ＝ 一次爆發。去重清單在**每次 Release 開頭**重置而不是只在 <see cref="Begin"/>——
        /// 這樣連段技能的每一段都是獨立的一次結算。
        ///
        /// 📌 這一點是 2026-09-05 的教訓：`ThrowProjectileEmitter` 原本把「一次執行只發一次」
        /// 記在 sink 自己身上，連段上線後直接吞掉第 2、3 段（`docs/11` §4.3）。
        /// **sink 不保存跨 Release 的節流狀態**，那是 `ActionState` 的職責。
        /// </summary>
        public void Release(in ActionReleaseContext context)
        {
            _hitRootCount = 0;

            if (!TryResolveGroundPoint(in context, out Vector3 center)) return;

            SpawnVisual(center);
            ApplyToTargetsAround(center);
        }

        public void Cleanup() { }

        /// <summary>
        /// 落點 ＝ 段落邊界承諾的 AimPoint 所指水平世界方向 × 固定距離，再往下探地。
        ///
        /// AimPoint 只決定方向、<c>castDistance</c> 仍決定「從玩家身前長出來」的距離；高度一律交給探地。
        /// 這份方向與 Action facing 來自同一個 <see cref="ActionReleaseContext"/>，不再假設
        /// <c>casterRoot.forward</c> 碰巧指著目標。只有 HasAim=false 時才保留既有 forward fallback。
        /// </summary>
        private bool TryResolveGroundPoint(in ActionReleaseContext context, out Vector3 point)
        {
            Transform casterRoot = transform.root;
            float distance = Mathf.Max(0f, castDistance);
            Vector3 horizontalOffset;
            if (context.HasAim)
            {
                horizontalOffset = context.AimPoint - casterRoot.position;
                horizontalOffset.y = 0f;
                horizontalOffset = horizontalOffset.sqrMagnitude > 0f
                    ? horizontalOffset.normalized * distance
                    : Vector3.zero;
            }
            else
            {
                horizontalOffset = casterRoot.forward * distance;
            }

            Vector3 flat = casterRoot.position + horizontalOffset;

            if (TryProbeGround(flat, out point)) return true;

            // 探不到地就用水平落點：寧可高度不完美，也不要整個技能靜默消失。
            point = flat;
            return true;
        }

        /// <summary>
        /// 由上往下找地面。
        ///
        /// 🐞 **2026-09-05 Play 修正——「冰刺飄在空中」的成因**：原本用單發 `Physics.Raycast`
        /// 取第一個命中，而 `groundMask` 預設是 Everything ⇒ **站在落點上的敵人就是第一個命中**，
        /// 冰刺於是長在牠頭頂。
        ///
        /// ⇒ 改為取「最近的**非角色**命中」。角色的判準沿用既有標記 `ActionRequestTarget`
        /// （＋排除施法者自己），**沒有新建「什麼算地面」的分類系統**。
        /// 📌 `groundMask` 仍建議在 Inspector 收斂成真正的地面層——這裡的過濾是防線，不是藉口。
        /// </summary>
        private bool TryProbeGround(Vector3 flat, out Vector3 point)
        {
            point = flat;

            Vector3 probeStart = flat + Vector3.up * groundProbeHeight;
            int count = Physics.RaycastNonAlloc(
                probeStart, Vector3.down, _groundProbeBuffer, groundProbeHeight * 2f,
                groundMask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float closest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = _groundProbeBuffer[i].collider;
                if (hitCollider == null) continue;

                // 角色不是地面。
                if (hitCollider.transform.root == transform.root) continue;
                if (hitCollider.GetComponentInParent<ActionRequestTarget>() != null) continue;

                float hitDistance = _groundProbeBuffer[i].distance;
                if (hitDistance >= closest) continue;

                closest = hitDistance;
                point = _groundProbeBuffer[i].point;
                found = true;
            }

            return found;
        }

        private void SpawnVisual(Vector3 center)
        {
            if (effectPrefab == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{gameObject.name}] GroundEffectSink 未綁定 effect prefab；只會有判定沒有視覺。", this);
#endif
                return;
            }

            GameObject instance = Instantiate(effectPrefab, center, ResolveEffectRotation(center));
            if (Application.isPlaying) Destroy(instance, Mathf.Max(0.01f, effectLifetime));
        }

        /// <summary>
        /// 🐞 **2026-09-05 Play 修正**：原本用 `Quaternion.identity` 生成。
        /// `Human_Spell_Ice` 是**有方向性的**（冰刺沿自身 +Z 排成一列），identity 讓它永遠指向世界 +Z
        /// ⇒ 不管角色朝哪、法術打哪，冰刺都以同一個世界方向長出來，畫面上就是「歪的」。
        ///
        /// 改為朝「施法者 → 落點」的水平方向。落點就在腳下（方向退化）時保留施法者朝向，
        /// 避免 `LookRotation(zero)` 的未定義行為。
        /// </summary>
        private Quaternion ResolveEffectRotation(Vector3 center)
        {
            Vector3 direction = center - transform.root.position;
            direction.y = 0f;

            return direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized)
                : transform.root.rotation;
        }

        private void ApplyToTargetsAround(Vector3 center)
        {
            int count = Physics.OverlapSphereNonAlloc(
                center, effectRadius, _overlapBuffer, targetMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider other = _overlapBuffer[i];
                if (other == null) continue;

                Transform otherRoot = other.transform.root;
                if (otherRoot == transform.root) continue;   // 不打自己
                if (!TryRegisterRoot(otherRoot)) continue;   // 同一個目標一次爆發只結算一次

                ApplyToRoot(other);
            }
        }

        /// <summary>
        /// 投遞的兩件事都是**既有**機制：Reaction 走 `ActionRequestTarget`（與投射物、近戰同一條鏈），
        /// Slow 走 `TemporaryGameplayEffectState`。本元件不認識傷害、血量或狀態堆疊。
        /// </summary>
        internal void ApplyToRoot(Collider other)
        {
            ActionRequestTarget target = other.GetComponentInParent<ActionRequestTarget>();
            if (target != null) target.RequestAction(ActionSlot.Reaction);

            TemporaryGameplayEffectState effectState = other.GetComponentInParent<TemporaryGameplayEffectState>();
            if (effectState != null)
            {
                effectState.ApplySlow(
                    TemporaryGameplayEffectState.SlowMovementSpeedMultiplier, slowDuration);
            }
        }

        /// <summary>
        /// 每個目標一次爆發只結算一次。<c>internal</c> 是為了讓 EditMode 能驗去重——
        /// 完整路徑要經 <c>Physics.OverlapSphere</c>，而 EditMode 沒有物理場景可依賴。
        /// </summary>
        internal bool TryRegisterRoot(Transform root)
        {
            for (int i = 0; i < _hitRootCount; i++)
            {
                if (_hitRoots[i] == root) return false;
            }

            if (_hitRootCount >= _hitRoots.Length)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{gameObject.name}] 單次爆發超過 {MaxTargetsPerCast} 個目標；" +
                                 "超出的目標本次不結算，以維持每目標只結算一次。", this);
#endif
                return false;
            }

            _hitRoots[_hitRootCount++] = root;
            return true;
        }

        /// <summary>測試與 Inspector 調參用的顯式 fallback 版本；production 走 <see cref="Release"/>。</summary>
        internal bool TryResolveGroundPointForTests(out Vector3 point)
        {
            ActionReleaseContext context = default;
            return TryResolveGroundPoint(in context, out point);
        }

        private void OnDrawGizmosSelected()
        {
            ActionReleaseContext context = default;
            if (!TryResolveGroundPoint(in context, out Vector3 center)) return;
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(center, effectRadius);
        }
    }
}
