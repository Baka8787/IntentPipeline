using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Survivability;
using UnityEngine;

namespace Project.Core.Combat
{
    /// <summary>
    /// 敵人 CombatContext 的唯一 producer（管線順序 2.4；`docs/54` §3.3.2、§3.4，方案③）。
    /// 與 <see cref="PlayerCombatContextSource"/> 並列：每隻角色恰好一個 combat context producer（ADR-007 D3）。
    ///
    /// <para><b>為什麼從 <c>AIMovementSource</c> 拆出來</b></para>
    /// `docs/19` §3.1 當初讓移動 producer 兼寫 CombatContext，並自訂了重拆條件：
    /// 「若日後敵人的戰鬥語境長出與移動無關的判定，再拆」。目標存活（Q3）與受擊激怒／寬限（Q4）
    /// 正是這類判定——它們需要讀 <c>Project.Core.Survivability</c>，而 <c>Core/Movement</c> 的
    /// 白名單（ADR-003 D2，A4 守）不允許。拆開後每個概念一個 owner：本元件＝目標關係／交戰；
    /// <c>AIMovementSource</c>＝移動意圖；<c>CharacterHealth</c>＝存活。
    ///
    /// <para><b>Target 的唯一權威仍是 <c>AIMovementSource.target</c></b>（`docs/54` §3.3.2-①，T1）</para>
    /// 本元件**不持有**第二份 target，只讀 <see cref="AIMovementSource.Target"/> 這個唯讀屬性。
    /// 那是**參照資料依賴**（讀一個序列化引用），不是呼叫對方的決策邏輯；反方向完全不存在——
    /// <c>AIMovementSource</c> 只經黑板讀本元件的輸出（A4：<c>Core/Movement</c> 不得引用 <c>Project.Core.Combat</c>）。
    /// </summary>
    public sealed class AICombatContextSource : MonoBehaviour
    {
        [Header("Aggro（未受擊；Q4-G＝(i) 逐位元保留）")]
        [SerializeField, Min(0f)] private float aggroEnterRadius = 8f;
        [SerializeField, Min(0f)] private float aggroLeaveRadius = 12f;

        [Header("Provoked（本次交戰受過傷；⚠️ 量測候選值，不是平衡定值）")]
        [Tooltip("本次交戰受過有效傷害後的追擊半徑 L。小於 aggroLeaveRadius 時以後者為下限——受傷不會讓敵人更早放手。")]
        [SerializeField, Min(0f)] private float provokedLeaveRadius = 20f;
        [Tooltip("受過傷的交戰中，目標離開 L 之後還追多久（秒）才放手。寬限內回到 L 內或再受擊 ⇒ 取消。")]
        [SerializeField, Min(0f)] private float provokedGraceSeconds = 5f;

        /// <summary>
        /// 交戰的私有記憶。只有本元件讀寫；黑板上只發布結果（<see cref="CombatContextData"/>）。
        /// 一個 episode ＝ InCombat 由 false 變 true 到再變回 false 的一段。
        /// </summary>
        internal struct EngagementMemory
        {
            public bool InCombat;
            public bool DamagedThisEpisode;
            public bool GraceActive;
            public float GraceStartTime;
        }

        private AIMovementSource _targetSource;
        private EngagementMemory _memory;

        // 目標的生命值元件快取（零 GC）。缺席（null）＝目標沒有生命值，視為活著。
        private Transform _healthResolvedFor;
        private CharacterHealth _targetHealth;

