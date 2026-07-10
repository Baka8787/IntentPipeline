using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class RollState : BaseState
    {
        public override StateType Type => StateType.Roll;
        private float _rollTimer;
        public bool IsRollFinished { get; private set; }
        public override bool CanTransitionAway => IsRollFinished;

        private MotionBakeData _rollBakeData;

        // 🆕 移除建構子，改在 Initialize 裡查表
        public override void Initialize(StateMachineConfigSO config)
        {
            base.Initialize(config);
            _rollBakeData = config.GetBakeData(Type);
        }

        public override bool CanEnter(PlayerRuntimeData data) => data.Intent.RollRequested;

        public override void OnEnter(PlayerRuntimeData data)
        {
            Debug.Log("<color=cyan>[State] 進入 ROLL 翻滾（無敵幀開始）</color>");
            _rollTimer = _rollBakeData != null ? _rollBakeData.Duration : 0.5f;
            IsRollFinished = false;
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (IsRollFinished) return;

            _rollTimer -= deltaTime;
            if (_rollTimer <= 0)
            {
                IsRollFinished = true;
                Debug.Log("<color=blue>[State] ROLL 翻滾結束</color>");
            }
        }

        public override void OnExit(PlayerRuntimeData data) => IsRollFinished = false;

        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            // 在信任 GetNormalizedTime() 之前，先確認動畫實際上真的在播 Roll clip。
            // 若 AnimancerFacade.Play() 因為 clip mapping 查表失敗而提前 return（只會 LogWarning，不拋例外），
            // GetNormalizedTime() 仍會回傳「目前 Layer 0 正在播放的舊動畫」進度，
            // 若不做這層檢查，位移會被一個跟 Roll 完全不相干的進度值驅動，只會在畫面上看到角色亂飄，
            // 主控台卻只有一句容易被忽略的 warning。
            bool hasValidBakeData = _rollBakeData != null;
            bool isActuallyPlayingRoll = animationFacade != null && animationFacade.IsPlaying(AnimationKey);

            if (!hasValidBakeData || !isActuallyPlayingRoll)
            {
                motionDriver.ExecuteBaseMovement(data); // 防呆：退回一般 Procedural 結算
                return;
            }

            float normalizedTime = animationFacade.GetNormalizedTime();
            motionDriver.ExecuteBakedCurveMovement(_rollBakeData, normalizedTime, data);
        }
    }
}