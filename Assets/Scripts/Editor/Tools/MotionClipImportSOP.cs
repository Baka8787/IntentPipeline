#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>
    /// Humanoid 動畫 clip 的 Root Transform 匯入設定 SOP 套用工具。
    /// 依「clip 類型 × Root Transform 匯入設定」矩陣（已定調於 docs/02-dev-spec.md §0.4）：
    /// 執行期用不到的 root motion 成分一律 Bake Into Pose——本專案 applyRootMotion 恆為 false（ADR-001），
    /// 未 Bake 的成分會被引擎「抽出後丟棄」，hips 被錨定在 root 上、雙腳反向滑動，即腳掌滑移的主嫌；
    /// 烘焙器要採樣的成分（Jump 的 Y、Roll 的 XZ/旋轉）維持不 Bake。
    /// Based Upon：XZ／旋轉一律 Original（所有 clip 共用 armature 原點參考系，杜絕切換瞬間的水平跳移）；
    /// Y 軸一般為 Original，唯 Jump 家族採 **Feet**——Y 的 Based Upon 是「全程連續追蹤」而非僅起始對齊，
    /// 用 Feet 可讓貼地段（前搖/收勢）root Y 持平（下沉量保留在姿勢、執行期腳踩得住），
    /// 滯空段腳升高才進 root Y（烘焙器量測 apex 用、執行期丟棄改由物理接管），
    /// 一舉化解「烘焙要量 root Y、執行期又不能丟蹲下」的兩難。
    /// <para>
    /// 實作以 <see cref="ModelImporter.defaultClipAnimations"/>（或既有 clipAnimations）為基底覆寫旗標，
    /// take 名稱與影格範圍由 Unity 自動填入，不手改 .meta；`X Bot@動作名` 命名慣例會讓子 clip 自動獲得 @ 後的名稱。
    /// </para>
    /// <para>
    /// ⚠️ 資產管理規範（2026-07-17 定調，dev-spec §0.4 規則 0）：全專案一律**直接引用 FBX 子 clip**
    /// （AnimationClip 預設不可變、FBX 為唯一真相來源），Ctrl+D 重萃取 .anim 快照已廢止——
    /// 因此本工具改的就是執行期實際播放的 clip，套用後立即生效，不再有「快照過期」的同步問題。
    /// 該 clip 若有對應的 MotionBakeData，套用後必須重烘焙。
    /// </para>
    /// <para>
    /// 🆕 套用粒度（per-clip）：選取 FBX 本體 → 整檔套用（單 clip FBX、或確實要整檔時用）；選取個別
    /// AnimationClip 子資產 → **只套那幾支具名 clip、不碰同 FBX 其他 clip**——這是 Kubold 這類「一支 FBX
    /// 內含多類型 clip（Idle／Walk／Start／Turn…）」的正確用法，避免把單一 preset 誤灌到整檔。
    /// 同一 FBX 兩者都選時整檔優先（涵蓋較廣）。
    /// </para>
    /// </summary>
    public static class MotionClipImportSOP
    {
        private const string MenuRoot = "Assets/Project 動畫匯入 SOP/";

        private readonly struct ImportPreset
        {
            public readonly bool BakeXZ;
            public readonly bool BakeY;
            public readonly bool BakeRotation;
            public readonly bool LoopTime;
            public readonly string Name;
            public readonly bool YBasedUponFeet;

            public ImportPreset(
                bool bakeXZ,
                bool bakeY,
                bool bakeRotation,
                bool loopTime,
                string name,
                bool yBasedUponFeet = false)
            {
                BakeXZ = bakeXZ;
                BakeY = bakeY;
                BakeRotation = bakeRotation;
                LoopTime = loopTime;
                Name = name;
                YBasedUponFeet = yBasedUponFeet;
            }
        }

        // Preset 參數的唯一真相來源：MenuItem 與 batchmode 明確清單入口都只引用這些值。
        private static readonly ImportPreset _locomotionInPlacePreset =
            new ImportPreset(bakeXZ: true, bakeY: true, bakeRotation: true, loopTime: true, name: "Locomotion-原地");
        private static readonly ImportPreset _locomotionTravelPreset =
            new ImportPreset(bakeXZ: false, bakeY: true, bakeRotation: true, loopTime: true, name: "Locomotion-位移");
        private static readonly ImportPreset _jumpPreset =
            new ImportPreset(bakeXZ: true, bakeY: false, bakeRotation: true, loopTime: false, name: "Jump", yBasedUponFeet: true);
        private static readonly ImportPreset _bakedCurvePreset =
            new ImportPreset(bakeXZ: false, bakeY: true, bakeRotation: false, loopTime: false, name: "BakedCurve(Roll)");

        // === Preset：Locomotion-原地（Idle 類；clip 本身無位移，微量 root 漂移全 Bake 保留原作姿勢）===
        [MenuItem(MenuRoot + "套用 Locomotion-原地 設定（Idle 類；XZ·Y·Rot 全 Bake＋Loop）")]
        private static void ApplyLocomotionInPlace()
            => ApplyToSelection(_locomotionInPlacePreset);

        // === Preset：Locomotion-位移（Walk / Run 類；clip 帶真實 root motion，v0.16.1 新增）===
        // XZ 不 Bake：執行期 applyRootMotion=false 會把位移「抽出後丟棄」＝天然原地化；
        // 同時烘焙器（採樣時開 root motion）仍量得到天生步速——速度真相，供 MotionDriver 校準與 Mixer 門檻換算。
        // 若誤套全 Bake，位移會被烤進姿勢：執行期原地播放時角色在動畫內前衝、循環點瞬移回彈。
        // （配套慣例：Mixamo 下載一律不勾 In Place，保留 root motion 作為資料來源，見 dev-spec §0.4 規則 3。）
        [MenuItem(MenuRoot + "套用 Locomotion-位移 設定（Walk/Run 類；XZ 不 Bake 供採速度＋Loop）")]
        private static void ApplyLocomotionTravel()
            => ApplyToSelection(_locomotionTravelPreset);

        // === Preset：Jump 家族（物理 launch 驅動；Y 不 Bake 供烘焙器採 AutoApexHeight，見 changelog v0.13）===
        // Y 的 Based Upon 採 Feet（而非 Original）：讓 root Y 追蹤「腳的高度」——
        // 貼地階段（前搖蹲下/落地收勢）root Y 持平，下沉量留在姿勢裡，執行期腳能踩住地面；
        // 滯空階段腳升高才進 root Y，執行期丟棄、由 ApplyJumpLaunch 物理接管，不會二重上升。
        // 若用 Original，蹲下下沉會被歸入 root motion Y 而在 applyRootMotion=false 下被抹平，
        // 表現為「雙腿向骨盆收攏、蹲不下去」（2026-07-14 Step 1 實測確認）。
        [MenuItem(MenuRoot + "套用 Jump 設定（XZ·Rot Bake；Y 不 Bake·Based Upon Feet）")]
        private static void ApplyJump()
            => ApplyToSelection(_jumpPreset);

        // === Preset：烘焙曲線驅動（Roll 等；XZ／旋轉不 Bake 供烘焙器採 SpeedCurve／RotationCurve）===
        [MenuItem(MenuRoot + "套用烘焙曲線驅動設定（XZ·Rot 不 Bake 供採樣；Y Bake）")]
        private static void ApplyBakedCurve()
            => ApplyToSelection(_bakedCurvePreset);

        [MenuItem(MenuRoot + "套用 Locomotion-原地 設定（Idle 類；XZ·Y·Rot 全 Bake＋Loop）", true)]
        [MenuItem(MenuRoot + "套用 Locomotion-位移 設定（Walk/Run 類；XZ 不 Bake 供採速度＋Loop）", true)]
        [MenuItem(MenuRoot + "套用 Jump 設定（XZ·Rot Bake；Y 不 Bake·Based Upon Feet）", true)]
        [MenuItem(MenuRoot + "套用烘焙曲線驅動設定（XZ·Rot 不 Bake 供採樣；Y Bake）", true)]
        private static bool ValidateSelectionHasModelImporter()
        {
            foreach (Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter) return true;
            }
            return false;
        }

        /// <summary>
        /// 對明確列出的 FBX 子 clip 套用 Locomotion-位移 preset；不讀取或改寫 Project Selection。
        /// </summary>
        internal static bool ApplyLocomotionTravelTo(IReadOnlyList<AnimationClip> clips, out string report)
            => ApplyPresetToExplicitClips(clips, _locomotionTravelPreset, out report);

        /// <summary>
        /// 對明確列出的 FBX 子 clip 套用 **Jump 家族** preset；不讀取或改寫 Project Selection。
        ///
        /// ⚠️ **跳躍一定要走這條 per-clip 路徑，⛔ 不得整檔套用**：
        /// Movement Animset Pro 的**同一支 FBX** 同時裝著 locomotion（Locomotion-位移）與 jump 兩類 clip，
        /// 而兩者的 preset 在 XZ／Y／Based Upon／Loop 上**幾乎每一項都相反**。
        /// 整檔套用會把另一類的匯入設定直接灌掉——dev-spec §0.4 規則 1 明文要求用子 clip 選取。
        /// </summary>
        internal static bool ApplyJumpTo(IReadOnlyList<AnimationClip> clips, out string report)
            => ApplyPresetToExplicitClips(clips, _jumpPreset, out report);

        /// <summary>
        /// 對明確列出的 FBX 子 clip 套用 **Locomotion-原地** preset；不讀取或改寫 Project Selection。
        ///
        /// 🔴 **為什麼 `FallingLoop` 走這條而不是 Jump preset**（2026-09-09）：
        /// Jump preset 是 <c>loopTime: false</c>——套在一支**空中循環**上會讓它播完就停，
        /// 名字叫 `*Loop` 卻不循環。且我們只把它當**純視覺**用（垂直運動由物理擁有，ADR-002），
        /// 因此 XZ／Y／Rot **全部 Bake Into Pose**、不萃取任何 root motion，才不會與物理打架。
        /// ⇒ 這正是 Locomotion-原地 的語意：clip 自身不位移、姿勢原樣保留、可循環。
        /// </summary>
        internal static bool ApplyLocomotionInPlaceTo(IReadOnlyList<AnimationClip> clips, out string report)
            => ApplyPresetToExplicitClips(clips, _locomotionInPlacePreset, out report);

        /// <summary>
        /// 兩個「明確清單」入口的共用實作。
        /// ⚖️ 抽成共用是因為**出現了第二個使用者**（Jump）——在那之前只有一份，
        /// 依 CLAUDE.md「第二個使用者出現前不建抽象」不該提前一般化。
        /// </summary>
        private static bool ApplyPresetToExplicitClips(
            IReadOnlyList<AnimationClip> clips, ImportPreset preset, out string report)
        {
            if (!TryBuildExplicitClipTargets(clips, out Dictionary<string, HashSet<string>> targets, out int expectedClipCount, out report))
                return false;

            int appliedFbxCount = ApplyToTargets(targets, preset, out string applyReport, out int appliedClipCount);
            LogApplyResult(preset, appliedFbxCount, applyReport);

            if (appliedClipCount != expectedClipCount)
            {
                report = $"只成功套用 {appliedClipCount}/{expectedClipCount} 支明確指定的 clip。\n{applyReport}";
                return false;
            }

            report = applyReport;
            return true;
        }

        private static void ApplyToSelection(ImportPreset preset)
        {
            // 🆕 依選取內容決定套用粒度：
            // - 選到 FBX 本體（Model／GameObject）→ 整檔套用（wholeFbx；給單 clip FBX 或確要整檔時用）。
            // - 選到個別 AnimationClip 子資產 → 只套那幾支具名 clip（perClip），不碰同 FBX 其他 clip。
            // 同一 FBX 若兩者都選，整檔優先（涵蓋較廣）。
            var wholeFbx = new HashSet<string>();
            var perClip = new Dictionary<string, HashSet<string>>();

            foreach (Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not ModelImporter) continue;

                if (obj is AnimationClip clip)
                {
                    if (!perClip.TryGetValue(path, out HashSet<string> names)) perClip[path] = names = new HashSet<string>();
                    names.Add(clip.name);
                }
                else
                {
                    wholeFbx.Add(path); // FBX 本體（含 Model root GameObject）＝整檔
                }
            }

            // 彙整目標：value == null → 整檔套用；非 null → 只套集合內的 clip 名稱。整檔優先。
            var targets = new Dictionary<string, HashSet<string>>();
            foreach (string p in wholeFbx) targets[p] = null;
            foreach (KeyValuePair<string, HashSet<string>> kv in perClip)
                if (!wholeFbx.Contains(kv.Key)) targets[kv.Key] = kv.Value;

            int appliedFbxCount = ApplyToTargets(targets, preset, out string report, out _);
            LogApplyResult(preset, appliedFbxCount, report);
        }

        private static bool TryBuildExplicitClipTargets(
            IReadOnlyList<AnimationClip> clips,
            out Dictionary<string, HashSet<string>> targets,
            out int expectedClipCount,
            out string error)
        {
            targets = null;
            expectedClipCount = 0;

            if (clips == null || clips.Count == 0)
            {
                error = "明確 clip 清單不可為空。";
                return false;
            }

            var result = new Dictionary<string, HashSet<string>>();
            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null)
                {
                    error = $"明確 clip 清單第 {i + 1} 列為空。";
                    return false;
                }

                string path = AssetDatabase.GetAssetPath(clip);
                if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not ModelImporter)
                {
                    error = $"'{clip.name}' 不是 ModelImporter 管理的 FBX 子 clip。";
                    return false;
                }

                if (!result.TryGetValue(path, out HashSet<string> names))
                    result[path] = names = new HashSet<string>();

                if (!names.Add(clip.name))
                {
                    error = $"明確 clip 清單含有重複項目：{clip.name}";
                    return false;
                }

                expectedClipCount++;
            }

            targets = result;
            error = null;
            return true;
        }

        private static int ApplyToTargets(
            Dictionary<string, HashSet<string>> targets,
            ImportPreset preset,
            out string report,
            out int appliedClipCount)
        {
            bool bakeXZ = preset.BakeXZ;
            bool bakeY = preset.BakeY;
            bool bakeRotation = preset.BakeRotation;
            bool loopTime = preset.LoopTime;
            bool yBasedUponFeet = preset.YBasedUponFeet;

            var reportBuilder = new StringBuilder();
            int applied = 0;
            appliedClipCount = 0;

            foreach (KeyValuePair<string, HashSet<string>> target in targets)
            {
                string path = target.Key;
                HashSet<string> onlyClips = target.Value; // null = 全部
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);

                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    Debug.LogWarning($"[動畫匯入 SOP] 已略過 '{path}'：animationType 不是 Humanoid，與本 SOP 前提不符。");
                    continue;
                }

                // 以既有 clip 配置為基底（保留其他 clip 與手動調過的欄位）；從未配置過（clipAnimations 為空）
                // 則以 defaultClipAnimations 為基底——take 名稱／影格範圍由 Unity 填入，clip 引用不會斷。
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

                if (clips == null || clips.Length == 0)
                {
                    Debug.LogWarning($"[動畫匯入 SOP] 已略過 '{path}'：檔內沒有任何動畫 take。");
                    continue;
                }

                int clipApplied = 0;
                foreach (ModelImporterClipAnimation clip in clips)
                {
                    // per-clip：只套選中的具名 clip，其餘 clip 保持原設定不動。
                    if (onlyClips != null && !onlyClips.Contains(clip.name)) continue;

                    // Bake Into Pose 三軸：lockRootRotation＝Rotation、lockRootHeightY＝Y、lockRootPositionXZ＝XZ
                    clip.lockRootRotation = bakeRotation;
                    clip.lockRootHeightY = bakeY;
                    clip.lockRootPositionXZ = bakeXZ;

                    // Based Upon：XZ／旋轉一律 Original（統一所有 clip 的水平參考系）；
                    // Y 依 preset 決定——Original（一般）或 Feet（Jump 家族：貼地段 root Y 持平保住蹲下姿勢，
                    // 滯空段腳升高才進 root Y，供烘焙器量測、執行期由物理接管）。
                    clip.keepOriginalOrientation = true;
                    clip.keepOriginalPositionXZ = true;
                    clip.keepOriginalPositionY = !yBasedUponFeet;
                    clip.heightFromFeet = yBasedUponFeet;

                    clip.loopTime = loopTime;
                    // 刻意不動 loopPose／cycleOffset／事件／遮罩等其餘欄位（最小變更原則）。

                    clipApplied++;
                    appliedClipCount++;
                    reportBuilder.AppendLine(
                        $"  {System.IO.Path.GetFileName(path)} › clip '{clip.name}'：" +
                        $"BakeXZ={bakeXZ}, BakeY={bakeY}, BakeRot={bakeRotation}, Loop={loopTime}, " +
                        $"BasedUpon(Y)={(yBasedUponFeet ? "Feet" : "Original")}, BasedUpon(XZ/Rot)=Original");
                }

                if (clipApplied == 0)
                {
                    Debug.LogWarning($"[動畫匯入 SOP] '{path}'：選取的子 clip 名稱未在此 FBX 對到，未套用。");
                    continue;
                }

                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                applied++;
            }

            report = reportBuilder.ToString();
            return applied;
        }

        private static void LogApplyResult(ImportPreset preset, int appliedFbxCount, string report)
        {
            if (appliedFbxCount > 0)
            {
                Debug.Log($"[動畫匯入 SOP] Preset '{preset.Name}' 已套用（{appliedFbxCount} 個 FBX）：\n{report}" +
                          "提醒：若該 clip 有對應的 MotionBakeData 資產，請用烘焙工具重烘焙一次以保持資產與新設定一致。");
            }
            else
            {
                Debug.LogWarning("[動畫匯入 SOP] 選取範圍內沒有可套用的 Humanoid FBX／子 clip。請在 Project 視窗選取動畫 FBX 或其子 clip 後再執行。");
            }
        }
    }
}
#endif
