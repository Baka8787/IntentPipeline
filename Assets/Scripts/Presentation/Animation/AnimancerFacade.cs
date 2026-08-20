using UnityEngine;
using System;
using System.Collections.Generic;
using Animancer; // 需引入 Animancer 命名空間

namespace Project.Presentation.Animation
{
    public class AnimancerFacade : AnimationFacadeBase
    {
        [System.Serializable]
        public struct TransitionMapping
        {
            public string StateKey;
            // 欄位型別採抽象基底 TransitionAssetBase：同一條映射可承載 ClipTransition /
            // LinearMixerTransition / 2D Mixer 等任意過渡型態（TransitionAsset 以 [SerializeReference]
            // 多型持有內部 transition），未來新增混合型態零 Facade 改動。
            public TransitionAssetBase Transition;
        }

        [Header("Setup")]
        [SerializeField] private AnimancerComponent animancer;
        // 🆕（v0.7 Code Review）補上預設初始化，避免非 Editor 流程建立此元件時 Awake() 的 foreach 直接 NRE
        // 🆕（v0.16 F1）string→AnimationClip 升級為 string→TransitionAssetBase：
        // 過渡時長/播放速度/循環/事件全數由資產承載（單一真相），程式碼簽名不再提供 duration。
        [SerializeField] private List<TransitionMapping> transitionMappings = new();

        private readonly Dictionary<string, TransitionAssetBase> _transitionMap = new();
        private readonly Dictionary<string, AnimancerState> _stateCache = new();

        private void Awake()
        {
            // 🆕（ADR-001）AnimancerComponent 定調在 Root（本物件）。改用 GetComponent（同物件）
            // 取代原本的 GetComponentInChildren：後者會誤抓子物件上的動畫元件，違反 Root/Model 職責隔離。
            if (animancer == null) animancer = GetComponent<AnimancerComponent>();

            // 🆕（ADR-001）結構校驗與 Fail-Fast 防線：Runtime 違規直接拋例外，並強制關閉 Model Animator 的 Root Motion。
            ValidateHierarchy(runtimeThrow: true);

            // 建立快速查表，並預熱底層 AnimancerState：
            // 首次 Play 的 state 建立屬一次性堆配置，移到 Awake 一次做完，讓 Play/SetFloat 熱路徑維持零 GC。
            // 多鍵映射同一資產（如 Idle/Move → Locomotion）時，GetOrCreate 依 transition.Key 回傳同一個 state。
            foreach (var mapping in transitionMappings)
            {
                if (string.IsNullOrEmpty(mapping.StateKey) || mapping.Transition == null) continue;

                _transitionMap[mapping.StateKey] = mapping.Transition;

                if (mapping.Transition.IsValid)
                {
                    _stateCache[mapping.StateKey] = animancer.States.GetOrCreate(mapping.Transition);
                }
            }
        }

