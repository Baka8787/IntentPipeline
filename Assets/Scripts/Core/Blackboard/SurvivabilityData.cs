namespace Project.Core.Blackboard
{
    /// <summary>
    /// 角色的生存狀態（ADR-009 D1）。**持續型**資料：由唯一寫入者
    /// <c>CharacterHealth</c> 每幀整體發布，**不**參與 <see cref="PlayerRuntimeData.ResetTransientState"/>
    /// 的單幀復位——它不是 trigger 邊沿事件，而是「角色現在活著還是死了」這個持續真相。
    ///
    /// <para><b>⚠️ 第一版欄位就是全部（ADR-009 §7 允許 Trial 期修訂，但擴張要有第二個需求）</b></para>
    /// 刻意**不放**：hit direction、damage source、invulnerability／i-frame、death cause、
    /// hit stun 剩餘時間。那些都是**尚未裁決**的資訊；第二個讀者出現前不進黑板
    /// （CLAUDE.md：第二個使用者出現前不得建立 production abstraction）。
    /// </summary>
    public struct SurvivabilityData
    {
        /// <summary>目前生命值。恆 ≥ 0。</summary>
        public float CurrentHealth;

        /// <summary>生命值上限。沒有它，<see cref="CurrentHealth"/> 對任何讀者都不可解讀。</summary>
        public float MaxHealth;

        /// <summary>
        /// 已 **commit** 的死亡判定。
        ///
        /// ⭐ **刻意不是讓讀者自己算 <c>CurrentHealth &lt;= 0</c>。**
        /// 死亡是 <c>CharacterHealth</c> 做出的**決定**（含「已經死了就不再重複結算」這條規則），
        /// 讀者只消費結果。讓下游重新推導已 commit 的狀態，正是
        /// <c>docs/16-review-protocol.md</c> 點名的本專案加重缺陷之一。
        /// </summary>
        public bool IsDead;

        /// <summary>
        /// 🆕（`docs/26` §I，2026-09-14）本幀受到了**未致死**的傷害。
        /// <c>HurtState.CanEnter</c> ／ <c>CanReenter</c> 的唯一准入來源，
        /// 與 <see cref="IsDead"/> → <c>DeathState</c> **完全對稱**（同寫入者、同區、同發布點）。
        ///
        /// <para><b>⚠️ 單幀事件，但不走順序 7 復位</b></para>
        /// <c>CharacterHealth</c> 內部持 pending flag，於順序 0.5 `PublishTo` 寫出後**立即清除**；
        /// 因為 `PublishTo` 每幀**整體覆寫整個區**，下一幀自然回 `false`。
        /// ⇒ 理由與 <see cref="PresentationEventData"/> 一字相同：
        /// **整體覆寫即是它的復位機制**，`ResetTransientState` 刻意不認識本欄位。
        ///
        /// <para><b>⭐ 不排隊、不補播——這是語意，不是簡化</b></para>
        /// 被 Roll／Traversal 擋掉的那一次受擊反應**就是要被丟棄**：
        /// 翻滾的無敵幀結束後補播一次踉蹌是錯的。傷害與扣血照常成立（那是
        /// <c>CharacterHealth</c> 的事），被丟棄的只有**硬直反應**（Hurt 狀態）。
        /// 🔄（2026-10-12，使用者裁決 Q6）受擊**事件**本身的表現照常：`AudioController` 的受擊語音
        /// 讀的是本旗標、不是 FSM 狀態 ⇒ 翻滾中被打仍會出聲（`CombatAudioTests.CA11` 守）。
        /// 若改用單調計數器，`CanEnter` 會在 Roll 結束後才變真 ⇒ 正好做出錯的行為。
        ///
        /// <para><b>致死時為 <c>false</c></b></para>
        /// 致死走 <see cref="IsDead"/>，**不**同時抬起本旗標——否則 Hurt 與 Death
        /// 會在同一幀競爭同一顆 FSM（ADR-009 D2 的決策擁有權）。
        /// </summary>
        public bool JustTookDamage;

        /// <summary>
        /// 🆕（2026-09-17，戰鬥音效）本幀**由活著轉為死亡**的那一次邊沿。
        /// 與 <see cref="JustTookDamage"/> 逐字同構：同一個寫入者（<c>CharacterHealth</c>）、
        /// 同一個發布點（順序 0.5 `PublishTo`）、寫出後立即清除、不走 `ResetTransientState`。
        ///
        /// <para><b>⭐ 為什麼加欄位，而不是讓讀者自己對 <see cref="IsDead"/> 做邊沿偵測</b></para>
        /// 「由活轉死」是 <c>CharacterHealth.ApplyDamage</c> **當下就知道**的事——
        /// 它正是在那一行把 <c>_isDead</c> 翻成 true 的。讓下游各自快取上一幀的 <see cref="IsDead"/>
        /// 再比對，就是 <c>docs/16</c> 點名的加重缺陷「**下游重新推導已 commit 的狀態**」：
        /// 每多一個消費者（音效、VFX、ragdoll、鏡頭震動）就多一份 `_wasDead` 快取，
        /// 而它們會因為讀取時機不同而**彼此不一致**。一個欄位換掉 N 份會漂移的推導。
        ///
        /// <para><b>⚠️ 這與 <see cref="IsDead"/> 不重複，兩者語意不同</b></para>
        /// <see cref="IsDead"/> 是**狀態**（「現在是不是屍體」，FSM 的准入條件）；
        /// 本欄位是**事件**（「就是這一幀死的」，一次性表現的觸發點）。
        /// 死亡音必須用後者——用前者會每幀重播。
        ///
        /// <para><b>Revive 不抬起本旗標</b></para>
        /// 復活是活轉死的**反向**，不是死亡事件。比照 <c>Revive()</c> 一併清掉 pending 的作法。
        /// </summary>
        public bool JustDied;
    }
}
