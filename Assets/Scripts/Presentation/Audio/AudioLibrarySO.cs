using System;
using UnityEngine;

namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（M2）音效總表資產：AudioEventId → AudioDefinitionSO 的唯一查表窗口。
    /// Inspector 以 entry 清單維護（易編輯、易 diff）；執行期 Initialize() 一次攤平成
    /// 「以 enum 值為索引的陣列」→ 查表 O(1)、零 boxing 零 GC
    /// （Dictionary 以 enum 為 key 在部分 runtime 會 boxing，陣列索引最穩）。
    /// </summary>
    [CreateAssetMenu(fileName = "AudioLibrary", menuName = "Project/Audio/AudioLibrary")]
    public class AudioLibrarySO : ScriptableObject
    {
        [Serializable]
        public struct AudioEntry
        {
            public AudioEventId EventId;
            public AudioDefinitionSO Definition;
        }

        [Tooltip("事件 → 定義的映射清單。同一事件填重複時後者覆蓋前者（Editor 會警告）。")]
        [SerializeField] private AudioEntry[] entries;

        // 執行期攤平查表：索引 = (int)AudioEventId。共享資產被多個 Controller 重複 Initialize 是冪等的。
        private AudioDefinitionSO[] _lookup;

        /// <summary>
        /// 由 AudioController 於 Awake 呼叫一次，把 entry 清單攤平成 O(1) 陣列查表。
        /// 配置只發生在初始化期，不進執行期熱路徑。
        /// </summary>
        public void Initialize()
        {
            int maxId = -1;
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    maxId = Mathf.Max(maxId, (int)entries[i].EventId);
                }
            }

            _lookup = new AudioDefinitionSO[maxId + 1];
            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
#if UNITY_EDITOR
                if (_lookup[(int)entries[i].EventId] != null)
                {
                    Debug.LogWarning($"[AudioLibrary '{name}'] 事件 {entries[i].EventId} 在清單中重複，後者覆蓋前者。", this);
                }
#endif
                _lookup[(int)entries[i].EventId] = entries[i].Definition;
            }
        }

        /// <summary>O(1) 查表；未註冊的事件回傳 null，呼叫端據此靜默跳過。</summary>
        public AudioDefinitionSO Get(AudioEventId id)
        {
            int index = (int)id;
            if (_lookup == null || index < 0 || index >= _lookup.Length) return null;
            return _lookup[index];
        }
    }
}
