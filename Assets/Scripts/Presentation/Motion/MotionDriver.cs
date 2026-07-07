using UnityEngine;

namespace Project.Presentation.Motion
{
    public class MotionDriver : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Animator animator;

        private Vector3 _rootMotionDelta = Vector3.zero;

        private void Awake()
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void OnAnimatorMove()
        {
            if (animator != null)
            {
                _rootMotionDelta += animator.deltaPosition;
            }
        }

        /// <summary>
        /// 【順序 6a】基礎常規運動（走跑跳繞過原本的RootMotion，由常規邏輯結算）
        /// </summary>
        public void ExecuteBaseMovement()
        {
            if (_rootMotionDelta != Vector3.zero)
            {
                characterController.Move(_rootMotionDelta);
                _rootMotionDelta = Vector3.zero;
            }
        }

        /// <summary>
        /// 🚀 新增：直接使用烘焙曲線驅動角色 (適用於無目標的空揮、特定技能位移)
        /// </summary>
        /// <param name="normalizedTime">動畫播放進度 (0~1)</param>
        public void ExecuteBakedCurveMovement(MotionBakeData bakeData, float normalizedTime)
        {
            if (bakeData == null) return;

            // 1. 將 0~1 的進度還原為實際時間（秒）
            float currentTime = normalizedTime * bakeData.Duration;
            float previousTime = Mathf.Max(0f, currentTime - Time.deltaTime);

            // 2. 透過曲線評估當前速度，並換算成這一幀的位移量
            float currentSpeed = bakeData.GetSpeedAt(currentTime);
            Vector3 moveDelta = transform.forward * currentSpeed * Time.deltaTime;

            // 3. 計算這一幀與上一幀的「角度差 (Delta Yaw)」並套用旋轉
            float currentYaw = bakeData.GetRotationAt(currentTime);
            float previousYaw = bakeData.GetRotationAt(previousTime);
            float deltaYaw = currentYaw - previousYaw;

            transform.Rotate(Vector3.up, deltaYaw);

            // 4. 最終送入 CharacterController 執行移動
            characterController.Move(moveDelta);
        }

        /// <summary>
        /// 🚀 【順序 6b】工業級實作：進階烘焙補償速度疊加位移（動態吸附/校準）
        /// 適用場景：刺客處決、格鬥技精準追蹤敵人、飛身攀爬登頂
        /// </summary>
        /// <param name="actualTarget">動態目標的世界座標點</param>
        /// <param name="normalizedTime">動畫播放進度 (0~1)</param>
        public void ApplyBakedCompensation(MotionBakeData bakeData, Vector3 actualTarget, float normalizedTime)
        {
            if (bakeData == null) return;

            float currentTime = normalizedTime * bakeData.Duration;
            float remainingTime = bakeData.Duration - currentTime;

            // 防呆：如果動畫快播完了或時間異常，直接退回普通曲線移動
            if (remainingTime <= 0.001f)
            {
                ExecuteBakedCurveMovement(bakeData, normalizedTime);
                return;
            }

            // 1. 旋轉修正：強迫角色在剩餘時間內逐漸轉向目標
            Vector3 toTarget = actualTarget - transform.position;
            toTarget.y = 0f; // 忽略高度差
            if (toTarget.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized);
                // 使用 SmoothDamp 或 Slerp 讓轉向在動畫結束前完美對齊
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime / remainingTime);
            }

            // 2. 位移補償 (Warping)：
            // 基礎理論速度（來自曲線）
            float curveSpeed = bakeData.GetSpeedAt(currentTime);
            Vector3 baseMoveDelta = transform.forward * curveSpeed * Time.deltaTime;

            // 計算「目前位置」距離「目標落點」還差多少向量
            Vector3 distanceToGo = actualTarget - transform.position;
            distanceToGo.y = 0f;

            // 計算要在剩餘時間內抵達目的地，這一幀額外需要補償的「橫向/縱向速度差」
            // 公式：剩餘距離 / 剩餘時間 = 理論總速度；再減去動畫給的基礎速度，就是補償速度
            Vector3 requiredRequiredVelocity = distanceToGo / remainingTime;
            Vector3 compensationVelocity = requiredRequiredVelocity - (transform.forward * curveSpeed);

            // 將補償速度化為這一幀的位移
            Vector3 compensationDelta = compensationVelocity * Time.deltaTime;

            // 3. 最終結合：基礎動畫位移 + 動態校準補償
            characterController.Move(baseMoveDelta + compensationDelta);
        }
    }
}