#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>
    /// CapsuleFitter v1：對選取的 CharacterRoot 一鍵匹配 <see cref="CharacterController"/> 膠囊與 Model 對齊。
    /// 規範（dev-spec §0.3 Capsule 對齊規範）：Root 原點＝腳底 ——
    /// <c>center = (0, height/2 + skinWidth, 0)</c>（膠囊上抬 skinWidth，補償 CharacterController
    /// 落地時與地面恆定保持的 skinWidth 緩衝間隙，否則角色會整體懸浮該距離），
    /// Model 子物件 local transform 必須為 identity，
    /// 禁止用 Model 偏移遷就未校準的膠囊（取代舊 Prefab 的 -0.996 魔法數字補償）。
    /// <para>
    /// v1 量測來源：Height＝rest pose 下 Model 網格 bounds 高度；Radius＝gameplay 統一基準半徑 × Animator.humanScale
    /// （碰撞半徑屬 gameplay 參數，不隨模型胖瘦跳動；humanScale 處理不同身高縮放）。
    /// Editor-time 一次性執行、含 Undo，零 Runtime 成本、不改任何 Public API、不動 ADR-001 分層
    /// （Editor 工具觸碰 Model 屬離線配置，比照 MotionBakeEditor 先例；ADR-001 約束的是 Runtime 玩法邏輯）。
    /// </para>
    /// <remarks>
    /// TODO（v2，Future Work，見 WORKLOG Backlog）：
    ///  1. 身高改以 Humanoid Head/Neck 骨骼推估為權威、bounds 僅做條件精化（防頭飾/舉高武器污染 bounds.max.y）。
    ///  2. Radius 補「髖寬（LeftUpperLeg↔RightUpperLeg）/2」下限交叉檢查。
    ///  3. skinWidth／stepOffset 納入自動匹配（v1 刻意不動，僅提示建議值）。
    /// </remarks>
    /// </summary>
    public static class CharacterCapsuleFitter
    {
        /// <summary>gameplay 統一基準半徑（公尺，humanScale = 1 時）。命中判定/通道寬度的一致性參數。</summary>
        private const float BaseRadius = 0.3f;

        /// <summary>humanScale = 1 時的參考身高（公尺），僅供量測合理性警告，不參與寫入。</summary>
        private const float ReferenceHeight = 1.8f;

        /// <summary>量測身高與 humanScale 推估身高的容許相對偏差，超過即警告可能有配件污染 bounds。</summary>
        private const float HeightSanityTolerance = 0.25f;

        /// <summary>rest pose 下網格底部（腳底）允許偏離 Root 原點的量（公尺），超過即警告模型原點不在腳底。</summary>
        private const float FeetOriginTolerance = 0.05f;

        private const string MenuPath = "Tools/Project/角色 Capsule 自動對齊 (CapsuleFitter v1)";

        [MenuItem(MenuPath, true)]
        private static bool ValidateFitSelected()
            => Selection.activeGameObject != null && !Application.isPlaying;

        [MenuItem(MenuPath)]
        private static void FitSelected()
        {
            GameObject root = Selection.activeGameObject;

            // === 前置校驗（全部通過才動任何資料，避免半套用）===
            if (!root.TryGetComponent<CharacterController>(out var controller))
            {
                Debug.LogError($"[CapsuleFitter] '{root.name}' 上沒有 CharacterController。請選取角色的 Root（Adapter）物件。", root);
                return;
            }

            // 比照 ADR-001 §2.2 元件獲取規範：Animator 一律以「子物件搜尋、排除 Root 自身」取得，禁止名稱硬編碼。
            Animator modelAnimator = FindModelAnimator(root);
            if (modelAnimator == null)
            {
                Debug.LogError($"[CapsuleFitter] '{root.name}' 的子物件中找不到 Animator（Root 自身不算）。階層須符合 ADR-001 Root/Model 分離。", root);
                return;
            }

            if (modelAnimator.avatar == null || !modelAnimator.avatar.isHuman)
            {
                Debug.LogError($"[CapsuleFitter] Model Animator '{modelAnimator.name}' 未綁定 Humanoid Avatar，無法量測 humanScale。", modelAnimator);
                return;
            }

            Transform model = modelAnimator.transform;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogError($"[CapsuleFitter] Model '{model.name}' 底下找不到任何 Renderer，無法量測身高。", model);
                return;
            }

            var warnings = new StringBuilder();

            if (model.parent != root.transform)
            {
                warnings.AppendLine($" - Model Animator 所在節點 '{model.name}' 不是 Root 的直接子物件；工具仍以該節點為 Model 對齊目標，但建議檢查階層是否符合 §0.3。");
            }

            // === Undo：一次記錄兩個受影響物件，之後的變更可整體還原 ===
            Undo.RecordObjects(new Object[] { model, controller }, "CapsuleFitter v1");

            // === Model 對齊：local transform 歸零（identity；scale 不動），Root 原點自此即腳底 ===
            bool modelWasOffset = model.localPosition != Vector3.zero || model.localRotation != Quaternion.identity;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;

            // === 量測（rest pose、Model 歸零後）===
            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

            float rootY = root.transform.position.y;
            float height = worldBounds.max.y - rootY;
            float feetOffset = worldBounds.min.y - rootY;

            float humanScale = modelAnimator.humanScale;
            if (humanScale <= 0.01f)
            {
                warnings.AppendLine($" - Animator.humanScale 回傳異常值（{humanScale:F3}），已退回 1.0。");
                humanScale = 1f;
            }

            float radius = BaseRadius * humanScale;

            // CharacterController 幾何約束：膠囊需 height ≥ 2×radius，否則退化為球，這裡直接夾住並警告。
            if (radius > height * 0.5f)
            {
                warnings.AppendLine($" - 計算半徑 {radius:F3} 超過身高的一半，已夾至 {height * 0.5f:F3}（模型可能過矮或 humanScale 異常）。");
                radius = height * 0.5f;
            }

            // === 必要警告（量測合理性）===
            if (Mathf.Abs(feetOffset) > FeetOriginTolerance)
            {
                warnings.AppendLine($" - 網格底部離 Root 原點 {feetOffset:+0.###;-0.###} m：模型原點可能不在腳底（Mixamo 慣例應貼地）。膠囊底仍錨定 Root 原點，請確認來源模型。");
            }

            float scaleEstimatedHeight = ReferenceHeight * humanScale;
            if (Mathf.Abs(height - scaleEstimatedHeight) / scaleEstimatedHeight > HeightSanityTolerance)
            {
                warnings.AppendLine($" - bounds 身高 {height:F2} m 與 humanScale 推估 {scaleEstimatedHeight:F2} m 差異過大：模型可能帶頭飾/舉高武器等配件污染 bounds（v2 將以骨骼推估為權威）。");
            }

            if (controller.skinWidth > radius * 0.2f)
            {
                warnings.AppendLine($" - 目前 skinWidth {controller.skinWidth:F3} 偏大：懸浮量已由 Center 補償，但過大的 skin 會放大窄縫／斜坡的物理誤差，建議手動調為 radius×10% ≈ {radius * 0.1f:F3} 後**重新執行本工具**（v1 依範圍約定不自動修改該欄位）。");
            }

            // === 寫入（僅 v1 範圍核可的三個欄位）===
            // Center 補償 skinWidth：CharacterController 落地時，膠囊表面與地面恆保持約 skinWidth 的
            // 緩衝間隙（引擎防穿插設計）。若膠囊底直接對齊 Root 原點，角色落地後會整體懸浮 skinWidth
            //（2026-07-14 實測：skin 0.08 → 所有狀態均勻懸浮 8cm）。故將膠囊上抬 skinWidth，
            // 落地時 Root 原點（＝腳底）恰好貼地。skinWidth 本身依 v1 範圍約定仍不自動修改（唯讀取用）。
            float skinWidth = controller.skinWidth;
            controller.height = height;
            controller.radius = radius;
            controller.center = new Vector3(0f, height * 0.5f + skinWidth, 0f);

            // === 結果報告 ===
            var summary = new StringBuilder();
            summary.AppendLine($"[CapsuleFitter] '{root.name}' 已完成匹配（可 Ctrl+Z 還原；若為 Prefab 實例請記得 Apply Override）：");
            summary.AppendLine($" - Height = {height:F3} m（Model 網格 bounds）");
            summary.AppendLine($" - Radius = {radius:F3} m（基準 {BaseRadius} × humanScale {humanScale:F3}）");
            summary.AppendLine($" - Center = (0, {height * 0.5f + skinWidth:F3}, 0) → 膠囊上抬 skinWidth({skinWidth:F3})，落地時 Root 原點＝腳底剛好貼地");
            summary.AppendLine(" - 註：Center 依當前 skinWidth 計算，之後若手動調整 skinWidth 請重新執行本工具。");
            summary.AppendLine(modelWasOffset
                ? " - Model local transform 已歸零（原有偏移已移除；Root 錨點從此在腳底）"
                : " - Model local transform 原本即為 identity，未變更");
            summary.AppendLine(" - ⚠️ 套用後 Root 原點語意改為腳底：場景中角色可能浮空約舊偏移量，請下移貼地（或進 Play 由重力落地）。");

            if (warnings.Length > 0)
            {
                summary.AppendLine("警告：");
                summary.Append(warnings);
                Debug.LogWarning(summary.ToString(), root);
            }
            else
            {
                Debug.Log(summary.ToString(), root);
            }
        }

        /// <summary>取得第一顆「非 Root 自身」的子物件 Animator（ADR-001：Animator 必在 Model 層）。</summary>
        private static Animator FindModelAnimator(GameObject root)
        {
            root.TryGetComponent<Animator>(out var rootAnimator);
            foreach (Animator candidate in root.GetComponentsInChildren<Animator>(true))
            {
                if (candidate != rootAnimator) return candidate;
            }
            return null;
        }
    }
}
#endif
