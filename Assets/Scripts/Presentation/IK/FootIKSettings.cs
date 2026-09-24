using System;
using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// Foot IK 可調參數的集中容器（Serializable，嵌入 FootIKController 的 Inspector）。
    /// M3.5 基線參數＋L1 v4 Heel/Ankle/Toe Ground Sampling——M3.2~M3.4 的實驗參數
    /// （fade 閾值／Slope Gate／濾波速率／ReachRatio）已隨實驗機制一併移除，
    /// 實驗結論與復刻指引見 changelog v0.18.2~v0.18.6 與 WORKLOG「Foot IK 品質路線圖」。
    /// 純數據容器（比照 MotionBakeData 的 public 欄位慣例），不含任何邏輯。
    /// </summary>
    [Serializable]
    public class FootIKSettings
    {
        [Header("Ground Detection")]
        [Tooltip("地面偵測的 Layer 遮罩。務必包含地形所在 Layer；留 Nothing 會使所有 raycast 落空、IK 權重恆 0。")]
        public LayerMask GroundLayers = ~0;

        [Tooltip("raycast 起點＝動畫腳部 goal 正上方此高度（公尺），向下打。需大於腳部單步抬升與台階落差。")]
        public float RaycastUpOffset = 0.5f;

        [Tooltip("raycast 總長度（公尺）。至少涵蓋 RaycastUpOffset＋預期最大向下落差（斜坡／台階）。")]
        public float RaycastDistance = 1.1f;

        [Tooltip("Heel 端點在腳自身座標系中，腳踝沿 local -forward 到腳跟的距離（公尺）。不決定腳踝基礎高度或法線。")]
        [Min(0f)]
        public float HeelOffset = 0.1f;

        [Tooltip("Toe 端點在腳自身座標系中，腳踝沿 local +forward 到腳尖的距離（公尺）。不決定腳踝基礎高度或法線。")]
        [Min(0f)]
        public float ToeOffset = 0.15f;

        [Tooltip("僅切換 Heel/Toe 戳穿殘差抬升；關閉後仍使用 ankle ray 與泰勒斯修正。執行期品質不得依地形動態切換。")]
        public bool UseTwoPointSampling = true;

        [Header("Foot Alignment Limit（L1 v3：踝關節角度上限）")]
        [Tooltip("腳底對齊地面法線的最大角度（度）。超過此值時腳保持較自然的姿勢，改由戳穿殘差抬升，形成『一端接觸、另一端浮空』的真人行為。設為 180 等同完全貼合（A/B 對照用）。")]
        [Range(0f, 180f)]
        public float MaxFootAlignAngle = 15f;

        [Header("Foot Weight（Q3：Runtime Pose Heuristic，單因子二態系統）")]
        [Tooltip("動畫腳部 goal 相對 Root 平面的高度 ≤ 此值 → 目標權重 1（踩地相，IK 全接管）。")]
        public float FootGroundedHeightMin = 0.08f;

        [Tooltip("動畫腳部 goal 相對 Root 平面的高度 ≥ 此值 → 目標權重 0（抬腳相，動畫全接管）。兩值之間線性過渡。")]
        public float FootGroundedHeightMax = 0.25f;

        [Tooltip("權重朝目標值的收斂速率（每秒）。權重目標變化經此平滑，保證不瞬切。")]
        public float WeightSmoothSpeed = 8f;

        [Header("Pelvis Compensation（Q2）")]
        [Tooltip("骨盆最大下沉量（公尺）。超出的高差＝低腳搆不到，屬單點採樣的設計極限（未來由雙點採樣／Foot Contact 升級）。")]
        public float MaxPelvisOffset = 0.35f;

        [Tooltip("骨盆偏移的收斂速率（每秒）。")]
        public float PelvisSmoothSpeed = 5f;

        [Tooltip("🆕（2026-09-24，docs/05 §3.5.4.3）身體參考面的取樣半徑（公尺）。0 ＝ 關閉（行為與之前逐位元相同）。\n" +
                 "在「代理移動面」（角色移動會碰、腳看不到的面，例如樓梯的 CharacterMovementSurface）上，\n" +
                 "身體高度改為跟隨 root 周圍 8 個世界固定方向、此半徑處的移動面包絡——連續、與朝向無關；\n" +
                 "腳仍由 IK 搆真實踏面（彎膝吸收）。\n" +
                 "推導（X Bot）：支撐腳離髖部最大水平距離 Walk 0.29／Run 0.23 m，加上踏面可比移動面低一級（約半個踏面深度）⇒ 0.45。\n" +
                 "⚠️ 每個角色的動畫不同，必須各自量測；未量測的角色保持 0。")]
        [Min(0f)] public float ReferenceSampleRadius = 0f;

        [Header("🧪 T3 腳部接觸原型（docs/45 §6–§7；預設關閉，只在 X Bot 開啟）")]
        [Tooltip("開啟後**位置**目標改由 T3 構建：擺盪時腳相對移動面保留動畫離地高度，落腳時以帶朝向的最小抬升貼上接觸面，" +
                 "踩住後鎖住水平位置。位置權重維持 1；旋轉權重與旋轉目標不變（Codex High 2）。\n" +
                 "關閉 ＝ 與原型之前逐位元相同。⚠️ 原型：未量測的角色保持關閉。")]
        public bool UseContactPrototype = false;

        [Tooltip("落腳相位 β 的速度貢獻：動畫腳水平速度 ≤ 此值（m/s）⇒ 1。")]
        [Min(0f)] public float ContactPhaseSpeedMin = 1f;

        [Tooltip("落腳相位 β 的速度貢獻：動畫腳水平速度 ≥ 此值（m/s）⇒ 0。")]
        [Min(0f)] public float ContactPhaseSpeedMax = 4f;

        [Tooltip("β ≥ 此值（且上一帧有接觸）⇒ 鎖住腳的水平位置。")]
        [Range(0f, 1f)] public float LockBeta = 0.95f;

        [Tooltip("鎖定中 β < 此值 ⇒ 開始釋放（與 LockBeta 構成滯後）。")]
        [Range(0f, 1f)] public float UnlockBeta = 0.7f;

        [Tooltip("鎖定中動畫腳離鎖定點的水平距離 > 此值（公尺）⇒ 開始釋放。需大於動畫支撐腳本身的滑移（實測 4–9 cm）。")]
        [Min(0f)] public float UnlockRadius = 0.2f;

        [Tooltip("釋放時從鎖定點平滑回到動畫腳位的時間（秒）。")]
        [Min(0f)] public float ReleaseTime = 0.12f;

        [Tooltip("動畫腳一帧位移超過此值（公尺）視為瞬移／不連續：狀態與速度歷史立即重設。")]
        [Min(0f)] public float MaxPoseStepPerFrame = 0.5f;
    }
}
