using System;
using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（Stage 2）腳步音的**單一配置窗口**：`(Surface, Kind, Gait) → clip pool`，外加所有音量係數。
    ///
    /// ## 為什麼把這些收在一顆資產裡
    /// 使用者的要求很具體：「**之後要調整整體腳步聲大小，不需要去改 20 個 asset**」。
    /// 因此音量拆成兩層——**個別 clip 池的相對音量**留在各自的 `AudioDefinitionSO`（那是音色的一部分），
    /// 而**跨類別的相對關係與總音量**集中在這裡。要調整整體大小只要動 <see cref="masterVolume"/> 一個欄位。
    ///
    /// ## ⚠️ 不做的事
    /// 這裡**沒有**任何 clip 選取邏輯——選哪一顆仍然是 `AudioDefinitionSO` 的洗牌袋負責。
    /// 本資產只回答「該用哪一個池」與「該乘多少音量」。
    /// </summary>
    [CreateAssetMenu(fileName = "FootstepAudioBank", menuName = "Project/Audio/Footstep Audio Bank")]
    public sealed class FootstepAudioBankSO : ScriptableObject
    {
        /// <summary>單一材質的四個池。⚠️ 留空的欄位會走 <see cref="ResolveDefinition"/> 的 fallback 鏈。</summary>
        [Serializable]
        public struct SurfaceSet
        {
            public SurfaceId Surface;

            [Tooltip("Normal Step / Walk 檔位。")]
            public AudioDefinitionSO Walk;

            [Tooltip("Normal Step / Run 檔位。⚠️ Sprint 也用這個池（WASD Free Bundle 沒有 Sprint 素材）。")]
            public AudioDefinitionSO Run;

            [Tooltip("Shuffle / Scuff（收步、轉身的小幅腳部調整）。**不分 gait**。")]
            public AudioDefinitionSO Shuffle;

            [Tooltip("Landing（整體落地）。**不分 gait**。")]
            public AudioDefinitionSO Landing;
        }

        [Header("Surface Sets")]
        [Tooltip("每個材質一組。未列出的材質會退回 Default Surface 的那一組。")]
        [SerializeField] private SurfaceSet[] surfaces = Array.Empty<SurfaceSet>();

        [Tooltip("找不到對應材質時使用的材質。應與 SurfaceMapSO 的 Default 一致。")]
        [SerializeField] private SurfaceId defaultSurface = SurfaceId.Stone;

        [Header("Volume（集中調整處）")]
        [Tooltip("⭐ 腳步聲總音量。要整體調大調小**只需要動這一個欄位**。")]
        [Range(0f, 2f)] [SerializeField] private float masterVolume = 1f;

        [Range(0f, 2f)] [SerializeField] private float normalStepVolume = 1f;
        [Range(0f, 2f)] [SerializeField] private float shuffleVolume = 0.45f;
        [Range(0f, 2f)] [SerializeField] private float landingVolume = 1.1f;

        [Header("Gait Multiplier（只作用於 Normal Step）")]
        [Range(0f, 2f)] [SerializeField] private float walkMultiplier = 0.9f;
        [Range(0f, 2f)] [SerializeField] private float runMultiplier = 1f;
        [Range(0f, 2f)] [SerializeField] private float sprintMultiplier = 1.05f;

        [Header("Per-Foot Pitch")]
        [Tooltip("左右腳的音高微差。真實的左右腳本來就不完全一樣；這比再配一整組素材便宜得多。\n" +
                 "⚠️ 與 AudioDefinition 自身的 pitchRange 相乘，總幅度請維持在聽不出走音的範圍內。")]
        [Range(0.9f, 1.1f)] [SerializeField] private float leftFootPitchScale = 0.98f;
        [Range(0.9f, 1.1f)] [SerializeField] private float rightFootPitchScale = 1.02f;

        // 執行期攤平：以 (int)SurfaceId 為索引，O(1)、零 boxing（同 AudioLibrarySO 先例）
        private SurfaceSet[] _lookup;
        private bool _initialized;

        public float LeftFootPitchScale => leftFootPitchScale;
        public float RightFootPitchScale => rightFootPitchScale;

        /// <summary>由使用端於 Awake 呼叫一次。冪等。</summary>
        public void Initialize()
        {
            int max = (int)defaultSurface;
            if (surfaces != null)
                for (int i = 0; i < surfaces.Length; i++) max = Mathf.Max(max, (int)surfaces[i].Surface);

            _lookup = new SurfaceSet[max + 1];
            if (surfaces != null)
            {
                for (int i = 0; i < surfaces.Length; i++)
                {
                    int index = (int)surfaces[i].Surface;
                    if (index < 0 || index >= _lookup.Length) continue;
                    _lookup[index] = surfaces[i];
                }
            }
            _initialized = true;
        }

        /// <summary>
        /// 選出 clip 池。
        ///
        /// **Fallback 鏈（刻意「同材質優先」而不是「同類別優先」）**：
        /// <code>
        /// (surface, kind)              例：Wood Shuffle
        ///   ↓ 缺
        /// (surface, 較通用的 kind)      例：Wood Walk —— **仍然是木頭**
        ///   ↓ 缺
        /// (defaultSurface, kind)        例：Stone Shuffle
        ///   ↓ 缺
        /// (defaultSurface, Walk)
        /// </code>
        /// ⚠️ **為什麼寧可換 kind 也不換 surface**：使用者回報的首要問題正是「材質忽然變了」。
        /// 音色類別不對（用 Walk 聲當 Shuffle）只是不夠細緻；材質不對（在木橋上聽到草地）會直接出戲。
        /// ⛔ 絕不靜默挑一個完全不相干的材質。
        /// </summary>
        /// <param name="volumeScale">該路徑對應的音量縮放（fallback 到較大聲的 kind 時會自動壓低）。</param>
        public AudioDefinitionSO ResolveDefinition(SurfaceId surface, FootstepKind kind, bool landing,
            GaitTier gait, out float volumeScale)
        {
            if (!_initialized) Initialize();

            volumeScale = 1f;
            if (_lookup == null || _lookup.Length == 0) return null;

            SurfaceSet set = GetSet(surface);
            SurfaceSet fallbackSet = GetSet(defaultSurface);

            if (landing)
            {
                volumeScale = landingVolume;
                return set.Landing != null ? set.Landing
                     : set.Walk != null ? set.Walk
                     : fallbackSet.Landing != null ? fallbackSet.Landing
                     : fallbackSet.Walk;
            }

            if (kind == FootstepKind.Shuffle)
            {
                volumeScale = shuffleVolume;
                if (set.Shuffle != null) return set.Shuffle;

                // 同材質的 Walk 當替身，但要壓得更低——它本來就是比較響的聲音。
                if (set.Walk != null) { volumeScale = shuffleVolume * 0.7f; return set.Walk; }
                if (fallbackSet.Shuffle != null) return fallbackSet.Shuffle;
                if (fallbackSet.Walk != null) { volumeScale = shuffleVolume * 0.7f; return fallbackSet.Walk; }
                return null;
            }

            // Normal Step
            volumeScale = normalStepVolume * GaitMultiplier(gait);
            AudioDefinitionSO byGait = gait == GaitTier.Walk ? set.Walk : set.Run;
            if (byGait != null) return byGait;
            if (set.Walk != null) return set.Walk;
            if (set.Run != null) return set.Run;

            AudioDefinitionSO fallbackByGait = gait == GaitTier.Walk ? fallbackSet.Walk : fallbackSet.Run;
            if (fallbackByGait != null) return fallbackByGait;
            return fallbackSet.Walk != null ? fallbackSet.Walk : fallbackSet.Run;
        }

        /// <summary>最終音量＝clip 池自身音量 × master × 類別 × gait。</summary>
        public float ApplyMasterVolume(float definitionVolume, float volumeScale)
        {
            return Mathf.Clamp(definitionVolume * masterVolume * volumeScale, 0f, 1f);
        }

        private float GaitMultiplier(GaitTier gait)
        {
            switch (gait)
            {
                case GaitTier.Walk: return walkMultiplier;
                case GaitTier.Sprint: return sprintMultiplier;
                default: return runMultiplier;
            }
        }

        private SurfaceSet GetSet(SurfaceId surface)
        {
            int index = (int)surface;
            if (_lookup != null && index > 0 && index < _lookup.Length && _lookup[index].Surface == surface)
                return _lookup[index];

            int fallbackIndex = (int)defaultSurface;
            if (_lookup != null && fallbackIndex >= 0 && fallbackIndex < _lookup.Length)
                return _lookup[fallbackIndex];

            return default;
        }
    }
}
