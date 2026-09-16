using UnityEngine;

namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（M2）音效定義資產：描述一個音效事件「怎麼播」——clip 池 + 音量 + 音高範圍。
    /// 職責分離：AudioController 決定「何時播」，本資產決定「播什麼、怎麼變化」。
    /// clip 池隨機 + 音高微變化是抗重複疲勞的標準做法（同一事件連續觸發不死板）；
    /// 只放一個 clip、pitchRange 留 (1,1) 即為「固定播放」。
    ///
    /// 🔄（Stage 0）選 clip 由**純隨機**改為 **shuffle bag**（見 <see cref="GetNextClip"/>）。
    /// </summary>
    [CreateAssetMenu(fileName = "AudioDefinition", menuName = "Project/Audio/AudioDefinition")]
    public class AudioDefinitionSO : ScriptableObject
    {
        [Tooltip("候選 clip 池：每次播放取一個，抗重複疲勞。至少放一個。")]
        [SerializeField] private AudioClip[] clips;

        [Tooltip("播放音量（送入 PlayOneShot 的 volumeScale）。")]
        [Range(0f, 1f)]
        [SerializeField] private float volume = 1f;

        [Tooltip("音量隨機抖動（±比例）。0 = 不變化。0.1 表示每次在 volume 的 ±10% 內取值。\n" +
                 "腳步這類高頻事件建議 0.08~0.15：夠打破機械感，又不會讓人以為音量壞掉。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float volumeJitter;

        [Tooltip("音高隨機範圍（x = min, y = max）。(1,1) = 不變化。")]
        [SerializeField] private Vector2 pitchRange = new Vector2(1f, 1f);

        public float Volume => volume;

        // === Shuffle bag 的執行期狀態 ===
        // ⚠️ ScriptableObject 是**共用資產**，這份狀態因此是「每個 Definition 一份」而非「每個角色一份」。
        //    對腳步來說這正是要的：同一個池被多個角色共用時，整體上仍然是不重複的輪替。
        //    首次使用時 lazy 配置一次，之後零配置（不依賴 OnEnable——domain reload 後不保證會再跑）。
        private int[] _bag;
        private int _bagCursor;

        /// <summary>
        /// 從 clip 池取下一個；池空回傳 null（呼叫端據此跳過播放）。
        ///
        /// **Shuffle bag（洗牌袋）而不是 <c>Random.Range</c>**：純隨機在 6 顆的池子裡，
        /// 每次都有 1/6 機率立刻重複上一顆，連續兩三次相同的機率高到聽得出來——
        /// 而「剛剛那一聲又來了」正是重複疲勞的主要來源。
        /// 洗牌袋保證**每一輪 N 次播放剛好用完 N 顆**，並在換袋的接縫處避免與前一顆相同
        /// ⇒ 任意兩次連續播放都不同。
        ///
        /// ⚠️ 接縫處理刻意與 WASD 官方腳本的寫法不同：它把 <c>ShuffleSeed[0]</c> 連續賦值兩次
        /// （`[0] = [1]` 之後又 `[0] = temp`），結果是**丟掉 `[1]` 的值並留下重複元素**。
        /// 這裡改成與袋內任一非首位元素交換，是純排列運算，不會改變袋的內容。
        /// </summary>
        public AudioClip GetNextClip()
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];

            // 🐛 2026-09-17：**袋子只裝「非空」的索引。**
            //
            // 原本袋子裝的是 0..N-1 全部索引，直接 `return clips[_bag[_bagCursor++]]`
            // ⇒ 只要陣列裡有一個空引用（Inspector 留空、或素材檔被刪掉），
            //   就會**隨機**回傳 null、呼叫端靜默跳過 ⇒ **同一個事件時有聲時無聲**。
            //   本輪移除 43 顆素材後 `Ice_Cast` 剩 2 格其中 1 格失效，使用者回報
            //   「冰法術偶爾有聲沒聲」——就是這個。
            //
            // ⚠️ **第一版修法是錯的**：我原本保留整袋、改成「最多重試 _bag.Length 次」。
            //    那個上限**不是**正確性保證——重試途中若跨過換袋邊界就會重新洗牌，
            //    可能重複造訪同一格而漏掉有效的那一格（測試實際抓到）。
            // ⇒ 改成**由建構保證**：袋裡根本沒有空格子，所以抽到的一定有效。
            int validCount = 0;
            for (int i = 0; i < clips.Length; i++) if (clips[i] != null) validCount++;
            if (validCount == 0) return null;

            if (_bag == null || _bag.Length != validCount)
            {
                _bag = new int[validCount];
                int w = 0;
                for (int i = 0; i < clips.Length; i++) if (clips[i] != null) _bag[w++] = i;
                _bagCursor = _bag.Length; // 迫使第一次取用前先洗牌
            }

            if (_bag.Length == 1) return clips[_bag[0]];

            if (_bagCursor >= _bag.Length)
            {
                Reshuffle();
                _bagCursor = 0;
            }

            return clips[_bag[_bagCursor++]];
        }

        /// <summary>Fisher–Yates 洗牌，並避免新袋首顆與舊袋末顆相同。</summary>
        private void Reshuffle()
        {
            int previousLast = _bag[_bag.Length - 1];

            for (int i = _bag.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int tmp = _bag[i];
                _bag[i] = _bag[j];
                _bag[j] = tmp;
            }

            if (_bag[0] == previousLast)
            {
                int swap = Random.Range(1, _bag.Length);
                _bag[0] = _bag[swap];
                _bag[swap] = previousLast;
            }
        }

        /// <summary>在 pitchRange 內隨機取本次播放音高。</summary>
        public float GetRandomPitch()
        {
            return Random.Range(pitchRange.x, pitchRange.y);
        }

        /// <summary>取本次播放音量（含 ±<c>volumeJitter</c> 抖動，夾在 [0,1]）。</summary>
        public float GetRandomVolume()
        {
            if (volumeJitter <= 0f) return volume;
            return Mathf.Clamp01(volume * (1f + Random.Range(-volumeJitter, volumeJitter)));
        }
    }
}
