using UnityEngine;

namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（M2）音效定義資產：描述一個音效事件「怎麼播」——clip 池 + 音量 + 音高範圍。
    /// 職責分離：AudioController 決定「何時播」，本資產決定「播什麼、怎麼變化」。
    /// clip 池隨機 + 音高微變化是抗重複疲勞的標準做法（同一事件連續觸發不死板）；
    /// 只放一個 clip、pitchRange 留 (1,1) 即為「固定播放」。
    /// </summary>
    [CreateAssetMenu(fileName = "AudioDefinition", menuName = "Project/Audio/AudioDefinition")]
    public class AudioDefinitionSO : ScriptableObject
    {
        [Tooltip("候選 clip 池：每次播放隨機挑一個，抗重複疲勞。至少放一個。")]
        [SerializeField] private AudioClip[] clips;

        [Tooltip("播放音量（送入 PlayOneShot 的 volumeScale）。")]
        [Range(0f, 1f)]
        [SerializeField] private float volume = 1f;

        [Tooltip("音高隨機範圍（x = min, y = max）。(1,1) = 不變化。")]
        [SerializeField] private Vector2 pitchRange = new Vector2(1f, 1f);

        public float Volume => volume;

        /// <summary>從 clip 池隨機挑一個；池空回傳 null（呼叫端據此跳過播放）。</summary>
        public AudioClip GetRandomClip()
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[Random.Range(0, clips.Length)];
        }

        /// <summary>在 pitchRange 內隨機取本次播放音高。</summary>
        public float GetRandomPitch()
        {
            return Random.Range(pitchRange.x, pitchRange.y);
        }
    }
}
