using UnityEngine;

namespace Project.Core.Blackboard
{
    /// <summary>
    /// 🆕（ADR-003 D1，Migration Stage 1）Movement 領域的黑板意圖區——
    /// 「domain-partitioned intents」原則的第一個 region（未來 CombatIntent／InteractionIntent 為兄弟 region，
    /// 加 region、不脹本結構；控制範式不同的 model（Vehicle throttle/brake/steer）另開兄弟 schema，見 ADR-003 §13.1）。
    ///
    /// **模型無關（model-agnostic）**：只描述「往哪個方向、以多強的強度移動」，
    /// **不含 gait 語意**——Walk/Run/Sprint 是 Locomotion model 對 [0-1] 的命名門檻（docs/04 §10），不屬本契約。
    /// 因此 Swim（StrokeRate）／Strafe（2D）等未來 model 可共用同一 seam。
    ///
    /// 生命週期：**連續型 domain intent**——每幀由當下唯一 active 的 <c>IMovementIntentSource</c> 重算覆寫；
    /// **不**參與 <see cref="PlayerRuntimeData.ResetTransientState"/> 的單幀復位（那是 <see cref="IntentData"/>
    /// 的 trigger 邊沿語意）。兩類 intent 的區別見 docs/04 §14.2。
    ///
    /// 校準責任：本結構是模型無關的 [0-1]，**各 model 自行校準 [0-1] → 實際值**
    /// （Locomotion 的 mixer 門檻＝speed_i / speed_max）。校準錯＝滑步，屬各 model 責任（ADR-003 §9-L4）。
    /// </summary>
    public struct MovementIntentData
    {
        /// <summary>
        /// 期望移動強度，值域 [0-1]，模型無關。0＝無移動意圖。
        /// 寫入者：該 domain 當下唯一 active 的 <c>IMovementIntentSource</c>（Stage 1＝<c>PlayerLocomotionPolicy</c>）。
        /// </summary>
        public float DesiredSpeedNormalized;

        /// <summary>
        /// 期望移動方向（2D 平面輸入座標系，與 <see cref="InputData.MoveInput"/> 同語意；
        /// 轉成世界方向是 MotionDriver 依相機基底的職責，本層不做）。零向量＝無方向意圖。
        /// </summary>
        public Vector2 DesiredDirection;

        /// <summary>
        /// 🆕 Walk 型態是否啟用（**mode state，非單幀事件**）。
        ///
        /// 語意固定為「本帧 walk 型態開著沒有」，**與它是怎麼被觸發的無關**——
        /// 按住式（hold）方案下等同修飾鍵是否按住；切換式（toggle）方案下是被閂住的持久狀態。
        /// 由 <c>GaitProfileSO</c> 決定採哪一種（換玩法＝換資產），producer 每帧解析後寫入本欄位。
        ///
        /// ⚠️ **為什麼 toggle 狀態必須放黑板、不能是 producer 的私有 bool**（ADR-003 D5 明文
        /// 「mode/toggle state 進黑板」＋§9-L5）：netcode 的 rewind／replay 前提是「所有狀態皆可 snapshot」，
        /// 藏在元件私有欄位裡的 mode 會讓回捲後的角色型態與紀錄不一致。同理它也**不**參與
        /// <see cref="PlayerRuntimeData.ResetTransientState"/>——那是 trigger 邊沿的生命週期，
        /// 持久型態被每帧清零就永遠關不起來。
        /// </summary>
        public bool WalkModeActive;
    }
}
