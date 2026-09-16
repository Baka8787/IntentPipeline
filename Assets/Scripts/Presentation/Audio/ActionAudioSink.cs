using UnityEngine;
using Project.Core.Actions;

namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（2026-09-17，戰鬥音效）**Action 生命週期的音效 adapter**：
    /// 把 <c>ActionState</c> 已經在發的 <see cref="IActionLifecycleSink"/> 回呼接成聲音。
    ///
    /// <para><b>⭐ 為什麼是這個 seam，而不是動畫時間或新的事件系統</b></para>
    /// 使用者明確要求「不要靠動畫時間硬猜」。這裡連猜的機會都沒有：
    /// <c>Begin()</c> ／ <c>Release()</c> 是 <c>ActionState</c> **自己**在段落邊界呼叫的，
    /// 與決定「什麼時候真的出手」的是**同一個**時間權威
    /// （`Release` 正是投射物生成、命中窗開啟、AoE 結算的那一刻——
    /// `ThrowProjectileEmitter`／`MeleeHitboxSink`／`GroundEffectSink` 全都掛在同一個回呼上）。
    /// ⇒ 聲音與 gameplay **結構上不可能不同步**，因為它們讀的是同一次呼叫。
    ///
    /// <para><b>⭐ 為什麼定義在元件上，而不是 <see cref="AudioEventId"/> 列舉</b></para>
    /// 每個 slot 的施法音本來就不同（火球 vs 冰刺 vs 揮劍）。
    /// 若把它們變成列舉值，就等於宣告「全域只會有這幾種施法音」，
    /// 而下一個法術要加時得改 enum ＋ 改全域查表 ＋ 改程式。
    /// 掛在 slot 自己的元件上之後，**新增一個法術＝掛一顆元件、填兩個欄位，程式零改動**。
    /// 這與 `MeleeHitboxSink.damage`／`ThrownProjectile.slowDuration` 是同一種配置慣例：
    /// **per-slot 的數值屬於那個 slot 的資產，不屬於程式。**
    ///
    /// <para><b>接線</b></para>
    /// 掛在角色身上，再於 <c>CharacterPipelineRunner.actionSinkBindings</c> 綁到目標 slot。
    /// **同一個 slot 可以有多顆不同的 sink** ⇒ 本元件與既有的
    /// `ThrowProjectileEmitter`／`MeleeHitboxSink` 並存，互不干擾。
    ///
    /// <para><b>⚠️ 本元件不做任何 gameplay 決定</b></para>
    /// 不讀 <c>PlayerRuntimeData</c>、不看冷卻、不判斷 Action 該不該發生、不碰 <c>ActionReleaseContext</c>
    /// 的內容。被呼叫就播，沒被呼叫就不播——**要不要出手是 `ActionState` 的事**。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActionAudioSink : MonoBehaviour, IActionLifecycleSink
    {
        [Header("Output")]
        [Tooltip("本角色的音效輸出。留空 ⇒ Awake 時沿階層向上尋找。")]
        [SerializeField] private AudioController audioController;

        [Tooltip("播放位置。留空 ⇒ 用本元件所在的 Transform。\n" +
                 "⭐ 想讓施法音從手上發出，就指到與 Throw Spawn Point 同一根骨骼。")]
        [SerializeField] private Transform emitPoint;

        [Header("Definitions（留空該階段即靜音）")]
        [Tooltip("Action 開始（抬手／起手式）。火球／冰刺的 Cast 音放這裡。")]
        [SerializeField] private AudioDefinitionSO castDefinition;

        [Tooltip("Action release（真正出手的那一刻——投射物生成／命中窗開啟／AoE 結算）。\n" +
                 "火球 Release、冰刺 Spawn、近戰 Swing 揮擊聲都放這裡。")]
        [SerializeField] private AudioDefinitionSO releaseDefinition;

        private void Awake() => ResolveAudioController();

        /// <summary>
        /// 補齊未在 Inspector 指派的 <see cref="audioController"/>。
        /// **與 <see cref="Awake"/> 分開，是為了 EditMode 測試**——EditMode 的 `AddComponent`
        /// **不會**呼叫 `Awake`（本 repo 踩過：commit 58b79ca），
        /// 沿用 `MeleeHitboxSink.ResolveHitbox` 已經確立的寫法。
        /// </summary>
        internal void ResolveAudioController()
        {
            if (audioController == null) audioController = GetComponentInParent<AudioController>();
        }

        public void Begin() => PlayDefinition(castDefinition);

        /// <summary>
        /// ⚠️ **刻意沒有「一次執行只播一次」的閘門**，與 <c>MeleeHitboxSink</c> 不同。
        /// <c>ActionState.TryAdvanceChain</c> 會在**每個連段**重置 `_releaseEmittedThisExecution`
        /// 並再發一次 Release（「每段各出一顆投射物」，見該處註解）——
        /// 那正是我們要的：三段連擊要有三聲揮擊。
        /// 加上閘門會讓第 2、3 段**無聲**，而那是 chain 的正常行為不是重複觸發。
        /// ⇒ 「一次 Action 該響幾聲」已經由 <c>ActionState</c> 回答完了，這裡不重複回答。
        /// </summary>
        public void Release(in ActionReleaseContext context) => PlayDefinition(releaseDefinition);

        /// <summary>
        /// ⚠️ **刻意什麼都不做。** Cleanup 是所有結束／中斷路徑的共用收尾點，
        /// 而音效是**一次性**的：已經播出去的聲音不該因為 Action 被中斷而被切掉
        /// （揮到一半被打斷，揮擊聲仍應自然播完）。
        /// 需要「中斷就停聲」的是 loop 音，本輪沒有 loop 音。
        /// </summary>
        public void Cleanup() { }

        private void PlayDefinition(AudioDefinitionSO definition)
        {
            if (definition == null || audioController == null) return;
            Transform point = emitPoint != null ? emitPoint : transform;
            audioController.PlayAt(definition, point.position);
        }
    }
}
