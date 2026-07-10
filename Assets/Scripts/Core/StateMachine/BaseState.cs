using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Core.StateMachine
{
    public abstract class BaseState
    {
        public abstract StateType Type { get; }
        protected StateMachineConfigSO Config;

        // 🆕 預設用 enum 名稱當動畫鍵，對應 AnimancerFacade 的 ClipMapping.StateKey
        public virtual string AnimationKey => Type.ToString();

        public virtual void Initialize(StateMachineConfigSO config)
        {
            Config = config;
        }

        public abstract bool CanEnter(PlayerRuntimeData data);
        public abstract void OnEnter(PlayerRuntimeData data);
        public abstract void OnTick(PlayerRuntimeData data, float deltaTime);
        public abstract void OnExit(PlayerRuntimeData data);

        /// <summary>
        /// 💡 新增：由當前狀態決定本影格在 LateUpdate 該如何結算物理位移
        /// </summary>
        public virtual void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            // 預設行為：全面改為傳入黑板資料的純 Procedural 移動結算
            motionDriver.ExecuteBaseMovement(data);
        }

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