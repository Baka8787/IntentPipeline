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
        [SerializeField] private List<ClipMapping> clipMappings;

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
            if (!_clipMap.TryGetValue(stateKey, out var clip)) return;

            var state = animancer.Play(clip, transitionDuration);
            _stateCache[stateKey] = state;
        }

        public override void PlayWithCallback(string stateKey, Action onComplete, float transitionDuration = 0.1f)
        {
            if (!_clipMap.TryGetValue(stateKey, out var clip)) return;

            var state = animancer.Play(clip, transitionDuration);
            _stateCache[stateKey] = state;

            // 💡 規格書優化提示：利用 Animancer 原生事件系統，並在結束後自動移除，防止記憶體殘留與每次 new 的 GC Alloc
            state.Events.OnEnd = () =>
            {
                state.Events.OnEnd = null; // 清空避免重複觸發
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
            if (layerIndex < animancer.Layers.Count)
            {
                animancer.Layers[layerIndex].SetWeight(weight); // 註：精準平滑過渡可搭配動態內插，此處提供基礎賦值
            }
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
            var currentRootState = animancer.States.CurrentState;
            return currentRootState != null ? currentRootState.NormalizedTime : 0f;
        }
    }
}