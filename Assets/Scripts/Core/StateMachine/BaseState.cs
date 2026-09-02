using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Core.StateMachine
{
    public abstract class BaseState
    {
        public abstract StateType Type { get; }
        protected StateMachineConfigSO Config;

        /// <summary>
        /// 🆕（ADR-003 Stage 2）當下 active 的 Movement Model。**全狀態共用同一實例**——
        /// 由 <see cref="FullBodyStateMachine"/> 在註冊時統一發放，沒有任何 state 自行建立的路徑。
        /// 這條注入鏈是「跨幀平滑狀態全域唯一」的結構性保證（見 <see cref="IMovementModel"/> remarks）。
        ///
        /// ambient 狀態（Idle／Move）用它 delegate 位移與取得門檻信號（D3）；
        /// intrinsic-motion 狀態（Jump／Roll）維持既有 override 自帶位移，可不理會。
        /// </summary>
        protected IMovementModel MovementModel { get; private set; }

        // 🆕 預設用 enum 名稱當動畫鍵，對應 AnimancerFacade 的 ClipMapping.StateKey
        public virtual string AnimationKey => Type.ToString();

        public virtual void Initialize(StateMachineConfigSO config, IMovementModel movementModel)
        {
            Config = config;
            MovementModel = movementModel;
        }

        public abstract bool CanEnter(PlayerRuntimeData data);
        public abstract void OnEnter(PlayerRuntimeData data);
        public abstract void OnTick(PlayerRuntimeData data, float deltaTime);
        public abstract void OnExit(PlayerRuntimeData data);

        /// <summary>
        /// 💡 新增：由當前狀態決定本影格在 LateUpdate 該如何結算物理位移
        ///
        /// 🆕（ADR-003 D3）三種歸屬：**ambient 狀態**（Idle／Move）override 成 delegate 給
        /// <see cref="MovementModel"/>；**intrinsic-motion 狀態**（Jump／Roll）override 成自帶位移；
        /// 下方預設實作是**兩者皆非時的保底**（也是 model 未注入時的降級路徑）。
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
        /// 🆕（ADR-005）本狀態是否允許**被自己重入**。
        ///
        /// 預設 <c>false</c>——`EvaluateInterrupts` 依型別排除自己，那對 Idle／Move／Jump／Roll
        /// 是正確的（重入 Jump 沒有意義）。只有承載多份資料的狀態（<c>ActionState</c>）需要它：
        /// 兩個不同技能共用同一顆 state，型別排除會讓它們永遠無法互相打斷（FU-1）。
        ///
        /// ⚠️ 覆寫時**不得**引入新的准入來源——重入的判準必須與 <see cref="CanEnter"/> 同源，
        /// 否則就是 ADR-004 D2 禁止的第二個 gate 權威。
        /// </summary>
        public virtual bool CanReenter(PlayerRuntimeData data) => false;

        /// <summary>
        /// 控制目前狀態是否允許被「自然過渡」打斷。
        /// 預設為 true（如 Idle, Move）；有鎖定期的狀態（Jump, Roll）應複寫為 false 直到動作完成。
        /// </summary>
        public virtual bool CanTransitionAway => true;
    }
}