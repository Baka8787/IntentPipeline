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

        // === Movement 意圖區（🆕 ADR-003 D1，Migration Stage 1）===
        // domain-partitioned intents 的第一個 region：連續型意圖，**每幀由 active producer 重算覆寫**，
        // 不參與 ResetTransientState() 的單幀復位（那是上方 IntentData 的 trigger 邊沿語意）。
        // 註：同 Intent，維持公開欄位而非 Property，避免 struct 值複製導致無法直接修改內部成員。
        // 寫入者（唯一）：當下 active 的 IMovementIntentSource（Stage 1＝PlayerLocomotionPolicy）。
        // 讀取者：Locomotion dynamics（Stage 1＝Runner 持有的 LocomotionSpeedSmoother，Stage 2 遷入 model）。
        public MovementIntentData MovementIntent;

        // === Combat Context 區（ADR-007 S3a）===
        // 連續型 mode state：每幀由 active combat context producer 整體覆寫，
        // 不參與 ResetTransientState() 的單幀事件復位。
        public CombatContextData CombatContext;

        // === 表現層事件區（🆕 M3.x-B）===
        // 每帧由 PresentationPipeline 於順序 6.5 的**末尾**整體覆寫（發布），各 Controller 於**下一帧**讀取。
        // 廣播快照語意：consumer 只讀不清除，彼此不會吃掉事件；整體覆寫即是復位機制，
        // 故 ResetTransientState()（順序 7）**刻意不認識本欄位**，不需要任何例外。
        // 註：同 Intent，維持公開欄位而非 Property，避免 struct 值複製導致無法直接修改內部旗標。
        public PresentationEventData PresentationEvents;

        // === 生存區（🆕 ADR-009 D1）===
        // 持續型角色真相：每幀由 CharacterHealth 於順序 0.5 整體發布，**不**參與
        // ResetTransientState()（它不是 trigger 邊沿事件）。
        // 寫入者（唯一）：CharacterHealth.cs。
        // 讀取者：DeathState.CanEnter／HurtState.CanEnter／PlayerCombatContextSource（受擊即進戰鬥）／
        //         DeathArbiterSource（死後 BlockInput ⇒ 停止產生 intent）／
        //         CharacterFacingSource（死後不送 facing request——屍體不該繼續轉向攻擊者）。
        // ⚠️ 輸入層**不**讀本區（A27）：「死後停止產生 intent」是經由仲裁層的 BlockInput 達成的，
        //    不是讓 PlayerInputSource／AIInputSource 自己回讀 gameplay state。
        // ⚠️ 缺少 CharacterHealth 的角色（例如純場景物件）此欄位恆為 default ⇒ IsDead false、
        //    MaxHealth 0 ⇒ 行為與導入本欄位之前完全相同。
        public SurvivabilityData Survivability;

        // === 仲裁區 ===
        // 每幀由仲裁管線統一覆寫，表現層下游只讀不寫
        // 註：同 Intent，維持公開欄位而非 Property，避免 struct 值複製導致無法直接修改內部旗標
        public ArbiterData Arbitration;

        // === Movement Output 區（持續存在，每帧更新，公開屬性採用 PascalCase）===
        // 🆕（ADR-003 Stage 2，2026-07-25）以下三欄的語意已重定義：
        // **不再是管線維護的 locomotion state，而是「當下 active Movement Model 發布的運動輸出」**。
        // 寫入者唯一且恆為一個 model（順序 3 Tick）；換 model 就換這組值的產生者，欄位形狀不變。
        /// <summary>
        /// 本帧移動強度 [0-1]（Locomotion 時＝B9 平滑後的值）。
        /// ⚠️（ADR-003 §13.4）**衍生值，不是獨立真相**——恆由 <see cref="MovementIntent"/> 經
        /// active model 的 dynamics 導出，**禁止任何路徑繞過 MovementIntent 直寫**（防「兩個真相來源」病）。
        /// </summary>
        public float MoveSpeed { get; set; }

        /// <summary>
        /// 有效移動方向（世界座標 XZ 平面，y 恆為 0）。同 <see cref="MoveSpeed"/>，為
        /// <see cref="MovementIntent"/> 的下游衍生值（含減速滑行期保留最後方向的 dynamics），
        /// 非獨立真相。
        /// </summary>
        public Vector3 MoveDirection { get; set; }

        public Transform CameraTransform { get; set; }

        /// <summary>
        /// 角色是否觸地。依專案規範採用公開欄位（field，非 property）。
        /// 寫入者：MotionDriver。由 SyncGroundedState() 統一寫入；重力與 committed 垂直曲線
        ///         兩條位移路徑共用同一個發布點。
        /// 讀取者：狀態機（JumpState / RollState 的起跳資格閘門與 JumpState 的真實落地判定），
        ///         取代先前用固定計時器模擬落地的做法，並杜絕無限空中跳。
        /// ⚠️ 時序注意：Unity 的 CharacterController.isGrounded 只在呼叫過 Move() 之後才會更新，
        /// 而 Move() 發生在本影格 LateUpdate，狀態機 Tick 發生在 Update，順序上更早。
        /// 因此本影格 Update 讀到的 IsGrounded，實際上是「上一影格」LateUpdate 結算後的結果，
        /// 這是 CharacterController 架構下的正常延遲，非 Bug，使用端只需知悉即可。
        /// </summary>
        public bool IsGrounded;

        /// <summary>
        /// 角色在上一次 <c>CharacterController.Move()</c> 結算後的實際垂直速度。
        /// 寫入者：MotionDriver（於 <c>SyncGroundedState</c> 內）——唯一寫入者。
        /// 與同一段程式寫入的 <see cref="IsGrounded"/>／<see cref="JustLanded"/>／
        /// <see cref="JustLeftGround"/> 是同一瞬間的一致快照；因此 <see cref="IsGrounded"/>
        /// 由 false 轉 true 的那一幀，本值就是撞地當下的 impact velocity，尚未被
        /// <c>reboundForce</c> 貼地力覆寫。
        /// 讀取者：JumpState（落地 Normal／Hard 分類）、表現層 Controller（唯讀）。
        /// 兌現紀錄：v0.10 定案 → ADR-002 §6-1 延後至「出現第二個垂直速度消費者」→
        /// walk-off falling 的落地分類即為該第二消費者，閘門達成。
        /// ⚠️ 時序注意：與 <see cref="IsGrounded"/> 相同，Update 讀到的是上一幀 LateUpdate
        /// 結算後的結果，這是 CharacterController 架構下的正常延遲。
        /// </summary>
        public float VerticalVelocity { get; internal set; }

        // === 單幀事件區（🆕 M2：當幀生、當幀死，由順序 7 統一復位）===
        /// <summary>
        /// 單幀事件：本影格剛落地（上一影格空中 → 本影格觸地）。
        /// 寫入者（唯一觸發源）：MotionDriver.SyncGroundedState()——前後幀觸地狀態的邊沿偵測。
        /// 讀取者：PresentationPipeline 的各 Controller（M2 首個消費者：AudioController 落地音）。
        /// 生命週期：順序 6（MotionDriver 生）→ 6.5（Presentation 消費）→ 7（ResetTransientState 死）。
        /// 統一復位屬生命週期管理，不視為第二寫入者。
        /// </summary>
        public bool JustLanded;

        /// <summary>
        /// 單幀事件：本影格剛離地（上一影格觸地 → 本影格空中）。寫入者與生命週期同 JustLanded。
        /// M2 尚無消費者，與 JustLanded 成對維護，保持邊沿語義完整。
        /// </summary>
        public bool JustLeftGround;

        /// <summary>
        /// 🆕（M2）統一復位所有單幀瞬態：意圖旗標 + 落地/離地邊沿事件。
        /// 呼叫者：CharacterPipelineRunner.LateUpdate 順序 7（管線最末端），
        /// 確保所有單幀事件遵守「當幀生、當幀死」的一致生命週期。
        /// ⚠️（ADR-003）<see cref="MovementIntent"/> **刻意不在此復位**——它是連續型 domain intent
        /// （每幀由 producer 整體覆寫），不是 trigger 邊沿事件；若在此清零，會在 producer 缺席的幀
        /// 產生「意圖瞬間歸零」的假訊號。兩類 intent 的語意區別見 docs/04 §14.2。
        /// </summary>
        public void ResetTransientState()
        {
            Intent.Reset();
            JustLanded = false;
            JustLeftGround = false;
        }

        // === 引用區 ===
        /// <summary>
        /// 當前裝備的武器。規格書規範：唯讀引用，禁止外部修改內容。
        /// 限制 setter 為 internal，僅允許 EquipmentDriver 進行修改。
        /// </summary>
        public ItemInstance CurrentWeapon { get; internal set; }


    }
}
