using System;
using UnityEngine;
using UnityEngine.Serialization;

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

        [Header("🧪 停在樓梯上時站直（docs/05 §3.5.4.6；需要 T3 開啟；預設關閉，只在 X Bot 開啟）")]
        [Tooltip("開啟後：移動中骨盆照舊（參考面包絡）；靜止後骨盆以彈簧升到「兩腳都搆得到」的最高點（永不低於包絡），" +
                 "一有移動意圖或 root／腳開始動就立即回到包絡。關閉 ＝ 與原型之前逐位元相同。")]
        public bool UseStandingPosturePrototype = false;

        [Tooltip("偏好站姿：兩腳都以此伸展比（髖→踝 ／ 腿長）搆到目標的最高骨盆。平地待機本身約 1.0；留一點餘裕避免卡在 Leg Stretch。")]
        [Range(0.9f, 1f)] public float StandingPreferredExtension = 0.995f;

        [Tooltip("新可靠 T3 落腳邊沿只有在 root 水平速度低於此值時才視為最後支撐腳（這不是站起門檻）。")]
        [FormerlySerializedAs("StandingRootSpeedMax")]
        [Min(0f)] public float StandingFinalSupportSpeedMax = 1.35f;

        [Tooltip("腳處於 Locked、β 已達 LockBeta、有即時接觸，且動畫腳水平速度低於此值時才算可靠支撐。")]
        [Min(0f)] public float StandingReliableFootSpeedMax = 0.3f;

        [Tooltip("已 Upright 後對向上小變動的臨界阻尼角頻率（rad/s）。StopProgress 主軌跡由支撐腿伸展驅動，不用這個值。")]
        [Min(0.1f)] public float StandingRiseOmega = 6f;

        [Tooltip("① Early Stop Recovery：放開移動意圖後，依 root 減速進度釋放包絡深度的臨界阻尼角頻率（rad/s）。6 ＝ 離線驗證的 soft 候選（未定案）。")]
        [Min(0.1f)] public float StandingEarlyLiftOmega = 6f;

        [Tooltip("① 抬升量頂到硬可達上限時的最大退讓速度（m/s）：不瞬間下拉，以此速率讓出。")]
        [Min(0.01f)] public float StandingEarlyLiftRetreatSpeed = 1f;

        [Tooltip("① 放開當下 root 水平速度低於此值（m/s）不視為 locomotion 停步，不啟動 Early Recovery。")]
        [Min(0f)] public float StandingEarlyReleaseMinRootSpeed = 0.1f;

        [Tooltip("② 第一支撐後支撐腿伸展時長（s）：尾腳 Unlocked ∧ β≥0.8 ∧ 下降中（即將落定）。")]
        [Min(0.02f)] public float StandingRiseDurationNearSettle = 0.18f;

        [Tooltip("② 支撐腿伸展時長（s）：尾腳已接觸且 Locked，或 Releasing ∧ β≥0.6 ∧ 下降中。")]
        [Min(0.02f)] public float StandingRiseDurationSettling = 0.20f;

        [Tooltip("② 支撐腿伸展時長（s）：其餘情況。承接速度夠大時會更短（1.5 × 剩餘伸展 ／ 進入速度）。")]
        [Min(0.02f)] public float StandingRiseDurationDefault = 0.40f;

        [Tooltip("③ 尾腳前瞻可達上限的飽和帶寬（m）：骨盆低於 上限−帶寬 時完全不動，以上才平滑逼近上限。")]
        [Min(0f)] public float StandingTrailingCapBand = 0.03f;

        [Tooltip("③ 上升途中上限消失（尾腳離開 Locked）時，被壓住的差距以此角頻率（rad/s）的臨界阻尼彈簧從靜止收掉。")]
        [Min(0.1f)] public float StandingTrailingCapReleaseOmega = 10f;

        [Tooltip("回到包絡的臨界阻尼彈簧角頻率（rad/s）。硬性可達上限另外保證腳搆得到。")]
        [Min(0.1f)] public float StandingReturnOmega = 15f;

        [Tooltip("交回 Moving 的條件：離包絡的距離（公尺）與速度（m/s）都低於這兩個值。")]
        [Min(0f)] public float StandingHandbackTolerance = 0.01f;
        [Min(0f)] public float StandingHandbackSpeed = 0.05f;

        [Header("🧪 統一骨盆求解器——只替換 Moving（docs/46 §6；需要 T3；預設關閉，只在 X Bot 開啟）")]
        [Tooltip("開啟後：Moving 的骨盆＝ Intent 0 → Support／Landing 可達約束 → 非對稱臨界阻尼；Terrain Envelope 只剩後備。" +
                 "Stop／Standing 的規則不變，但它們讀到的「移動參考」改成這條路徑。關閉 ＝ 與原型之前逐位元相同。")]
        public bool UseUnifiedMovingPelvis = false;

        [Tooltip("目標低於骨盆（必須跟上可達約束）時的臨界阻尼角頻率（rad/s）。")]
        [Min(0.1f)] public float MovingPelvisDropOmega = 12f;

        [Tooltip("StepRecovery：步態內約束暫時解除時的回升角頻率（rad/s）。越小 ⇒ 每步起伏越小、平均越深。")]
        [Min(0.1f)] public float MovingPelvisRiseOmega = 1.5f;

        [Tooltip("下樓中（移動、root 下降）的 Step 回升角頻率（rad/s；docs/46 §33）。下樓的下蹲大半是回升太慢累積的記憶；" +
                 "以最終骨骼重播選定 6：Walk 下樓 −30 → −22 cm、Run 下樓 −26 → −15 cm，起伏 ≤ 動畫 ＋3 cm。設成與 MovingPelvisRiseOmega 相同 ＝ 關閉。")]
        [Min(0.1f)] public float MovingPelvisDescendRiseOmega = 6f;

        [Tooltip("TerrainExitRecovery：地形提示（Terrain Envelope）說周圍已不需要這麼低時的回升角頻率（rad/s）。" +
                 "提示只選回升速度，不是目標也不是上限。")]
        [Min(0.1f)] public float MovingTerrainExitOmega = 6f;

        [Tooltip("進入 TerrainExitRecovery：地形提示高於骨盆超過此值（m）。")]
        [Min(0f)] public float MovingTerrainExitMargin = 0.05f;

        [Tooltip("離開 TerrainExitRecovery：地形提示高於骨盆不到此值（m）。與進入門檻構成滯後。")]
        [Min(0f)] public float MovingTerrainExitReleaseMargin = 0.02f;

        [Header("🧪 停步／站立也交給統一求解器（docs/46 §14；需要 UseUnifiedMovingPelvis；預設關閉）")]
        [Tooltip("開啟後：舊的停步鏈（EarlyRecovery／StopProgress／Upright／Return）整條旁路，停步只改變回升語意：" +
                 "意圖放開到落定前 StopHold（不回升），落定後 Stand。約束與目標與移動中完全相同。關閉 ＝ 維持舊停步鏈。")]
        public bool UseUnifiedStandingPelvis = false;

        [Tooltip("Stand 回升角頻率（rad/s）：停步已落定、沒有步態起伏要控制時回到兩腳都搆得到的最高處。")]
        [Min(0.1f)] public float StandingUnifiedRiseOmega = 6f;

        [Tooltip("停步「落定」：兩腳都 T3 Locked 且 root 水平速度低於此值（m/s）。")]
        [Min(0f)] public float StandingUnifiedSettleRootSpeed = 0.05f;

        [Tooltip("SupportTransfer（docs/46 §19）：前腳成為 Reliable Primary Support 就開始站直；尾腳 Releasing／Swing 不往下拉；" +
                 "Walk 下樓停步以預測落點當 FutureReachCap。關閉 ＝ 回到 StopHold（等兩腳都 Locked 才站）。")]
        public bool StandingUseSupportTransfer = true;

        [Tooltip("Landing 預告：下降中的未鎖腳，其可達上限放寬 κ × 腳底離地高度，接觸瞬間精確。")]
        [Min(0f)] public float MovingLandingAnticipation = 1.5f;

        [Tooltip("Support 腳的硬可達比（× 腿長）。1.05 ＝ Humanoid Leg Stretch 的邊界。")]
        [Range(1f, 1.1f)] public float MovingHardReachRatio = 1.05f;

        [Header("T3 Locked riser-intrusion rule（docs/46 §22–§23、§25）")]
        [Tooltip("Locked（支撐已選定）時，腳跟／腳尖的即時命中比 LockedSupportY 高出 [MinRise, MaxRise]、且腳踝底仍在原支撐上 ⇒ " +
                 "判成腳部 pivot 側向碰到 riser：該點改用 LockedSupportY，不把整隻腳抬到上一階。查詢仍用實際腳位與旋轉。預設關閉 ＝ 現行。")]
        public bool LockedRiserIntrusionRule = false;

        [Tooltip("riser 侵入：高出鎖定支撐至少這麼多（m）才算「相鄰的上一階」。")]
        [Min(0f)] public float LockedRiserIntrusionMinRise = 0.08f;

        [Tooltip("riser 侵入：高出鎖定支撐最多這麼多（m）——超過就不是相鄰一階，照常處理。")]
        [Min(0f)] public float LockedRiserIntrusionMaxRise = 0.30f;

        [Tooltip("腳踝底的即時命中與 LockedSupportY 相差在此之內（m）＝ 腳的中心仍站在原支撐上。")]
        [Min(0f)] public float LockedRiserIntrusionSupportTolerance = 0.03f;

        [Tooltip("riser 侵入的幾何條件：端點高度必須仍在 riser 的垂直範圍內，最多只能高出上一階頂面這麼多（m）。" +
                 "高出更多 ＝ 端點其實已抬到上一階之上（抬跟／離地），不是側面侵入。" +
                 "4 mm 由量測決定（docs/46 §23）：腳跟階角站姿 max +1.4 mm、抬跟誤判 min +6.8 mm。")]
        [Min(0f)] public float LockedRiserIntrusionEdgeTolerance = 0.004f;
    }
}
