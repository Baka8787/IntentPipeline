using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（ADR-003 Migration Stage 1）B9 MoveSpeed 平滑的**純運算單元**：
    /// 把模型無關的 <see cref="MovementIntentData"/> 轉為 Locomotion 的實際 dynamics（平滑速度＋有效方向）。
    ///
    /// **定位＝Stage 1 過渡 shim，也是 Stage 2 的遷移單位。**
    /// 依 ADR-003 D4／§9-L1，B9 平滑與 gait→tier 是 **Locomotion model 的內部 dynamics**，
    /// 正確歸屬不在通用 Runner。Stage 1 先把它抽成無 MonoBehaviour 相依的 struct（可單元測試、可整體搬移），
    /// 由 <c>CharacterPipelineRunner</c> 持有並每幀 Tick；**Stage 2 整顆搬進 Locomotion model 的
    /// <c>OnUpdateMotion</c> 路徑**，Runner 隨之不再認識任何 locomotion 概念。
    ///
    /// **Stage 1 單一真相紀律（ADR-003 §13.4）**：本單元的輸入**只有** <see cref="MovementIntentData"/>——
    /// 黑板 <c>MoveSpeed</c>／<c>MoveDirection</c> 因此恆為「可由 intent（＋本 dynamics）重新導出」的衍生值，
    /// 不是第二個真相來源。禁止任何路徑繞過 intent 直寫 MoveSpeed。
    /// </summary>
    /// <remarks>
    /// 為何是 <c>struct</c>：狀態（平滑值／SmoothDamp 速度緩存／最後方向）需跨幀持續但屬**單一擁有者私有**，
    /// 值型別可零配置內嵌於持有者，且 Stage 2 遷移時只是換一個持有者。所有狀態顯式在此、無隱藏靜態態，
    /// 符合 ADR-003 §9-L5 的 snapshot-able 前提。
    /// </remarks>
    public struct LocomotionSpeedSmoother
    {
        /// <summary>速度／方向的有效閾值：低於此值視為「完全靜止／無方向」（沿用 Migration 前的 0.001f）。</summary>
        public const float Epsilon = 0.001f;

        private float _speed;
        private float _smoothVelocity;   // Mathf.SmoothDamp 的 ref 速度緩存
        private Vector2 _lastDirection;  // 減速滑行期保留的最後移動方向
        private Vector2 _direction;      // 本幀輸出方向

        /// <summary>本幀平滑後的移動強度 [0-1]（黑板 <c>MoveSpeed</c> 的來源）。</summary>
        public float Speed => _speed;

        /// <summary>本幀有效移動方向（黑板 <c>MoveDirection</c> 的來源）。</summary>
        public Vector2 Direction => _direction;

        /// <summary>
        /// 依本幀意圖推進 dynamics。純運算：<c>deltaTime</c> 由呼叫端傳入（不隱式取 <c>Time.deltaTime</c>），
        /// 因此可在 EditMode 以固定步長確定性驗證。
        /// </summary>
        /// <param name="intent">本幀 Movement 意圖（唯一輸入真相）。</param>
        /// <param name="accelTime">0→滿 的加速平滑時間（秒）。</param>
        /// <param name="decelTime">滿→0 的減速平滑時間（秒），通常略大於加速＝放開後自然滑行收步。</param>
        /// <param name="deltaTime">本幀時間步長。</param>
        public void Tick(in MovementIntentData intent, float accelTime, float decelTime, float deltaTime)
        {
            // 鍵盤輸入強度為 0/1 二值，直送會讓 1D Mixer 一幀從 Idle 跳到 Sprint、中間 Walk/Run tier 踩不到。
            // 以 SmoothDamp 隨時間平順爬升/回落，經過各速度 tier；加速/減速採不同時間常數。
            float desired = Mathf.Clamp01(intent.DesiredSpeedNormalized);
            float smoothTime = desired > _speed ? accelTime : decelTime;
            _speed = Mathf.SmoothDamp(_speed, desired, ref _smoothVelocity, smoothTime, Mathf.Infinity, deltaTime);

            // 完全停止時 snap 到 0（清 SmoothDamp 殘尾）：確保 Mixer 回純 Idle、狀態機依 MoveSpeed<0.1 收斂進 Idle。
            if (desired < Epsilon && _speed < Epsilon)
            {
                _speed = 0f;
                _smoothVelocity = 0f;
            }

            // 有意圖時直接採用並記憶；放開（desired≈0）但仍在減速滑行（speed>0）時保留最後方向，
            // 讓 MotionDriver 以殘速續走該方向，動畫與位移同步收步、不滑步；
            // 完全停止才歸零 → ExecuteBaseMovement 的方向閘門關閉，乾淨停步。
            if (desired > Epsilon)
            {
                _lastDirection = intent.DesiredDirection;
                _direction = intent.DesiredDirection;
            }
            else
            {
                _direction = _speed > Epsilon ? _lastDirection : Vector2.zero;
            }
        }
    }
}
