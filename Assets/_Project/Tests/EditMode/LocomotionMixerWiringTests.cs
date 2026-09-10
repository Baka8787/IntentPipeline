using System.Collections.Generic;
using Animancer;
using NUnit.Framework;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// Verification Ladder L1：Locomotion mixer 的唯讀資產接線稽核（**1D／2D 皆適用**）。
    ///
    /// 本檔只透過 <see cref="AssetDatabase"/>、<see cref="AssetImporter"/> 與 Animancer 公開 API 讀取；
    /// 不呼叫任何會修改或儲存 `.asset`／`.meta` 的 API。
    ///
    /// ⚠️ **只斷言「形狀無關」的東西**（L-1＝dev-spec §0.4 匯入設定的共同欄位）。
    /// 取樣點座標、`_Speeds`、環半徑、單環或多環——全部**只進報表不進斷言**，
    /// 因為那些形狀尚未由 prototype ＋ Play 定案（`docs/13` §10，使用者裁決 2026-09-07）。
    /// </summary>
    public class LocomotionMixerWiringTests
    {
        private const string AnimationAssetFolder = "Assets/ScriptableObjects/Animation";
        private const string LocomotionAssetPath = "Assets/ScriptableObjects/Animation/Locomotion.asset";
        private const string MotionBakeSearchRoot = "Assets/ScriptableObjects/Motion";
        private const string PlayerPrefabPath = "Assets/Prefabs/X Bot.prefab";

        /// <summary>
        /// ⚠️ **刻意不記錄「這是不是 Idle」，也不假設 mixer 是 1D 還是 2D。**
        /// 先前版本把 `Threshold == Vector2.zero` 當成 Idle 判準，並硬性要求
        /// `MixerTransition2D` —— 於是 `Locomotion.asset` 還原成 1D 之後整個測項無法執行。
        /// **形狀未定案的東西不該被測試綁死**（`docs/13` §10 使用者裁決）。
        /// </summary>
        private struct MixerClip
        {
            public AnimationClip Clip;
            public int Index;
            public string AssetPath;        // 這支 clip 來自哪一份 mixer 資產
            public string ThresholdLabel;   // 2D：「(0.7,-0.7)」／1D：「0.75」／未知型別：「—」
            public float Radius;            // 取樣點到原點的距離；1D 取門檻絕對值；未知則 NaN
        }

        /// <summary>
        /// 解析執行期實際生效的 <c>MotionDriver.moveSpeed</c>：勾了 override 就用手填值，
        /// 否則用 <c>moveSpeedSource</c> 的代表速度（＝ <c>Awake</c> 的行為）。
        /// 全程唯讀 <see cref="SerializedObject"/>；解析不到回傳 0 讓呼叫端降級印表。
        /// </summary>
        private static float ResolveRuntimeMoveSpeed()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) return 0f;

            var driver = prefab.GetComponentInChildren<MotionDriver>(true);
            if (driver == null) return 0f;

            var so = new SerializedObject(driver);
            float manual = so.FindProperty("moveSpeed")?.floatValue ?? 0f;
            if (so.FindProperty("overrideMoveSpeed")?.boolValue == true) return manual;

            var source = so.FindProperty("moveSpeedSource")?.objectReferenceValue as MotionBakeData;
            if (source == null) return manual;

            float representative = source.GetRepresentativeSpeed();
            return representative > 0f ? representative : manual;
        }

        // =====================================================================
        // L-1 — Mixer clip 的 ModelImporter 設定符合 dev-spec §0.4
        // =====================================================================

        [Test]
        public void L1_MixerClips_FollowLocomotionImportSop()
        {
            List<MixerClip> clips = LoadAllLocomotionMixerClips();
            var violations = new List<string>();

            for (int i = 0; i < clips.Count; i++)
            {
                MixerClip entry = clips[i];
                string sourcePath = AssetDatabase.GetAssetPath(entry.Clip);
                var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;

                if (importer == null)
                {
                    violations.Add(
                        $"{entry.Clip.name}: 找不到來源 FBX 的 ModelImporter（Asset path: '{sourcePath}'）；" +
                        "Locomotion mixer 必須直接引用可套用 §0.4 SOP 的 FBX 子 clip。");
                    continue;
                }

                ModelImporterClipAnimation settings = FindClipSettings(importer, entry.Clip.name);
                if (settings == null)
                {
                    violations.Add(
                        $"{entry.Clip.name}: ModelImporter '{sourcePath}' 找不到同名 clip 設定；" +
                        "無法驗證 dev-spec §0.4 欄位。");
                    continue;
                }

                // ⚠️ **只驗兩個 Locomotion preset 的「共同部分」**。
                //    §0.4 的 Locomotion-原地 與 Locomotion-位移**只有 XZ Bake 一欄不同**
                //    （原地 ✅／位移 ❌），其餘六欄完全一致。
                //    先前版本用「取樣點是不是 (0,0)」去猜 clip 屬於哪一型再斷言 XZ——
                //    那是**用 mixer 的形狀推斷素材的類型**，形狀一改就失準。
                //    ⇒ XZ 改由 L-2 報表印出（供人判讀），不在此斷言。
                const string preset = "Locomotion-原地／位移（共同欄位）";
                AddMismatch(violations, entry.Clip.name, preset,
                    nameof(settings.lockRootRotation), settings.lockRootRotation, true);
                AddMismatch(violations, entry.Clip.name, preset,
                    nameof(settings.lockRootHeightY), settings.lockRootHeightY, true);
                AddMismatch(violations, entry.Clip.name, preset,
                    nameof(settings.keepOriginalOrientation), settings.keepOriginalOrientation, true);
                AddMismatch(violations, entry.Clip.name, preset,
                    nameof(settings.keepOriginalPositionY), settings.keepOriginalPositionY, true);
                AddMismatch(violations, entry.Clip.name, preset,
                    nameof(settings.keepOriginalPositionXZ), settings.keepOriginalPositionXZ, true);
                AddMismatch(violations, entry.Clip.name, preset,
                    nameof(settings.loopTime), settings.loopTime, true);
            }

            CollectionAssert.IsEmpty(violations,
                $"{LocomotionAssetPath} 引用的 clip 未符合 dev-spec §0.4 匯入規範：\n" +
                "請在 Project 視窗選取列出的 FBX 子 clip，套用訊息所示 preset（不要手改 .meta）。\n\n" +
                string.Join("\n", violations));
        }

        // =====================================================================
        // L-2 — 步幅校準「報表」（**永遠通過**，只印表）
        //
        // ⚠️ 這裡刻意**不是**斷言。原本寫成「每支 clip 都必須有 MotionBakeData」是錯的：
        //    Bake 是**量測工具**，不是 mixer child 的架構前提——真正需要 Bake 的是
        //    烘焙曲線驅動（Roll／收步）與速度來源，不是每一支被 blend 的 clip。
        //    把「還沒量」寫成「違反不變量」，等於用測試逼一個架構沒有要求的東西。
        //
        // 它存在的理由是**省掉人工算術**：烘焙完跑一次，直接得到要填的數字。
        // 唯讀（AssetDatabase／SerializedObject），不寫任何資產，不碰 Animancer 內部序列化
        // （CLAUDE.md B11 否決的是「寫入」第三方內部結構，唯讀稽核不在禁令內）。
        // =====================================================================

        [Test]
        public void L2_Report_StrideCalibrationTable()
        {
            List<MixerClip> clips = LoadAllLocomotionMixerClips();
            Dictionary<AnimationClip, MotionBakeData> bakeByClip = LoadBakeDataBySourceClip();
            float moveSpeed = ResolveRuntimeMoveSpeed();

            var report = new System.Text.StringBuilder();
            report.AppendLine("=== Locomotion 2D Mixer 步幅校準報表（唯讀，不修改任何資產）===");
            report.AppendLine(moveSpeed > 0f
                ? $"MotionDriver 執行期 moveSpeed = {moveSpeed:0.####} m/s"
                : "⚠️ 無法解析 moveSpeed（找不到 X Bot 的 MotionDriver 或其 moveSpeedSource）；下表只印天生速度。");
            report.AppendLine("資產 | clip | threshold | 半徑 | XZ Bake | 天生速度 | 現在的 _Speeds | 建議 A：改 _Speeds | 建議 B：改半徑");

            for (int i = 0; i < clips.Count; i++)
            {
                MixerClip entry = clips[i];
                float radius = entry.Radius;
                string assetName = System.IO.Path.GetFileNameWithoutExtension(entry.AssetPath);
                float current = ResolveMixerSpeed(entry);
                string xz = ResolveXzBakeLabel(entry.Clip);

                if (!bakeByClip.TryGetValue(entry.Clip, out MotionBakeData bake))
                {
                    report.AppendLine(
                        $"{assetName} | {entry.Clip.name} | {entry.ThresholdLabel} | {radius:0.###} | {xz} | " +
                        "⚠️ 尚未烘焙 | " + $"{current:0.####} | — | —");
                    continue;
                }

                float natural = bake.GetRepresentativeSpeed();
                string suggestSpeed = natural > 0f && moveSpeed > 0f && !float.IsNaN(radius)
                    ? (radius * moveSpeed / natural).ToString("0.####")
                    : "—";
                string suggestRadius = natural > 0f && moveSpeed > 0f
                    ? (natural / moveSpeed).ToString("0.####")
                    : "—";

                report.AppendLine(
                    $"{assetName} | {entry.Clip.name} | {entry.ThresholdLabel} | {radius:0.###} | {xz} | " +
                    $"{natural:0.####} | {current:0.####} | {suggestSpeed} | {suggestRadius}");
            }

            report.AppendLine();
            report.AppendLine("建議 A（維持取樣點座標、改 _Speeds）：speed_i = 半徑 × moveSpeed ÷ 天生速度");
            report.AppendLine("建議 B（維持 _Speeds = 1、把取樣點移到該 clip 的天生速度環）：半徑_i = 天生速度 ÷ moveSpeed");
            report.AppendLine("⚠️ A 會讓 clip 被加速播放（倍率離 1 越遠越假）；B 保持動畫原速，但整個環會內縮，");
            report.AppendLine("   環外（更快）的區間就沒有 clip 可用 —— 那正是『單環撐不住整個速度域』的證據。");

            UnityEngine.Debug.Log(report.ToString());
            Assert.Pass("報表已輸出至 Console（本測項永遠通過）。");
        }

        // =====================================================================
        // 🗑️ L-3（原「非 Idle 的 _Speeds 不可全為 1」）**已移除**
        //
        // 它假設「全 1 ＝ 校準遺失」，但那不成立：若取樣點半徑正好落在該 clip 的天生速度環上，
        // **1 才是正確值**（見 L-2 報表的建議 B）。把一個可能正確的狀態斷言成錯誤，
        // 只會逼出「為了讓測試變綠而亂填數字」——那比沒有測試更糟。
        //
        // 真正該驗的是「動起來對不對」，那需要 Play；EditMode 能誠實守住的只有 L-1 的匯入設定。
        // =====================================================================

        // =====================================================================
        // 唯讀探索 helpers
        // =====================================================================

        /// <summary>
        /// 掃出 `Assets/ScriptableObjects/Animation/` 底下所有 `Locomotion*` 的 mixer 資產並讀出 clip。
        /// **同時涵蓋現行 1D 與未來的 2D prototype**——新增 `Locomotion_2D_Prototype.asset` 之後
        /// 不必改測試就會被納入稽核。
        /// ⚠️ 形狀（1D／2D）與取樣點座標**不在斷言範圍**，只用於報表。
        /// </summary>
        private static List<MixerClip> LoadAllLocomotionMixerClips()
        {
            var result = new List<MixerClip>();
            string[] guids = AssetDatabase.FindAssets("Locomotion t:TransitionAsset", new[] { AnimationAssetFolder });
            var scanned = new List<string>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<TransitionAsset>(path);
                if (asset == null || !asset.HasTransition) continue;
                if (!TryReadMixer(asset.Transition, out Object[] animations, out string[] labels, out float[] radii))
                    continue;

                scanned.Add(path);
                ReadMixerClips(path, animations, labels, radii, result);
            }

            Assert.IsNotEmpty(scanned,
                $"在 '{AnimationAssetFolder}' 找不到任何名稱含 Locomotion 的 mixer TransitionAsset。" +
                $"預期至少有 '{LocomotionAssetPath}'。");
            Assert.IsNotEmpty(result,
                $"掃到 mixer 資產（{string.Join(", ", scanned)}）但沒有任何 AnimationClip 引用。");
            return result;
        }

        /// <summary>
        /// ⚠️ **Animancer 的 1D 與 2D mixer 沒有共同的非泛型基底**
        /// （`LinearMixerTransition : MixerTransition&lt;LinearMixerState, float&gt;`、
        ///  `MixerTransition2D : MixerTransition&lt;Vector2MixerState, Vector2&gt;`，
        ///  而非泛型的 `ManualMixerTransition` 是**另一個**具體類別）
        /// ⇒ 只能型別分支，不能靠基底轉型。**不支援的型別回 false 而不是丟例外**，
        /// 讓未來出現第三種 mixer 時是「沒被稽核到」而不是「測試整個爆掉」。
        /// </summary>
        private static bool TryReadMixer(
            object transition, out Object[] animations, out string[] labels, out float[] radii)
        {
            switch (transition)
            {
                case MixerTransition2D mixer2D:
                    animations = mixer2D.Animations;
                    Vector2[] thresholds2D = mixer2D.Thresholds;
                    BuildThresholdLabels(animations, thresholds2D, out labels, out radii);
                    return animations != null;

                case LinearMixerTransition mixer1D:
                    animations = mixer1D.Animations;
                    float[] thresholds1D = mixer1D.Thresholds;
                    BuildThresholdLabels(animations, thresholds1D, out labels, out radii);
                    return animations != null;

                default:
                    animations = null;
                    labels = null;
                    radii = null;
                    return false;
            }
        }

        private static void BuildThresholdLabels(
            Object[] animations, Vector2[] thresholds, out string[] labels, out float[] radii)
        {
            int count = animations?.Length ?? 0;
            labels = new string[count];
            radii = new float[count];
            for (int i = 0; i < count; i++)
            {
                bool has = thresholds != null && i < thresholds.Length;
                labels[i] = has ? $"({thresholds[i].x:0.##},{thresholds[i].y:0.##})" : "—";
                radii[i] = has ? thresholds[i].magnitude : float.NaN;
            }
        }

        private static void BuildThresholdLabels(
            Object[] animations, float[] thresholds, out string[] labels, out float[] radii)
        {
            int count = animations?.Length ?? 0;
            labels = new string[count];
            radii = new float[count];
            for (int i = 0; i < count; i++)
            {
                bool has = thresholds != null && i < thresholds.Length;
                labels[i] = has ? thresholds[i].ToString("0.##") : "—";
                radii[i] = has ? Mathf.Abs(thresholds[i]) : float.NaN;
            }
        }

        private static void ReadMixerClips(
            string path, Object[] animations, string[] labels, float[] radii, List<MixerClip> into)
        {
            if (animations == null) return;

            for (int i = 0; i < animations.Length; i++)
            {
                if (animations[i] is not AnimationClip clip) continue;

                into.Add(new MixerClip
                {
                    Clip = clip,
                    Index = i,
                    AssetPath = path,
                    ThresholdLabel = labels != null && i < labels.Length ? labels[i] : "—",
                    Radius = radii != null && i < radii.Length ? radii[i] : float.NaN
                });
            }
        }

        /// <summary>報表用：讀該 clip 在其所屬 mixer 的 `_Speeds`。讀不到回傳 NaN，不影響測試通過。</summary>
        private static float ResolveMixerSpeed(MixerClip entry)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TransitionAsset>(entry.AssetPath);
            if (asset == null || !asset.HasTransition) return float.NaN;

            float[] speeds = asset.Transition switch
            {
                MixerTransition2D mixer2D => mixer2D.Speeds,
                LinearMixerTransition mixer1D => mixer1D.Speeds,
                _ => null
            };

            return speeds != null && entry.Index < speeds.Length ? speeds[entry.Index] : float.NaN;
        }

        /// <summary>報表用：印出 XZ Bake 現值（原地型應為 On、位移型應為 Off；**不斷言**）。</summary>
        private static string ResolveXzBakeLabel(AnimationClip clip)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as ModelImporter;
            if (importer == null) return "—";

            ModelImporterClipAnimation settings = FindClipSettings(importer, clip.name);
            if (settings == null) return "—";

            return settings.lockRootPositionXZ ? "On（原地型）" : "Off（位移型）";
        }


        private static ModelImporterClipAnimation FindClipSettings(ModelImporter importer, string clipName)
        {
            ModelImporterClipAnimation[] settings = importer.clipAnimations;
            if (settings == null || settings.Length == 0)
                settings = importer.defaultClipAnimations;

            if (settings == null) return null;

            for (int i = 0; i < settings.Length; i++)
                if (settings[i].name == clipName)
                    return settings[i];

            return null;
        }

        private static void AddMismatch(
            List<string> violations,
            string clipName,
            string preset,
            string field,
            bool actual,
            bool expected)
        {
            if (actual == expected) return;

            violations.Add(
                $"- {clipName}: {field} = {actual.ToString().ToLowerInvariant()}; " +
                $"expected {expected.ToString().ToLowerInvariant()}（請套用 {preset} preset）");
        }

        private static Dictionary<AnimationClip, MotionBakeData> LoadBakeDataBySourceClip()
        {
            var result = new Dictionary<AnimationClip, MotionBakeData>();
            string[] guids = AssetDatabase.FindAssets("t:MotionBakeData", new[] { MotionBakeSearchRoot });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MotionBakeData bake = AssetDatabase.LoadAssetAtPath<MotionBakeData>(path);
                if (bake == null || bake.SourceClip == null || result.ContainsKey(bake.SourceClip)) continue;

                result.Add(bake.SourceClip, bake);
            }

            return result;
        }
    }
}
