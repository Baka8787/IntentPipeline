using UnityEngine;
using UnityEngine.InputSystem;
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
        public InputAction FireAction;

        // 🆕（ADR-005）第二／第三個 action 按鍵（預設綁 Q／E，實際綁定在 .inputactions 資產）。
        // 未綁定＝恆 false＝該技能不可觸發，不影響其他功能（同 SprintAction/WalkAction 先例）。
        public InputAction SecondaryAction;
        public InputAction TertiaryAction;

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
            FireAction?.Enable();
            SecondaryAction?.Enable();
            TertiaryAction?.Enable();
            SprintAction?.Enable();
            WalkAction?.Enable();
        }

        private void OnDisable()
        {
            MoveAction?.Disable();
            LookAction?.Disable();
            JumpAction?.Disable();
            RollAction?.Disable();
            FireAction?.Disable();
            SecondaryAction?.Disable();
            TertiaryAction?.Disable();
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
            data.FireButtonDown = FireAction != null && FireAction.WasPressedThisFrame();
            data.SecondaryActionButtonDown = SecondaryAction != null && SecondaryAction.WasPressedThisFrame();
            data.TertiaryActionButtonDown = TertiaryAction != null && TertiaryAction.WasPressedThisFrame();

            // 持續型（IsPressed）：供「按住才生效」的控制方案使用。
            data.SprintButtonHeld = SprintAction != null && SprintAction.IsPressed();
            data.WalkButtonHeld = WalkAction != null && WalkAction.IsPressed();

            // 🆕 邊沿（WasPressedThisFrame）：供「按一下切換型態」的控制方案使用。
            // 兩種訊號同時提供，由 GaitProfileSO 決定採用哪一種——input 層不做這個選擇。
            data.WalkButtonDown = WalkAction != null && WalkAction.WasPressedThisFrame();
        }
    }
}