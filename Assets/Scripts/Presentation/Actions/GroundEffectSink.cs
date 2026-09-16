using Project.Core.Actions;
using Project.Core.Effects;
using Project.Core.Survivability;
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

        // 🔄 **2026-09-15：判定形狀由「球」改為「沿施法方向的膠囊」。**
        //
        // 🐞 舊形狀是錯的，而且錯得很安靜：`Human_Spell_Ice` 的六排冰刺沿 local +Z 排在
        //    0.58／1.15／1.87／2.66／3.42／4.42 m，**左右只有 ±0.17 m**——它是一條**線**。
        //    舊判定卻是以身前 0.4 m 為心、半徑 3 m 的**球**：
        //      前緣只到 0.4 + 3 = 3.4 m ⇒ **最後兩排冰刺（3.82／4.82 m）完全在判定外**
        //      （使用者 Play 回報：「冰刺中了但沒生效」；debug gizmo 顯示 overlap 查到 0 個 collider）
        //      同時左右與**背後** 3 m 內卻都會被打到——和「前方一列冰刺」的語意完全相反。
        //
        // ⛔ **不可以只把 effectRadius 加大到 4.42**：那會讓背後與側面的打擊範圍一起變成 4.42 m。
        //    形狀錯了就要換形狀，不是把錯的形狀放大。
        //
        // 📌 數值的來源是 VFX 本身的 emitter 佈局（見上），不是手感猜測；
        //    若日後換掉 ice VFX，這三個值要跟著該素材重新量。
        [Tooltip("判定膠囊的橫向半徑。涵蓋冰刺 ±0.17m 的排列偏移 ＋ 冰刺 mesh 本身的寬度。")]
        [SerializeField, Min(0f)] private float effectRadius = 0.7f;
        [Tooltip("判定膠囊沿施法方向的**起點**（相對落點）。對齊第一排冰刺。")]
        [SerializeField, Min(0f)] private float effectNearReach = 0.58f;
        [Tooltip("判定膠囊沿施法方向的**終點**（相對落點）。對齊最後一排冰刺。")]
        [SerializeField, Min(0f)] private float effectFarReach = 4.42f;
        [SerializeField, Min(0.01f)] private float slowDuration = 3f;

        // 🆕（ADR-009 D2）爆發傷害。數值在資產上，不在程式裡（同 ThrownProjectile／MeleeHitboxSink）。
        [SerializeField, Min(0f)] private float damage = 10f;
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

            Quaternion rotation = ResolveEffectRotation(center);
            SpawnVisual(center, rotation);
            ApplyToTargetsAround(center, rotation * Vector3.forward);
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

        /// <summary>
        /// 判定膠囊的兩個端點。**端點抬高一個 radius**，讓膠囊自地面向上撐開約 2×radius
        /// ⇒ 站立角色（CharacterController 高 1.83）落在覆蓋範圍內，而不是只掃到腳踝。
        /// </summary>
        internal void ResolveHitCapsule(Vector3 center, Vector3 forward, out Vector3 start, out Vector3 end)
        {
            // ⛔ **方向只能來自呼叫端交付的承諾**（A29：sink 不得自行解算方向權威）。
            // 退化時產生零長度膠囊（＝一顆球），而不是就地發明一個朝向。
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.zero;

            Vector3 lift = Vector3.up * effectRadius;
            float near = Mathf.Min(effectNearReach, effectFarReach);
            float far = Mathf.Max(effectNearReach, effectFarReach);
            start = center + flat * near + lift;
            end = center + flat * far + lift;
        }

        private void SpawnVisual(Vector3 center, Quaternion rotation)
        {
            if (effectPrefab == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{gameObject.name}] GroundEffectSink 未綁定 effect prefab；只會有判定沒有視覺。", this);
#endif
                return;
            }

            GameObject instance = Instantiate(effectPrefab, center, rotation);
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

        private void ApplyToTargetsAround(Vector3 center, Vector3 forward)
        {
            ResolveHitCapsule(center, forward, out Vector3 capsuleStart, out Vector3 capsuleEnd);
            int count = Physics.OverlapCapsuleNonAlloc(
                capsuleStart, capsuleEnd, effectRadius, _overlapBuffer, targetMask,
                QueryTriggerInteraction.Collide);

#if UNITY_EDITOR
            // 記錄**這一次真正送進 OverlapSphere 的中心與結果**（不是 gizmo 事後重算的預測值）。
            _debugLastOverlapCenter = center;
            _debugCapsuleStart = capsuleStart;
            _debugCapsuleEnd = capsuleEnd;
            _debugLastOverlapCount = count;
            _debugLastOverlapTime = Time.time;
            _debugHasOverlap = true;
#endif

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
        /// 投遞的兩件事都是**既有**機制：傷害走 `CharacterHealth`（與投射物、近戰同一條鏈），
        /// Slow 走 `TemporaryGameplayEffectState`。
        ///
        /// 🔄（ADR-009 D2）原本這裡直接呼叫 `RequestAction(ActionSlot.Reaction)`。
        /// 改成只送傷害之後，本元件**依然**不認識血量或狀態堆疊——它只知道「打多少」，
        /// 「要播受擊還是要死」由 `CharacterHealth` 決定。受擊鏈路本身一字未改。
        /// </summary>
        internal void ApplyToRoot(Collider other)
        {
            CharacterHealth health = other.GetComponentInParent<CharacterHealth>();
            if (health != null) health.ApplyDamage(damage);

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

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            ActionReleaseContext context = default;
            if (!TryResolveGroundPoint(in context, out Vector3 center)) return;
            ResolveHitCapsule(center, ResolveEffectRotation(center) * Vector3.forward,
                out Vector3 start, out Vector3 end);
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(start, effectRadius);
            Gizmos.DrawWireSphere(end, effectRadius);
            Gizmos.DrawLine(start, end);
        }

        // ⚠️ **Editor-only 診斷，不參與判定。**
        //
        // 上面那個 `OnDrawGizmosSelected` 畫的是**事後用 default context 重算的預測中心**，
        // 而且只在選取時顯示 ⇒ 它回答不了「這一發到底在哪裡查的、查到幾個」。
        // 2026-09-15 使用者回報「冰刺視覺上命中但實際不會中」——要分辨
        // 「中心算錯」與「中心對但沒查到目標」，就必須看**實際送進 OverlapSphere 的那一組值**。
        private Vector3 _debugLastOverlapCenter;
        private Vector3 _debugCapsuleStart;
        private Vector3 _debugCapsuleEnd;
        private int _debugLastOverlapCount;
        private float _debugLastOverlapTime;
        private bool _debugHasOverlap;

        [Header("Debug（Editor-only）")]
        [Tooltip("把最近一次 OverlapSphere 的實際中心與半徑畫出來，並標示查到幾個 collider。")]
        [SerializeField] private bool drawOverlapDebug = true;
        [SerializeField, Min(0.1f)] private float overlapDebugPersistSeconds = 3f;

        /// <summary>點到膠囊軸線的最短距離——判定是否落在膠囊內的正確量度（不是到中心點的距離）。</summary>
        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= 0.0001f) return Vector3.Distance(point, a);
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSquared);
            return Vector3.Distance(point, a + ab * t);
        }

        private void OnDrawGizmos()
        {
            if (!drawOverlapDebug || !_debugHasOverlap) return;
            if (Time.time - _debugLastOverlapTime > overlapDebugPersistSeconds) return;

            // 🟢 查到目標／🔴 一個都沒查到。顏色直接把「中心對不對」與「有沒有打到」分開回答：
            //    球畫在敵人身上卻是紅的 ⇒ 中心對、但 overlap 沒收到 ⇒ 問題在 mask／collider／時序。
            //    球根本不在敵人身上 ⇒ 中心就錯了 ⇒ 問題在落點解析。
            Gizmos.color = _debugLastOverlapCount > 0
                ? new Color(0.2f, 1f, 0.4f, 0.9f)
                : new Color(1f, 0.25f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(_debugCapsuleStart, effectRadius);
            Gizmos.DrawWireSphere(_debugCapsuleEnd, effectRadius);
            Gizmos.DrawLine(_debugCapsuleStart, _debugCapsuleEnd);
            Gizmos.DrawLine(_debugLastOverlapCenter, _debugLastOverlapCenter + Vector3.up * 2f);

            // 從施法者拉一條線到判定中心：一眼看出 castDistance 把中心放在哪裡
            // （它只有 0.4 m，而 VFX 的冰刺是**向前排成一列**、會伸得遠得多）。
            Vector3 casterPosition = transform.root.position;
            Gizmos.color = new Color(1f, 1f, 1f, 0.8f);
            Gizmos.DrawLine(casterPosition, _debugLastOverlapCenter);

            // 📊 沒查到目標時，把**最近的敵對標記有多遠**也算出來——
            //    那直接回答「是超出半徑，還是半徑內卻沒收到」。
            float nearest = float.PositiveInfinity;
            var markers = FindObjectsByType<ActionRequestTarget>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] == null || markers[i].transform.root == transform.root) continue;
                float distance = DistanceToSegment(
                    markers[i].transform.position, _debugCapsuleStart, _debugCapsuleEnd);
                if (distance < nearest) nearest = distance;
            }

            string verdict = _debugLastOverlapCount > 0
                ? "命中"
                : float.IsInfinity(nearest)
                    ? "場上沒有敵對標記"
                    : nearest > effectRadius
                        ? $"⚠ 最近目標 {nearest:0.##}m > 半徑 {effectRadius:0.##}m ⇒ **超出判定範圍**"
                        : $"⚠ 最近目標 {nearest:0.##}m 在半徑內卻沒收到 ⇒ 查 mask／collider";

            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.Label(_debugLastOverlapCenter + Vector3.up * 2.1f,
                $"AoE overlap: {_debugLastOverlapCount} collider(s)  r={effectRadius:0.##}\n"
                + $"center {Vector3.Distance(casterPosition, _debugLastOverlapCenter):0.##}m "
                + $"from caster (castDistance={castDistance:0.##})\n"
                + verdict);
        }
#endif
    }
}
