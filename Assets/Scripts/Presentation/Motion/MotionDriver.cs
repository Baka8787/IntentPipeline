using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Motion
{
    public class MotionDriver : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] private CharacterController characterController;

        [Header("Procedural Move Speed")]
        [SerializeField] private float moveSpeed = 5f; // WASD 移動基礎速度 (m/s)

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

            // 1. 將 0~1 的進度還原為實際時間（秒）
            float currentTime = normalizedTime * bakeData.Duration;
            float previousTime = Mathf.Max(0f, currentTime - Time.deltaTime);

            // 2. 透過曲線評估當前瞬時速度，生成水平速度向量
            float currentSpeed = bakeData.GetSpeedAt(currentTime);
            Vector3 horizontalVelocity = transform.forward * currentSpeed;

            // 3. 計算這一影格與上一影格的「累計角度差 (Delta Yaw)」並套用旋轉
            float currentYaw = bakeData.GetRotationAt(currentTime);
            float previousYaw = bakeData.GetRotationAt(previousTime);
            float deltaYaw = currentYaw - previousYaw;

            if (Mathf.Abs(deltaYaw) > 0.0001f)
            {
                transform.Rotate(Vector3.up, deltaYaw, Space.World);
            }

            // 4. 曲線特殊狀態同樣疊加世界物理重力結算，打包送出（同時同步觸地狀態回黑板）
            Vector3 finalMovement = horizontalVelocity + GetGravityThisFrame(data);
            characterController.Move(finalMovement * Time.deltaTime);
        }

        /// <summary>
        /// 🚀 工業級實作：進階烘焙補償速度疊加位移（動態吸附/校準）
        /// </summary>
        public void ApplyBakedCompensation(MotionBakeData bakeData, Vector3 actualTarget, float normalizedTime, PlayerRuntimeData data)
        {
            if (bakeData == null) return;

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

            data.IsGrounded = characterController.isGrounded;

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
