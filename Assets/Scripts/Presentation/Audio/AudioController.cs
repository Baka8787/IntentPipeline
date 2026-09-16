using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（M2）音效控制器——第一個 IPresentationController 實作，兼作後續表現模組的範本：
    /// 讀黑板 → 經 Library／Bank 查表 → 播放。
    /// 對 PlayerRuntimeData 只讀不寫；由 PresentationPipeline 在順序 6.5 集中驅動，本身沒有 Update。
    ///
    /// ---
    /// ## 🆕（Stage 2）腳步音改走 <see cref="FootstepAudioBankSO"/> ＋ AudioSource 池
    ///
    /// **三個 M2／Stage 1 的已知侷限在本輪一起解掉**：
    /// 1. **材質不一致**——Normal／Shuffle／Landing 現在讀的是黑板上**同一個** `Surface` 欄位
    ///    （由 `FootstepDetector` 一次決定），型別層保證三者同材質。
    /// 2. **pitch 互相干擾**——每一發借一顆獨立的 `AudioSource`（<see cref="AudioSourcePool"/>）。
    /// 3. **只能從角色中心播**——左右腳各自從自己的世界位置播。
    ///
    /// ⚠️ **本類別不做任何材質或步態判斷**：`Surface` 與 `Gait` 都是上游已 commit 的決定，
    /// 這裡只做「查表 → 播放」。下游重新推導已 commit 的狀態是 `docs/16` 明列的加重缺陷。
    ///
    /// `BlockAudio` 讀取**契約先行**（M2）：目前沒有任何 `IArbiterSource` 要求它，旗標恆 false。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioController : MonoBehaviour, IPresentationController
    {
        [Header("Setup")]
        [Tooltip("非腳步事件用的 AudioSource，同時是腳步 Source 池的 3D 設定樣板。\n" +
                 "⚠️ 調 3D 設定（spatialBlend／距離／mixer group）只要改這一顆，整池跟著變。")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("非腳步音效的查表資產（事件 → 定義）。腳步／落地已改由 Footstep Bank 負責。")]
        [SerializeField] private AudioLibrarySO library;

        [Header("Footstep（🆕 Stage 2）")]
        [Tooltip("腳步音的單一配置窗口：(Surface, Kind, Gait) → clip pool，以及所有音量係數。\n" +
                 "⭐ 要調整整體腳步音量，改這顆資產的 Master Volume 即可。")]
        [SerializeField] private FootstepAudioBankSO footstepBank;

        [Tooltip("AudioSource 池大小。腳步＋戰鬥共用同一個池。\n" +
                 "⚠️ 太小會讓最舊的一發被搶掉（例如死亡音被後續腳步切斷）；太大只是多幾顆閒置物件。\n" +
                 "⭐ 聽到聲音被切斷時，先加大這個數字，不要去 AudioSourcePool 裡加優先權。")]
        [Range(2, 24)]
        [SerializeField] private int audioSourceCount = 10;

        /// <summary>
        /// 🔄 2026-09-17：由 `_footstepSources` 更名。腳步與戰鬥共用同一個池——
        /// 它們對 Source 的需求完全一樣（獨立 pitch、獨立世界位置、會重疊），
        /// 開兩個池只會讓「誰該用哪一個」變成一個要記住的規則。
        /// </summary>
        private AudioSourcePool _sources;

        private void Awake()
        {
            // 與 Runner / MotionDriver 的防禦線風格一致：缺引用在啟動時明確報錯，不留到觸發時才炸。
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                Debug.LogError($"[{gameObject.name}] AudioController 缺少 AudioSource，且未在 Inspector 綁定！", this);
            }

            if (library == null)
            {
                Debug.LogError($"[{gameObject.name}] AudioController 未綁定 AudioLibrarySO，非腳步音效將全部靜默！", this);
            }
            else
            {
                library.Initialize();
            }

            if (footstepBank == null)
            {
                Debug.LogError($"[{gameObject.name}] AudioController 未綁定 FootstepAudioBankSO，腳步與落地音將全部靜默！", this);
            }
            else
            {
                footstepBank.Initialize();
            }

            // 🔄 2026-09-17：池的建立**移出 footstepBank 的 null 分支**。
            // 舊版把池綁在「有沒有腳步 Bank」上；戰鬥音效共用同一個池之後，
            // 沒接 Bank 的角色會連帶失去所有戰鬥音，而那兩件事沒有任何關係。
            if (audioSource != null)
            {
                _sources = new AudioSourcePool(audioSource, transform, audioSourceCount);
            }
        }

        public void Tick(PlayerRuntimeData data)
        {
            if (data.Arbitration.BlockAudio) return;

            // ⚠️ 讀到的是**上一帧**由 PresentationPipeline 發布的快照（發布在所有 Controller Tick 之後），
            //    這正是「事件正確性與 Hierarchy 順序無關」的代價與保證，見 PresentationPipeline.Tick。
            // ⚠️ **只讀不清除**——這是廣播快照不是佇列，未來的 VFX／鏡頭震動要讀到同一份。
            PresentationEventData events = data.PresentationEvents;

            // 🆕（Stage 2）Landing 改讀事件快照（不再讀 data.JustLanded）——
            //    為的是與腳步共用同一次 Surface 決策，見 PresentationEventData.Landed 的說明。
            if (events.Landed)
            {
                PlayFootstep(events.Surface, FootstepKind.None, landing: true,
                    gait: data.Gait, worldPosition: transform.position, isRightFoot: false);
            }

            // Landing 與 Footstep 的互斥已由事件源處理（JustLanded 當帧抑制腳步），此處不重複判斷。
            PlayFootstep(events.Surface, events.LeftFootstep, false, data.Gait, events.LeftFootPosition, false);
            PlayFootstep(events.Surface, events.RightFootstep, false, data.Gait, events.RightFootPosition, true);

            // =================================================================
            // 🆕（2026-09-17）黑板驅動的三個戰鬥／移動事件。
            //
            // ⚠️ 三者都是**已 commit 的單幀邊沿**，本類別一個都沒有重新推導：
            //    不比對血量、不快取上一幀的 IsDead、不看速度猜起跳。
            //    讀到 true 就播一次，讀到 false 就什麼都不做。
            //
            // ⚠️ Hurt 與 Death **互斥由上游保證**：致死那一幀 `JustTookDamage` 明確為 false
            //    （`CharacterHealth.ApplyDamage` 的致死分支不抬起它，ADR-009 D2），
            //    所以這裡不需要「死了就別播受擊」的防禦——**那會是第二個回答者**。
            // =================================================================
            if (data.Survivability.JustTookDamage) Play(AudioEventId.Hurt);
            if (data.Survivability.JustDied) Play(AudioEventId.Death);

            // ⚠️ `JustLeftGround` 是「離地」不是「起跳」——走下平台邊緣也會抬起它。
            //    這個已知近似記在 `AudioEventId.JumpTakeoff` 上。
            if (data.JustLeftGround) Play(AudioEventId.JumpTakeoff);
        }

        /// <summary>
        /// 🆕（2026-09-17）**本角色音效輸出的公開入口**：在指定世界位置播一發。
        ///
        /// <para><b>為什麼由 <see cref="AudioController"/> 持有，而不是各元件自己開 AudioSource</b></para>
        /// `PresentationPipeline` 的類別註解早就標記了這個問題：
        /// 「未來若多個 Controller 競爭同一輸出（例如兩個模組都要控制 AudioSource），
        /// 屆時升級為表現層的輸出仲裁」。戰鬥音效正是那個「未來」——
        /// 若 `ActionAudioSink`／`MeleeHitboxSink`／`ThrownProjectile` 各自 `AddComponent&lt;AudioSource&gt;`，
        /// 一個角色身上就會有五、六個彼此不知道對方存在的音源，
        /// 3D 設定要改六個地方、同時播放時誰也不讓誰。
        /// ⇒ **本角色的 AudioSource 由一個元件持有**，其他人只說「播什麼、在哪裡播」。
        ///
        /// <para><b>⛔ 這不是 singleton</b></para>
        /// 一個角色一顆，呼叫端一律靠 <c>[SerializeField]</c> 或 <c>GetComponentInParent</c> **沿階層**取得，
        /// 沒有靜態欄位、沒有 `FindObjectOfType`、沒有全域註冊表。
        /// 玩家與敵人各有各的，兩者互不知道對方存在。
        ///
        /// <para><b>⚠️ 已知侷限：音源屬於角色，不屬於聲音</b></para>
        /// 角色被銷毀時，仍在播的聲音會一起消失。對命中音有實際影響：
        /// 射手若在飛行途中死亡並被回收，那一發命中音會沒了。
        /// 本輪接受——正確解是場景層的音效服務，而那是「完整 Audio Framework」，使用者裁決不做。
        /// </summary>
        /// <param name="definition">要播什麼（clip 池／音量／音高全由資產決定）。null ⇒ 靜默跳過。</param>
        /// <param name="worldPosition">播放的世界位置。</param>
        public void PlayAt(AudioDefinitionSO definition, Vector3 worldPosition)
        {
            if (definition == null || _sources == null) return;

            AudioClip clip = definition.GetNextClip();
            if (clip == null) return;

            _sources.Play(clip, worldPosition, definition.GetRandomVolume(), definition.GetRandomPitch());
        }

        /// <summary>
        /// 腳步／落地的統一播放路徑。**材質、分類、步態全部是傳入的既成事實**，這裡不推導任何一項。
        /// </summary>
        private void PlayFootstep(SurfaceId surface, FootstepKind kind, bool landing,
            GaitTier gait, Vector3 worldPosition, bool isRightFoot)
        {
            if (!landing && kind == FootstepKind.None) return;
            if (footstepBank == null || _sources == null) return;

            AudioDefinitionSO definition = footstepBank.ResolveDefinition(
                surface, kind, landing, gait, out float volumeScale);
            if (definition == null) return;

            AudioClip clip = definition.GetNextClip();
            if (clip == null) return;

            float volume = footstepBank.ApplyMasterVolume(definition.GetRandomVolume(), volumeScale);
            float pitch = definition.GetRandomPitch() *
                (isRightFoot ? footstepBank.RightFootPitchScale : footstepBank.LeftFootPitchScale);

            _sources.Play(clip, worldPosition, volume, pitch);
        }

        /// <summary>
        /// Library 事件的播放路徑（Hurt／Death／JumpTakeoff），從**角色位置**播出。
        ///
        /// 🔄 **2026-09-17：改走 AudioSource 池，不再是單顆 `PlayOneShot`。**
        /// 舊註解說「那些事件不像腳步會高頻重疊，沒有理由為它們也開池」——
        /// 戰鬥事件推翻了這個前提：受擊音必然與腳步、與施法音重疊，
        /// 而 `pitch` 是 `AudioSource` 的**狀態**不是 `PlayOneShot` 的參數
        /// ⇒ 共用單顆 Source 會讓受擊音的 pitch 改到仍在播的腳步上（`AudioSourcePool` 開頭的兩個問題）。
        /// </summary>
        private void Play(AudioEventId eventId)
        {
            if (library == null) return;

            // 未在 Library 註冊的事件：靜默跳過（允許逐步填表）
            PlayAt(library.Get(eventId), transform.position);
        }
    }
}