        /// <summary>
        /// 🛡️（ADR-001）GameObject 階層 Root/Model 分離的結構校驗與 Fail-Fast 防線。
        /// Root 只保留動畫「邏輯」元件（<see cref="AnimancerComponent"/>），Animator 一律隔離到 Model 子物件，
        /// 藉此把 <c>CharacterController</c> 的物理世界座標與 Animator 根動作徹底分開。
        /// </summary>
        /// <param name="runtimeThrow">
        /// true：執行期（<see cref="Awake"/>）呼叫，違規直接拋例外，讓錯誤在最早期就爆出來；
        /// false：編輯期（<c>OnValidate</c>）呼叫，只記錄清楚的錯誤訊息，不中斷編輯流程。
        /// </param>
        private void ValidateHierarchy(bool runtimeThrow)
        {
            // 一次性（Awake / OnValidate）呼叫，非每幀 Update 熱路徑，此處少量配置不違反 Zero-GC 規範。
            var errors = new List<string>();

            // === Root 職責校驗：必須恰好 1 個 AnimancerComponent，且不得掛任何 Animator ===
            var rootAnimancers = GetComponents<AnimancerComponent>();
            if (rootAnimancers.Length != 1)
            {
                errors.Add($"Root 物件 '{name}' 必須恰好掛載 1 個 AnimancerComponent（目前偵測到 {rootAnimancers.Length} 個）。");
            }

            bool rootHasAnimator = TryGetComponent<Animator>(out var rootAnimator);
            if (rootHasAnimator)
            {
                errors.Add($"Root 物件 '{name}' 不得掛載 Animator；Animator 必須隔離到 Model 子物件，避免根動作與 CharacterController 世界座標互搶。");
            }

            // === Model 職責校驗：子物件（不含 Root 自身）中的 Animator 必須恰好 1 個 ===
            // 不依賴任何名稱字串（禁止 transform.Find("Model")），純以元件搜尋界定「Model 層」。
            var animators = GetComponentsInChildren<Animator>(true);
            int childAnimatorCount = rootHasAnimator ? animators.Length - 1 : animators.Length;
            if (childAnimatorCount != 1)
            {
                errors.Add($"Model 子物件中必須恰好存在 1 個 Animator（目前子物件偵測到 {childAnimatorCount} 個）。");
            }

            // 取出第一顆「非 Root」的 Animator 當作 Model Animator（保留未來多 Model 擴充彈性，不綁死唯一子物件）。
            Animator modelAnimator = null;
            foreach (var candidate in animators)
            {
                if (candidate != rootAnimator)
                {
                    modelAnimator = candidate;
                    break;
                }
            }

            if (modelAnimator != null)
            {
                // === Humanoid Avatar 校驗 ===
                if (modelAnimator.avatar == null || !modelAnimator.avatar.isHuman)
                {
                    errors.Add($"Model 的 Animator（'{modelAnimator.name}'）必須綁定 Humanoid 類型的 Avatar。");
                }

                // === 強制關閉 Root Motion：隔離物理與美術動畫位移，防止未來換模型時美術誤勾選 ===
                if (modelAnimator.applyRootMotion)
                {
                    modelAnimator.applyRootMotion = false;
                    Debug.LogWarning($"[AnimancerFacade] 已強制關閉 Model Animator '{modelAnimator.name}' 的 Apply Root Motion，以隔離物理位移與美術動畫位移（ADR-001）。", this);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(modelAnimator);
#endif
                }

                // === 連線校驗：AnimancerComponent 的 Animator 欄位必須指向這顆 Model Animator ===
                if (rootAnimancers.Length == 1 && rootAnimancers[0].Animator != modelAnimator)
                {
                    string wired = rootAnimancers[0].Animator == null ? "null（未指定）" : $"'{rootAnimancers[0].Animator.name}'";
                    errors.Add($"AnimancerComponent 的 Animator 欄位必須指向 Model 子物件的 Animator，目前指向 {wired}。");
                }
            }

            if (errors.Count == 0) return;

            string report = $"[AnimancerFacade] GameObject 階層 Root/Model 校驗失敗（ADR-001）：\n - {string.Join("\n - ", errors)}";
            if (runtimeThrow)
            {
                throw new InvalidOperationException(report);
            }
            else
            {
                Debug.LogError(report, this);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// [Editor-Only]（ADR-001）編輯期（非執行狀態）結構校驗。延遲到 OnValidate 之外執行，
        /// 既避免「不可於 OnValidate 期間修改其他物件」的 Unity 警告，也能在拖 Prefab／改場景時即時報錯。
        /// </summary>
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += EditorValidateDelayed;
        }

        private void EditorValidateDelayed()
        {
            UnityEditor.EditorApplication.delayCall -= EditorValidateDelayed;
            if (this == null) return;          // 元件可能已在延遲期間被刪除
            if (Application.isPlaying) return; // 執行期交給 Awake 的 Fail-Fast，避免重複校驗
            ValidateHierarchy(runtimeThrow: false);
        }
#endif

        public override void Play(string stateKey)
        {
            if (!TryGetTransition(stateKey, out var transition)) return;

            // 過渡時長/FadeMode/起始時間全部由資產決定。對「已在播放中」的同一資產重播為冪等
            //（Animancer 依 transition.Key 對應同一個 state，不會從頭重播），
            // Idle/Move 共用 Locomotion 資產時的狀態切換因此無縫。
            var state = animancer.Play(transition);
            _stateCache[stateKey] = state;
        }

        /// <summary>
        /// 查表＋資產有效性雙防線。任一層失敗回傳 false 並警告（不拋例外、不中斷管線），
        /// 呼叫端直接 return——行為與 v0.15 前的 clip 查表防線一致，RollState 的
        /// IsPlaying 防呆（查表失敗時退回 Procedural 結算）依然成立。
        /// </summary>
        private bool TryGetTransition(string stateKey, out TransitionAssetBase transition)
        {
            if (!_transitionMap.TryGetValue(stateKey, out transition))
            {
                // 💡 升級防禦線：如果是這裡噴出警告，代表狀態機有叫它播，但 Inspector 的連線斷了！
                Debug.LogWarning($"<color=red>[AnimancerFacade] 警告：狀態機請求播放 '{stateKey}'，但 Transition Mappings 查表失敗！請檢查 Inspector 是否殘留 Missing 欄位！</color>", this);
                return false;
            }

            if (transition == null || !transition.IsValid)
            {
                Debug.LogWarning($"<color=red>[AnimancerFacade] 警告：狀態機請求播放 '{stateKey}'，但對應的 Transition 資產為 null 或內容無效（內部 transition／clip 未指定）！</color>", this);
                return false;
            }

            return true;
        }

        public override void PlayWithCallback(string stateKey, Action onComplete)
        {
            if (!TryGetTransition(stateKey, out var transition)) return;

            var state = animancer.Play(transition);
            _stateCache[stateKey] = state;

            // 💡 利用 Animancer 原生事件系統，並在結束後自動移除，防止事件掛載殘留污染下一次播放。
            // ⚠️ 誠實揭露：下方 lambda 捕獲 state / onComplete，每次呼叫仍會產生一次閉包 GC Alloc——
            // 規格書 §5 既有待辦（回調 ObjectPool 分配器）尚未實作，目前也無執行期呼叫端，維持追蹤。
            state.Events(this).OnEnd = () =>
            {
                state.Events(this).OnEnd = null; // ✨ 修正點 1：移除了不必要的括號 ()
                onComplete?.Invoke();
            };
        }

        public override void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f)
        {
            // （v0.16）Animancer 已升級 Pro，移除 v0.5 時代「Lite 打包後 Layer 1+ 失效」的編輯器警報；
            // 多層混合的實際落地屬 F4（Upper Body Layer）範疇，屆時再處理 transitionDuration 的平滑內插。
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
            // 🆕（v0.16 F2）寫入 Animancer v8 參數字典。誰訂閱這個參數，由 Transition 資產內
            // 序列化的 ParameterName（StringAsset）綁定決定——Facade 不持有任何 Mixer 引用，
            // 維持「黑板 → 參數 → 資產綁定」的單向資料流。
            // 零 GC：string → StringReference 隱式轉換走 intern 快取（僅首次遇到新字串配置一次），
            // Parameter<float> 為型別化容器，無裝箱。
            animancer.Parameters.SetValue(key, value);
        }

