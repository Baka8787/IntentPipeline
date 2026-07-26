using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（ADR-003 Migration Stage 2）**Movement Model 的通用抽象**（D3／D4）：
    /// 封裝「本角色此刻怎麼動」的全部 dynamics——把模型無關的 <c>MovementIntent</c>
    /// 轉成該模型的實際運動輸出，並**驅動自己的動畫參數**（Locomotion→<c>MoveSpeed</c>、
    /// 未來 Swim→<c>StrokeRate</c>），共用同一支通用 <see cref="AnimationFacadeBase"/>。
    ///
    /// **Runner 只認識這支介面**：管線不知道「locomotion」「gait」「平滑」是什麼，
    /// 換 model ＝ 換一顆元件（DIP＋OCP），Runner 零改。
    /// </summary>
    /// <remarks>
    /// ⚠️ **為何是兩個進入點而不是一個**（Stage 2 落地時實測出的時序陷阱，2026-07-25 裁決）：
    /// 1. <see cref="Tick"/>＝**管線順序 3（Update）**，**每幀無條件**推進，與當前狀態無關。
    ///    - 若把 dynamics 併進 <see cref="UpdateMotion"/>（只有 ambient 狀態會呼叫），
    ///      Jump／Roll 期間平滑會**凍結**——而 <c>JumpState.OnUpdateMotion</c> 的空中控制
    ///      正是吃這組衍生值，落地瞬間會拿起跳時的殘值續走＝滑步。行為等價因此被破壞。
    ///    - 動畫參數也在此驅動：Animator 的評估發生在 Update 與 LateUpdate 之間，
    ///      若 <c>SetFloat</c> 落到 LateUpdate，參數會比位移**晚一幀**（動畫／位移分岔）。
    /// 2. <see cref="UpdateMotion"/>＝**管線順序 6（LateUpdate）**，由 ambient 狀態
    ///    （Idle／Move）的 <c>OnUpdateMotion</c> delegate 而來（D3）；intrinsic-motion 狀態
    ///    （Jump／Roll）維持既有 override 自帶位移，不走這裡。
    ///
    /// **實例唯一性是結構性保證**：跨幀平滑狀態（SmoothDamp 緩存）必須全域唯一，否則
    /// Idle↔Move 切換時平滑值會被重置、放開輸入的收步會斷掉。因此 model 由
    /// <c>CharacterPipelineRunner</c> 解析成**單一實例**後注入 <c>FullBodyStateMachine</c>，
    /// 再由狀態機發給所有 state——沒有任何 state 自己 new 一份的路徑。
    /// </remarks>
    public interface IMovementModel
    {
        /// <summary>
        /// 本模型此刻是否正在產生運動（FSM 的 ambient 門檻信號）。
        ///
        /// 🆕 Stage 2 起取代「FSM 直接讀衍生的 <c>MoveSpeed &lt; 0.1</c>」（dev-spec §7.3 第三列結案）：
        /// 門檻是 **model 內部的 locomotion 概念**，狀態機不該認識它。讀原始 intent 亦不可——
        /// 放開輸入的瞬間就會切 Idle，但角色仍在滑行，動畫與位移分岔。
        /// </summary>
        bool IsProducingMotion { get; }

        /// <summary>
        /// 【管線順序 3，Update】推進本模型的 dynamics：讀黑板 <c>MovementIntent</c>（唯一輸入真相，
        /// ADR-003 §13.4）→ 發布運動輸出到黑板 → 驅動自己的動畫參數。**每幀無條件呼叫**（見 remarks）。
        /// </summary>
        /// <param name="data">黑板。輸入讀 <c>MovementIntent</c>，輸出寫本模型負責的 Movement Output 欄位。</param>
        /// <param name="animationFacade">通用動畫門面（可為 null，模型須自行防禦）。</param>
        /// <param name="deltaTime">本幀時間步長，由呼叫端傳入以維持可測性。</param>
        void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime);

        /// <summary>
        /// 【管線順序 6，LateUpdate】結算本幀物理位移。由 ambient 狀態的
        /// <c>BaseState.OnUpdateMotion</c> delegate 而來（ADR-003 D3）。
        /// </summary>
        void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data);
    }
}
