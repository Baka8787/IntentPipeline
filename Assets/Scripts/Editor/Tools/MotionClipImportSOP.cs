#if UNITY_EDITOR
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
    /// </summary>
    public static class MotionClipImportSOP
    {
        private const string MenuRoot = "Assets/Project 動畫匯入 SOP/";

        // === Preset：Locomotion-原地（Idle 類；clip 本身無位移，微量 root 漂移全 Bake 保留原作姿勢）===
        [MenuItem(MenuRoot + "套用 Locomotion-原地 設定（Idle 類；XZ·Y·Rot 全 Bake＋Loop）")]
        private static void ApplyLocomotionInPlace()
            => ApplyToSelection(bakeXZ: true, bakeY: true, bakeRotation: true, loopTime: true, presetName: "Locomotion-原地");

        // === Preset：Locomotion-位移（Walk / Run 類；clip 帶真實 root motion，v0.16.1 新增）===
        // XZ 不 Bake：執行期 applyRootMotion=false 會把位移「抽出後丟棄」＝天然原地化；
        // 同時烘焙器（採樣時開 root motion）仍量得到天生步速——速度真相，供 MotionDriver 校準與 Mixer 門檻換算。
        // 若誤套全 Bake，位移會被烤進姿勢：執行期原地播放時角色在動畫內前衝、循環點瞬移回彈。
        // （配套慣例：Mixamo 下載一律不勾 In Place，保留 root motion 作為資料來源，見 dev-spec §0.4 規則 3。）
        [MenuItem(MenuRoot + "套用 Locomotion-位移 設定（Walk/Run 類；XZ 不 Bake 供採速度＋Loop）")]
        private static void ApplyLocomotionTravel()
            => ApplyToSelection(bakeXZ: false, bakeY: true, bakeRotation: true, loopTime: true, presetName: "Locomotion-位移");

        // === Preset：Jump 家族（物理 launch 驅動；Y 不 Bake 供烘焙器採 AutoApexHeight，見 changelog v0.13）===
        // Y 的 Based Upon 採 Feet（而非 Original）：讓 root Y 追蹤「腳的高度」——
        // 貼地階段（前搖蹲下/落地收勢）root Y 持平，下沉量留在姿勢裡，執行期腳能踩住地面；
        // 滯空階段腳升高才進 root Y，執行期丟棄、由 ApplyJumpLaunch 物理接管，不會二重上升。
        // 若用 Original，蹲下下沉會被歸入 root motion Y 而在 applyRootMotion=false 下被抹平，
        // 表現為「雙腿向骨盆收攏、蹲不下去」（2026-07-14 Step 1 實測確認）。
        [MenuItem(MenuRoot + "套用 Jump 設定（XZ·Rot Bake；Y 不 Bake·Based Upon Feet）")]
        private static void ApplyJump()
            => ApplyToSelection(bakeXZ: true, bakeY: false, bakeRotation: true, loopTime: false, presetName: "Jump", yBasedUponFeet: true);

        // === Preset：烘焙曲線驅動（Roll 等；XZ／旋轉不 Bake 供烘焙器採 SpeedCurve／RotationCurve）===
        [MenuItem(MenuRoot + "套用烘焙曲線驅動設定（XZ·Rot 不 Bake 供採樣；Y Bake）")]
        private static void ApplyBakedCurve()
            => ApplyToSelection(bakeXZ: false, bakeY: true, bakeRotation: false, loopTime: false, presetName: "BakedCurve(Roll)");

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

        private static void ApplyToSelection(bool bakeXZ, bool bakeY, bool bakeRotation, bool loopTime, string presetName, bool yBasedUponFeet = false)
        {
            var report = new StringBuilder();
            int applied = 0;

            foreach (Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;

                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    Debug.LogWarning($"[動畫匯入 SOP] 已略過 '{path}'：animationType 不是 Humanoid，與本 SOP 前提不符。");
                    continue;
                }

                // 以既有 clip 配置為基底（保留使用者手動調過的其他欄位）；從未配置過（clipAnimations 為空）
                // 則以 defaultClipAnimations 為基底——take 名稱／影格範圍由 Unity 填入，clip 引用不會斷。
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

                if (clips == null || clips.Length == 0)
                {
                    Debug.LogWarning($"[動畫匯入 SOP] 已略過 '{path}'：檔內沒有任何動畫 take。");
                    continue;
                }

                foreach (ModelImporterClipAnimation clip in clips)
                {
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

                    report.AppendLine(
                        $"  {System.IO.Path.GetFileName(path)} › clip '{clip.name}'：" +
                        $"BakeXZ={bakeXZ}, BakeY={bakeY}, BakeRot={bakeRotation}, Loop={loopTime}, " +
                        $"BasedUpon(Y)={(yBasedUponFeet ? "Feet" : "Original")}, BasedUpon(XZ/Rot)=Original");
                }

                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                applied++;
            }

            if (applied > 0)
            {
                Debug.Log($"[動畫匯入 SOP] Preset '{presetName}' 已套用至 {applied} 個 FBX 並重新匯入：\n{report}" +
                          "提醒：若該 clip 有對應的 MotionBakeData 資產，請用烘焙工具重烘焙一次以保持資產與新設定一致。");
            }
            else
            {
                Debug.LogWarning("[動畫匯入 SOP] 選取範圍內沒有可套用的 Humanoid FBX。請在 Project 視窗選取動畫 FBX 後再執行。");
            }
        }
    }
}
#endif
