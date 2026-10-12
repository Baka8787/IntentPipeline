using UnityEngine;
using Project.Core.Actions;
using Project.Core.Survivability;
using Project.Presentation.Audio;

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

        // 🆕（2026-09-17，戰鬥音效）**命中音**。與 damage 同一種配置慣例：數值／資產在 Inspector，不在程式裡。
        // ⚠️ 這是 **Hit**（打到東西了），不是 **Swing**（揮了一下）——
        //    揮擊聲掛在同一個 slot 的 ActionAudioSink.releaseDefinition 上，**必定**會響；
        //    本欄位只在真的結算了傷害時才響。兩者是不同的事件，刻意不合併。
        [Header("Audio（🆕 2026-09-17）")]
        [Tooltip("命中並結算傷害時播放。留空 ⇒ 無命中音。")]
        [SerializeField] private AudioDefinitionSO hitAudio;

        [Tooltip("本角色的音效輸出。留空 ⇒ Awake 時沿階層向上尋找。")]
        [SerializeField] private AudioController audioController;

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
            if (audioController == null) audioController = GetComponentInParent<AudioController>();
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

        /// <returns>
        /// 這一下是否為**有效命中**（命中當下目標仍活著或沒有生命值）。🔄 2026-10-12：
        /// 打屍體回 false（不播命中音），與 <c>ThrownProjectile.TryRequestHit</c> 同義；
        /// 屍體仍會登記進去重表，同一次揮擊不再重複處理它。
        /// </returns>
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

            // 🆕（2026-10-12，`docs/54` §3.3，Q3a）**Effect Eligibility**：命中**當下**目標是否還活著。
            // 必須在 ApplyDamage **之前**取——致死的那一擊是有效命中（要有命中音），
            // 打一具屍體則不是（與投射物「命中屍體不算一次命中」同義）。
            // ⚠️ 這不是 Damage Eligibility：扣不扣血仍只由 CharacterHealth.ApplyDamage 回答，下一行照常呼叫。
            bool validHit = CharacterHealth.IsAliveOrUntracked(health);

            // 🆕（ADR-009 D2）近戰與投射物、地面 AoE 走同一條新 seam：只送傷害，
            // 「要播受擊還是要死」由持有生命值的一方決定。
            health.ApplyDamage(damage);
            if (!validHit) return false;

            // 🆕（2026-09-17）命中音在**被打者身上**播，不在武器上——
            // 「打到東西」這件事在空間上發生在接觸的那一側，而去重迴圈已保證每個目標只響一次。
            // ⚠️ 用 `health.transform.position`（被打者 root）而非 collider 接觸點：
            //    `OnTriggerEnter` 沒有給我們接觸點，而編一個出來就是在發明資料。
            //    近戰距離下 root 與接觸點的差距聽不出來；真的需要精確接觸點時
            //    要換成 `OnTriggerStay` ＋ `ClosestPoint`，那是獨立的一件事。
            if (audioController != null) audioController.PlayAt(hitAudio, health.transform.position);

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
