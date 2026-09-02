using UnityEngine;
using UnityEngine.InputSystem; // 💡 關鍵：引入新版輸入系統命名空間

namespace Project.Presentation.CameraControl
{
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Follow Setup")]
        [SerializeField] private Transform target;         // 拖入你的角色物件
        // ⚠️ **本欄位的語意在 WP1（D1）改變了，舊值不可沿用**：
        //    舊版＝「相對腳底的位置」，之後 LookAt 會把鏡頭轉回去對準胸口，故 y 只是把相機抬高。
        //    現版＝「相對胸口 pivot 的**相機座標系**偏移」，**沒有任何回正** ⇒ y 的意思變成
        //    「把角色推到光軸下方多少」，角度＝ atan(y / |z|)。
        //    以垂直 FOV 60（半視角 30°）為例：舊值 (0, 2, -3.5) ⇒ atan(2/3.5)=29.7° ⇒ 角色貼齊畫面**下緣**，
        //    直接照搬會看起來像相機壞掉（2026-08-31 首次 Play 就是這樣壞的）。
        //    ⛔ 調整 x／y 只影響構圖，**不影響移動基底**（rotation 只由 yaw/pitch 決定，與 offset 無關）。
        //
        // 📌 現行預設＝**使用者 2026-08-31 實機調定並確認的取景**：
        //    y 為**負**是刻意的 ⇒ 局部 pivot 落在光軸**上方** atan(0.39/1.83)=12.0°，
        //    即角色位於畫面中心**上方** 40%，下方留給前方地面與空間；
        //    水平 atan(0.44/1.83)=13.5° ⇒ 中心左方 30%（右肩過肩）；
        //    相機高度 pivotHeight + y = 1.11m，距離 1.83m。
        //    ⚠️ **y 的正負號會直接翻轉構圖**（正＝角色在下半部、負＝在上半部），改動前請先實機比對。
        [SerializeField] private Vector3 offset = new Vector3(0.44f, -0.39f, -1.83f); // 鏡頭相對 pivot 的基礎偏移
        [SerializeField] private float followSpeed = 15f;

        [Header("Framing Setup")]
        // 胸口高度。pivot 同時是防穿牆 SphereCast 的起點，改動會一併改變相機被拉近時的落點。
        [SerializeField] private float pivotHeight = 1.5f;
        // 同上語意（正 y ＝角色在光軸**下方**）。瞄準 FOV 45 ⇒ 垂直半視角 22.5°、|z|=2：
        //   y=0.42 ⇒ atan(0.42/2)=11.9° ⇒ 中心**下方** 53%；x=0.5 ⇒ 14° 偏左（右肩過肩）。
        //   （失敗對照：y=1.6 ⇒ 38.7° > 22.5° ⇒ 角色整個離開畫面。）
        // ⚠️ 探索 offset.y 為**負**（角色在中心上方）、瞄準為**正**（下方）⇒ 按下瞄準時角色會有
        //    約 24° 的垂直擺動。這是目前的資料組合，**尚未經實機驗證**（aimResolver 未接線前
        //    瞄準路徑根本不會啟動）。若擺動過大，調整這裡的 y 即可，屬純構圖資料。
        [SerializeField] private Vector3 aimOffset = new Vector3(0.5f, 0.42f, -2f);
        [SerializeField] private float aimFieldOfView = 45f;
        [SerializeField, Min(0f)] private float framingBlendSpeed = 8f;
        [SerializeField] private AimResolver aimResolver;

        [Header("Camera Collision")]
        [SerializeField] private LayerMask obstructionMask;
        [SerializeField, Min(0f)] private float probeRadius = 0.25f;
        [SerializeField, Min(0f)] private float collisionSkin = 0.1f;
        [SerializeField, Min(0f)] private float minDistance = 0.6f;
        [SerializeField, Min(0f)] private float returnSpeed = 6f;

        [Header("Rotation Setup")]
        [SerializeField] private float mouseSensitivity = 0.1f; // 💡 新版滑鼠數值基數較大，靈敏度建議調小（如 0.05 ~ 0.15）
        [SerializeField] private float minPitch = -20f;
        [SerializeField] private float maxPitch = 60f;

        // 🆕 開場俯角。**明確指定，不再沿用相機在編輯器裡被擺放的角度**——
        //    舊寫法 `_pitch = transform.eulerAngles.x` 讓「開場視角」取決於場景中相機 gizmo 的擺法，
        //    症狀是每次進 Play 都從俯視 25° 開始、必須手動轉下來（2026-08-31 使用者回報）。
        //    留空（NaN 以外）都視為有效值；要回到舊行為就把 useAuthoredPitchOnStart 打開。
        [SerializeField] private float initialPitch = 10f;
        [SerializeField] private bool useAuthoredPitchOnStart = false;

        private float _yaw;
        private float _pitch;
        private float _framingBlend;
        private float _currentDist;
        private float _exploreFieldOfView;
        private UnityEngine.Camera _camera;

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

