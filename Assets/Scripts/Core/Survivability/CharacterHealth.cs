using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Core.Survivability
{
    /// <summary>
    /// 角色的生命值與「被打之後會發生什麼」的**唯一決定者**（ADR-009 D1／D2）。
    /// 掛在角色 **Root**（與 <c>CharacterController</c> 同一顆），玩家與敵人共用同一顆元件——
    /// 生存語意不分陣營。
    ///
    /// <para><b>資料流（`docs/26` §I，2026-09-14 使用者裁決）</b></para>
    /// <code>
    /// Damage source → ApplyDamage → commit → SurvivabilityData
    ///                                          ├ 未致死：JustTookDamage = true → HurtState eligibility
    ///                                          └ 致死　：IsDead        = true → DeathState eligibility
    /// </code>
    /// **兩種後果由同一個發布動作表達**，因此「受擊」與「死亡」不可能在同一幀互相競爭。
    ///
    /// <para><b>為什麼後果由這裡決定，而不是攻擊方 sink</b></para>
    /// 攻擊方知道「我打中了、打多少」，**不知道**「這一下會不會致死」。
    /// 讓不掌握資訊的一方做決定，就會在致死幀出現「受擊動畫先播、死亡再蓋上去」。
    ///
    /// <para><b>⚰️ 已退役的舊路徑</b></para>
    /// 2026-09-14 之前本類別會呼叫 <c>ActionRequestTarget.RequestAction(ActionSlot.Reaction)</c>，
    /// 讓受擊走 <c>ActionState</c>。那條路已由 <c>StateType.Hurt</c> 取代——
    /// **本類別不再認識 `ActionRequestTarget`、不再認識任何 Action 概念**（`docs/26`）。
    ///
    /// <para><b>⚠️ 不依賴 <c>Awake</c></b></para>
    /// EditMode 測試與 <c>AddComponent</c> 都不會呼叫 <c>Awake</c>
    /// （本 repo 踩過：commit 58b79ca「sibling 快取為 null ⇒ 三條測試假性失敗」）。
    /// 初始化一律走 <see cref="EnsureInitialized"/> 的惰性路徑。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterHealth : MonoBehaviour
    {
        [Tooltip("生命值上限。placeholder 數值——本輪不做平衡，但它必須可在此調整，不得寫死在程式裡。")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;

        private float _currentHealth;
        private bool _isDead;
        private bool _initialized;

        /// <summary>
        /// 尚未發布的「本次受到未致死傷害」。由 <see cref="PublishTo"/> 寫出後立即清除。
        /// ⚠️ 同一幀內多次命中只會抬起一次 ⇒ 一次硬直。這與舊 mailbox 的單格語意一致，是刻意的。
        /// </summary>
        private bool _pendingHurt;

        public float MaxHealth => maxHealth;

        public float CurrentHealth
        {
            get
            {
                EnsureInitialized();
                return _currentHealth;
            }
        }

        /// <summary>已 commit 的死亡判定。<see cref="SurvivabilityData.IsDead"/> 的來源。</summary>
        public bool IsDead
        {
            get
            {
                EnsureInitialized();
                return _isDead;
            }
        }

        /// <summary>
        /// 施加傷害。**這是本元件唯一的輸入**——攻擊方只說「打多少」，不說「要播什麼」。
        /// </summary>
        /// <returns>本次呼叫是否真的結算了傷害（已死亡／非正數傷害 ⇒ false）。</returns>
        public bool ApplyDamage(float amount)
        {
            EnsureInitialized();

            // 已死亡不再結算：屍體不會再踉蹌，也不會把生命值扣成負數。
            if (_isDead) return false;
            if (!(amount > 0f) || float.IsNaN(amount) || float.IsInfinity(amount)) return false;

            _currentHealth = Mathf.Max(0f, _currentHealth - amount);

            if (_currentHealth <= 0f)
            {
                // 致死：commit 死亡，**不抬起 _pendingHurt**。
                // 抬了會讓 Hurt 與 Death 在同一幀爭同一顆 FSM。
                _isDead = true;
                _pendingHurt = false;
                return true;
            }

            // 未致死：commit 一次受擊事件，交給 HurtState 決定要不要播
            // （被 Roll／Traversal 擋掉時就是丟棄，不補播——見 SurvivabilityData.JustTookDamage）。
            _pendingHurt = true;
            return true;
        }

        /// <summary>
        /// 🆕（2026-09-14，respawn）把角色從死亡狀態救回來。
        /// **這是唯一能把 <see cref="IsDead"/> 由 true commit 回 false 的 API。**
        ///
        /// <para><b>⭐ authority ／ orchestration 分離（使用者 2026-09-14 裁決）</b></para>
        /// 本方法**只負責 survivability**：血量與死亡旗標。它**不知道**重生點、傳送、
        /// 速度歸零、collider 還原、或「重生」這個概念存在——那些是
        /// <c>RespawnController</c> 的協調工作，由它在呼叫本方法**之前**完成。
        /// ⇒ 這條線與 ADR-009 D2 同一個理由：讓不掌握資訊的一方做決定，就會多出一個真相來源。
        ///
        /// <para><b>FSM 怎麼知道？——它不被通知，它只是照常讀黑板</b></para>
        /// 下一次順序 0.5 的 <see cref="PublishTo"/> 會把 <c>IsDead = false</c> 發布出去，
        /// <c>DeathState</c> 於同一幀的順序 4 看到後自然放行（`CanTransitionAway => !IsDead`）。
        /// ⛔ 本方法**不**呼叫 FSM、不送事件、不認識 <c>StateType</c>。
        ///
        /// <para><b>⚠️ 重生不是受擊</b></para>
        /// 一併清掉 <see cref="_pendingHurt"/>：死亡那一幀的致死傷害不會抬起它，但若在
        /// 「受擊 pending 尚未發布」與「死亡」之間重生，殘留的旗標會讓角色一復活就踉蹌一下。
        /// </summary>
        /// <returns>本次呼叫是否真的復活了（本來就活著 ⇒ false，呼叫端可據此避免重複播效果）。</returns>
        public bool Revive()
        {
            EnsureInitialized();
            if (!_isDead) return false;

            _currentHealth = maxHealth;
            _isDead = false;
            _pendingHurt = false;
            return true;
        }

        /// <summary>
        /// 把本元件持有的真相發布到黑板。**`PlayerRuntimeData.Survivability` 的唯一寫入者**
        /// （ADR-009 D1；由 <c>ArchitectureRegressionTests.WriterRules</c> 機器化守住）。
        ///
        /// 呼叫者：<c>CharacterPipelineRunner</c> 順序 0.5——必須早於順序 4 的狀態機，
        /// 受擊與死亡才能在**同一幀**被 FSM 看到。
        ///
        /// ⚠️ <see cref="_pendingHurt"/> 在此**寫出後立即清除**；整區每幀覆寫，下一幀自然回 false。
        /// </summary>
        public void PublishTo(PlayerRuntimeData data)
        {
            if (data == null) return;
            EnsureInitialized();
            data.Survivability.CurrentHealth = _currentHealth;
            data.Survivability.MaxHealth = maxHealth;
            data.Survivability.IsDead = _isDead;
            data.Survivability.JustTookDamage = _pendingHurt;
            _pendingHurt = false;
        }

        /// <summary>測試用的確定性初始狀態設定；正式路徑一律由 <see cref="EnsureInitialized"/> 帶起。</summary>
        internal void InitializeForTest(float startingHealth)
        {
            _initialized = true;
            _currentHealth = Mathf.Max(0f, startingHealth);
            _isDead = _currentHealth <= 0f;
            _pendingHurt = false;
        }

        /// <summary>測試用：檢查尚未發布的受擊事件。</summary>
        internal bool HasPendingHurt => _pendingHurt;

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _currentHealth = maxHealth;
            _isDead = false;
            _pendingHurt = false;
        }
    }
}
