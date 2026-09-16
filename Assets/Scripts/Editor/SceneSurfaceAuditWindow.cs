using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Footstep;

namespace Project.Editor
{
    /// <summary>
    /// 🆕（Stage 2）**Scene Surface Audit**：掃描目前場景會被踩到的地面，列出
    /// 「哪些 Material 已對應／哪些還沒」，並提供一鍵補上缺漏。
    ///
    /// ## 為什麼需要這個工具（Editor Tool 兩道閘門）
    /// * **Gate A（投資值得）**：沒有它，「這張場景還有哪塊地板沒設定」只能靠人走進去一塊一塊踩。
    ///   漏掉的後果是**無聲失效**——地板會落到 default surface，聽起來仍然「有聲音」，
    ///   所以不會有人發現配錯了。這正是「human operation is error-prone（silent mistakes）」那一款。
    /// * **Gate B（不依賴第三方內部結構）**：只用 `Renderer.sharedMaterials` 與 `SerializedObject`，
    ///   沒有碰任何套件的私有序列化。
    ///
    /// ## ⚠️ 名稱建議只是建議
    /// 本視窗會依 Material 名稱猜一個材質（`"wood"` → Wood…），但那**只出現在 Editor**，
    /// 而且要由人按下按鈕才會寫進 `SurfaceMapSO`。
    /// ⛔ runtime **永遠不**做名稱比對——名稱會改、會本地化、會被美術換規則，
    /// 而且「叫什麼」本來就不等於「聽起來像什麼」（貼石紋的木橋）。**建議與權威是兩回事。**
    /// </summary>
    public sealed class SceneSurfaceAuditWindow : EditorWindow
    {
        private sealed class Row
        {
            public Material Material;
            public int ColliderCount;
            public SurfaceId Mapped;
            public SurfaceId Suggestion;
            public string SampleObject;
        }

        private SurfaceMapSO _map;
        private readonly List<Row> _rows = new();
        private readonly List<string> _notes = new();
        private Vector2 _scroll;

