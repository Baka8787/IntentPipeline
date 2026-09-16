using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（M2）音效控制器——第一個 IPresentationController 實作，兼作後續表現模組的範本：
    /// 讀黑板（單幀事件 JustLanded + 仲裁旗標 BlockAudio）→ 經 Library 查表 → 播放。
    /// 對 PlayerRuntimeData 只讀不寫；由 PresentationPipeline 在順序 6.5 集中驅動，本身沒有 Update。
    /// 掛載：Character Root 階層下（Runner 的 GetComponentsInChildren 收集得到即可）。
    ///
    /// M2 裁決：單一 AudioSource + PlayOneShot（多音軌 / Source 池屬 dev-spec §5 Future Work）。
    /// ⚠️ **已知侷限（Stage 1 重新確認，仍未解）**：`pitch` 與位置都是 **Source 層**屬性——
    /// * 連續觸發時後一發會改到仍在播的前一發音高。腳步 clip 約 0.39~0.45 s、跑步步距約 0.3 s
    ///   ⇒ **重疊是必然的**。目前的緩解是把 pitchRange 壓在 ±5%（聽不出走音），
    ///   真正的解法是 AudioSource 池（Stage 2）。
    /// * 所有事件都從**角色中心**播，不是從實際左／右腳的世界位置播。第三人稱鏡頭距離下
    ///   （3~5 m）腳與中心的角度差很小，暫時可接受；同樣等 Source 池（Stage 2）才有得改。
    ///
    /// BlockAudio 現在就讀（契約先行）。🆕（輪 4）writer 已存在（ArbiterPipeline，順序 4.5），
    /// 但目前沒有任何 IArbiterSource 要求 BlockAudio，故旗標仍恆 false，直到死亡等來源進場——
    /// 屆時本檔**零改動**即生效，這正是當初契約先行要買的東西。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioController : MonoBehaviour, IPresentationController
    {
        [Header("Setup")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("音效總表：事件 → 定義的查表資產。未綁定時本控制器全程靜默並於啟動時報錯。")]
        [SerializeField] private AudioLibrarySO library;

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
                Debug.LogError($"[{gameObject.name}] AudioController 未綁定 AudioLibrarySO，音效事件將全部靜默！", this);
            }
            else
            {
                library.Initialize();
            }
        }

        public void Tick(PlayerRuntimeData data)
        {
            if (data.Arbitration.BlockAudio) return;

            if (data.JustLanded)
            {
                Play(AudioEventId.Landing);
            }

            // 🆕（M3.x-B）腳步音：讀黑板的表現層事件廣播快照。
            // ⚠️ 讀到的是**上一帧**由 PresentationPipeline 發布的快照（發布在所有 Controller Tick 之後），
            //    這正是「事件正確性與 Hierarchy 順序無關」的代價與保證，見 PresentationPipeline.Tick。
            // ⚠️ **只讀不清除**——這是廣播快照不是佇列，未來的 VFX／鏡頭震動要讀到同一份。
            //    復位由發布端的整體覆寫負責，本類別不得插手。
            // Landing 與 Footstep 的互斥已由事件源處理（JustLanded 當帧抑制腳步），此處不重複判斷。
            PlayFootstep(data.PresentationEvents.LeftFootstep, AudioEventId.LeftFootstep);
            PlayFootstep(data.PresentationEvents.RightFootstep, AudioEventId.RightFootstep);
        }

        /// <summary>
        /// 🆕（Stage 1）依分類選事件。**分類的語意屬黑板契約，這裡只做對應、不做判斷**——
        /// 「這一步算不算真正的腳步」是偵測器的決定，AudioController 不得重新推導
        /// （下游重新推導已 commit 的狀態是本專案明列的加重項，見 `docs/16` ）。
        /// </summary>
        private void PlayFootstep(FootstepKind kind, AudioEventId normalEvent)
        {
            switch (kind)
            {
                case FootstepKind.Normal:
                    Play(normalEvent);
                    break;
                case FootstepKind.Shuffle:
                    Play(AudioEventId.Shuffle);
                    break;
            }
        }

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
