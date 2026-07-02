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

        private void OnEnable()
        {
            MoveAction?.Enable();
            LookAction?.Enable();
            JumpAction?.Enable();
            RollAction?.Enable();
            FireAction?.Enable();
        }

        private void OnDisable()
        {
            MoveAction?.Disable();
            LookAction?.Disable();
            JumpAction?.Disable();
            RollAction?.Disable();
            FireAction?.Disable();
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
        }
    }
}