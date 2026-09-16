// throwaway：匯入 Assets/Audio/Combat 並套用與既有腳步素材相同的 import 設定。
// 參照：Assets/Audio/Footsteps/WASD/... 的 .meta
//   loadType 0 = DecompressOnLoad, compressionFormat 0 = PCM, preloadAudioData 1,
//   forceToMono 1, normalize 1, 3D 1, sampleRateSetting 0 = PreserveSampleRate
var log = new System.Text.StringBuilder();

// Refresh 已在前一次呼叫完成（.meta 已生成）；再跑一次只會白等。

var guids = UnityEditor.AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/Combat" });
int changed = 0, total = 0;

foreach (var g in guids)
{
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var imp = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.AudioImporter;
    if (imp == null) continue;
    total++;

    var s = imp.defaultSampleSettings;
    bool dirty = false;

    if (s.loadType != UnityEngine.AudioClipLoadType.DecompressOnLoad)
    { s.loadType = UnityEngine.AudioClipLoadType.DecompressOnLoad; dirty = true; }
    if (s.compressionFormat != UnityEngine.AudioCompressionFormat.PCM)
    { s.compressionFormat = UnityEngine.AudioCompressionFormat.PCM; dirty = true; }
    if (!s.preloadAudioData) { s.preloadAudioData = true; dirty = true; }
    if (s.sampleRateSetting != UnityEditor.AudioSampleRateSetting.PreserveSampleRate)
    { s.sampleRateSetting = UnityEditor.AudioSampleRateSetting.PreserveSampleRate; dirty = true; }

    // ⚠️ forceToMono：檔案本身已經是 mono（ffmpeg -ac 1），但仍然勾起來——
    //    它是「保證」不是「轉換」，日後有人換了立體聲素材進來時會自動修正。
    if (!imp.forceToMono) { imp.forceToMono = true; dirty = true; }
    // ⛔ normalize 關掉：我們在 ffmpeg 階段已經做過 peak 正規化到 -1 dBFS，
    //    再讓 Unity 正規化一次會把「刻意做輕的變化版本」全部拉到同一個響度，
    //    等於抵銷 AudioDefinitionSO 的 volumeJitter 想營造的差異。
    // ⚠️ `normalize` 沒有公開 API（只存在於序列化），必須走 SerializedObject。
    var impSo = new UnityEditor.SerializedObject(imp);
    var normProp = impSo.FindProperty("m_Normalize");
    if (normProp != null && normProp.boolValue)
    {
        normProp.boolValue = false;
        impSo.ApplyModifiedPropertiesWithoutUndo();
        dirty = true;
    }
    // ⚠️ 3D 在 Unity 6 已由 AudioImporter 移除（obsolete）——空間化改由 AudioSource.spatialBlend 決定，
    //    而那一項已經在 AudioController 的樣板 Source 上設為 1（docs/36）。這裡不需要也不能設。
    if (imp.loadInBackground) { imp.loadInBackground = false; dirty = true; }

    if (dirty)
    {
        imp.defaultSampleSettings = s;
        UnityEditor.AssetDatabase.WriteImportSettingsIfDirty(p);
        changed++;
    }
}

log.AppendLine("AudioClip total=" + total + " changed=" + changed);
UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();
