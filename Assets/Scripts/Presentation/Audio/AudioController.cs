using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（M2）音效控制器——第一個 IPresentationController 實作，兼作後續表現模組的範本：
    /// 讀黑板（單幀事件 JustLanded + 仲裁旗標 BlockAudio）→ 經 Library 查表 → 播放。
    /// 對 PlayerRuntimeData 只讀不寫；由 PresentationPipeline 在順序 6.5 集中驅動，本身沒有 Update。
    /// 掛載：Character Root 階層下（Runner 的 GetComponentsInChildren 收集得到即可）。
    /// M2 裁決：單一 AudioSource + PlayOneShot（多音軌 / Source 池屬 dev-spec §5 Future Work；
    /// 已知侷限：pitch 是 Source 層屬性，連續觸發時後一發會改到仍在播的前一發）。
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
        }

        private void Play(AudioEventId eventId)
        {
            if (audioSource == null || library == null) return;

            AudioDefinitionSO definition = library.Get(eventId);
            if (definition == null) return; // 未在 Library 註冊的事件：靜默跳過（允許逐步填表）

            AudioClip clip = definition.GetRandomClip();
            if (clip == null) return;

            audioSource.pitch = definition.GetRandomPitch();
            audioSource.PlayOneShot(clip, definition.Volume);
        }
    }
}
