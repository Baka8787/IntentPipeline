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

        // 🆕 這是「資料流沒問題但無法轉成表現」最可能卡住的地方——
        // 之前 RollState 完全沒 override OnUpdateMotion，等於一直在用預設的 ExecuteBaseMovement，
        // 烘焙資料從頭到尾沒有被真正拿去驅動位移
        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            if (_rollBakeData == null)
            {
                motionDriver.ExecuteBaseMovement(data); // 防呆：還沒烘焙好就退回一般 Procedural 結算
                return;
            }

            float normalizedTime = animationFacade.GetNormalizedTime();
            motionDriver.ExecuteBakedCurveMovement(_rollBakeData, normalizedTime);
        }
    }
}