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

            // 🗑️（輪 4.2）原本這裡做開場的「隱藏並鎖定滑鼠指標」，已移交
            //     Project.App.CursorModeController——它是 Cursor API 的唯一擁有者，
            //     沒有任何來源要求自由游標時（＝開場）它自然會鎖上。
            // ⚠️ 留在這裡就等於有第二個寫入者，「唯一擁有者」只會是文件上的說法。
            //     代價：場景若沒掛 CursorModeController，開場游標不會鎖、連帶本相機也不轉
            //     （下方閘門以 Cursor.lockState 為判準）。**這是刻意讓它大聲壞掉**，一眼可見，
            //     不是靜默的行為漂移。

            Vector3 angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // 💡 升級防線：檢查當前是否有滑鼠裝置連結
            // 🆕（輪 4）游標解鎖期間（UI 模式）不消費滑鼠位移——否則玩家移動滑鼠去點 UI 時鏡頭會跟著轉，
            //    「顯示滑鼠」就只做了一半。
            // 為什麼用 Cursor.lockState 當判準而不是讀黑板 Arbitration：本元件不是 IPresentationController、
            //    也不持有 PlayerRuntimeData，而「游標有沒有被鎖住」本身就是「該不該吃滑鼠位移」的正解——
            //    零新增依賴、零新增欄位。時序也對：游標由 CursorModeController 在 **Update** 套用，
            //    而所有 Update 都跑在所有 LateUpdate 之前，所以本幀讀到的必定是已套用的值，不會有一幀誤轉。
            // ⚠️ 這是**現階段**的取捨，不是「Cursor.lockState 永遠是全域權威」的宣告。
            //    🔄（輪 4.2 複驗）現在已有**兩個**滑鼠模式（UI 模式、暫停），兩者也都會放開游標——
            //    但它們對相機的期望**一致**（都要停轉），所以這個判準依然是對的答案。
            //    **真正的失效條件因此收窄為**：出現一個「游標自由**但相機仍該轉**」（或反之）的模式，
            //    屆時再裁決是否需要一份更上游的 camera-input contract（見 dev-spec §7.3）。
            if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
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