        public override void SetBool(string key, bool value)
        {
            // 同 SetFloat：通用參數通道，訂閱關係定義在資產端。
            animancer.Parameters.SetValue(key, value);
        }

        public override void SetApplyAnimatorIK(int layerIndex, bool value)
        {
            // 🆕（M3）Playables 下 OnAnimatorIK 需對應層開啟 ApplyAnimatorIK（Animancer v8 API）。
            // 僅擋負數、不做上界略過：Animancer 的 Layers[index] 索引器對未建立的層會自動建層，
            // 初始化期（FootIKController.Start）呼叫屬一次性配置，非熱路徑。
            if (layerIndex < 0)
            {
                Debug.LogWarning($"[AnimancerFacade] SetApplyAnimatorIK 收到負數 layerIndex={layerIndex}，已略過此次呼叫。", this);
                return;
            }

            animancer.Layers[layerIndex].ApplyAnimatorIK = value;
        }

        public override bool IsPlaying(string stateKey)
        {
            if (_stateCache.TryGetValue(stateKey, out var state))
            {
                return state.IsPlaying;
            }
            return false;
        }

        public override bool TryGetDominantChildNormalizedTime(string stateKey, out float normalizedTime)
        {
            normalizedTime = 0f;
            if (!_stateCache.TryGetValue(stateKey, out var state) ||
                !state.IsPlaying ||
                !(state is ParentState parentState))
            {
                return false;
            }

            // Mixer root 的 NormalizedTime 是子狀態時間的加權聚合；腳相查詢需要實際主導 pose
            // 的 child clock。索引迴圈不產生配置；權重相同時保留較前的 child，結果可重現。
            AnimancerState dominantState = null;
            float dominantWeight = 0f;
            int childCount = parentState.ChildCount;
            for (int i = 0; i < childCount; i++)
            {
                AnimancerState child = parentState.GetChild(i);
                if (child == null || child.Weight <= dominantWeight) continue;

                dominantState = child;
                dominantWeight = child.Weight;
            }

            if (dominantState == null) return false;

            float value = dominantState.NormalizedTime;
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;

            normalizedTime = value;
            return true;
        }

        public override float GetNormalizedTime()
        {
            // ✨ 修正點 2：將 animancer.States.CurrentState 改為 animancer.Layers[0].CurrentState
            var currentRootState = animancer.Layers[0].CurrentState;
            return currentRootState != null ? currentRootState.NormalizedTime : 0f;
        }
    }
}
