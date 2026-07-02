using Project.Core.Blackboard;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class RollState : BaseState
    {
        public override StateType Type => StateType.Roll;
        private float _rollTimer;
        public bool IsRollFinished { get; private set; }

        public override bool CanEnter(PlayerRuntimeData data) => data.Intent.RollRequested;

        public override void OnEnter(PlayerRuntimeData data)
        {
            Debug.Log("<color=cyan>[State] 進入 ROLL 翻滾（無敵幀開始）</color>");
            _rollTimer = 0.5f; // 模擬翻滾持續 0.5 秒
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
    }
}