        /// <summary>
        /// 管線順序 2.4——**必須早於**順序 2.5 的 movement intent producer，
        /// 讓 <c>AIMovementSource</c> 讀到**同帧**的交戰結果（脫戰／目標死亡當帧就停止追擊）。
        /// 時間由 Runner 注入，讓寬限規則可確定性測試。
        /// </summary>
        public void Tick(PlayerRuntimeData data, float currentTime)
        {
            if (data == null) return;

            // 自己死亡：**不寫** CombatContext，由各消費者自己收手（`docs/19` §3.1 否決過「死亡時清 CombatContext」；
            // Facing 的 DeathSuppressed、AIMovementSource 的屍體分支都是這個模式）。
            if (data.Survivability.IsDead) return;

            Transform target = ResolveTarget();
            if (target == null || !CharacterHealth.IsAliveOrUntracked(ResolveTargetHealth(target)))
            {
                // 沒有目標，或目標已死（Q3／Q4）：**立即**解除，不給寬限、不留 episode 記憶。
                _memory = default;
                data.CombatContext = default;
                return;
            }

            Vector3 targetPosition = target.position;
            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;
            float distance = Mathf.Sqrt(toTarget.sqrMagnitude);

            _memory = ResolveEngagement(
                _memory,
                distance,
                data.Survivability.JustTookDamage,
                currentTime,
                aggroEnterRadius,
                aggroLeaveRadius,
                provokedLeaveRadius,
                provokedGraceSeconds);

            // 整體覆寫 value struct，不讓下游回頭讀 Transform。
            bool inCombat = _memory.InCombat;
            data.CombatContext = new CombatContextData
            {
                InCombat = inCombat,
                HasTarget = inCombat,
                TargetPosition = inCombat ? targetPosition : Vector3.zero,
            };
        }

        /// <summary>
        /// （純函數）交戰的進入／保持／離開（`docs/54` §3.4）。
        ///
        /// <list type="bullet">
        /// <item>進入：<c>d ≤ enter</c> ∨ 本帧受到有效傷害（任何距離）。</item>
        /// <item>受擊：標記本 episode 受過傷、取消寬限——**任何距離、任何時候都一樣**（政策不看觸發原因）。</item>
        /// <item>保持半徑 R：受過傷 ⇒ L（以 leave 為下限）；否則 leave（8／12 m 的黏性帶，逐位元保留）。</item>
        /// <item>超出 R：從未受傷 ⇒ 立即離開；受過傷 ⇒ 開始寬限，期滿（<c>≥ grace</c>）離開。</item>
        /// <item>離開時清空整份記憶（下一個 episode 從頭算）。</item>
        /// </list>
        /// 目標死亡／缺席不經過這裡——呼叫端在進來之前就整份清空（不給寬限）。
        /// </summary>
        internal static EngagementMemory ResolveEngagement(
            EngagementMemory previous,
            float distance,
            bool tookDamage,
            float currentTime,
            float enterRadius,
            float leaveRadius,
            float provokedLeaveRadius,
            float provokedGraceSeconds)
        {
            float effectiveEnterRadius = Mathf.Max(0f, enterRadius);
            float effectiveLeaveRadius = Mathf.Max(leaveRadius, effectiveEnterRadius);
            float effectiveProvokedRadius = Mathf.Max(provokedLeaveRadius, effectiveLeaveRadius);

            EngagementMemory next = previous;
            if (!previous.InCombat)
            {
                if (distance > effectiveEnterRadius && !tookDamage) return default;
                next = new EngagementMemory { InCombat = true };
            }

            if (tookDamage)
            {
                next.DamagedThisEpisode = true;
                next.GraceActive = false;
            }

            float retentionRadius = next.DamagedThisEpisode ? effectiveProvokedRadius : effectiveLeaveRadius;
            if (distance <= retentionRadius)
            {
                next.GraceActive = false;
                return next;
            }

            // Q4-G＝(i)：未受擊的交戰沒有寬限。
            if (!next.DamagedThisEpisode) return default;

            if (!next.GraceActive)
            {
                next.GraceActive = true;
                next.GraceStartTime = currentTime;
            }

            return currentTime - next.GraceStartTime >= Mathf.Max(0f, provokedGraceSeconds)
                ? default
                : next;
        }

        private Transform ResolveTarget()
        {
            // EditMode 不呼叫 Awake ⇒ 一律惰性解析；找到後沿用（同物件上的元件不會換人）。
            if (_targetSource == null) _targetSource = GetComponent<AIMovementSource>();
            return _targetSource != null ? _targetSource.Target : null;
        }

        private CharacterHealth ResolveTargetHealth(Transform target)
        {
            // 找不到（null）時每次重找：目標可能稍後才掛上生命值元件；找到後只要 target 不變就沿用。
            if (_healthResolvedFor != target || _targetHealth == null)
            {
                _healthResolvedFor = target;
                _targetHealth = target.GetComponentInParent<CharacterHealth>();
            }
            return _targetHealth;
        }
    }
}