        [MenuItem("Tools/Project/Scene Surface Audit")]
        private static void Open()
        {
            GetWindow<SceneSurfaceAuditWindow>("Surface Audit").minSize = new Vector2(560f, 320f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "掃描場景中所有非 trigger collider 會用到的 Material，比對 Surface Map。\n" +
                "目標：常見 Material 設定一次即可，不需要逐個 GameObject 掛 Tag。",
                MessageType.Info);

            _map = (SurfaceMapSO)EditorGUILayout.ObjectField("Surface Map", _map, typeof(SurfaceMapSO), false);

            using (new EditorGUI.DisabledScope(_map == null))
            {
                if (GUILayout.Button("Scan Scene", GUILayout.Height(26f))) Scan();
            }

            if (_notes.Count > 0)
            {
                EditorGUILayout.Space();
                foreach (string note in _notes) EditorGUILayout.HelpBox(note, MessageType.Warning);
            }

            if (_rows.Count == 0) return;

            int unmapped = 0;
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].Mapped == SurfaceId.Unknown) unmapped++;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"共 {_rows.Count} 種 Material；已對應 {_rows.Count - unmapped}，未對應 {unmapped}",
                EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _rows.Count; i++) DrawRow(_rows[i]);
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(unmapped == 0))
            {
                if (GUILayout.Button($"把 {unmapped} 個未對應的 Material 依「建議」加入 Surface Map", GUILayout.Height(24f)))
                {
                    ApplySuggestions();
                }
            }
        }

        private void DrawRow(Row row)
        {
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                bool isMapped = row.Mapped != SurfaceId.Unknown;
                Color previous = GUI.color;
                GUI.color = isMapped ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.8f, 0.5f);
                GUILayout.Label(isMapped ? "✔" : "?", GUILayout.Width(16f));
                GUI.color = previous;

                EditorGUILayout.ObjectField(row.Material, typeof(Material), false, GUILayout.Width(200f));
                GUILayout.Label($"{row.ColliderCount} collider", GUILayout.Width(80f));

                if (isMapped)
                {
                    GUILayout.Label("→ " + row.Mapped, EditorStyles.boldLabel, GUILayout.Width(80f));
                }
                else
                {
                    GUILayout.Label("建議 " + row.Suggestion, GUILayout.Width(90f));
                }

                GUILayout.Label(row.SampleObject, EditorStyles.miniLabel);
            }
        }

        private void Scan()
        {
            _rows.Clear();
            _notes.Clear();
            if (_map == null) return;
            _map.Initialize();

            var byMaterial = new Dictionary<Material, Row>();
            int noRenderer = 0;
            int overrideCount = 0;
            int terrainCount = 0;

            var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider.isTrigger) continue;
                if (collider is CharacterController) continue; // 角色自己不是地面

                if (collider.GetComponentInParent<SurfaceOverride>() != null) { overrideCount++; continue; }
                if (collider is TerrainCollider) { terrainCount++; continue; }

                var renderer = collider.GetComponent<Renderer>();
                if (renderer == null) renderer = collider.GetComponentInParent<Renderer>();
                if (renderer == null) renderer = collider.GetComponentInChildren<Renderer>();
                if (renderer == null) { noRenderer++; continue; }

                Material material = renderer.sharedMaterial;
                if (material == null) { noRenderer++; continue; }

                if (!byMaterial.TryGetValue(material, out Row row))
                {
                    row = new Row
                    {
                        Material = material,
                        Mapped = _map.LookupMaterial(material),
                        Suggestion = Suggest(material.name),
                        SampleObject = collider.gameObject.name,
                    };
                    byMaterial.Add(material, row);
                    _rows.Add(row);
                }
                row.ColliderCount++;
            }

            _rows.Sort((a, b) => b.ColliderCount.CompareTo(a.ColliderCount));

            if (noRenderer > 0)
                _notes.Add($"{noRenderer} 個 collider 取不到 Material（沒有 Renderer 或 Material 為空）。" +
                           "它們會落到 Default Surface；若其中有需要特定音效的，請掛 SurfaceOverride。");
            if (terrainCount > 0)
                _notes.Add($"場景有 {terrainCount} 個 TerrainCollider。請在 Surface Map 的 Terrain Layers 區塊設定對應。");
            if (overrideCount > 0)
                _notes.Add($"{overrideCount} 個 collider 由 SurfaceOverride 顯式指定，已跳過（這是預期行為）。");
        }

        /// <summary>
        /// ⚠️ **僅供 Editor 建議**。名稱是給人看的線索，不是 runtime 的判斷依據。
        /// 猜不出來時一律回 Stone（＝泛用硬地板），因為那是本專案的 default。
        /// </summary>
        private static SurfaceId Suggest(string materialName)
        {
            string n = materialName.ToLowerInvariant();
            if (Contains(n, "wood", "plank", "timber", "parquet")) return SurfaceId.Wood;
            if (Contains(n, "grass", "turf", "lawn", "moss")) return SurfaceId.Grass;
            if (Contains(n, "dirt", "soil", "mud", "earth", "sand", "gravel")) return SurfaceId.Dirt;
            return SurfaceId.Stone;
        }

        private static bool Contains(string haystack, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++) if (haystack.Contains(needles[i])) return true;
            return false;
        }

        private void ApplySuggestions()
        {
            if (_map == null) return;

            var so = new SerializedObject(_map);
            SerializedProperty materials = so.FindProperty("materials");

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                if (row.Mapped != SurfaceId.Unknown) continue;

                int index = materials.arraySize;
                materials.arraySize = index + 1;
                SerializedProperty entry = materials.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Material").objectReferenceValue = row.Material;
                entry.FindPropertyRelative("Surface").enumValueIndex = (int)row.Suggestion;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(_map);
            AssetDatabase.SaveAssetIfDirty(_map);
            Scan();
        }
    }
}
