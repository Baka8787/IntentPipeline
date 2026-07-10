using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class JumpState : BaseState
    {
        public override StateType Type => StateType.Jump;

        // 🆕（v0.7 修正）原本掛在這裡的 [SerializeField] 對純 C# 類別無效，Inspector 永遠調不到。
        // 改為程式碼內建預設值，實際數值優先從 StateMachineConfigSO 查表覆寫（見 Initialize）。
        private float _jumpImpulseForce = 7.5f; // 起跳發射初速度 (m/s) 的內建 fallback 預設

        // 🆕（v0.8，除錯「先蹲下再往上」發現）
        // 根因：物理衝量原本在進入 Jump 狀態的第一個 LateUpdate 就無條件注入，
        // 但 Jump 動畫若帶有蹲下預備姿勢（純骨骼 Pose，跟根位移無關），該姿勢仍會照常播放，
        // 於是「角色物理上已經在往上飛」跟「身體姿勢還在演蹲下」兩條時間軸對不上，
        // 玩家看到的就是「先蹲下才往上」的違和畫面。
        // 解法：把衝量注入延後到 _takeoffDelay 秒之後，這段期間維持一般貼地移動（ExecuteBaseMovement），
        // 讓預備動畫在角色仍在地面時把姿勢演完，時間到才真正離地。
        private float _takeoffDelay; // 內建 fallback 0（=不延遲，等同 v0.7 之前的行為）
        private float _stateElapsedTime;

        // 🆕（v0.7 修正）落地判定不再用固定計時器，改讀黑板 IsGrounded（來源 CharacterController.isGrounded）。
        // 保留一段最短滯空保護時間，避免離地瞬間 isGrounded 還沒切換成 false 就被誤判為已落地。
        private const float MinAirborneTimeBeforeLandingCheck = 0.15f;
        private float _airborneTimer;

        public bool IsLanded { get; private set; }
        public override bool CanTransitionAway => IsLanded;

        /// <summary>
        /// 🆕 從 Config SO 查表拿取衝量值與起跳延遲；若該狀態未在 Config 內設定（回傳 0），沿用內建預設值。
        /// 比照 RollState 從 Config 查 MotionBakeData 的既有慣例。
        /// </summary>
        public override void Initialize(StateMachineConfigSO config)
        {
            base.Initialize(config);

            float configuredForce = config != null ? config.GetJumpImpulseForce(Type) : 0f;
            if (configuredForce > 0f)
            {
                _jumpImpulseForce = configuredForce;
            }

            _takeoffDelay = config != null ? Mathf.Max(0f, config.GetJumpTakeoffDelay(Type)) : 0f;
        }

        public override bool CanEnter(PlayerRuntimeData data) => data.Intent.JumpRequested;

        public override void OnEnter(PlayerRuntimeData data)
        {
            Debug.Log("<color=yellow>[State] 進入 JUMP 狀態！等待起跳延遲後注入垂直初速度！</color>");
            IsLanded = false;
            _stateElapsedTime = 0f;
            _airborneTimer = 0f;

            // 🆕 核心修正：配合 OnAnimatorMove 移除，離地時刻由 OnUpdateMotion 依 _takeoffDelay 決定，
            // 不再是「一進狀態立刻發射」
            _isVelocityInjected = false;
        }

        private bool _isVelocityInjected;

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            _stateElapsedTime += deltaTime;

            if (IsLanded) return;

            // 🆕 尚未真正離地（還在起跳延遲/預備動畫階段）時，不進行落地判定，
            // 避免延遲期間角色仍貼地站著卻被 IsGrounded == true 誤判為「已經跳完落地了」。
            if (!_isVelocityInjected) return;

            _airborneTimer += deltaTime;
            if (_airborneTimer >= MinAirborneTimeBeforeLandingCheck && data.IsGrounded)
            {
                IsLanded = true;
                Debug.Log("<color=orange>[State] JUMP 偵測到真實落地（IsGrounded == true）</color>");
            }
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            IsLanded = false;
            _isVelocityInjected = false;
        }

        // 🆕 覆寫運動結算：延遲期間維持一般貼地 Procedural 移動，delay 結束才點火注入起跳衝量
        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            if (!_isVelocityInjected && _stateElapsedTime >= _takeoffDelay)
            {
                motionDriver.ApplyJumpImpulse(_jumpImpulseForce);
                _isVelocityInjected = true;
                _airborneTimer = 0f; // 從真正離地那一刻開始重新計時滯空保護
            }

            // 無論是否已注入衝量，都走同一個常規物理移動出口：
            // 延遲期間角色靠重力系統的貼地力（ReboundForce）維持站立，離地後則是自然拋物線
            motionDriver.ExecuteBaseMovement(data);
        }
    }
}