using Project.Core.Blackboard;

namespace Project.Core.StateMachine
{
    public abstract class BaseState
    {
        public abstract StateType Type { get; }
        protected StateMachineConfigSO Config;

        public virtual void Initialize(StateMachineConfigSO config)
        {
            Config = config;
        }

        public abstract bool CanEnter(PlayerRuntimeData data);
        public abstract void OnEnter(PlayerRuntimeData data);
        public abstract void OnTick(PlayerRuntimeData data, float deltaTime);
        public abstract void OnExit(PlayerRuntimeData data);

        /// <summary>
        /// 由 ScriptableObject 的資料驅動判斷，子類別若有特殊極限狀況可 override 擴充
        /// </summary>
        public virtual bool CanBeInterruptedBy(BaseState other)
        {
            if (Config == null) return false;
            return Config.CheckCanInterrupt(this.Type, other.Type);
        }
        /// <summary>
        /// 控制目前狀態是否允許被「自然過渡」打斷。
        /// 預設為 true（如 Idle, Move）；有鎖定期的狀態（Jump, Roll）應複寫為 false 直到動作完成。
        /// </summary>
        public virtual bool CanTransitionAway => true;
    }
}