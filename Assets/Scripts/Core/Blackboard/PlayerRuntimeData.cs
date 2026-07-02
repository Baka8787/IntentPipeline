using Project.Core.Arbitration;
using UnityEngine;

namespace Project.Core.Blackboard
{
    /// <summary>
    /// 武器實例的基礎 Dummy 類別（供編譯與後續擴充使用）
    /// </summary>
    public class ItemInstance { }

    /// <summary>
    /// 資料中心黑板：所有玩法模組唯一的讀寫窗口
    /// ⚠️ 規格書防禦警語：絕對不可在此加入 public InputData RawInput; 否則編譯直接失敗。
    /// </summary>
    public class PlayerRuntimeData
    {
        // === 意圖區（每帧處理完由 Pipeline 在 LateUpdate 復位）===
        // 註：在 C# 中，若 Intent 改為 Property 會因為結構體值複製機制導致無法直接修改內部成員
        // (例如 data.Intent.JumpRequested = true 會編譯失敗)。因此依規格書維持公開欄位。
        public IntentData Intent;
        // === 仲裁區 ===
        // 每幀由仲裁管線統一覆寫，表現層下游只讀不寫
        // 註：同 Intent，維持公開欄位而非 Property，避免 struct 值複製導致無法直接修改內部旗標
        public ArbiterData Arbitration;

        // === 參數區（持續存在，每帧更新，公開屬性採用 PascalCase）===
        public float MoveSpeed { get; set; }
        public Vector2 MoveDirection { get; set; }
        public float UpperBodyWeight { get; set; }
        public Transform CameraTransform { get; set; }

        // === 引用區 ===
        /// <summary>
        /// 當前裝備的武器。規格書規範：唯讀引用，禁止外部修改內容。
        /// 限制 setter 為 internal，僅允許 EquipmentDriver 進行修改。
        /// </summary>
        public ItemInstance CurrentWeapon { get; internal set; }
        public Transform AimTarget { get; set; }


    }
}