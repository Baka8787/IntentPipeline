using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Project.Core.Blackboard;

namespace Project.Core.Pipeline
{
    public class PlayerInputSource : MonoBehaviour, IInputSource
    {
        [Header("Unity New Input System Actions")]
        public InputAction MoveAction;
        public InputAction LookAction;
        public InputAction JumpAction;
        public InputAction RollAction;

        // ─────────────────────────────────────────────────────────────────
        // Action 鍵位表：一格對應一個 ActionSlot（2026-09-02 命名統一）
        //
        // 原本這三顆各說各話——`FireAction`（ADR-004 Throw 時期的語意名）、`SecondaryAction`、
        // `TertiaryAction`（拉丁序號）——三種詞彙描述同一排按鍵，且都對不上 `ActionSlot.Slot1/2/3`。
        // 現在改為與 slot 同名，「哪顆鍵餵哪一格」在欄位名上就看得出來。
        //
        // ⚖️ **關於「raw input 不該知道 slot」**：那個說法在這裡已經是修辭而非事實——
        // 一旦存在三顆編號的 action 按鍵、且與三個 slot 一一對應，這一層就已經知道 slot 了。
        // 真正需要保持乾淨的界線在別處且仍然成立：`ActionState` 從不認識按鍵、
        // AI 走 `ActionRequestTarget` 而不經這些欄位。**`PlayerInputSource` 就是玩家的鍵位表，
        // 而鍵位表綁的正是「格子」。** 按鍵→slot 的實際映射仍在管線順序 2（`ProcessIntents`）。
        //
        // ⚠️ `[FormerlySerializedAs]` 是**必要的**：這些欄位以名字序列化在 prefab 內，
        // 少了它，改名等於清空既有 binding（左鍵／Q／E 全部歸零）。⛔ 不要因為「看起來是雜訊」刪掉。
        // 未綁定＝恆 false＝該格不可觸發，不影響其他功能（同 SprintAction/WalkAction 先例）。
        // ─────────────────────────────────────────────────────────────────

        [FormerlySerializedAs("FireAction")]
        public InputAction Slot1Action;

        [FormerlySerializedAs("SecondaryAction")]
        public InputAction Slot2Action;

        [FormerlySerializedAs("TertiaryAction")]
        public InputAction Slot3Action;

        // 🆕（ADR-003 Stage 1）持續型中性 action：本層不解讀語意，只回報按住與否。
        // 未綁定＝恆 false＝無修飾鍵（行為等同 Migration 前），因此不綁也能正常遊玩。
        public InputAction SprintAction;
        public InputAction WalkAction;

        private void OnEnable()
        {
            MoveAction?.Enable();
            LookAction?.Enable();
            JumpAction?.Enable();
            RollAction?.Enable();
            Slot1Action?.Enable();
            Slot2Action?.Enable();
            Slot3Action?.Enable();
            SprintAction?.Enable();
            WalkAction?.Enable();
        }

        private void OnDisable()
        {
            MoveAction?.Disable();
            LookAction?.Disable();
            JumpAction?.Disable();
            RollAction?.Disable();
            Slot1Action?.Disable();
            Slot2Action?.Disable();
            Slot3Action?.Disable();
            SprintAction?.Disable();
            WalkAction?.Disable();
        }

        /// <summary>
        /// v0.3 變更：不再 new 或回傳物件，改為直接將採樣數值寫入傳進來的 Stack 記憶體
        /// </summary>
        public void FetchRawInput(ref InputData data)
        {
            data.MoveInput = MoveAction != null ? MoveAction.ReadValue<Vector2>() : Vector2.zero;
            data.LookInput = LookAction != null ? LookAction.ReadValue<Vector2>() : Vector2.zero;

            data.JumpButtonDown = JumpAction != null && JumpAction.WasPressedThisFrame();
            data.RollButtonDown = RollAction != null && RollAction.WasPressedThisFrame();
            data.Slot1ButtonDown = Slot1Action != null && Slot1Action.WasPressedThisFrame();
            data.Slot2ButtonDown = Slot2Action != null && Slot2Action.WasPressedThisFrame();
            data.Slot3ButtonDown = Slot3Action != null && Slot3Action.WasPressedThisFrame();

            // 持續型（IsPressed）：供「按住才生效」的控制方案使用。
            data.SprintButtonHeld = SprintAction != null && SprintAction.IsPressed();
            data.WalkButtonHeld = WalkAction != null && WalkAction.IsPressed();

            // 🆕 邊沿（WasPressedThisFrame）：供「按一下切換型態」的控制方案使用。
            // 兩種訊號同時提供，由 GaitProfileSO 決定採用哪一種——input 層不做這個選擇。
            data.WalkButtonDown = WalkAction != null && WalkAction.WasPressedThisFrame();
        }
    }
}