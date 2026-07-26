using UnityEngine;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（ADR-003 D2／§10 Stage 1-3）**Locomotion 專屬**的 gait 配置資產：
    /// 「修飾鍵 → 移動強度 [0-1]」的 per-game 映射（Movement Policy 的資料部分）。
    ///
    /// 範圍刻意收窄（ADR-003 §6.2 的教訓）：本資產**只管 on-foot 的 gait**，不是「泛用 movement profile」。
    /// Strafe(2D)／Swim／Vehicle 各有自己的控制範式，屆時各自帶自己的配置，不擴脹本資產。
    /// 體力／鎖定等能力門檻由「需要它的那個 policy 自行查詢」，不進 universal caps 雜物袋。
    ///
    /// 換玩法控制方案（Souls-like／ARPG／平台）＝**換這顆 asset**，Pipeline 核心零改（docs/01 §1 願景）。
    /// </summary>
    /// <remarks>
    /// 🛑 **數值填寫紀律（CLAUDE.md B11／docs/04 §10）**：三個強度值是「呈現層可調參數」，
    /// 公式為 <c>intensity_i = speed_i / speed_max</c>——<b>speed_i 一律從對應 clip 的 MotionBakeData
    /// 代表速度讀取，由設計師手動填入</b>，**不在程式中硬編實測值**（避免動畫資產一換、程式常數即過期）。
    /// 預設值一律為 1＝「無 gait 差異」，行為等同「未指派本資產」（＝現況），
    /// 逼設計師顯式從 Bake Data 校準，而不是繼承一組看不出來源的魔術數字。
    /// </remarks>
    [CreateAssetMenu(fileName = "GaitProfile", menuName = "Project/Core/Movement/GaitProfile")]
    public class GaitProfileSO : ScriptableObject
    {
        [Header("Gait Intensity（[0-1]，公式：intensity = speed_i / speed_max，speed 取自 MotionBakeData）")]
        [Tooltip("無修飾鍵時的移動強度。典型 ARPG 方案＝『預設 Run』時填入 run_speed / sprint_speed。")]
        [Range(0f, 1f)]
        [SerializeField] private float defaultIntensity = 1f;

        [Tooltip("按住 Sprint 修飾鍵時的移動強度。典型方案＝滿速 1.0（sprint 即 speed_max）。")]
        [Range(0f, 1f)]
        [SerializeField] private float sprintIntensity = 1f;

        [Tooltip("按住 Walk 修飾鍵時的移動強度。典型方案填入 walk_speed / sprint_speed。")]
        [Range(0f, 1f)]
        [SerializeField] private float walkIntensity = 1f;

        [Header("Analog")]
        [Tooltip("勾選＝類比搖桿的推桿量會再乘進強度（鍵盤 0/1 輸入下無差異）；" +
                 "取消＝只要有輸入就直接吃 gait 檔位的固定強度（數位手感）。")]
        [SerializeField] private bool respectAnalogMagnitude = true;

        [Header("Modifier Mode")]
        [Tooltip("Walk 修飾鍵的觸發方式。取消＝按住才走路（hold，多數 Souls-like）；" +
                 "勾選＝按一下切換 walk 型態、再按一下切回（toggle，終末地／多數開放世界 ARPG）。")]
        [SerializeField] private bool walkIsToggle = false;

        /// <summary>
        /// Walk 修飾鍵是否採「切換型態」語意（true）而非「按住生效」（false）。
        ///
        /// 這是 **per-game 控制方案差異**，所以住在資產而不是 policy 程式碼裡——
        /// 否則「換玩法＝換一顆資產」的承諾就破了（docs/01 §1 願景）。
        /// producer 依此決定要讀 <c>InputData.WalkButtonDown</c>（邊沿）還是 <c>WalkButtonHeld</c>（持續）。
        /// </summary>
        /// <remarks>
        /// 為何是 bool 而非 per-modifier 的 enum：目前只有 Walk 有實體按鍵（Sprint 規劃為 buff 驅動、未綁鍵）。
        /// 若日後 Sprint 也需要 hold/toggle 選擇，再升級為 enum 或 per-modifier 結構——**現在不預造**。
        /// </remarks>
        public bool WalkIsToggle => walkIsToggle;

        /// <summary>
        /// 純函數：把「原始推桿量 ＋ 修飾鍵狀態」解讀為模型無關的移動強度 [0-1]。
        /// 無 Unity 相依、無配置——可直接單元測試（EditMode 以 <c>ScriptableObject.CreateInstance</c> 建立）。
        /// </summary>
        /// <param name="inputMagnitude">原始移動輸入的長度（會先 Clamp01）。0＝無移動意圖。</param>
        /// <param name="sprintHeld">Sprint 修飾鍵是否按住（中性 action 訊號，非領域標籤）。</param>
        /// <param name="walkActive">
        /// Walk 型態**是否啟用**——注意不是「按鍵是否按住」：hold 方案下兩者等價，
        /// toggle 方案下這是被閂住的持久狀態（由 producer 依 <see cref="WalkIsToggle"/> 解析後傳入）。
        /// </param>
        /// <remarks>
        /// **Walk 型態與 Sprint 同時成立時，Walk 優先**——刻意採「較保守者勝」的固定規則，
        /// 不做成第四個可調欄位（避免為極端邊角新增配置面；真有 per-game 需求時再議）。
        /// </remarks>
        public float ResolveIntensity(float inputMagnitude, bool sprintHeld, bool walkActive)
        {
            float magnitude = Mathf.Clamp01(inputMagnitude);
            if (magnitude <= 0f) return 0f;

            float gait = walkActive ? walkIntensity
                       : sprintHeld ? sprintIntensity
                       : defaultIntensity;

            gait = Mathf.Clamp01(gait);
            return respectAnalogMagnitude ? gait * magnitude : gait;
        }
    }
}
