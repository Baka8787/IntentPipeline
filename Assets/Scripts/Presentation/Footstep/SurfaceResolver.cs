using System;
using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Footstep
{
    /// <summary>
    /// 🆕（Stage 2）把 Foot IK 觀測到的 <c>Collider</c> 解析成聲學材質。
    ///
    /// ## 優先級（由高到低）
    /// <code>
    /// ① SurfaceOverride（GetComponentInParent）  ← 顯式，最高
    /// ② Collider.sharedMaterial（PhysicsMaterial）
    /// ③ TerrainLayer（僅 TerrainCollider；取腳下權重最高者）
    /// ④ Renderer.sharedMaterial[0]（self → parent → child）
    /// ⑤ SurfaceMapSO.DefaultSurface              ← 保證有聲音
    /// </code>
    ///
    /// ## 為什麼是 struct ＋ 記憶化
    /// 材質解析每帧都要（落地那一瞬間也必須有值），但 <c>GetComponentInParent</c> 不是免費的。
    /// 實務上腳會在**同一個 collider** 上停留數十帧，因此只要記住「上一次問的是哪個 collider、答案是什麼」，
    /// 就能把絕大多數帧的成本降到一次參考比對。⚠️ 記憶化的 key 是 collider **參考**，
    /// 不是位置——換了 collider 就重算，同一個 collider 上換位置不重算（同一塊地板材質不會變）。
    ///
    /// 每隻腳各持有一份（左右腳可能踩在不同 collider 上）。值型別 ⇒ 擁有者明確、零配置。
    /// </summary>
    public struct SurfaceResolver
    {
        private Collider _cachedCollider;
        private SurfaceId _cachedSurface;

        /// <summary>
        /// 解析本帧的材質。
        /// </summary>
        /// <param name="collider">Foot IK 本帧觀測到的碰撞體；<c>null</c>（在空中）回 <see cref="SurfaceId.Unknown"/>。</param>
        /// <param name="hitPoint">命中點（僅 Terrain 需要，用來取 splat 權重）。</param>
        /// <param name="map">對應表；<c>null</c> 時一律回 <see cref="SurfaceId.Unknown"/>。</param>
        public SurfaceId Resolve(Collider collider, Vector3 hitPoint, SurfaceMapSO map)
        {
            // 在空中／沒有觀測：不表態。⚠️ 刻意**不**沿用上一次的結果——
            // 「上一次踩在木橋上」不代表「現在空中的我等一下會落在木橋上」。
            if (collider == null || map == null)
            {
                _cachedCollider = null;
                return SurfaceId.Unknown;
            }

            // 記憶化：同一個 collider 直接回上次的答案。
            // ⚠️ Terrain 例外——同一個 TerrainCollider 上不同位置是不同 layer，不能快取。
            if (ReferenceEquals(collider, _cachedCollider) && !(collider is TerrainCollider))
            {
                return _cachedSurface;
            }

            SurfaceId resolved = ResolveUncached(collider, hitPoint, map);

            _cachedCollider = collider;
            _cachedSurface = resolved;
            return resolved;
        }

        private static SurfaceId ResolveUncached(Collider collider, Vector3 hitPoint, SurfaceMapSO map)
        {
            // ① 顯式 Override（掛在父層即可涵蓋所有子 collider）
            var over = collider.GetComponentInParent<SurfaceOverride>();
            if (over != null && over.Surface != SurfaceId.Unknown) return over.Surface;

            // ② PhysicsMaterial
            SurfaceId fromPhysics = map.LookupPhysicsMaterial(collider.sharedMaterial);
            if (fromPhysics != SurfaceId.Unknown) return fromPhysics;

            // ③ Terrain（只有 TerrainCollider 走這條）
            if (collider is TerrainCollider && collider.TryGetComponent(out Terrain terrain))
            {
                SurfaceId fromTerrain = ResolveTerrain(terrain, hitPoint, map);
                if (fromTerrain != SurfaceId.Unknown) return fromTerrain;
            }

            // ④ Renderer 的第一個 Material（self → parent → child）
            //    實測 demo map：所有地面 collider 的 Renderer 都在自己身上，parent／child 只是防禦線。
            var renderer = collider.GetComponent<Renderer>();
            if (renderer == null) renderer = collider.GetComponentInParent<Renderer>();
            if (renderer == null) renderer = collider.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                SurfaceId fromMaterial = map.LookupMaterial(renderer.sharedMaterial);
                if (fromMaterial != SurfaceId.Unknown) return fromMaterial;
            }

            // ⑤ 兜底：沒有配置的地板也必須有聲音
            return map.DefaultSurface;
        }

        /// <summary>
        /// 取腳下**權重最高**的 TerrainLayer 對應的材質。
        ///
        /// ⚠️ **先把「對應到同一個材質」的 layer 權重相加，再取最大**——
        /// 兩個都對應 Dirt 的 layer 各 0.3，合起來 0.6 應該贏過單一 0.4 的 Rock。
        /// 逐 layer 取 argmax 會判成 Rock（`dropecho/unity_footstep` 正是這樣，見 `docs/34` §3.1）。
        ///
        /// ⛔ 本輪**不做**多材質音效混合（使用者明確要求）；權重只用來選出唯一勝者。
        /// ⚠️ `GetAlphamaps` 會配置陣列 ⇒ **有 GC**。目前場景沒有 Terrain，這條路徑不會被走到；
        ///    真的用上 Terrain 時應先做 splat 快取，屆時再處理。
        /// </summary>
        private static SurfaceId ResolveTerrain(Terrain terrain, Vector3 worldPosition, SurfaceMapSO map)
        {
            TerrainData data = terrain.terrainData;
            if (data == null) return SurfaceId.Unknown;

            Vector3 terrainPosition = terrain.transform.position;
            float nx = (worldPosition.x - terrainPosition.x) / data.size.x;
            float nz = (worldPosition.z - terrainPosition.z) / data.size.z;
            if (nx < 0f || nx > 1f || nz < 0f || nz > 1f) return SurfaceId.Unknown;

            int mapX = Mathf.Clamp(Mathf.FloorToInt(nx * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int mapZ = Mathf.Clamp(Mathf.FloorToInt(nz * data.alphamapHeight), 0, data.alphamapHeight - 1);

            float[,,] splat = data.GetAlphamaps(mapX, mapZ, 1, 1);
            TerrainLayer[] layers = data.terrainLayers;

            // 以 SurfaceId 為索引累加權重（enum 值小且連續，用小陣列即可，不需要 Dictionary）
            Span<float> weights = stackalloc float[8];
            int count = Mathf.Min(splat.GetLength(2), layers.Length);
            for (int i = 0; i < count; i++)
            {
                SurfaceId s = map.LookupTerrainLayer(layers[i]);
                if (s == SurfaceId.Unknown) continue;
                int index = (int)s;
                if (index >= 0 && index < weights.Length) weights[index] += splat[0, 0, i];
            }

            SurfaceId best = SurfaceId.Unknown;
            float bestWeight = 0f;
            for (int i = 1; i < weights.Length; i++)
            {
                if (weights[i] <= bestWeight) continue;
                bestWeight = weights[i];
                best = (SurfaceId)i;
            }
            return best;
        }
    }
}