            // ⚠️ `eulerAngles` 回傳 [0, 360)，直接拿去 Clamp(minPitch, maxPitch) 會在「相機朝上擺」時出錯：
            //    例如編輯器裡 X = -10°，讀到的是 350，夾完變成 maxPitch(60) ⇒ 相機開場直接甩到最大俯角。
            //    故一律先正規化回 [-180, 180]，再依設定決定要不要採用。
            _pitch = useAuthoredPitchOnStart ? NormalizeAngle(angles.x) : initialPitch;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            _camera = GetComponent<UnityEngine.Camera>();
            if (_camera != null) _exploreFieldOfView = _camera.fieldOfView;

            if (target != null)
            {
                Vector3 pivot = target.position + Vector3.up * pivotHeight;
                _currentDist = Mathf.Max(minDistance, Vector3.Distance(transform.position, pivot));
            }
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

            float targetBlend = aimResolver != null && aimResolver.IsAiming ? 1f : 0f;
            _framingBlend = Mathf.MoveTowards(_framingBlend, targetBlend,
                framingBlendSpeed * Time.deltaTime);

            Vector3 framingOffset = Vector3.Lerp(offset, aimOffset, _framingBlend);
            if (_camera != null)
            {
                _camera.fieldOfView = Mathf.Lerp(_exploreFieldOfView, aimFieldOfView, _framingBlend);
            }

            // 🎯 **相機旋轉的唯一權威**（WP1-D1）：只由 _pitch／_yaw 決定，**與 offset 完全無關**。
            //    ⇒ camera forward（及既有移動消費者取用的壓平值）恆等於 yaw 方向，
            //      不論 offset 的 x／y 是多少 ⇒ **側向／過肩 offset 不影響 camera-relative 移動基底**。
            // ⚠️ 舊註解曾聲稱「側向 offset 會改變移動方向」——那是把舊 LookAt 實作的直覺套過來，**已勘誤**
            //    （docs/09 §6.2）。與 E3 的關係：E3 只是在論證「移除 LookAt 相對舊版是行為中性」，
            //    其 offset.x == 0 前提是拿來和**舊版**比對用的，不是本實作的約束。
            // ⛔ 防穿牆（ResolveCollisionPosition）只准改「位置」，一旦讓它動 rotation，本權威即失效。
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0);
            Vector3 pivotPosition = target.position + Vector3.up * pivotHeight;
            Vector3 targetPosition = ComputeOrbitPosition(pivotPosition, _yaw, _pitch, framingOffset);

            // 位置保留既有阻尼，但 rotation 直接採玩家輸入。如此角色移動可保持重量感，準心不會繼承位置延遲。
            Vector3 dampedPosition = Vector3.Lerp(transform.position, targetPosition,
                followSpeed * Time.deltaTime);
            transform.position = ResolveCollisionPosition(pivotPosition, dampedPosition, Time.deltaTime);
            transform.rotation = rotation;
        }

        private Vector3 ResolveCollisionPosition(Vector3 pivot, Vector3 dampedPosition, float deltaTime)
        {
            Vector3 pivotToCamera = dampedPosition - pivot;
            float desiredDistance = pivotToCamera.magnitude;
            if (desiredDistance <= Mathf.Epsilon)
            {
                _currentDist = 0f;
                return pivot;
            }

            Vector3 direction = pivotToCamera / desiredDistance;
            bool hasHit = Physics.SphereCast(pivot, Mathf.Max(0f, probeRadius), direction,
                out RaycastHit hit, desiredDistance + Mathf.Max(0f, collisionSkin),
                obstructionMask, QueryTriggerInteraction.Ignore);

            float targetDistance = ResolveCameraDistance(desiredDistance, hasHit, hit.distance,
                collisionSkin, minDistance);

            // 幾何硬約束採非對稱處理：遮蔽時立即拉近以免先穿牆，解除遮蔽後才漸進推遠以免彈出。
            if (_currentDist <= 0f) _currentDist = desiredDistance;
            _currentDist = AdvanceOccludedDistance(_currentDist, targetDistance, returnSpeed, deltaTime);
            return pivot + direction * _currentDist;
        }

        internal static Vector3 ComputeOrbitPosition(Vector3 pivot, float yaw, float pitch, Vector3 orbitOffset)
        {
            return pivot + Quaternion.Euler(pitch, yaw, 0f) * orbitOffset;
        }

        internal static float ClampPitch(float pitch, float minimum, float maximum)
        {
            return Mathf.Clamp(pitch, minimum, maximum);
        }

        /// <summary>
        /// 把 <c>Transform.eulerAngles</c> 的 [0, 360) 值域正規化回 [-180, 180]。
        /// 開場俯角必須先過這一關再 Clamp——否則「朝上擺的相機」(-10° ⇒ 讀到 350) 會被夾成 maxPitch。
        /// </summary>
        internal static float NormalizeAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            else if (degrees < -180f) degrees += 360f;
            return degrees;
        }

        internal static float ResolveCameraDistance(float desiredDistance, bool hasHit,
            float hitDistance, float skin, float minimumDistance)
        {
            if (!hasHit) return desiredDistance;

            return Mathf.Max(Mathf.Max(0f, minimumDistance), hitDistance - Mathf.Max(0f, skin));
        }

        internal static float AdvanceOccludedDistance(float current, float target,
            float speed, float deltaTime)
        {
            if (target < current) return target;
            return Mathf.MoveTowards(current, target, Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime));
        }
    }
}
