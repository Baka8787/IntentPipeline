using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（ADR-003 Migration Stage 2）**Locomotion Model**——雙足地面移動模型，
    /// <see cref="IMovementModel"/> 的第一個實作。收攏了 Stage 1 遺留在通用 Runner 的三件事
    /// （ADR-003 §9-L1 的殘餘耦合，本輪結案）：
    /// 1. **B9 平滑**（<see cref="LocomotionSpeedSmoother"/>，整顆從 Runner 換持有者搬來，計算邏輯零改寫）
    /// 2. **運動輸出導出**（速度／方向／上半身權重）
    /// 3. **自驅動畫參數**（<c>SetFloat(MoveSpeed)</c>）——D4「每個 model 驅動自己的參數」
    ///
    /// 遷移後 <c>CharacterPipelineRunner</c> 不再認識任何 locomotion 概念（無 MoveSpeed、無平滑時間、無 gait）。
    /// </summary>
    /// <remarks>
    /// **為何是 MonoBehaviour**：平滑時間是 per-game 手感參數，需要 Inspector 可調；且比照
    /// <see cref="PlayerLocomotionPolicy"/>（同為掛在角色 Root 的可替換零件）的既有 pattern，
    /// 讓「換 model」＝「換一顆元件」。model 不持有 Facade／MotionDriver 的跨幀引用——
    /// 兩者皆由呼叫端逐幀傳入，使本模型的**全部跨幀狀態**僅為下方 <c>_smoother</c>
    /// （ADR-003 §9-L5 snapshot-able 前提）。
    ///
    /// **黑板 Movement Output 的語意**（2026-07-25 裁決）：<c>MoveSpeed</c>／<c>MoveDirection</c>／
    /// <c>UpperBodyWeight</c> 自本輪起**不再是 Runner 維護的 locomotion state**，而是
    /// **當下 active Movement Model 發布的 Movement Output**——消費端（MotionDriver、Jump 空中控制、
    /// Editor 監視器）讀到的是「模型算出來的運動」，寫入者唯一且為本檔。
    /// D4 的最終形態（欄位完全內化、不經黑板）目標不變，但需連動 MotionDriver API 與 Jump 空中控制，
    /// 留待後續階段；本輪刻意不一次做完（migration intermediate state，見 dev-spec §7.3）。
    /// </remarks>
    public class LocomotionModel : MonoBehaviour, IMovementModel
    {
        /// <summary>
        /// ambient 門檻：平滑速度達此值即視為「正在移動」（Move），低於則為 Idle。
        /// 沿用 Migration 前 <c>IdleState/MoveState.CanEnter</c> 內硬編的 0.1f，數值與行為完全等價；
        /// 差別只在**歸屬**——這是 locomotion 的內部門檻，不該長在狀態機裡。
        /// </summary>
        public const float MoveThreshold = 0.1f;

        // 🆕（B9 Game Feel，自 CharacterPipelineRunner 原樣搬入）鍵盤 0/1 輸入直送會讓 1D Mixer
        // 一幀從 Idle 跳 Sprint、中間 Walk/Run tier 踩不到。以 SmoothDamp 讓速度隨時間爬升/回落，
        // 平順經過各速度 tier；動畫混合與實際位移共用同一平滑值，加減速全程不滑步。
        [Header("Move Speed Smoothing (B9)")]
        [Tooltip("MoveSpeed 0→滿 的加速平滑時間（秒，SmoothDamp）。越小越 snappy。")]
        [SerializeField] private float moveSpeedAccelTime = 0.12f;
        [Tooltip("MoveSpeed 滿→0 的減速平滑時間（秒）。通常略大於加速＝放開後自然滑行收步。")]
        [SerializeField] private float moveSpeedDecelTime = 0.18f;

        // 本模型唯一的跨幀狀態。值型別零配置內嵌；全域唯一性由「Runner 解析單一實例 → 注入狀態機 →
        // 狀態機發給所有 state」的注入鏈保證（見 IMovementModel remarks）。
        private LocomotionSpeedSmoother _smoother;

        /// <inheritdoc />
        public bool IsProducingMotion => _smoother.Speed >= MoveThreshold;

        /// <inheritdoc />
        public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime)
        {
            // 唯一輸入＝黑板意圖（ADR-003 §13.4：輸出恆可由 intent ＋ 本 dynamics 重新導出，
            // 禁止任何路徑繞過 intent 直寫運動輸出）。
            _smoother.Tick(in data.MovementIntent, moveSpeedAccelTime, moveSpeedDecelTime, deltaTime);

            data.MoveSpeed = _smoother.Speed;
            data.MoveDirection = _smoother.Direction;
            data.UpperBodyWeight = _smoother.Speed > LocomotionSpeedSmoother.Epsilon ? 0.5f : 0.0f;

            // 🆕（ADR-003 D4）**model 自驅動畫參數**：由 Locomotion Transition 資產內的 ParameterName
            // 綁定驅動 1D Mixer 混合，本層不認識任何 Mixer（tier 門檻是資料，住在 Locomotion.asset）。
            // ⚠️ 驅動點刻意留在 Update（順序 3）而非 LateUpdate：Animator 評估卡在兩者之間，
            //    移到 LateUpdate 會讓動畫參數比位移晚一幀。
            if (animationFacade != null)
            {
                animationFacade.SetFloat(AnimationFacadeBase.ParamMoveSpeed, _smoother.Speed);
            }
        }

        /// <inheritdoc />
        public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data)
        {
            // 目前 Locomotion 的位移結算＝純 Procedural 基礎移動（MotionDriver 讀黑板 Movement Output）。
            // 與 Migration 前 BaseState 的預設行為逐字等價，差別只在「由誰決定」——現在是 model。
            motionDriver.ExecuteBaseMovement(data);
        }
    }
}
