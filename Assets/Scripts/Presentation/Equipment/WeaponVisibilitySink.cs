using Project.Core.Actions;
using UnityEngine;

namespace Project.Presentation.Equipment
{
    /// <summary>
    /// 讓武器**只在出手期間看得見**。綁在哪個 slot，就只跟著那個 Action 顯隱——
    /// 近戰揮劍時劍出現，放火球時不出現。
    ///
    /// <para><b>⚖️ 為什麼是獨立的一顆 sink，不是塞進 <c>MeleeHitboxSink</c></b></para>
    /// 命中判定是 gameplay，武器顯隱是 presentation。它們**剛好共用同一組時點**，
    /// 但沒有任何一方應該知道另一方存在：換成不會造成傷害的演出動作（收劍、挑釁）時
    /// 只需要這顆；換成不帶武器的拳擊時只需要那顆。
    /// 這也是 2026-09-14 把「一個 slot 一顆 sink」放寬成「一個 slot 多顆 sink」的直接理由——
    /// 舊限制會逼人把兩個不相干的責任併進同一個類別。
    ///
    /// <para><b>時點語意</b></para>
    /// <list type="bullet">
    /// <item><c>Begin</c>：出手開始 ⇒ 顯現。</item>
    /// <item><c>Release</c>：**刻意什麼都不做**。release 是「命中／發射」那一瞬間，
    /// 武器在整個動作期間都該留著，不是在 release 才出現、也不是在 release 就消失。</item>
    /// <item><c>Cleanup</c>：動作結束 ⇒ 收起。自然播完與被中斷**都會**走到這裡
    /// （`ActionState` 的六個結束路徑全都呼叫 `Cleanup`），因此不會出現「被打斷後劍卡在手上」。</item>
    /// </list>
    ///
    /// <para><b>v1 ＝ 瞬間顯隱，不是 shader dissolve</b></para>
    /// 使用者 2026-09-14 裁決：先把**何時**做對，**怎麼**消失留待材質／VFX 工作。
    /// 真正的溶解需要在 Sword 上換一份 dissolve 材質並逐幀推參數，而 Sword 是第三方資產
    /// （CLAUDE.md 禁止修改），屬於另一個層次的工作。
    /// ⇒ 之後要換成 dissolve，替換的是 <see cref="ApplyVisibility"/> 一個方法，
    /// 時點與接線都不必再動。
    ///
    /// <para><b>⛔ 這不是裝備系統</b></para>
    /// 同 <see cref="WeaponSocket"/>：沒有背包、沒有切換、沒有黑板欄位。
    /// 本檔要解的問題只有一個：**沒在攻擊時，手上不該一直握著一把劍**。
    /// </summary>
    public sealed class WeaponVisibilitySink : MonoBehaviour, IActionLifecycleSink
    {
        [Tooltip("要控制的武器掛點。留空 ⇒ 從同物件與子物件尋找（含未啟用者）。")]
        [SerializeField] private WeaponSocket weaponSocket;

        [Tooltip("沒有在出手時，武器是否仍然可見。\n" +
                 "預設 false ＝「只有攻擊時才顯現」。改成 true 可以在不拆接線的情況下" +
                 "暫時退回「武器一直掛著」來比對手感。")]
        [SerializeField] private bool visibleWhenIdle = false;

        private void Awake()
        {
            ResolveSocket();
            // 一進場就先套一次，否則在第一次出手之前武器會維持 WeaponSocket 的預設（可見）。
            ApplyVisibility(visibleWhenIdle);
        }

        /// <summary>
        /// 解析掛點。**與 <see cref="Awake"/> 分開，是為了 EditMode 測試**——
        /// EditMode 不在 Play mode，<c>AddComponent</c> 不會呼叫 <c>Awake</c>
        /// ⇒ <c>weaponSocket</c> 恆為 null、所有斷言假性通過。
        /// 比照 <c>AIMovementSource.ResolveEffectState</c> 的既有慣例。
        /// </summary>
        internal void ResolveSocket()
        {
            if (weaponSocket == null) weaponSocket = GetComponentInChildren<WeaponSocket>(true);
        }

        public void Begin() => ApplyVisibility(true);

        /// <summary>命中／發射那一瞬間與武器該不該看得見無關。見型別註解的時點語意。</summary>
        public void Release(in ActionReleaseContext context)
        {
        }

        public void Cleanup() => ApplyVisibility(visibleWhenIdle);

        /// <summary>
        /// 唯一實際改變畫面的地方。**日後換成 dissolve 就是換掉這一個方法**——
        /// 時點（`Begin`／`Cleanup`）與接線（slot binding）都不必動。
        /// </summary>
        private void ApplyVisibility(bool visible)
        {
            if (weaponSocket == null) return;
            weaponSocket.SetVisible(visible);
        }

        /// <summary>供 EditMode 驗證：解析到的掛點（可能為 null ＝ 接線缺失）。</summary>
        internal WeaponSocket ResolvedSocket => weaponSocket;

        /// <summary>供 EditMode 驗證：非出手期間的可見性政策。</summary>
        internal bool VisibleWhenIdle => visibleWhenIdle;
    }
}
