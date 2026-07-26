using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（ADR-003 D2，Migration Stage 1）玩家的 Movement 意圖 producer：
    /// 讀「中性輸入訊號 ＋ <see cref="GaitProfileSO"/>」→ 寫黑板 <c>MovementIntent</c> region。
    ///
    /// 這是 ADR-003 為「移動控制方案（per-game Movement Policy）」找到的家：
    /// 「預設 Run／Shift=Sprint／Ctrl=Walk」這類規則**既不屬 State（會把控制方案焊進 FSM 拓撲）、
    /// 也不屬 Input（會把 gameplay 語意烤進 raw input）、更不屬通用 Runner（會讓它認識 locomotion 概念）**。
    ///
    /// 本類別是**唯一**寫入 <c>MovementIntent</c> 的執行期元件（single-writer）。
    /// 換 AI／Replay／Network 驅動＝換掛一個別的 <see cref="IMovementIntentSource"/> 元件，Runner 零改。
    /// </summary>
    /// <remarks>
    /// **Context-free 紀律（ADR-003 §13.1-R3）**：本類別**不得**回讀 gameplay state／當前 model，
    /// 也不得判斷「這顆 Shift 現在是不是給移動用的」（那是上游 Input 層的 action map 職責，§13.3）。
    /// 一旦破例，producer → state 的同幀回圈與情境耦合會同時回來。
    /// </remarks>
    public class PlayerLocomotionPolicy : MonoBehaviour, IMovementIntentSource
    {
        [Header("Movement Policy")]
        [Tooltip("gait 配置資產（修飾鍵→強度映射）。**留空＝直接採用原始推桿量**，" +
                 "行為與 Migration 前的 Runner.ProcessParameters 完全等價（Stage 1 行為等價保證）。")]
        [SerializeField] private GaitProfileSO gaitProfile;

        /// <summary>
        /// 【管線順序 2.5】產生本幀 Movement 意圖。熱路徑零配置（純數值運算，無 new／無 LINQ／無字串）。
        /// </summary>
        public void ProduceIntent(ref InputData input, PlayerRuntimeData data)
        {
            if (data == null) return;

            float magnitude = Mathf.Clamp01(input.MoveInput.magnitude);

            // 🆕 Walk 型態解析（hold vs toggle 由資產決定，不寫死在 policy）：
            //   hold  ：型態＝修飾鍵當下是否按住，每帧覆寫。
            //   toggle：讀黑板現值 → 按下的**那一帧**翻轉 → 寫回黑板。
            // ⚠️ toggle 的持久狀態刻意「讀黑板改黑板」，policy **不持有任何私有欄位**——
            //    D5 明文 mode/toggle state 進黑板，§9-L5 的 snapshot-able 前提也要求無隱藏態。
            //    未指派 profile 時退化為 hold（＝Migration 前行為，無 gait 差異時本欄位也不影響強度）。
            bool walkActive = gaitProfile != null && gaitProfile.WalkIsToggle
                ? data.MovementIntent.WalkModeActive ^ input.WalkButtonDown
                : input.WalkButtonHeld;

            data.MovementIntent.WalkModeActive = walkActive;

            // 未指派 profile＝無 gait 差異，強度即原始推桿量（＝Migration 前行為）。
            data.MovementIntent.DesiredSpeedNormalized = gaitProfile != null
                ? gaitProfile.ResolveIntensity(magnitude, input.SprintButtonHeld, walkActive)
                : magnitude;

            // 方向為**純意圖**：放開即歸零。減速滑行期的「保留最後方向」屬 model dynamics，
            // 不在 producer（Stage 1 暫由 LocomotionSpeedSmoother 承擔，Stage 2 隨之遷入 Locomotion model）。
            data.MovementIntent.DesiredDirection = input.MoveInput;
        }
    }
}
