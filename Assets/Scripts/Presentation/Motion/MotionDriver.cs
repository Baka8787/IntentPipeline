using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Motion
{
    public class MotionDriver : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] private CharacterController characterController;

        [Header("Procedural Move Speed")]
        [Tooltip("跑步（輸入強度=1）時的水平速度 (m/s)。可由下方 moveSpeedSource 於啟動時自動帶入動畫天生速度。")]
        [SerializeField] private float moveSpeed = 5f; // WASD 移動基礎速度 (m/s)

        // 🆕（v0.16.2）「動畫數據 → 配置」資料流：以最高速 clip（Fast Run）的烘焙代表速度作為 gameplay
        // 滿速的預設來源，讓「動畫天生跑多快」成為速度真相，根除腳步視覺與位移速度不一致的滑步。
        // 保留手動調整能力（dev-spec §3.2）：留空 = 純用手填 moveSpeed；勾 overrideMoveSpeed = 忽略來源。
        [Tooltip("移動速度的動畫數據來源：通常指向最高速 clip（如 Fast Run）的 Bake 資產。設定後，啟動時以其" +
                 "代表速度（GetRepresentativeSpeed）覆寫 moveSpeed。留空則純用上方手填值。此為『Bake 提供預設＋來源可追蹤』機制。")]
        [SerializeField] private MotionBakeData moveSpeedSource;

        [Tooltip("勾選 = 忽略 moveSpeedSource，強制使用上方手填的 moveSpeed（Designer 明確 override 動畫數據，如刻意的風格化快/慢）。")]
        [SerializeField] private bool overrideMoveSpeed = false;

        [Header("Physics Fallback")]
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float reboundForce = -2f; // 踩在地面時的固定貼地力

        private float _verticalVelocity;

        // 🆕（ADR-002）本次垂直運動採用的重力（負值＝向下）。
        // 預設等於序列化的 gravity；起跳時由 ApplyJumpLaunch 覆寫為「該段烘焙逆推重力」，
        // 落地（IsGrounded）時於 GetGravityThisFrame 內部自動回復預設。寫入者僅本類別，守住單一寫入者。
        private float _activeGravity;

        // 參考高階架構：單幀重力緩存，避免一幀內被多處呼叫時重複積分垂直重力速度
        private int _gravityFrame = -1;
        private Vector3 _cachedGravity;

        // 🆕（M2）上一影格的觸地狀態，供 GetGravityThisFrame 做落地/離地邊沿偵測
        // （JustLanded / JustLeftGround 的唯一觸發源比較基準）。
        private bool _wasGrounded;

        /// <summary>
        /// 🆕（2026-07-27）本帧沒有時間流逝（實務上＝<c>Time.timeScale == 0</c> 的暫停）。
        /// 為 true 時**整個位移結算跳過**：不呼叫 <c>Move</c>、不重算觸地與單幀邊沿旗標。
        ///
        /// **為什麼需要這道守衛（真實 bug，非防禦性程式碼）**：
        /// 位移出口是 <c>Move(finalMovement * Time.deltaTime)</c>，<c>deltaTime = 0</c> 時等同
        /// <c>Move(Vector3.zero)</c>。而 Unity 的 <c>CharacterController.isGrounded</c> 是由
        /// **上一次 Move 有沒有向下撞到東西**決定的——零位移沒有向下推，於是回報 **false**。
        /// 症狀是連鎖的：暫停第 2 帧 <c>JustLeftGround</c> 假觸發、暫停期間 <c>IsGrounded</c> 恆 false、
        /// 解除暫停後角色「重新著地」→ **<c>JustLanded</c> 假觸發 → 站在地上暫停再解除就會聽到落地聲**
        /// （2026-07-27 實測確認）。
        ///
        /// ⚠️ 本守衛刻意表述為「**沒有時間流逝**」而不是「暫停」——<c>MotionDriver</c> 不認識暫停，
        /// 它只知道「沒有時間就沒有東西要積分、沒有位移要結算、也沒有邊沿可偵測」。
        /// 這讓守衛對任何造成 <c>deltaTime == 0</c> 的原因都成立，且不引入對應用層的依賴。
        ///
        /// 📌 **連帶效應（已知且刻意）**：修好之後 <c>IsGrounded</c> 在暫停期間會正確地維持 true，
        /// 於是 <c>JumpState.CanEnter</c>（＝<c>JumpRequested &amp;&amp; IsGrounded</c>）會成立。
        /// 原本「暫停中按跳躍不會跳」靠的正是上述 bug 的副作用，**不是任何設計**。
        /// 該缺口改由 <c>GamePauseController</c> 以 <c>IArbiterSource</c> 要求 <c>BlockInput</c> 正式關閉。
        /// </summary>
        private static bool IsTimeFrozen => Time.deltaTime <= 0f;

        private void Awake()
        {
            _activeGravity = gravity; // 預設重力，之後可被單次跳躍發射覆寫

            if (characterController == null) characterController = GetComponent<CharacterController>();

            // 🆕（v0.7 Code Review 補上）與 CharacterPipelineRunner 對 motionDriver/animationFacade
            // 的防禦線保持一致：缺元件時明確報錯，而不是留到第一次 Move() 呼叫時才 NullReferenceException。
            if (characterController == null)
            {
                Debug.LogError($"[{gameObject.name}] MotionDriver 缺少 CharacterController，且未在 Inspector 綁定！", this);
            }

            // 🆕（v0.16.2）動畫數據 → 配置：以烘焙代表速度覆寫 gameplay 滿速（Designer 未 override 且有指定來源時）。
            // 唯一寫入時機在啟動，之後 moveSpeed 就是一般序列化欄位——不改變執行期熱路徑、不新增每幀成本。
            if (moveSpeedSource != null && !overrideMoveSpeed)
            {
                float sourceSpeed = moveSpeedSource.GetRepresentativeSpeed();
                if (sourceSpeed > 0f)
                {
                    moveSpeed = sourceSpeed;
                }
#if UNITY_EDITOR
                else
                {
                    Debug.LogWarning($"[{gameObject.name}] MotionDriver.moveSpeedSource（'{moveSpeedSource.name}'）的代表速度為 0，" +
                        "無法作為速度來源（SpeedCurve 為空或該 clip 非移動動畫）。維持手填 moveSpeed，請確認來源是否指對最高速 clip。", this);
                }
#endif
            }
        }

        /// <summary>
        /// 🆕（ADR-002 選項 A）供 JumpState 在 OnUpdateMotion 點火呼叫：一次注入「起跳初速 + 該次重力」。
        /// 初速與重力皆由 JumpState 依烘焙資料逆推（v = √(2gh)）後傳入，本類別僅負責積分執行，
        /// 不感知跳躍/動畫細節；_verticalVelocity 與 _activeGravity 的寫入者仍只有本類別。
        /// </summary>
        public void ApplyJumpLaunch(in JumpLaunchData launch)
        {
            _verticalVelocity = launch.InitialVerticalVelocity; // 向上為正
            _activeGravity = -Mathf.Abs(launch.Gravity);        // 契約傳正值大小，於此轉為向下（負）
            _gravityFrame = -1;                                 // 強制刷新本影格的重力快取
        }

        /// <summary>
        /// 🚀 【大一統完全體：順序 6a】基礎常規運動
        /// 融合了相機視角解耦、procedural 水平轉向、以及世界重力結算
        /// </summary>
        public void ExecuteBaseMovement(PlayerRuntimeData data)
        {
            if (IsTimeFrozen) return; // 見 IsTimeFrozen 的說明：零位移的 Move 會毀掉 isGrounded

            Vector3 horizontalVelocity = Vector3.zero;

            // 只有在玩家有推搖桿(WASD)且相機引用存在時，才計算轉向與 procedural 速度
            if (data.MoveDirection.sqrMagnitude > 0.001f && data.CameraTransform != null)
            {
                // 1. 取得相機在水平面上的 forward 與 right 向量（忽略仰角與俯角，防止角色往前跌倒）
                Vector3 camForward = data.CameraTransform.forward;
                Vector3 camRight = data.CameraTransform.right;
                camForward.y = 0f;
                camRight.y = 0f;
                camForward.Normalize();
                camRight.Normalize();

                // 2. 將玩家的輸入方向，投影到相機的世界座標系中，算出「期望的世界移動方向」
                Vector3 targetDirection = camForward * data.MoveDirection.y + camRight * data.MoveDirection.x;

                if (targetDirection.sqrMagnitude > 0.001f)
                {
                    // 3. 讓角色身體平滑地 Slerp 轉向這個期望的世界移動方向
                    Quaternion targetRotation = Quaternion.LookRotation(targetDirection.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 12f * Time.deltaTime);
                }

                // 4. 水平速度採用 transform.forward（角色當前肉體正前方），使轉彎具備流暢的體重弧線感
                float currentSpeed = data.MoveSpeed * moveSpeed;
                horizontalVelocity = transform.forward * currentSpeed;
            }

            // 5. 疊加單幀快取重力（保持原有的自由落體與貼地力優化），同時同步觸地狀態回黑板
            Vector3 finalMovement = horizontalVelocity + GetGravityThisFrame(data);

            // 6. 總出口呼叫
            characterController.Move(finalMovement * Time.deltaTime);
        }

        /// <summary>
        /// 🚀 【順序 6b】特殊動作曲線運動：直接使用離線烘焙曲線驅動角色位移與旋轉（如 Roll）
        /// </summary>
        public void ExecuteBakedCurveMovement(MotionBakeData bakeData, float normalizedTime, PlayerRuntimeData data)
        {
            if (bakeData == null) return;
            if (IsTimeFrozen) return; // 同 ExecuteBaseMovement，見 IsTimeFrozen 的說明

            float currentTime = normalizedTime * bakeData.Duration;
            float previousTime = Mathf.Max(0f, currentTime - Time.deltaTime);
            ExecuteBakedCurveMovementAtTimes(bakeData, currentTime, previousTime, false, data);
        }

        /// <summary>
        /// 以前後播放頭驅動烘焙曲線；TransitionAsset 的播放倍率已包含在 playhead delta 內。
        /// </summary>
        public void ExecuteBakedCurveMovement(
            MotionBakeData bakeData,
            float normalizedTime,
            float previousNormalizedTime,
            PlayerRuntimeData data)
        {
            if (bakeData == null) return;
            if (IsTimeFrozen) return;

            float currentTime = Mathf.Max(0f, normalizedTime * bakeData.Duration);
            float previousTime = Mathf.Clamp(previousNormalizedTime * bakeData.Duration, 0f, currentTime);
            ExecuteBakedCurveMovementAtTimes(bakeData, currentTime, previousTime, true, data);
        }

        private void ExecuteBakedCurveMovementAtTimes(
            MotionBakeData bakeData,
            float currentTime,
            float previousTime,
            bool usePlayheadDeltaSpeed,
            PlayerRuntimeData data)
        {
            float currentSpeed = bakeData.GetSpeedAt(currentTime);
            float worldSpeed = currentSpeed;

            if (usePlayheadDeltaSpeed)
            {
                float clipDeltaTime = Mathf.Max(0f, currentTime - previousTime);
                float previousSpeed = bakeData.GetSpeedAt(previousTime);
                float averageClipSpeed = 0.5f * (previousSpeed + currentSpeed);
                worldSpeed = clipDeltaTime > 0f
                    ? averageClipSpeed * (clipDeltaTime / Time.deltaTime)
                    : 0f;
            }

            Vector3 horizontalVelocity = transform.forward * worldSpeed;

            float currentYaw = bakeData.GetRotationAt(currentTime);
            float previousYaw = bakeData.GetRotationAt(previousTime);
            float deltaYaw = currentYaw - previousYaw;

            if (Mathf.Abs(deltaYaw) > 0.0001f)
            {
                transform.Rotate(Vector3.up, deltaYaw, Space.World);
            }

            Vector3 finalMovement = horizontalVelocity + GetGravityThisFrame(data);
            characterController.Move(finalMovement * Time.deltaTime);
        }

        /// <summary>
        /// 🚀 工業級實作：進階烘焙補償速度疊加位移（動態吸附/校準）
        /// </summary>
        public void ApplyBakedCompensation(MotionBakeData bakeData, Vector3 actualTarget, float normalizedTime, PlayerRuntimeData data)
        {
            if (bakeData == null) return;
            if (IsTimeFrozen) return; // 同 ExecuteBaseMovement，見 IsTimeFrozen 的說明

            float currentTime = normalizedTime * bakeData.Duration;
            float remainingTime = bakeData.Duration - currentTime;

            if (remainingTime <= 0.001f)
            {
                ExecuteBakedCurveMovement(bakeData, normalizedTime, data);
                return;
            }

            // 1. 旋轉修正
            Vector3 toTarget = actualTarget - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime / remainingTime);
            }

            // 2. 位移補償 (Warping)
            float curveSpeed = bakeData.GetSpeedAt(currentTime);
            Vector3 baseMoveDelta = transform.forward * curveSpeed * Time.deltaTime;

            Vector3 distanceToGo = actualTarget - transform.position;
            distanceToGo.y = 0f;

            Vector3 requiredVelocity = distanceToGo / remainingTime;
            Vector3 compensationVelocity = requiredVelocity - (transform.forward * curveSpeed);
            Vector3 compensationDelta = compensationVelocity * Time.deltaTime;

            // 3. 結合重力统一打包（同時同步觸地狀態回黑板）
            Vector3 finalMovement = (baseMoveDelta + compensationDelta) / Time.deltaTime + GetGravityThisFrame(data);
            characterController.Move(finalMovement * Time.deltaTime);
        }

        /// <summary>
        /// 工業級實作：獲取本影格重力（保證一影格內即使被多處調用，也只做一次垂直速度積分）
        /// 把 IsGrounded 同步回黑板收斂進這裡，
        /// 因為所有移動路徑（ExecuteBaseMovement / ExecuteBakedCurveMovement / ApplyBakedCompensation）
        /// 最終都會呼叫這個方法，等於「只要角色這幀有移動，IsGrounded 就保證是新的」，
        /// 不再需要額外從 CharacterPipelineRunner 顯式呼叫一次 SyncGroundedState，
        /// 也不用擔心未來新增移動路徑時忘記同步。
        /// </summary>
        private Vector3 GetGravityThisFrame(PlayerRuntimeData data)
        {
            int currentFrame = Time.frameCount;
            if (_gravityFrame == currentFrame) return _cachedGravity;

            _gravityFrame = currentFrame;

            // 🆕（M2）單幀落地/離地邊沿：MotionDriver 是這兩個旗標的唯一觸發源（set true）；
            // 復位（set false）由管線末尾 PlayerRuntimeData.ResetTransientState() 負責，非此處。
            bool grounded = characterController.isGrounded;
            data.JustLanded = !_wasGrounded && grounded;
            data.JustLeftGround = _wasGrounded && !grounded;
            data.IsGrounded = grounded;
            _wasGrounded = grounded;

            if (data.IsGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = reboundForce;
                _activeGravity = gravity; // 🆕（ADR-002）落地即回復預設重力，讓下一次一般移動/起跳前用預設值積分
            }
            else
            {
                _verticalVelocity += _activeGravity * Time.deltaTime; // 🆕 改用單次跳躍發射帶入的重力（預設等於 gravity）
            }

            _cachedGravity = new Vector3(0f, _verticalVelocity, 0f);
            return _cachedGravity;
        }
    }
}
