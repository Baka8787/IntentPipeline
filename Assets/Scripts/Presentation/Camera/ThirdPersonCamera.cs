using UnityEngine;
using UnityEngine.InputSystem; // 💡 關鍵：引入新版輸入系統命名空間

namespace Project.Presentation.CameraControl
{
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Follow Setup")]
        [SerializeField] private Transform target;         // 拖入你的角色物件
        [SerializeField] private Vector3 offset = new Vector3(0, 2f, -3.5f); // 鏡頭相對角色的基礎偏移
        [SerializeField] private float followSpeed = 15f;

        [Header("Rotation Setup")]
        [SerializeField] private float mouseSensitivity = 0.1f; // 💡 新版滑鼠數值基數較大，靈敏度建議調小（如 0.05 ~ 0.15）
        [SerializeField] private float minPitch = -20f;
        [SerializeField] private float maxPitch = 60f;

        private float _yaw;
        private float _pitch;

        private void Start()
        {
            // Fail-Fast（比照 FootIKController／AnimancerFacade 既有防線）：target 未指派時鏡頭會靜默不跟隨，
            // 一次性報錯把「為什麼不動」直接指出來，取代先前的靜默 return（LateUpdate 仍安全跳出，不噴例外）。
            if (target == null)
            {
                Debug.LogError($"[{name}] ThirdPersonCamera.target 未指派——鏡頭不會跟隨角色。" +
                    "請在 Inspector 將角色 Root（掛 CharacterPipelineRunner 的物件，非 Model 子物件）拖入 Target 欄位。", this);
            }

            // 隱藏並鎖定滑鼠指標
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Vector3 angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // 💡 升級防線：檢查當前是否有滑鼠裝置連結
            if (Mouse.current != null)
            {
                // 讀取新版輸入系統的滑鼠每影格偏移量 (Delta X / Y)
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();

                _yaw += mouseDelta.x * mouseSensitivity;
                _pitch -= mouseDelta.y * mouseSensitivity; // 減法符合標準滑鼠視角邏輯
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            // 2. 根據旋轉角度計算出鏡頭在角色後方的 3D 世界座標
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0);
            Vector3 targetPosition = target.position + (rotation * offset);

            // 3. 移動鏡頭並強迫鏡頭看著角色中心點
            transform.position = Vector3.Lerp(transform.position, targetPosition, followSpeed * Time.deltaTime);
            transform.LookAt(target.position + Vector3.up * 1.5f); // 瞄準角色胸口高度
        }
    }
}