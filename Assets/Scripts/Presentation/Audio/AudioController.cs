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
    /// 2. **pitch 互相干擾**——每一發借一顆獨立的 `AudioSource`（<see cref="FootstepAudioSourcePool"/>）。
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

        [Tooltip("腳步專用 AudioSource 池大小。腳步是高頻短音，6 顆足以涵蓋雙腳重疊＋落地。\n" +
                 "⚠️ 太小會讓最舊的一發被搶掉（可接受）；太大只是多幾顆閒置物件。")]
        [Range(2, 16)]
        [SerializeField] private int footstepSourceCount = 6;

        private FootstepAudioSourcePool _footstepSources;

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
                _footstepSources = new FootstepAudioSourcePool(audioSource, transform, footstepSourceCount);
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
        }

        /// <summary>
        /// 腳步／落地的統一播放路徑。**材質、分類、步態全部是傳入的既成事實**，這裡不推導任何一項。
        /// </summary>
        private void PlayFootstep(SurfaceId surface, FootstepKind kind, bool landing,
            GaitTier gait, Vector3 worldPosition, bool isRightFoot)
        {
            if (!landing && kind == FootstepKind.None) return;
            if (footstepBank == null || _footstepSources == null) return;

            AudioDefinitionSO definition = footstepBank.ResolveDefinition(
                surface, kind, landing, gait, out float volumeScale);
            if (definition == null) return;

            AudioClip clip = definition.GetNextClip();
            if (clip == null) return;

            float volume = footstepBank.ApplyMasterVolume(definition.GetRandomVolume(), volumeScale);
            float pitch = definition.GetRandomPitch() *
                (isRightFoot ? footstepBank.RightFootPitchScale : footstepBank.LeftFootPitchScale);

            _footstepSources.Play(clip, worldPosition, volume, pitch);
        }

        /// <summary>
        /// 非腳步事件的播放路徑（保留給未來的 UI／技能／環境音）。
        /// ⚠️ 仍然是單一 AudioSource ＋ PlayOneShot，沿用 M2 的已知侷限——
        /// 那些事件不像腳步會高頻重疊，沒有理由為它們也開池。
        /// </summary>
        private void Play(AudioEventId eventId)
        {
            if (audioSource == null || library == null) return;

            AudioDefinitionSO definition = library.Get(eventId);
            if (definition == null) return; // 未在 Library 註冊的事件：靜默跳過（允許逐步填表）

            AudioClip clip = definition.GetNextClip();
            if (clip == null) return;

            audioSource.pitch = definition.GetRandomPitch();
            audioSource.PlayOneShot(clip, definition.GetRandomVolume());
        }
    }
}
