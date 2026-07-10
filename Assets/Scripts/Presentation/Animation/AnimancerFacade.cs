using UnityEngine;
using System;
using System.Collections.Generic;
using Animancer; // 需引入 Animancer 命名空間

namespace Project.Presentation.Animation
{
    public class AnimancerFacade : AnimationFacadeBase
    {
        [System.Serializable]
        public struct ClipMapping
        {
            public string StateKey;
            public AnimationClip Clip;
        }

        [Header("Setup")]
        [SerializeField] private AnimancerComponent animancer;
        // 🆕（v0.7 Code Review）補上預設初始化，避免非 Editor 流程建立此元件時 Awake() 的 foreach 直接 NRE
        [SerializeField] private List<ClipMapping> clipMappings = new();

        private readonly Dictionary<string, AnimationClip> _clipMap = new();
        private readonly Dictionary<string, AnimancerState> _stateCache = new();

        private void Awake()
        {
            if (animancer == null) animancer = GetComponentInChildren<AnimancerComponent>();

            // 建立快速查表
            foreach (var mapping in clipMappings)
            {
                if (!string.IsNullOrEmpty(mapping.StateKey) && mapping.Clip != null)
                {
                    _clipMap[mapping.StateKey] = mapping.Clip;
                }
            }
        }

        public override void Play(string stateKey, float transitionDuration = 0.15f)
        {
            if (!_clipMap.TryGetValue(stateKey, out var clip))
            {
                // 💡 升級防禦線：如果是這裡噴出警告，代表狀態機有叫它播，但 Inspector 的連線斷了！
                Debug.LogWarning($"<color=red>[AnimancerFacade] 警告：狀態機請求播放 '{stateKey}'，但 Clip Mappings 查表失敗！請檢查 Inspector 是否殘留 Missing 欄位！</color>", this);
                return;
            }

            if (clip == null)
            {
                Debug.LogWarning($"<color=red>[AnimancerFacade] 警告：狀態機請求播放 '{stateKey}'，但對應的 AnimationClip 實體為 null！</color>", this);
                return;
            }

            var state = animancer.Play(clip, transitionDuration);
            _stateCache[stateKey] = state;
        }

        public override void PlayWithCallback(string stateKey, Action onComplete, float transitionDuration = 0.1f)
        {
            if (!_clipMap.TryGetValue(stateKey, out var clip)) return;

            var state = animancer.Play(clip, transitionDuration);
            _stateCache[stateKey] = state;

            // 💡 規格書優化提示：利用 Animancer 原生事件系統，並在結束後自動移除，防止記憶體殘留與每次 new 的 GC Alloc
            state.Events(this).OnEnd = () =>
            {
                state.Events(this).OnEnd = null; // ✨ 修正點 1：移除了不必要的括號 ()
                onComplete?.Invoke();
            };
        }

        public override void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f)
        {
            // 💡 v0.5 規格書防禦策略：Lite 版打包後不支援 Layer 1+，在此加入編輯器警報
#if UNITY_EDITOR
            if (layerIndex > 0)
            {
                Debug.LogWarning($"[AnimancerFacade] 偵測到嘗試修改 Layer {layerIndex} 的權重。請注意 Animancer Lite 打包發行版後此功能將失效！", this);
            }
#endif
            // 🆕（v0.7 Code Review 修正）原本只檢查 layerIndex < animancer.Layers.Count，
            // 負數 index 會直接繞過檢查、在下面的索引存取時丟例外。改為雙邊界檢查並安全略過。
            if (layerIndex < 0 || layerIndex >= animancer.Layers.Count)
            {
                Debug.LogWarning($"[AnimancerFacade] SetLayerWeight 收到超出範圍的 layerIndex={layerIndex}（合法範圍 0~{animancer.Layers.Count - 1}），已略過此次呼叫。", this);
                return;
            }

            animancer.Layers[layerIndex].SetWeight(weight); // 註：精準平滑過渡可搭配動態內插，此處提供基礎賦值
        }

        public override void SetFloat(string key, float value)
        {
            // Lite 版限制了動態 Mixer 建立，後續若接預烘焙的 BlendTree SO，可在此同步參數
        }

        public override void SetBool(string key, bool value) { }

        public override bool IsPlaying(string stateKey)
        {
            if (_stateCache.TryGetValue(stateKey, out var state))
            {
                return state.IsPlaying;
            }
            return false;
        }

        public override float GetNormalizedTime()
        {
            // ✨ 修正點 2：將 animancer.States.CurrentState 改為 animancer.Layers[0].CurrentState
            var currentRootState = animancer.Layers[0].CurrentState;
            return currentRootState != null ? currentRootState.NormalizedTime : 0f;
        }
    }
}