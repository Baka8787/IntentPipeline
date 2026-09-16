// throwaway：把 Assets/Audio/Combat 的 clip 填進 13 個 AudioDefinition 資產，
// 並設定各自的 volume / volumeJitter / pitchRange。
var log = new System.Text.StringBuilder();

System.Func<string, UnityEngine.AudioClip[]> clipsIn = folder =>
{
    var guids = UnityEditor.AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
    var list = new System.Collections.Generic.List<UnityEngine.AudioClip>();
    foreach (var g in guids)
    {
        var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
        var c = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>(p);
        if (c != null) list.Add(c);
    }
    list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    return list.ToArray();
};

// 只取名字符合任一關鍵字的（Hurt / Death 要把「人」與「非人」分開）
System.Func<UnityEngine.AudioClip[], string[], UnityEngine.AudioClip[]> only = (src, keys) =>
{
    var list = new System.Collections.Generic.List<UnityEngine.AudioClip>();
    foreach (var c in src)
        foreach (var k in keys)
            if (c.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0) { list.Add(c); break; }
    return list.ToArray();
};

System.Action<string, UnityEngine.AudioClip[], float, float, float, float> wire =
    (assetName, clips, vol, jitter, pMin, pMax) =>
{
    var path = "Assets/ScriptableObjects/Audio/Combat/" + assetName + ".asset";
    var def = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>(path);
    if (def == null) { log.AppendLine("MISSING " + path); return; }

    var so = new UnityEditor.SerializedObject(def);
    var arr = so.FindProperty("clips");
    arr.arraySize = clips.Length;
    for (int i = 0; i < clips.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    so.FindProperty("volume").floatValue = vol;
    so.FindProperty("volumeJitter").floatValue = jitter;
    so.FindProperty("pitchRange").vector2Value = new UnityEngine.Vector2(pMin, pMax);
    so.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.EditorUtility.SetDirty(def);
    log.AppendLine(assetName + "  clips=" + clips.Length + "  vol=" + vol + "  pitch=" + pMin + "-" + pMax);
};

const string A = "Assets/Audio/Combat";

// ── 法術 ───────────────────────────────────────────────────────────────
// pitch 範圍給得比腳步窄：法術是 tonal 素材，音高一動就會聽出「變調」而不是「變化」。
wire("Fireball_Cast",    clipsIn(A + "/Spells/Fireball/Cast"),    0.70f, 0.08f, 0.97f, 1.03f);
wire("Fireball_Release", clipsIn(A + "/Spells/Fireball/Release"), 0.85f, 0.10f, 0.95f, 1.05f);
wire("Fireball_Impact",  clipsIn(A + "/Spells/Fireball/Impact"),  0.90f, 0.10f, 0.94f, 1.06f);
wire("Ice_Cast",         clipsIn(A + "/Spells/Ice/Cast"),         0.70f, 0.08f, 0.97f, 1.03f);
wire("Ice_Release",      clipsIn(A + "/Spells/Ice/Spawn"),        0.85f, 0.10f, 0.96f, 1.04f);
wire("Ice_Impact",       clipsIn(A + "/Spells/Ice/Impact"),       0.90f, 0.10f, 0.94f, 1.06f);

// ── 近戰 ───────────────────────────────────────────────────────────────
// 揮擊音**必響**、命中音只在打到時響 ⇒ 揮擊給低一點，免得蓋過命中的回饋。
wire("Melee_Swing", clipsIn(A + "/Melee/Swing"), 0.65f, 0.12f, 0.93f, 1.07f);
wire("Melee_Hit",   clipsIn(A + "/Melee/Hit"),   0.95f, 0.10f, 0.94f, 1.06f);

// ── 角色 ───────────────────────────────────────────────────────────────
// ⚠️ 人聲的 pitch 抖動要更保守：±3% 以上就會聽成「另一個人」而不是「同一個人的另一聲」。
var hurtAll = clipsIn(A + "/Character/Hurt");
wire("Player_Hurt", only(hurtAll, new[] { "VOXMale", "VOXFem" }),          0.80f, 0.08f, 0.98f, 1.02f);
wire("Enemy_Hurt",  only(hurtAll, new[] { "HMNBrth", "CREAEthr" }),        0.80f, 0.08f, 0.97f, 1.03f);

var deathAll = clipsIn(A + "/Character/Death");
wire("Player_Death", only(deathAll, new[] { "VOXReac" }),                  0.90f, 0.05f, 0.99f, 1.01f);
// 敵人死亡疊上小爆炸，讓它比「只有叫聲」更有結束感（使用者要求的 1–3 層做法）。
var enemyDeath = new System.Collections.Generic.List<UnityEngine.AudioClip>();
enemyDeath.AddRange(only(deathAll, new[] { "CREAMnstr" }));
enemyDeath.AddRange(only(clipsIn(A + "/Spells/Fireball/Impact"), new[] { "EXPLDsgn" }));
wire("Enemy_Death", enemyDeath.ToArray(), 0.90f, 0.05f, 0.98f, 1.02f);

// ── 移動 ───────────────────────────────────────────────────────────────
// 起跳的出力聲要**明顯比腳步小**——它是每次跳都會聽到的東西，太大會很快變吵。
wire("Player_JumpTakeoff", clipsIn(A + "/Movement/JumpTakeoff"), 0.45f, 0.12f, 0.96f, 1.04f);

UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();
