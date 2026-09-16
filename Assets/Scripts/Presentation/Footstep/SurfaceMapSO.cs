using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Footstep
{
    /// <summary>
    /// 🆕（Stage 2）**資料驅動的聲學材質對應表**——一張場景只要設定一次。
    ///
    /// ## 設計原則
    ///
    /// **「一顆 Material 設定一次，用到它的所有物件自動生效」**。demo map 實測：整張場景的地面
    /// 只用了 **6 顆 Material**（26 個物件共用 `GridOrange_01_Mat`、6 個共用 `GridWhite_01_Mat`…），
    /// 所以正確的配置成本是 6 筆，不是 43 個物件各掛一個 Tag。
    ///
    /// ⛔ **不在 runtime 用名稱字串猜**（`name.Contains("Wood")`）。
    /// 名稱猜測會在改名、本地化、美術換命名規則時**無聲失效**，而且「聽起來像什麼」本來就
    /// 不一定等於「叫什麼名字」。名稱只在 **Editor 的審計工具**裡當「建議」，
    /// 由人確認後寫進這張表——建議與權威是兩回事。
    ///
    /// ## 查表成本
    ///
    /// <see cref="Initialize"/> 把清單攤平成 `Dictionary`（以 Unity 物件的參考為 key，無字串、無 boxing）。
    /// 執行期是 O(1) 參考比對；而且 <see cref="SurfaceResolver"/> 還會對「同一個 collider」做記憶化，
    /// 實務上每幀查表次數趨近於 0。
    /// </summary>
    [CreateAssetMenu(fileName = "SurfaceMap", menuName = "Project/Footstep/Surface Map")]
    public sealed class SurfaceMapSO : ScriptableObject
    {
        [Serializable]
        public struct MaterialEntry
        {
            [Tooltip("Renderer 上的共用 Material。")]
            public Material Material;
            public SurfaceId Surface;
        }

        [Serializable]
        public struct PhysicMaterialEntry
        {
            [Tooltip("Collider 上的 PhysicsMaterial。⚠️ 目前 demo map 一個都沒有，此層保留給日後使用。")]
            public PhysicsMaterial PhysicsMaterial;
            public SurfaceId Surface;
        }

        [Serializable]
        public struct TerrainLayerEntry
        {
            [Tooltip("TerrainLayer 資產。⚠️ 目前場景沒有 Terrain，此層為擴充點。")]
            public TerrainLayer TerrainLayer;
            public SurfaceId Surface;
        }

        [Header("Default")]
        [Tooltip("完全無法辨識時使用的材質。**不得為 Unknown**——沒有配置的地板也必須有聲音。\n" +
                 "本專案設為 Stone，語意是「泛用硬地板」（水泥／磁磚／石材／一般室內硬地）。")]
        [SerializeField] private SurfaceId defaultSurface = SurfaceId.Stone;

        [Header("Mapping（優先級由上而下）")]
        [Tooltip("② Collider 的 PhysicsMaterial → 材質。比 Renderer Material 優先，因為它描述的是「物理表面」。")]
        [SerializeField] private PhysicMaterialEntry[] physicsMaterials = Array.Empty<PhysicMaterialEntry>();

        [Tooltip("③ Terrain Layer → 材質（取腳下權重最高的 layer）。")]
        [SerializeField] private TerrainLayerEntry[] terrainLayers = Array.Empty<TerrainLayerEntry>();

        [Tooltip("④ Renderer 的共用 Material → 材質。**這是主要配置管道**。")]
        [SerializeField] private MaterialEntry[] materials = Array.Empty<MaterialEntry>();

        private Dictionary<Material, SurfaceId> _materialLookup;
        private Dictionary<PhysicsMaterial, SurfaceId> _physicsLookup;
        private Dictionary<TerrainLayer, SurfaceId> _terrainLookup;

        /// <summary>完全無法辨識時的材質。保證非 <see cref="SurfaceId.Unknown"/>。</summary>
        public SurfaceId DefaultSurface => defaultSurface == SurfaceId.Unknown ? SurfaceId.Stone : defaultSurface;

        /// <summary>供 Editor 審計工具列舉已對應的 Material。</summary>
        public IReadOnlyList<MaterialEntry> Materials => materials;

        /// <summary>
        /// 攤平成查表結構。由使用端於 Awake 呼叫；重複呼叫是冪等的（同 <c>AudioLibrarySO.Initialize</c> 先例）。
        /// </summary>
        public void Initialize()
        {
            _materialLookup = Build(materials, e => e.Material, e => e.Surface);
            _physicsLookup = Build(physicsMaterials, e => e.PhysicsMaterial, e => e.Surface);
            _terrainLookup = Build(terrainLayers, e => e.TerrainLayer, e => e.Surface);
        }

        private static Dictionary<TKey, SurfaceId> Build<TEntry, TKey>(
            TEntry[] entries, Func<TEntry, TKey> keyOf, Func<TEntry, SurfaceId> valueOf) where TKey : class
        {
            var map = new Dictionary<TKey, SurfaceId>();
            if (entries == null) return map;
            for (int i = 0; i < entries.Length; i++)
            {
                TKey key = keyOf(entries[i]);
                if (key == null) continue;
                SurfaceId value = valueOf(entries[i]);
                if (value == SurfaceId.Unknown) continue; // 「對應到 Unknown」等同沒設定，讓它往下一層走
                map[key] = value;                          // 重複時後者覆蓋前者
            }
            return map;
        }

        /// <summary>PhysicsMaterial → 材質；查無回 <see cref="SurfaceId.Unknown"/>。</summary>
        public SurfaceId LookupPhysicsMaterial(PhysicsMaterial material)
        {
            if (material == null || _physicsLookup == null) return SurfaceId.Unknown;
            return _physicsLookup.TryGetValue(material, out SurfaceId s) ? s : SurfaceId.Unknown;
        }

        /// <summary>TerrainLayer → 材質；查無回 <see cref="SurfaceId.Unknown"/>。</summary>
        public SurfaceId LookupTerrainLayer(TerrainLayer layer)
        {
            if (layer == null || _terrainLookup == null) return SurfaceId.Unknown;
            return _terrainLookup.TryGetValue(layer, out SurfaceId s) ? s : SurfaceId.Unknown;
        }

        /// <summary>Material → 材質；查無回 <see cref="SurfaceId.Unknown"/>。</summary>
        public SurfaceId LookupMaterial(Material material)
        {
            if (material == null || _materialLookup == null) return SurfaceId.Unknown;
            return _materialLookup.TryGetValue(material, out SurfaceId s) ? s : SurfaceId.Unknown;
        }
    }
}
