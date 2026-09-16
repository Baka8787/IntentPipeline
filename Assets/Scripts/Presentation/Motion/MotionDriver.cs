using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Motion
{
    public readonly struct TraversalExecutionResult
    {
        public readonly bool HasResult;
        public readonly Vector3 RequestedDisplacement;
        public readonly Vector3 ActualDisplacement;
        public readonly Vector3 BlockedDisplacement;
        public readonly CollisionFlags CollisionFlags;
        public readonly Vector3 ControllerCenter;
        public readonly float ControllerHeight;
        public readonly float ControllerRadius;
        public readonly float NormalizedTime;

        public TraversalExecutionResult(
            Vector3 requestedDisplacement,
            Vector3 actualDisplacement,
            CollisionFlags collisionFlags,
            Vector3 controllerCenter,
            float controllerHeight,
            float controllerRadius,
            float normalizedTime)
        {
            HasResult = true;
            RequestedDisplacement = requestedDisplacement;
            ActualDisplacement = actualDisplacement;
            BlockedDisplacement = requestedDisplacement - actualDisplacement;
            CollisionFlags = collisionFlags;
            ControllerCenter = controllerCenter;
            ControllerHeight = controllerHeight;
            ControllerRadius = controllerRadius;
            NormalizedTime = normalizedTime;
        }
    }

    public class MotionDriver : MonoBehaviour
    {
        private const float MaxCommittedFrameDisplacement = 10f;
        [Header("Setup")]
        [SerializeField] private CharacterController characterController;

        [Header("Procedural Move Speed")]
        [Tooltip("跑步（輸入強度=1）時的水平速度 (m/s)。可由下方 moveSpeedSource 於啟動時自動帶入動畫天生速度。")]
        [SerializeField] private float moveSpeed = 5f; // WASD 移動基礎速度 (m/s)

        // 🆕（v0.16.2）「動畫數據 → 配置」資料流：以最高速 clip（Fast Run）的烘焙代表速度作為 gameplay
        // 滿速的預設來源，讓「動畫天生跑多快」成為速度真相，根除腳步視覺與位移速度不一致的滑步。
        // 保留手動調整能力（dev-spec §3.2）：留空 = 純用手填 moveSpeed；勾 overrideMoveSpeed = 忽略來源。
        [Tooltip("移動速度的動畫數據來源：通常指向最高速 clip（如 Fast Run）的 Bake 資產。設定後，啟動時以其" +
                 "代表速度（GetRepresentativeSpeed）覆寫 moveSpeed。留空則純用上方手填值。此為『Bake 提供預設＋來源可追蹤』機制。")]
        [SerializeField] private MotionBakeData moveSpeedSource;

        [Tooltip("勾選 = 忽略 moveSpeedSource，強制使用上方手填的 moveSpeed（Designer 明確 override 動畫數據，如刻意的風格化快/慢）。")]
        [SerializeField] private bool overrideMoveSpeed = false;

        [Header("Physics Fallback")]
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float reboundForce = -2f; // 踩在地面時的固定貼地力

        [Header("Facing Execution")]
        // 朝向方向與「本幀該不該轉」都由 CharacterFacingSource 決定；此處只保留執行速率。
        [SerializeField, Min(0f)] private float aimFacingTurnSpeed = 10f;

        [Header("Dynamic Capsule Offset V2（pose-driven；docs/27 §12／docs/28）")]
        // 🆕 讓碰撞體覆蓋**角色上半身實際佔用的位置**，而不是把 Capsule 放大。
        // ⚠️ 這是 CharacterController.center 的**第二個**執行期寫入者
        //    （第一個是下方的 traversal collision profile）。兩者不得同時作用——
        //    guard 在 UpdateCapsuleOffset 與 BeginTraversalCollisionProfile 各有一道。
        [SerializeField] private CapsuleOffsetSettings capsuleOffset = CapsuleOffsetSettings.Disabled;

        // 🆕（V2）landmark 取樣需要 rig。**只在 Awake 用一次 `GetBoneTransform` 當查表**，
        //    之後持有 Transform，執行期不再碰任何 Animation API
        //    ⇒ 不違反「Controller 對 Animation API 零依賴」——我們拿的是骨架，不是動畫控制權。
        [Tooltip("姿勢取樣用的 Animator。留空則 Awake 時 GetComponentInChildren 自動補。")]
        [SerializeField] private Animator poseAnimator;

        private float _verticalVelocity;

        private Vector3 _requestedFacingDirection;
        private int _facingRequestFrame = -1;

        // ── Dynamic capsule offset（docs/25 W1）─────────────────────────────
        // authored 的 center 是唯一基準；偏移永遠是「基準 ＋ 平滑後的 local 位移」，
        // **絕不**在上一幀的結果上再疊——否則誤差會累積成永久漂移。
        private Vector3 _capsuleBaseCenter;
        private bool _capsuleBaseCenterCaptured;
        private float _capsuleBaseRadius;
        private float _capsuleBaseHeight;

        // 🔄（V2，2026-09-15）**五層管線**，每一層都有獨立的意義，debug 面板全部畫得出來：
        //   Desired  ＝ 姿勢解算想要的（不認識上限、不認識幾何體）
        //   Capped   ＝ 套用 MaxOffset 安全上限與轉向權重之後
        //   Smoothed ＝ 時間平滑的**自有狀態**
        //   Safe     ＝ 幾何夾持之後（＝真正寫進 center 的值）
        //   Current  ＝ 從 center 讀回來的實際值（與 Safe 不符 ⇒ 有第三個寫入者）
        //
        // ⚠️ **為什麼平滑要有自己的狀態，而不是平滑「夾持後的值」**
        //    若把夾持結果餵回平滑器，牆就成了平滑狀態的第二個 authority：
        //    貼牆時平滑值被壓到 0，牆一消失還要重新慢慢長回來。
        //    現在 `_capsuleSmoothedOffset` 只追 Capped，夾持是**輸出端**的最後一道，
        //    牆消失的當幀就恢復完整偏移。
        private Vector3 _capsuleDesiredOffset;
        private Vector3 _capsuleCappedOffset;
        private Vector3 _capsuleSmoothedOffset;
        private Vector3 _capsuleSafeOffset;
        private Vector3 _capsuleCurrentOffset;
        private Vector3 _capsuleOffsetVelocity;
        private float _capsuleYawPrevious;
        private int _capsuleYawFrame = -1;
        private float _capsuleTurnRate;

        // landmark 取樣緩衝：**一次配置、每幀複用** ⇒ 熱路徑零配置。
        private CapsuleLandmarkSample[] _capsuleLandmarkSamples;
        private Transform[] _capsuleLandmarkTransforms;
        private float[] _capsuleLandmarkRadii;
        private int _capsuleLandmarkCount;
        private bool _capsuleLandmarksResolved;
        private CapsulePoseSolveResult _capsuleSolveResult;
#if UNITY_EDITOR
        // Gizmo 只在 Editor 畫；黑板在 OnDrawGizmos 時拿不到，所以在求解時順手留一份。
        private Vector3 _debugLastMoveDirection;
        private float _debugLastMoveSpeed;
#endif

        /// <summary>DesiredOffset（姿勢解算希望的偏移，Root local）。尚未套上限、平滑、幾何夾持。</summary>
        public Vector3 CapsuleDesiredOffset => _capsuleDesiredOffset;

        /// <summary>CappedOffset（套用 MaxOffset 安全上限與轉向權重之後）。與 Desired 的差＝上限吃掉多少。</summary>
        public Vector3 CapsuleCappedOffset => _capsuleCappedOffset;

        /// <summary>SmoothedOffset（時間平滑的自有狀態）。與 Capped 的差＝平滑滯後。</summary>
        public Vector3 CapsuleSmoothedOffset => _capsuleSmoothedOffset;

        /// <summary>
        /// SafeOffset（碰撞限制後允許的偏移，Root local）。
        /// 與 <see cref="CapsuleSmoothedOffset"/> 的差距＝牆／障礙物吃掉了多少。
        /// </summary>
        public Vector3 CapsuleSafeOffset => _capsuleSafeOffset;

        /// <summary>CurrentOffset（目前實際寫在 <c>center</c> 上的偏移，Root local）。</summary>
        public Vector3 CapsuleCurrentOffset => _capsuleCurrentOffset;

        /// <summary>BaseCenter（作者設定的基準膠囊中心）。**執行期偏移不影響它**。</summary>
        public Vector3 CapsuleBaseCenter
        {
            get { CaptureCapsuleBaseCenter(); return _capsuleBaseCenter; }
        }

        /// <summary>Debug／驗收用：最近一次姿勢求解的完整結果（狀態、殘餘、被略過的 landmark 數）。</summary>
        public CapsulePoseSolveResult CapsuleSolveResult => _capsuleSolveResult;

        /// <summary>Debug／驗收用：實際參與求解的 landmark 數（profile 啟用且骨骼解析成功的）。</summary>
        public int CapsuleLandmarkCount => _capsuleLandmarkCount;

        /// <summary>Debug／驗收用：本幀 Root 的 yaw 角速度（度／秒，帶號）。</summary>
        public float CapsuleTurnRateDegreesPerSecond => _capsuleTurnRate;

        // 🆕（ADR-002）本次垂直運動採用的重力（負值＝向下）。
        // 預設等於序列化的 gravity；起跳時由 ApplyJumpLaunch 覆寫為「該段烘焙逆推重力」，
        // 落地（IsGrounded）時於 GetGravityThisFrame 內部自動回復預設。寫入者僅本類別，守住單一寫入者。
        private float _activeGravity;

        // 參考高階架構：單幀重力緩存，避免一幀內被多處呼叫時重複積分垂直重力速度
        private int _gravityFrame = -1;
        private Vector3 _cachedGravity;

        // 🆕（M2）上一影格的觸地狀態，供 SyncGroundedState 做落地/離地邊沿偵測
        // （JustLanded / JustLeftGround 的唯一觸發源比較基準）。
        private bool _wasGrounded;

        private bool _traversalCollisionProfileActive;
        private Vector3 _traversalOriginalCenter;
        private float _traversalOriginalHeight;
        private float _traversalOriginalRadius;
        private TraversalCollisionProfile _activeTraversalCollisionProfile;

        public TraversalExecutionResult LastTraversalExecutionResult { get; private set; }
        public bool IsTraversalCollisionProfileActive => _traversalCollisionProfileActive;
        public bool HasActiveTraversalPlan { get; private set; }
        public TraversalWarpPlan ActiveTraversalPlan { get; private set; }
        public float ActiveTraversalNormalizedTime { get; private set; }
        /// <summary>🆕（docs/23 §R1）Commit 當下的 plan 決策與理由，唯讀、只供觀測。</summary>
        public TraversalWarpPlanDiagnostics ActiveTraversalPlanDiagnostics { get; private set; }

        // 🆕（2026-09-13）**最後一次 traversal 的留存紀錄。**
        // `EndTraversalPresentation` 會清掉 Active*，所以 traversal 一結束就再也看不到執行期到底用了什麼。
        // 這三個欄位刻意**不清除**，用來回答「執行期用的 plan 跟離線算的是不是同一個」。
        public TraversalWarpPlan LastCommittedTraversalPlan { get; private set; }
        public TraversalWarpPlanDiagnostics LastCommittedTraversalDiagnostics { get; private set; }
        /// <summary>整段 traversal 中「要求位移 − 實際位移」的最大值（m）。>0 代表被碰撞擋過。</summary>
        public float LastTraversalMaxBlockedDisplacement { get; private set; }
        public float LastTraversalMaxNormalizedTime { get; private set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private bool drawTraversalMotionDebug = false;
        /// <summary>
        /// Game View 的 <c>TextMesh</c> 面板。**預設關閉**——traversal 的診斷資訊量會直接蓋住
        /// 正在觀察的畫面。數值改看 Window ▸ Project ▸ Traversal Debug；空間關係看 Scene View。
        /// 這是新欄位，既有 prefab 沒有序列化它 ⇒ 一律吃這裡的 false。
        /// </summary>
        [SerializeField] private bool drawTraversalPanelText = false;

        /// <summary>
        /// 🆕（2026-09-15）Capsule Offset 三層圓環的**獨立顯示開關**。
        ///
        /// ⚠️ **與 `capsuleOffset.Enabled` 是兩件事**：那個管「功能要不要作用」，
        /// 這個只管「要不要畫」。舊版用 `Enabled && isPlaying` 當繪製條件，於是
        /// ①功能開著就強制畫、②功能關掉就完全看不到（而「為什麼沒有偏移」正是需要看的時候）。
        /// 這是新欄位，既有 prefab 沒有序列化它 ⇒ 一律吃這裡的 false。
        /// </summary>
        [SerializeField] private bool drawCapsuleOffsetDebug = true;      // 🔄 2026-09-15 預設開啟（使用者裁決）

        private TraversalWarpPlan _debugTraversalPlan;
        private TextMesh _debugTraversalRuntimeLabel;

        /// <summary>
        /// 供 <c>RuntimeDebugPanel</c> 遠端切換。⚠️ **旗標的真相仍住在本元件**——
        /// Panel 只是一個遙控器，它不持有狀態、也不因為被關掉而改變這裡的值。
        /// </summary>
        internal bool DebugDrawCapsuleOffset
        {
            get => drawCapsuleOffsetDebug;
            set => drawCapsuleOffsetDebug = value;
        }
#endif

        /// <summary>
        /// 🆕（2026-07-27）本帧沒有時間流逝（實務上＝<c>Time.timeScale == 0</c> 的暫停）。
        /// 為 true 時**整個位移結算跳過**：不呼叫 <c>Move</c>、不重算觸地與單幀邊沿旗標。
        ///
        /// **為什麼需要這道守衛（真實 bug，非防禦性程式碼）**：
        /// 位移出口是 <c>Move(finalMovement * Time.deltaTime)</c>，<c>deltaTime = 0</c> 時等同
        /// <c>Move(Vector3.zero)</c>。而 Unity 的 <c>CharacterController.isGrounded</c> 是由
        /// **上一次 Move 有沒有向下撞到東西**決定的——零位移沒有向下推，於是回報 **false**。
        /// 症狀是連鎖的：暫停第 2 帧 <c>JustLeftGround</c> 假觸發、暫停期間 <c>IsGrounded</c> 恆 false、
        /// 解除暫停後角色「重新著地」→ **<c>JustLanded</c> 假觸發 → 站在地上暫停再解除就會聽到落地聲**
        /// （2026-07-27 實測確認）。
        ///
        /// ⚠️ 本守衛刻意表述為「**沒有時間流逝**」而不是「暫停」——<c>MotionDriver</c> 不認識暫停，
        /// 它只知道「沒有時間就沒有東西要積分、沒有位移要結算、也沒有邊沿可偵測」。
        /// 這讓守衛對任何造成 <c>deltaTime == 0</c> 的原因都成立，且不引入對應用層的依賴。
        ///
        /// 📌 **連帶效應（已知且刻意）**：修好之後 <c>IsGrounded</c> 在暫停期間會正確地維持 true，
        /// 於是 <c>JumpState.CanEnter</c>（＝<c>JumpRequested &amp;&amp; IsGrounded</c>）會成立。
        /// 原本「暫停中按跳躍不會跳」靠的正是上述 bug 的副作用，**不是任何設計**。
        /// 該缺口改由 <c>GamePauseController</c> 以 <c>IArbiterSource</c> 要求 <c>BlockInput</c> 正式關閉。
        /// </summary>
        private static bool IsTimeFrozen => Time.deltaTime <= 0f;

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

            // 🔒 **authored 幾何必須在 Awake 擷取**（V2 不變量）。
            //    任何動態偏移最早也要到第一次 LateUpdate 才寫 center，所以 Awake 讀到的必定是
            //    prefab／場景上 authored 的值。惰性擷取（V1 的作法）在「先被別人改過 center
            //    才第一次呼叫」時會把偏移後的位置誤當基準——2026-09-15 的 C11 測試就是踩這個。
            CaptureCapsuleBaseCenter();

            if (poseAnimator == null) poseAnimator = GetComponentInChildren<Animator>();

            // 🆕（v0.16.2）動畫數據 → 配置：以烘焙代表速度覆寫 gameplay 滿速（Designer 未 override 且有指定來源時）。
            // 唯一寫入時機在啟動，之後 moveSpeed 就是一般序列化欄位——不改變執行期熱路徑、不新增每幀成本。
            if (moveSpeedSource != null && !overrideMoveSpeed)
            {
                float sourceSpeed = moveSpeedSource.GetRepresentativeSpeed();
                if (sourceSpeed > 0f)
                {
                    moveSpeed = sourceSpeed;
                }
#if UNITY_EDITOR
                else
                {
                    Debug.LogWarning($"[{gameObject.name}] MotionDriver.moveSpeedSource（'{moveSpeedSource.name}'）的代表速度為 0，" +
                        "無法作為速度來源（SpeedCurve 為空或該 clip 非移動動畫）。維持手填 moveSpeed，請確認來源是否指對最高速 clip。", this);
                }
#endif
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
        /// 提交僅限當帧的世界空間朝向請求。request 留在 MotionDriver 內結算，讓角色 rotation
        /// 仍只有一個寫入者；壓平則避免 AimPoint 的高度把角色 root 帶出水平面。
        /// </summary>
        public void RequestFacing(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            _requestedFacingDirection = worldDirection.sqrMagnitude > 0f
                ? worldDirection.normalized
                : Vector3.zero;
            _facingRequestFrame = Time.frameCount;
        }

        /// <summary>
        /// 🚀 【大一統完全體：順序 6a】基礎常規運動
        /// 融合了 procedural 水平轉向、世界方向位移、以及世界重力結算
        /// </summary>
        public void ExecuteBaseMovement(PlayerRuntimeData data)
        {
            if (IsTimeFrozen) return; // 見 IsTimeFrozen 的說明：零位移的 Move 會毀掉 isGrounded

            ApplyFacingRequest();

            // 🆕（docs/27 §12）V2 直接讀當幀骨架 ⇒ 不管位移來自哪個 source 都成立。
            // 必須在 Move **之前**：偏移後的膠囊才是這一幀真正參與碰撞的形狀。
            UpdateCapsuleOffset(data);

            Vector3 horizontalVelocity = Vector3.zero;

            // Movement Output 已是水平、正規化的世界方向；MotionDriver 只負責照著積分。
            if (data.MoveDirection.sqrMagnitude > 0.001f)
            {
                float currentSpeed = data.MoveSpeed * moveSpeed;
                horizontalVelocity = data.MoveDirection * currentSpeed;
            }

            // 疊加單幀快取重力（保持原有的自由落體與貼地力優化），同時同步觸地狀態回黑板
            Vector3 finalMovement = horizontalVelocity + GetGravityThisFrame(data);

            // 總出口呼叫
            characterController.Move(finalMovement * Time.deltaTime);
        }

        /// <summary>
        /// 只執行朝向、垂直重力、grounded 同步與 CharacterController collision，不施加
        /// Movement Output 的水平速度。首個使用者是 JumpState 的 HardRecovery；本方法不認識
        /// Jump 或 recovery 語意，且 Transform 寫入仍集中在 MotionDriver。
        /// </summary>
        public void ExecuteVerticalOnlyMovement(PlayerRuntimeData data)
        {
            if (IsTimeFrozen) return; // 見 IsTimeFrozen 的說明：零位移的 Move 會毀掉 isGrounded

            ApplyFacingRequest();

            // 本路徑不施加水平速度，但姿勢仍然存在 ⇒ V2 照常求解（空中的軀幹一樣會前傾）。
            UpdateCapsuleOffset(data);

            // 疊加單幀快取重力（保持原有的自由落體與貼地力優化），同時同步觸地狀態回黑板
            Vector3 finalMovement = GetGravityThisFrame(data);

            // 總出口呼叫
            characterController.Move(finalMovement * Time.deltaTime);
        }

        /// <summary>
        /// 🚀 【順序 6b】特殊動作曲線運動：直接使用離線烘焙曲線驅動角色位移與旋轉（如 Roll）
        /// </summary>
        public void ExecuteBakedCurveMovement(MotionBakeData bakeData, float normalizedTime, PlayerRuntimeData data)
        {
            if (bakeData == null) return;
            if (IsTimeFrozen) return; // 同 ExecuteBaseMovement，見 IsTimeFrozen 的說明

            ApplyFacingRequest();

            // committed motion（Roll／Action／Hurt）：V2 讀骨架，所以這些路徑**照常**求解。
            // ⛔ V1 在此歸零，理由是「MoveDirection 不代表 baked curve 的位移」——V2 不用 MoveDirection，該理由消失。
            UpdateCapsuleOffset(data);

            float currentTime = normalizedTime * bakeData.Duration;
            float previousTime = Mathf.Max(0f, currentTime - Time.deltaTime);
            ExecuteBakedCurveMovementAtTimes(bakeData, currentTime, previousTime, false, data);
        }

        /// <summary>
        /// 以前後播放頭驅動烘焙曲線；TransitionAsset 的播放倍率已包含在 playhead delta 內。
        /// </summary>
        public void ExecuteBakedCurveMovement(
            MotionBakeData bakeData,
            float normalizedTime,
            float previousNormalizedTime,
            PlayerRuntimeData data)
        {
            if (bakeData == null) return;
            if (IsTimeFrozen) return;

            ApplyFacingRequest();

            // 同上：V2 讀骨架，committed motion 期間照常求解。
            UpdateCapsuleOffset(data);

            float currentTime = Mathf.Max(0f, normalizedTime * bakeData.Duration);
            float previousTime = Mathf.Clamp(previousNormalizedTime * bakeData.Duration, 0f, currentTime);
            ExecuteBakedCurveMovementAtTimes(bakeData, currentTime, previousTime, true, data);
        }

        public void BeginTraversalCollisionProfile(in TraversalCollisionProfile profile)
        {
            if (characterController == null) return;
            RestoreTraversalCollisionProfile();

            // ⚠️ **必須先把動態偏移收乾淨，才能擷取 traversal 的基準 center。**
            //    否則 `_traversalOriginalCenter` 會把當下的 locomotion 偏移一起吃進去，
            //    整段 traversal 都建立在一個偏掉的基準上，而且 `RestoreTraversalCollisionProfile`
            //    之後會把那個偏移「還原」成永久值。這是兩個 center 寫入者唯一會真正撞在一起的點。
            ResetCapsuleOffsetImmediate();

            _traversalOriginalCenter = characterController.center;
            _traversalOriginalHeight = characterController.height;
            _traversalOriginalRadius = characterController.radius;
            _activeTraversalCollisionProfile = profile;
            _traversalCollisionProfileActive = true;
        }

        // =====================================================================
        // Dynamic Capsule Offset V2 —— pose-driven（docs/27 §12；白話教學見 docs/28）
        //
        // 資料流（每一層都有獨立意義，debug 面板全部畫得出來）：
        //
        //   當幀姿勢 → landmark 取樣 → Pose Solver → Safety Cap → Smoothing
        //                                                              → Collision Clamp
        //                                                                  → center
        //
        // ⭐ **為什麼 Clamp 一定要在 Smoothing 之後（順序裁決，2026-09-15）**
        //    Clamp 是唯一認識場景幾何的一層。任何排在它**後面**的處理，都可能把已經夾好的值
        //    再推回幾何體裡。V1 的順序是 Clamp → Smooth，於是走向牆時目標被夾小、
        //    但平滑值還停在上一幀的大值 ⇒ **實際寫進 center 的值大於安全值**，膠囊進牆。
        //    V2 改成 Smooth → Clamp：寫進 center 的永遠是剛剛夾過的那個值。
        // =====================================================================

        /// <summary>
        /// 把碰撞體移到「角色上半身實際佔用的位置」。
        ///
        /// <para><b>🔄 V2 與 V1 最大的差別：不再需要「誰有位移 authority」這個參數</b></para>
        /// V1 用 <c>MoveDirection</c> 猜姿勢，所以只有 locomotion 路徑能用，Roll／Action 一律歸零。
        /// V2 直接**讀當幀骨架**——不管位移是 locomotion、baked curve 還是 action motion，
        /// 骨架上擺出來的就是真的姿勢。⇒ 參數消失，不是被忽略。
        ///
        /// <para><b>guard 只剩三道，每一道都有現在仍然成立的理由</b></para>
        /// <list type="number">
        /// <item><b>traversal collision profile 正在持有 center</b> ⇒ 直接 return。
        /// 兩個寫入者同幀寫同一個欄位＝後寫的贏，行為取決於呼叫順序。
        /// traversal 擁有整顆膠囊（center/height/radius）的 authority，姿勢偏移必須讓路。</item>
        /// <item><b>停用或 profile 不可用</b> ⇒ 還原一次 authored center，不留殘值。</item>
        /// <item><b>死亡</b>（且 profile 未開放）⇒ 倒地姿勢下「上半身佔用位置」與站立膠囊
        /// 已經不是同一回事，解出來的偏移沒有意義。這是唯一仍按**狀態**關閉的 guard，
        /// 理由是姿勢本身離開了模型的適用範圍，不是「那個狀態比較特別」。</item>
        /// </list>
        /// ⛔ V1 的「不在地面 ⇒ 歸零」與「committed motion ⇒ 歸零」**已刪除**：
        /// 兩者都是「MoveDirection 在這些情況下不代表姿勢」的補丁，V2 不用 MoveDirection，補丁失去理由。
        /// </summary>
        private void UpdateCapsuleOffset(PlayerRuntimeData data)
        {
            if (characterController == null) return;

            // Guard ①：traversal 擁有整顆膠囊。
            if (_traversalCollisionProfileActive) return;

            CaptureCapsuleBaseCenter();

            CapsulePoseProfileSO profile = capsuleOffset.Profile;

            // Guard ②：停用／缺 profile／profile 不可用 ⇒ 不留殘值。
            if (!capsuleOffset.Enabled || profile == null || !profile.IsUsable())
            {
                ClearCapsuleOffsetState();
                return;
            }

            // Guard ③：死亡。
            if (data != null && data.Survivability.IsDead && !profile.ApplyWhileDead)
            {
                // ⚠️ 不是硬歸零——那會讓屍體的碰撞體瞬間彈回去。走正常的平滑收回路徑。
                UpdateCapsuleOffsetTowards(Vector3.zero, profile);
                return;
            }

            float deltaTime = Time.deltaTime;
            if (!IsFinite(deltaTime) || deltaTime <= 0f) return;

            UpdateCapsuleTurnRate(deltaTime);
            ResolveCapsuleLandmarks(profile);

#if UNITY_EDITOR
            // MoveDirection 在 V2 只剩 debug 對照用途——gizmo 會把舊模型的目標畫成虛線。
            // ⚠️ 這裡的 `#if` 必須與欄位宣告處**完全一致**（兩者都是 UNITY_EDITOR）。
            //    2026-09-15 踩過：宣告 UNITY_EDITOR、使用 UNITY_EDITOR||DEVELOPMENT_BUILD
            //    ⇒ Editor 與 `dotnet build` 都過（兩者都定義 UNITY_EDITOR），
            //    只有 Development Player build 會炸。
            _debugLastMoveDirection = data != null ? data.MoveDirection : Vector3.zero;
            _debugLastMoveSpeed = data != null ? data.MoveSpeed : 0f;
#endif

            // ── ① Pose Solver：解出最小必要修正 ────────────────────────────────
            _capsuleDesiredOffset = SolveCapsulePoseOffset();

            // ── ② Safety Cap：MaxOffset 是上限，不是驅動量 ────────────────────
            Vector2 capped = CapsulePoseSolver.ApplySafetyCap(
                new Vector2(_capsuleDesiredOffset.x, _capsuleDesiredOffset.z), profile.MaxOffset);

            // 轉向權重：見 CapsulePoseProfileSO.maximumTurnRateDegreesPerSecond 的說明。
            // clamp 只沿「偏移方向」掃一次，看不到角色原地轉身時膠囊繞 root 掃出的那道弧線。
            capped *= ComputeCapsuleTurnWeight(profile);

            UpdateCapsuleOffsetTowards(new Vector3(capped.x, 0f, capped.y), profile);
        }

        /// <summary>
        /// ③ 平滑 → ④ 幾何夾持 → ⑤ 寫入。抽出來是因為死亡路徑要走同一條收回管線。
        /// </summary>
        private void UpdateCapsuleOffsetTowards(Vector3 cappedOffset, CapsulePoseProfileSO profile)
        {
            float deltaTime = Time.deltaTime;
            if (!IsFinite(deltaTime) || deltaTime <= 0f) return;

            _capsuleCappedOffset = cappedOffset;

            // ── ③ Temporal Smoothing ──────────────────────────────────────────
            // ⚠️ 平滑器追的是 Capped，**不是**夾持後的 Safe。見欄位宣告處的說明：
            //    餵回 Safe 會讓牆變成平滑狀態的第二個 authority。
            bool releasing = _capsuleCappedOffset.sqrMagnitude < _capsuleSmoothedOffset.sqrMagnitude;
            float smoothTime = releasing ? profile.ReleaseSmoothTime : profile.SmoothTime;

            _capsuleSmoothedOffset = smoothTime > 0f
                ? Vector3.SmoothDamp(
                    _capsuleSmoothedOffset, _capsuleCappedOffset,
                    ref _capsuleOffsetVelocity, smoothTime, Mathf.Infinity, deltaTime)
                : _capsuleCappedOffset;

            if (!IsFinite(_capsuleSmoothedOffset))
            {
                ClearCapsuleOffsetState();
                return;
            }

            // ── ④ Collision-aware Clamp（最後一道，因為只有它認識場景）────────
            _capsuleSafeOffset = ClampCapsuleOffsetAgainstGeometry(_capsuleSmoothedOffset);
            _capsuleCurrentOffset = _capsuleSafeOffset;

            // ── ⑤ 寫入。永遠是「基準 ＋ 偏移」，不是「上一幀 ＋ 差值」⇒ 誤差不累積。
            //    y 維持基準值：改變膠囊底面高度會動到接地判定 ⇒ FootIKController 的二值閘門
            //    （IsGrounded && !BlockIK，無淡入）一次誤判就是可見的腳部彈跳。
            //    ⛔ center.y 本輪明確不動（使用者裁決）；height 的問題見 docs/27 §12.6。
            characterController.center = new Vector3(
                _capsuleBaseCenter.x + _capsuleCurrentOffset.x,
                _capsuleBaseCenter.y,
                _capsuleBaseCenter.z + _capsuleCurrentOffset.z);
        }

        /// <summary>
        /// 取樣當幀骨架並求解。取樣點是 <b>Root local</b>，與 <c>CharacterController.center</c> 同座標系。
        ///
        /// <para><b>時序為什麼成立</b></para>
        /// 本方法由 <c>ExecuteBaseMovement</c> 等在**管線順序 6（LateUpdate）**呼叫，
        /// 而 Animator 的評估發生在 Update 與 LateUpdate **之間**
        /// ⇒ 讀到的骨架就是本幀已經擺好的姿勢，不需要任何新的時序安排。
        /// （Foot IK 在順序 6.5 才改骨骼 ⇒ 我們讀到的是 pre-IK 姿勢，對軀幹而言正確。）
        /// </summary>
        private Vector3 SolveCapsulePoseOffset()
        {
            if (_capsuleLandmarkCount <= 0)
            {
                _capsuleSolveResult = CapsulePoseSolveResult.Invalid;
                return Vector3.zero;
            }

            for (int i = 0; i < _capsuleLandmarkCount; i++)
            {
                Transform bone = _capsuleLandmarkTransforms[i];
                if (bone == null) continue;
                _capsuleLandmarkSamples[i] = new CapsuleLandmarkSample(
                    transform.InverseTransformPoint(bone.position), _capsuleLandmarkRadii[i]);
            }

            var geometry = new AuthoredCapsuleGeometry(
                _capsuleBaseCenter, _capsuleBaseRadius, _capsuleBaseHeight);

            _capsuleSolveResult = CapsulePoseSolver.Solve(
                _capsuleLandmarkSamples, _capsuleLandmarkCount, geometry);

            return new Vector3(_capsuleSolveResult.Offset.x, 0f, _capsuleSolveResult.Offset.y);
        }

        /// <summary>
        /// 把 profile 的 <c>HumanBodyBones</c> 解析成實際 Transform。**只做一次**。
        ///
        /// ⛔ 執行期不再呼叫任何 Animation API——這裡把 Animator 當**骨架查表**用，
        /// 用完就只持有 Transform。這與「Controller 對 Animation API 零依賴」不衝突：
        /// 我們沒有讀動畫狀態、沒有驅動播放，只是問「Head 這根骨頭在哪個 Transform」。
        /// </summary>
        private void ResolveCapsuleLandmarks(CapsulePoseProfileSO profile)
        {
            if (_capsuleLandmarksResolved) return;
            _capsuleLandmarksResolved = true;
            _capsuleLandmarkCount = 0;

            if (poseAnimator == null) poseAnimator = GetComponentInChildren<Animator>();
            if (poseAnimator == null || !poseAnimator.isHuman)
            {
#if UNITY_EDITOR
                Debug.LogWarning(
                    $"[{gameObject.name}] MotionDriver: capsule pose profile 已指派，但找不到 humanoid Animator" +
                    " ⇒ dynamic capsule offset 不會作用（安全退化為 authored center）。", this);
#endif
                return;
            }

            int declared = profile.LandmarkCount;
            int capacity = Mathf.Min(declared, CapsulePoseSolver.MaxLandmarks);
            if (capacity <= 0) return;

            if (_capsuleLandmarkTransforms == null || _capsuleLandmarkTransforms.Length < capacity)
            {
                _capsuleLandmarkTransforms = new Transform[capacity];
                _capsuleLandmarkRadii = new float[capacity];
                _capsuleLandmarkSamples = new CapsuleLandmarkSample[capacity];
            }

            for (int i = 0; i < declared && _capsuleLandmarkCount < capacity; i++)
            {
                CapsulePoseProfileSO.LandmarkEntry entry = profile.GetLandmark(i);
                if (!entry.Enabled || entry.FleshRadius <= 0f) continue;

                Transform bone = poseAnimator.GetBoneTransform(entry.Bone);
                if (bone == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning(
                        $"[{gameObject.name}] MotionDriver: rig 沒有 {entry.Bone}，該 landmark 被略過。", this);
#endif
                    continue;
                }

                _capsuleLandmarkTransforms[_capsuleLandmarkCount] = bone;
                _capsuleLandmarkRadii[_capsuleLandmarkCount] = entry.FleshRadius;
                _capsuleLandmarkCount++;
            }
        }

        /// <summary>
        /// 轉向權重：角速度越高，偏移越收。**連續**（線性淡出），不是開關 ⇒ 不會造成 snap。
        /// </summary>
        private float ComputeCapsuleTurnWeight(CapsulePoseProfileSO profile)
        {
            float limit = profile.MaximumTurnRateDegreesPerSecond;
            if (limit <= 0f || !IsFinite(_capsuleTurnRate)) return 1f;
            return 1f - Mathf.Clamp01(Mathf.Abs(_capsuleTurnRate) / limit);
        }

        /// <summary>authored 幾何只擷取一次；它是所有偏移的唯一基準。</summary>
        private void CaptureCapsuleBaseCenter()
        {
            if (_capsuleBaseCenterCaptured || characterController == null) return;
            _capsuleBaseCenter = characterController.center;
            _capsuleBaseRadius = characterController.radius;
            _capsuleBaseHeight = characterController.height;
            _capsuleBaseCenterCaptured = true;
        }

        /// <summary>立刻把 center 還原成 authored 基準並清掉所有中間層狀態。</summary>
        private void ResetCapsuleOffsetImmediate()
        {
            if (characterController == null) return;
            CaptureCapsuleBaseCenter();
            ClearCapsuleOffsetState();
        }

        private void ClearCapsuleOffsetState()
        {
            _capsuleDesiredOffset = Vector3.zero;
            _capsuleCappedOffset = Vector3.zero;
            _capsuleSmoothedOffset = Vector3.zero;
            _capsuleSafeOffset = Vector3.zero;
            _capsuleCurrentOffset = Vector3.zero;
            _capsuleOffsetVelocity = Vector3.zero;
            _capsuleSolveResult = CapsulePoseSolveResult.Invalid;
            if (characterController != null) characterController.center = _capsuleBaseCenter;
        }

        /// <summary>
        /// 🆕（2026-09-15）**collision-aware clamp**：把期望偏移縮到「不會讓膠囊與幾何體重疊」的最大值。
        ///
        /// <para><b>⭐ 為什麼取代 `touchingWall → zero`</b></para>
        /// 舊 guard 問的是「**要不要**偏移」，答案在貼牆時是「不要」
        /// ⇒ 偏移只在空曠處（不需要）存在，貼牆時（需要）消失。
        /// 這裡問的是「**能偏多少**」——同樣不會把膠囊推進幾何體，但保留了貼牆那一刻的前方覆蓋。
        ///
        /// <para><b>從 base center 掃，不是從目前位置</b></para>
        /// 每幀都重新回答「從 authored 基準出發能走多遠」⇒ **無狀態**，不會累積誤差，
        /// 也不需要處理「上一幀的偏移是怎麼來的」。
        ///
        /// <para><b>⚠️ cast 半徑刻意縮 `skinWidth`</b></para>
        /// `CharacterController` 本來就允許 `skinWidth` 的互相穿透；用原半徑去掃，
        /// 角色**貼著牆站定時起點就已重疊**，cast 會回 `distance = 0` ⇒ 又變回舊行為。
        /// 縮一個 skin 之後，「貼牆」在幾何上不再算重疊，掃得出真正的可行距離。
        ///
        /// <para><b>排除自己</b></para>
        /// 角色自己的 `CharacterController` 也是 collider，必須排除，否則每次都命中自己。
        /// v1 直接排除**自身 layer**（Player=6／Enemy=7 分屬不同 layer，因此玩家仍會被敵人夾住）。
        /// ⛔ 尚未開放 authored layer mask——第二個需求出現前不建（trigger 已由
        /// <c>QueryTriggerInteraction.Ignore</c> 擋掉，近戰 hitbox 因此不影響）。
        ///
        /// <para><b>零配置</b></para>
        /// `Physics.CapsuleCast` 的 <c>out RaycastHit</c> 多載不配置；每角色每幀一次。
        /// </summary>
        private Vector3 ClampCapsuleOffsetAgainstGeometry(Vector3 desiredOffset)
        {
            if (characterController == null) return Vector3.zero;

            // 只取水平分量：center.y 永不改（見 UpdateCapsuleOffset 末端的賦值）。
            Vector3 planarOffset = new Vector3(desiredOffset.x, 0f, desiredOffset.z);
            if (planarOffset.sqrMagnitude <= CapsuleClampEpsilonSqr) return Vector3.zero;

            Vector3 worldBase = transform.TransformPoint(_capsuleBaseCenter);
            Vector3 worldTarget = transform.TransformPoint(_capsuleBaseCenter + planarOffset);
            Vector3 worldDelta = worldTarget - worldBase;
            float requestedDistance = worldDelta.magnitude;
            if (requestedDistance <= CapsuleClampEpsilon) return Vector3.zero;

            // ⚠️ 用 **authored** 半徑／高度，不是 live 值：live 值可能正被 traversal profile 改著。
            float radius = _capsuleBaseRadius;
            float castRadius = Mathf.Max(0.01f, radius - characterController.skinWidth);
            // CharacterController 的膠囊恆為世界直立 ⇒ 兩顆球心沿 world up 分開。
            float halfCylinder = Mathf.Max(0f, _capsuleBaseHeight * 0.5f - radius);
            Vector3 top = worldBase + Vector3.up * halfCylinder;
            Vector3 bottom = worldBase - Vector3.up * halfCylinder;

            int mask = ~(1 << gameObject.layer);
            float allowedDistance = requestedDistance;

            if (Physics.CapsuleCast(
                    top, bottom, castRadius,
                    worldDelta / requestedDistance,
                    out RaycastHit hit,
                    requestedDistance,
                    mask,
                    QueryTriggerInteraction.Ignore))
            {
                // 起點就重疊時 Unity 回 distance = 0 ⇒ 夾成 0（維持在基準位置）。
                // 那是安全的退化，且與導入本機制之前的行為相同。
                allowedDistance = hit.distance;
            }

            return CapsuleOffsetSolver.ClampToAllowedDistance(
                planarOffset, allowedDistance, requestedDistance);
        }

        private const float CapsuleClampEpsilon = 0.0001f;
        private const float CapsuleClampEpsilonSqr = CapsuleClampEpsilon * CapsuleClampEpsilon;

        private void UpdateCapsuleTurnRate(float deltaTime)
        {
            float yaw = transform.eulerAngles.y;
            if (_capsuleYawFrame < 0)
            {
                _capsuleTurnRate = 0f;
            }
            else
            {
                // DeltaAngle 處理 0/360 環繞——直接相減會在過零點產生 359 度／幀的假尖峰。
                _capsuleTurnRate = Mathf.DeltaAngle(_capsuleYawPrevious, yaw) / deltaTime;
            }

            _capsuleYawPrevious = yaw;
            _capsuleYawFrame = Time.frameCount;
        }

        public void ApplyTraversalCollisionProfile(float normalizedTime)
        {
            if (!_traversalCollisionProfileActive || characterController == null) return;
            // C1 is the first active channel. C2/C3 remain explicit serialized seams until
            // play evidence proves they are required.
            characterController.center = _traversalOriginalCenter +
                                         _activeTraversalCollisionProfile.EvaluateCenterOffset(normalizedTime);
        }

        /// <summary>
        /// 🆕（2026-09-14，respawn）**唯一的瞬間移動入口。**
        ///
        /// <para><b>⭐ 為什麼傳送必須住在 MotionDriver</b></para>
        /// 本類別是角色 position／rotation 的**單一寫入者**——全專案的位移都走
        /// <c>characterController.Move()</c>，沒有任何地方直接寫 <c>transform.position</c>。
        /// 若讓 <c>RespawnController</c> 自己搬 Transform，那個不變量當場就有第二個寫入者。
        /// ⇒ 呼叫端只說「把我放到那裡」，怎麼放是這裡的事。
        ///
        /// <para><b>⚠️ 必須先停用 CharacterController</b></para>
        /// 它持有自己的內部位置並會在下一次 <c>Move</c> 把 Transform 夾回去；直接寫
        /// <c>transform.position</c> 會被靜默吃掉或殘留舊的 collision 解算。
        ///
        /// <para><b>順帶清掉的跨幀狀態（都是「傳送後還留著就會立刻出事」的那種）</b></para>
        /// <list type="bullet">
        /// <item><b>垂直速度</b>：墜落致死時它是一個很大的負值。不清掉 ⇒ 重生後第一幀就以墜落速度往下衝。</item>
        /// <item><b>動態膠囊偏移</b>：偏移是相對 authored 基準的平滑值，換地點後那個平滑沒有意義。</item>
        /// <item><b>traversal collision profile</b>：死在翻越途中時膠囊還套著 traversal 的尺寸，
        /// 不還原會帶著一顆錯尺寸的膠囊重生。</item>
        /// <item><b>facing request</b>：丟掉上一幀殘留的朝向請求，避免傳送後立刻被轉回死前的方向。</item>
        /// </list>
        ///
        /// <para><b>⛔ 刻意不碰的</b></para>
        /// <c>_wasGrounded</c> **不重設**：它是傳送**前**的接地真相，下一幀的邊沿偵測因此仍回答
        /// 「我是不是到地上了」——那正是 <c>JustLanded</c> 的語意。硬清成 false 反而會在
        /// 原地傳送時偽造一次落地。
        /// <c>MoveSpeed</c>／平滑器也不碰：那是 <c>IMovementModel</c> 的內部狀態，
        /// 為了重生去動核心驅動介面並不划算——死亡期間輸入已被歸零，順序 3 每幀無條件推進的
        /// B9 減速本來就會把它收到 0。
        /// </summary>
        public void Teleport(Vector3 worldPosition, Quaternion worldRotation)
        {
            // ⚠️ 只有「停用 ／ 還原 CharacterController」這一步需要它存在；
            //    **下面的跨幀狀態清理無論如何都要跑**。
            //    早期版本在這裡 early-return，於是沒有 CharacterController 的退化配置會被傳送到新位置
            //    卻**留著死前的垂直速度**——而那正是這個方法存在的理由之一。
            if (characterController != null)
            {
                bool wasEnabled = characterController.enabled;
                characterController.enabled = false;
                transform.SetPositionAndRotation(worldPosition, worldRotation);
                characterController.enabled = wasEnabled;
            }
            else
            {
                transform.SetPositionAndRotation(worldPosition, worldRotation);
            }

            RestoreTraversalCollisionProfile();
            ResetCapsuleOffsetImmediate();

            _verticalVelocity = 0f;
            _activeGravity = gravity;
            _gravityFrame = -1;   // 強制下一幀重新結算重力，不沿用傳送前的單幀快取

            _requestedFacingDirection = Vector3.zero;
            _facingRequestFrame = -1;
        }

        public void RestoreTraversalCollisionProfile()
        {
            if (!_traversalCollisionProfileActive || characterController == null) return;
            characterController.center = _traversalOriginalCenter;
            characterController.height = _traversalOriginalHeight;
            characterController.radius = _traversalOriginalRadius;
            _activeTraversalCollisionProfile = default;
            _traversalCollisionProfileActive = false;
        }

        /// <summary>
        /// Committed 動作的 3D 曲線位移。水平沿用播放頭 delta 換速；垂直直接消費
        /// VerticalCurve 的前後位移差，期間不積分重力。TraversalState 是 gameplay 呼叫端。
        /// bake 缺失時仍走同一條 committed 同步路徑，但不移動也不退回普通重力。
        /// </summary>
        public void ExecuteCommittedCurveMovement(
            MotionBakeData bakeData,
            float normalizedTime,
            float previousNormalizedTime,
            PlayerRuntimeData data)
        {
            if (IsTimeFrozen) return;

            ApplyFacingRequest();

            if (bakeData == null)
            {
                _verticalVelocity = 0f;
                SyncGroundedState(data);
                return;
            }

            if (!IsFinite(normalizedTime) || !IsFinite(previousNormalizedTime) ||
                !IsFinite(bakeData.Duration) || bakeData.Duration <= 0f)
            {
                _verticalVelocity = 0f;
                SyncGroundedState(data);
                return;
            }

            float currentTime = Mathf.Max(0f, normalizedTime * bakeData.Duration);
            float previousTime = Mathf.Clamp(previousNormalizedTime * bakeData.Duration, 0f, currentTime);
            float clipDeltaTime = Mathf.Max(0f, currentTime - previousTime);

            float previousSpeed = bakeData.GetSpeedAt(previousTime);
            float currentSpeed = bakeData.GetSpeedAt(currentTime);
            float previousVertical = bakeData.GetVerticalAt(previousTime);
            float currentVertical = bakeData.GetVerticalAt(currentTime);
            float currentYaw = bakeData.GetRotationAt(currentTime);
            float previousYaw = bakeData.GetRotationAt(previousTime);
            if (!IsFinite(previousSpeed) || !IsFinite(currentSpeed) ||
                !IsFinite(previousVertical) || !IsFinite(currentVertical) ||
                !IsFinite(currentYaw) || !IsFinite(previousYaw))
            {
                _verticalVelocity = 0f;
                SyncGroundedState(data);
                return;
            }

            float averageClipSpeed = 0.5f * (previousSpeed + currentSpeed);
            float worldSpeed = clipDeltaTime > 0f
                ? averageClipSpeed * (clipDeltaTime / Time.deltaTime)
                : 0f;
            Vector3 horizontalVelocity = transform.forward * worldSpeed;

            _verticalVelocity = (currentVertical - previousVertical) / Time.deltaTime;

            float deltaYaw = currentYaw - previousYaw;
            if (Mathf.Abs(deltaYaw) > 0.0001f)
                transform.Rotate(Vector3.up, deltaYaw, Space.World);

            SyncGroundedState(data);
            Vector3 finalMovement = horizontalVelocity + Vector3.up * _verticalVelocity;
            Vector3 movementDelta = finalMovement * Time.deltaTime;
            if (!IsFinite(movementDelta) || movementDelta.magnitude > MaxCommittedFrameDisplacement)
            {
                _verticalVelocity = 0f;
                return;
            }
            MoveTraversalAndRecord(movementDelta, normalizedTime);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 🆕（docs/23 §R1）「traversal 開始了但沒有任何 warp」是最需要被看見的狀態之一：
            //    這條路徑代表兩條 plan 都建不起來、正在跑純 baked 曲線。仍要更新面板。
            if (ActiveTraversalPlanDiagnostics.HasResult)
                UpdateTraversalMotionRuntimeDebug(in _debugTraversalPlan, normalizedTime);
#endif
        }

        /// <summary>
        /// 由 MotionDriver 自己讀 entry Transform pose，讓 TraversalState 能在 OnEnter 鎖定 plan，
        /// 同時維持 Transform movement／rotation 的單一權威。
        /// </summary>
        public bool TryCreateTraversalWarpPlan(
            MotionBakeData bakeData,
            Vector3 targetPosition,
            Vector3 targetForward,
            float warpStartNormalizedTime,
            float warpEndNormalizedTime,
            float maxHorizontalCorrection,
            float maxVerticalCorrection,
            out TraversalWarpPlan plan) =>
            TryCreateTraversalWarpPlan(
                bakeData, targetPosition, targetForward,
                warpStartNormalizedTime, warpEndNormalizedTime,
                maxHorizontalCorrection, maxVerticalCorrection,
                out plan, out _, out _, out _);

        /// <summary>🆕（docs/23 §R1）帶理由的 endpoint plan 建立，供 commit 端組出可顯示的診斷。</summary>
        public bool TryCreateTraversalWarpPlan(
            MotionBakeData bakeData,
            Vector3 targetPosition,
            Vector3 targetForward,
            float warpStartNormalizedTime,
            float warpEndNormalizedTime,
            float maxHorizontalCorrection,
            float maxVerticalCorrection,
            out TraversalWarpPlan plan,
            out TraversalWarpPlanRejection rejection,
            out float requiredHorizontalCorrection,
            out float requiredVerticalCorrection)
        {
            return TraversalWarpPlan.TryCreate(
                bakeData,
                transform.position,
                transform.forward,
                targetPosition,
                targetForward,
                warpStartNormalizedTime,
                warpEndNormalizedTime,
                maxHorizontalCorrection,
                maxVerticalCorrection,
                out plan,
                out rejection,
                out requiredHorizontalCorrection,
                out requiredVerticalCorrection);
        }

        /// <summary>
        /// Publishes the already committed plan to presentation-only consumers such as Hand IK.
        /// This is not a second movement authority: consumers receive a read-only value snapshot
        /// and MotionDriver remains the only executor of root displacement.
        /// </summary>
        public void BeginTraversalPresentation(in TraversalWarpPlan plan) =>
            BeginTraversalPresentation(in plan, default);

        /// <summary>
        /// 🆕（docs/23 §R1）連同 commit 決策一起發布。**即使 plan 無效也要呼叫**——
        /// 「traversal 開始了但兩條 plan 都建不起來」正是最需要被看見的狀態。
        /// </summary>
        public void BeginTraversalPresentation(
            in TraversalWarpPlan plan,
            in TraversalWarpPlanDiagnostics diagnostics)
        {
            ActiveTraversalPlan = plan;
            ActiveTraversalNormalizedTime = 0f;
            HasActiveTraversalPlan = plan.IsValid;
            ActiveTraversalPlanDiagnostics = diagnostics;

            LastCommittedTraversalPlan = plan;
            LastCommittedTraversalDiagnostics = diagnostics;
            LastTraversalMaxBlockedDisplacement = 0f;
            LastTraversalMaxNormalizedTime = 0f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugTraversalPlan = plan;
#endif
        }

        public void EndTraversalPresentation()
        {
            HasActiveTraversalPlan = false;
            ActiveTraversalPlan = default;
            ActiveTraversalNormalizedTime = 0f;
            ActiveTraversalPlanDiagnostics = default;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_debugTraversalRuntimeLabel != null)
                _debugTraversalRuntimeLabel.gameObject.SetActive(false);
#endif
        }

        /// <summary>
        /// 以 committed plan 的絕對 pose 前後差值執行 traversal。位置仍只經 CharacterController.Move；
        /// 執行期不讀 Probe，也不逐幀重算 target correction。
        /// </summary>
        public void ExecuteTraversalWarpedMovement(
            in TraversalWarpPlan plan,
            float normalizedTime,
            float previousNormalizedTime,
            PlayerRuntimeData data)
        {
            if (IsTimeFrozen) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugTraversalPlan = plan;
#endif

            ActiveTraversalPlan = plan;
            ActiveTraversalNormalizedTime = Mathf.Clamp01(normalizedTime);
            HasActiveTraversalPlan = plan.IsValid;

            float currentNormalized = Mathf.Clamp01(normalizedTime);
            float previousNormalized = Mathf.Clamp(previousNormalizedTime, 0f, currentNormalized);
            if (!plan.IsValid ||
                !plan.TryEvaluate(previousNormalized, out Vector3 previousPosition, out float previousYaw) ||
                !plan.TryEvaluate(currentNormalized, out Vector3 currentPosition, out float currentYaw))
            {
                _verticalVelocity = 0f;
                SyncGroundedState(data);
                return;
            }

            Vector3 movementDelta = currentPosition - previousPosition;
            float deltaYaw = Mathf.DeltaAngle(previousYaw, currentYaw);
            if (!IsFinite(movementDelta) || !IsFinite(deltaYaw) ||
                movementDelta.magnitude > MaxCommittedFrameDisplacement)
            {
                _verticalVelocity = 0f;
                SyncGroundedState(data);
                return;
            }

            float frameDeltaTime = Time.deltaTime;
            if (!IsFinite(frameDeltaTime) || frameDeltaTime <= 0f)
            {
                _verticalVelocity = 0f;
                SyncGroundedState(data);
                return;
            }

            // ⭐ 2026-09-13 Playtest：「動畫結束會有微小浮空，有落地聲佐證」。
            //
            // Exit knot 之後 plan 的位置是**凍結**的（TryEvaluatePiecewise 的 positional contract），
            // 於是 movementDelta 恆為零 ⇒ `CharacterController.Move(Vector3.zero)` **不會回報觸地**
            // ⇒ `isGrounded` 整段 traversal 都是 false。兩個可見後果：
            //   ① `FootIKController` 的閘門是 `data.IsGrounded && !BlockIK` ⇒ **Foot IK 全程關閉**，
            //      而 clip 最後一格並不是完全站定的姿勢（實測腳趾比 Idle 站姿高 1.2 cm）⇒ 看起來就是浮著。
            //   ② 離開 traversal 的第一幀重力把角色壓下去才觸地 ⇒ `JustLanded` ⇒ **落地聲**。
            //
            // 修法不是宣稱自己觸地，而是**真的貼上去**：exit 之後沿用 locomotion 的 reboundForce
            // 貼地力，讓角色坐到已驗證的落腳面上，grounded 由物理回答。
            // Foot IK 因此在還在 traversal 內就接手，1.2 cm 的姿勢差由它吸收，不再留到狀態結束。
            if (plan.IsPiecewise && currentNormalized >= plan.ExitKnot.NormalizedTime)
                movementDelta.y += reboundForce * frameDeltaTime;

            _verticalVelocity = movementDelta.y / frameDeltaTime;
            if (Mathf.Abs(deltaYaw) > 0.0001f)
                transform.Rotate(Vector3.up, deltaYaw, Space.World);

            SyncGroundedState(data);
            MoveTraversalAndRecord(movementDelta, currentNormalized);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UpdateTraversalMotionRuntimeDebug(in plan, currentNormalized);
#endif
        }

        private void MoveTraversalAndRecord(Vector3 requestedDelta, float normalizedTime)
        {
            if (characterController == null) return;
            Vector3 before = transform.position;
            CollisionFlags flags = characterController.Move(requestedDelta);
            Vector3 actual = transform.position - before;
            LastTraversalExecutionResult = new TraversalExecutionResult(
                requestedDelta,
                actual,
                flags,
                characterController.center,
                characterController.height,
                characterController.radius,
                normalizedTime);

            // plan 是絕對 pose、執行是相對 delta——被碰撞擋掉的部分不會回授。
            // 累積最大值，讓「計畫對但結果不對」能被區分成「被擋」還是「用了別的 plan」。
            float blocked = LastTraversalExecutionResult.BlockedDisplacement.magnitude;
            if (blocked > LastTraversalMaxBlockedDisplacement)
                LastTraversalMaxBlockedDisplacement = blocked;
            if (normalizedTime > LastTraversalMaxNormalizedTime)
                LastTraversalMaxNormalizedTime = normalizedTime;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void UpdateTraversalMotionRuntimeDebug(
            in TraversalWarpPlan plan,
            float normalizedTime)
        {
            if (!drawTraversalMotionDebug || !drawTraversalPanelText)
            {
                if (_debugTraversalRuntimeLabel != null)
                    _debugTraversalRuntimeLabel.gameObject.SetActive(false);
                return;
            }
            if (_debugTraversalRuntimeLabel == null)
            {
                var labelObject = new GameObject("Traversal Motion Debug")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = gameObject.layer,
                };
                labelObject.transform.SetParent(transform, false);
                _debugTraversalRuntimeLabel = labelObject.AddComponent<TextMesh>();
                _debugTraversalRuntimeLabel.anchor = TextAnchor.LowerCenter;
                _debugTraversalRuntimeLabel.alignment = TextAlignment.Center;
                _debugTraversalRuntimeLabel.characterSize = 0.025f;
                _debugTraversalRuntimeLabel.fontSize = 42;
            }

            _debugTraversalRuntimeLabel.gameObject.SetActive(true);
            TraversalWarpPlanDiagnostics diagnostics = ActiveTraversalPlanDiagnostics;

            // ── 1. Plan 模式與理由（三種結局都要說得出口） ─────────────────────────
            string planLine = DescribePlanMode(in diagnostics, in plan);

            // ── 2. Root／Pose 同步金絲雀 ────────────────────────────────────────
            //    docs/23 §A2 的回歸守門：root 取樣 baked 曲線的時間必須等於動畫進度。
            //    修正前此值可達 0.25（root 快 25%），對應垂直去同步 0.5–1.0m。
            string syncLine;
            string heightLine;
            if (plan.IsValid)
            {
                float rootTime = plan.TrajectoryNormalizedAt(normalizedTime);
                float timeDesync = rootTime - Mathf.Clamp01(normalizedTime);
                TraversalWarpPlan.TryEvaluateOriginal(
                    plan.Bake, plan.StartPosition, plan.StartForward, normalizedTime,
                    out Vector3 original, out _);
                plan.TryEvaluate(normalizedTime, out Vector3 warped, out _);
                float correctionY = warped.y - original.y;
                float remainingY = plan.TargetPosition.y - warped.y;
                syncLine = $"Root/Pose dt: {timeDesync:+0.000;-0.000; 0.000}" +
                           (plan.IsPiecewise
                               ? string.Empty
                               : $"   Blend: {plan.CorrectionWeightAt(normalizedTime) * 100f:F0}%");
                heightLine = $"dY applied: {correctionY:+0.00;-0.00; 0.00} m" +
                             $"   to platform: {remainingY:+0.00;-0.00; 0.00} m";
            }
            else
            {
                syncLine = "Root/Pose dt: n/a (no plan — running raw baked curve)";
                heightLine = DescribeCorrectionBudget(in diagnostics);
            }

            // ── 3. Contact／IK 只保留狀態與純量殘差（Vector3 全值移到 world-space 繪製） ──
            string contactLine = plan.IsPiecewise
                ? $"Contact: L {plan.ContactTargets.LeftHandResidualError * 100f:F1} cm" +
                  $"   R {plan.ContactTargets.RightHandResidualError * 100f:F1} cm"
                : "Contact: none (needs piecewise plan)";

            Project.Presentation.IK.TraversalHandIKController handIK =
                GetComponent<Project.Presentation.IK.TraversalHandIKController>();
            string ikLine = handIK == null
                ? "Hand IK: NOT BOUND (component not attached)"
                : $"Hand IK: {handIK.Status}" +
                  (handIK.TargetData != null
                      ? $"  Lw {handIK.TargetData.LeftHandPositionWeight:F2}" +
                        $"  Rw {handIK.TargetData.RightHandPositionWeight:F2}"
                      : string.Empty);

            _debugTraversalRuntimeLabel.text =
                $"{planLine}   t {Mathf.Clamp01(normalizedTime):F2}\n" +
                $"{syncLine}\n{heightLine}\n{contactLine}\n{ikLine}";
            _debugTraversalRuntimeLabel.transform.position = transform.position + Vector3.up * 2.35f;
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector3 look = _debugTraversalRuntimeLabel.transform.position -
                               mainCamera.transform.position;
                if (look.sqrMagnitude > 0.000001f)
                    _debugTraversalRuntimeLabel.transform.rotation = Quaternion.LookRotation(look);
            }
        }

        /// <summary>Compact 的一行 plan 結論；失敗時把理由與超限量一起說完。</summary>
        private static string DescribePlanMode(
            in TraversalWarpPlanDiagnostics diagnostics,
            in TraversalWarpPlan plan)
        {
            if (!diagnostics.HasResult)
                return plan.IsPiecewise ? "Plan: PIECEWISE" : "Plan: ENDPOINT";

            switch (diagnostics.Mode)
            {
                case TraversalPlanMode.Piecewise:
                    return "Plan: PIECEWISE";
                case TraversalPlanMode.EndpointFallback:
                    return $"Plan: ENDPOINT (piecewise: {diagnostics.PiecewiseRejection})" +
                           $"  window {diagnostics.WarpStartNormalizedTime:F2}–" +
                           $"{diagnostics.WarpEndNormalizedTime:F2}" +
                           (diagnostics.WarpEndIsDataDerived ? " [from bake]" : " [authored]");
                case TraversalPlanMode.Failed:
                    return $"Plan: NONE — endpoint {diagnostics.EndpointRejection}" +
                           $" / piecewise {diagnostics.PiecewiseRejection}";
                default:
                    return "Plan: NOT COMMITTED";
            }
        }

        /// <summary>Plan 建不起來時，把「差多少」講清楚（例如 Vault1m 的水平超限）。</summary>
        private static string DescribeCorrectionBudget(in TraversalWarpPlanDiagnostics diagnostics)
        {
            if (!diagnostics.HasResult) return "Correction: unknown";
            string horizontal = float.IsNaN(diagnostics.RequiredHorizontalCorrection)
                ? "n/a"
                : $"{diagnostics.RequiredHorizontalCorrection:F2}/" +
                  $"{diagnostics.MaxHorizontalCorrection:F2} m";
            string vertical = float.IsNaN(diagnostics.RequiredVerticalCorrection)
                ? "n/a"
                : $"{diagnostics.RequiredVerticalCorrection:+0.00;-0.00; 0.00}/" +
                  $"{diagnostics.MaxVerticalCorrection:F2} m";
            return $"Correction needed H {horizontal}   V {vertical}";
        }

#endif

#if UNITY_EDITOR
        /// <summary>
        /// docs/25 §5 D1 的 Debug Visualization，🔄 2026-09-15 擴為 **Desired／Safe／Current 三層**
        /// （Scene View 不印字是 docs/22 §16 的既有規則）。
        ///
        /// <list type="bullet">
        /// <item><b>藍線</b>＝Facing（Root <c>transform.forward</c>）</item>
        /// <item><b>青線</b>＝MoveDirection（黑板的世界移動方向）</item>
        /// <item><b>黃圈</b>＝<b>Desired</b>：政策想要的膠囊位置（不認識幾何體）</item>
        /// <item><b>綠圈</b>＝<b>Safe</b>：collision-aware clamp 之後允許的位置</item>
        /// <item><b>洋紅圈</b>＝<b>Current</b>：平滑後實際套進 <c>center</c> 的位置</item>
        /// <item><b>灰圈</b>＝authored 基準（偏移為 0 時膠囊會在哪）</item>
        /// </list>
        ///
        /// <para><b>怎麼讀這三個圈</b></para>
        /// <list type="bullet">
        /// <item><b>黃 ≠ 綠</b> ⇒ **幾何體吃掉了多少**（貼牆時應該看得到黃圈穿進牆、綠圈停在牆面）。
        /// 這正是舊 `touchingWall → zero` 會讓兩者同時歸零、因而完全看不出來的東西。</item>
        /// <item><b>綠 ≠ 洋紅</b> ⇒ **平滑造成的滯後**。</item>
        /// <item><b>三圈重合在灰圈上</b> ⇒ 偏移為 0（停止／空中／committed／traversal）。</item>
        /// </list>
        /// 圈的半徑就是 <c>CharacterController.radius</c>，畫在膠囊中心高度
        /// ⇒ **看得到膠囊前緣有沒有蓋住前傾的身體**，不必讀數字。
        /// </summary>
        private void DrawCapsuleOffsetGizmos()
        {
            // 🔄 2026-09-15：改吃獨立的顯示開關。
            // ⛔ 刻意**不再**以 `capsuleOffset.Enabled` 當繪製條件——功能關掉時同樣需要看得到
            //    「基準膠囊在哪、為什麼沒有偏移」，那正是診斷「好像沒用」的第一步。
            if (!drawCapsuleOffsetDebug || !Application.isPlaying || characterController == null) return;

            Vector3 origin = transform.position + Vector3.up * 0.05f;
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(origin, transform.forward * 0.5f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(origin, _debugLastMoveDirection * 0.5f);

            float radius = _capsuleBaseRadius;
            Vector3 baseCenter = transform.TransformPoint(_capsuleBaseCenter);

            // 灰＝基準。先畫，讓另外三個疊在它上面時對比清楚。
            DrawCapsuleOffsetRing(baseCenter, radius, new Color(0.5f, 0.5f, 0.55f, 0.6f));

            DrawCapsuleOffsetRing(
                baseCenter + transform.TransformDirection(_capsuleDesiredOffset), radius, Color.yellow);
            DrawCapsuleOffsetRing(
                baseCenter + transform.TransformDirection(_capsuleCappedOffset), radius,
                new Color(1f, 0.55f, 0.1f, 1f));
            DrawCapsuleOffsetRing(
                baseCenter + transform.TransformDirection(_capsuleSmoothedOffset), radius, Color.cyan);
            DrawCapsuleOffsetRing(
                baseCenter + transform.TransformDirection(_capsuleSafeOffset), radius, Color.magenta);

            // 對照用：**舊 V1 模型**（MoveDirection × MaxOffset × speed）會想去的位置。
            // ⚠️ 這條線不參與任何計算，純粹讓「為什麼換掉 MoveDirection」看得見：
            //    純側移時它會指向與身體傾斜**相反**的一側。
            if (capsuleOffset.Profile != null && _debugLastMoveDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 legacyLocal = transform.InverseTransformDirection(_debugLastMoveDirection);
                legacyLocal.y = 0f;
                if (legacyLocal.sqrMagnitude > 0.000001f)
                {
                    Vector3 legacy = legacyLocal.normalized *
                                     (capsuleOffset.Profile.MaxOffset * Mathf.Clamp01(_debugLastMoveSpeed));
                    UnityEditor.Handles.color = new Color(1f, 0f, 0f, 0.5f);
                    UnityEditor.Handles.DrawDottedLine(
                        baseCenter, baseCenter + transform.TransformDirection(legacy), 3f);
                }
            }
        }

        /// <summary>畫一個水平圓環代表膠囊在該位置的水平截面。⚠️ 只讀不寫。</summary>
        private static void DrawCapsuleOffsetRing(Vector3 center, float radius, Color color)
        {
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.DrawWireDisc(center, Vector3.up, radius);
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // =====================================================================
        // Game view 版本（LineRenderer）
        //
        // 🔴 **為什麼非做不可**：上面那組用 `Gizmos`／`Handles`，那是 **Scene view 專用**——
        //    Game 視窗**完全看不到**。而「開關」的需求本來就來自「我要在 Game 視窗看到」，
        //    只畫在 Scene view 的話那個開關沒有意義。
        // 📌 Foot IK 與 Traversal Probe 早就是這個作法（各自的 runtime lines ＝ LineRenderer）；
        //    這裡只是把 Capsule Offset 補齊到同一條線上，不是發明新機制。
        // =====================================================================

        private const int CapsuleRingSegments = 32;
        private const float CapsuleDebugLineWidth = 0.012f;

        // 🔄 V2：**五個**圓環，對應管線的每一層。看得懂這五圈就看得懂整條管線：
        //   0 灰   Base     ＝ authored 基準膠囊（永遠在 root 正下方）
        //   1 黃   Desired  ＝ 姿勢解算想要的（黃離灰多遠 ＝ 身體前傾多少）
        //   2 橙   Capped   ＝ 套 MaxOffset 之後（黃≠橙 ⇒ **上限吃掉了多少**）
        //   3 青   Smoothed ＝ 平滑後（橙≠青 ⇒ 平滑滯後）
        //   4 洋紅 Safe     ＝ 幾何夾持後，也就是真正寫進 center 的（青≠洋紅 ⇒ **牆吃掉了多少**）
        private readonly LineRenderer[] _capsuleDebugRings = new LineRenderer[5];
        private Transform _capsuleDebugRoot;
        private Material _capsuleDebugMaterial;
        private readonly Vector3[] _capsuleRingBuffer = new Vector3[CapsuleRingSegments];

        /// <summary>
        /// 每幀更新 Game view 的五層圓環。**只讀**：所有位置都來自已經算好的偏移欄位。
        /// ⚠️ 刻意在 <c>LateUpdate</c>，與 <c>UpdateCapsuleOffset</c> 分開——
        /// 後者有多個 early-return（traversal 生效／功能停用／deltaTime 為 0），
        /// 把繪製放在裡面會讓「為什麼沒有偏移」的那些情況**剛好都看不到**。
        ///
        /// 📌 代價：同一幀內 `LateUpdate` 的元件執行順序未定義，圓環**有可能晚一幀**。
        /// 對 debug 可視化而言看不出來；⛔ 但不要因此拿這些欄位去做任何 gameplay 判斷。
        ///
        /// 📌 功能關閉（`capsuleOffset.Enabled = false`）時所有偏移都是 0
        /// ⇒ 五個圓環重合在基準上。**那正是「功能沒作用」該有的樣子**，不是畫錯。
        /// </summary>
        private void LateUpdate()
        {
            if (!drawCapsuleOffsetDebug || characterController == null)
            {
                for (int i = 0; i < _capsuleDebugRings.Length; i++)
                    if (_capsuleDebugRings[i] != null) _capsuleDebugRings[i].enabled = false;
                return;
            }

            CaptureCapsuleBaseCenter();
            float radius = _capsuleBaseRadius;
            Vector3 baseCenter = transform.TransformPoint(_capsuleBaseCenter);

            UpdateCapsuleDebugRing(0, baseCenter, radius, new Color(0.5f, 0.5f, 0.55f, 0.85f));
            UpdateCapsuleDebugRing(
                1, baseCenter + transform.TransformDirection(_capsuleDesiredOffset), radius, Color.yellow);
            UpdateCapsuleDebugRing(
                2, baseCenter + transform.TransformDirection(_capsuleCappedOffset), radius,
                new Color(1f, 0.55f, 0.1f, 1f));
            UpdateCapsuleDebugRing(
                3, baseCenter + transform.TransformDirection(_capsuleSmoothedOffset), radius, Color.cyan);
            UpdateCapsuleDebugRing(
                4, baseCenter + transform.TransformDirection(_capsuleSafeOffset), radius, Color.magenta);
        }

        private void UpdateCapsuleDebugRing(int index, Vector3 center, float radius, Color color)
        {
            LineRenderer ring = GetOrCreateCapsuleDebugRing(index);
            if (ring == null) return;

            for (int i = 0; i < CapsuleRingSegments; i++)
            {
                float angle = i * (Mathf.PI * 2f / CapsuleRingSegments);
                // 世界水平面上的圓：CharacterController 的膠囊恆為世界直立。
                _capsuleRingBuffer[i] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y,
                    center.z + Mathf.Sin(angle) * radius);
            }

            ring.enabled = true;
            ring.startColor = color;
            ring.endColor = color;
            ring.SetPositions(_capsuleRingBuffer);
        }

        private LineRenderer GetOrCreateCapsuleDebugRing(int index)
        {
            LineRenderer existing = _capsuleDebugRings[index];
            if (existing != null) return existing;

            if (_capsuleDebugMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) return null;
                _capsuleDebugMaterial = new Material(shader)
                {
                    name = "Capsule Offset Debug (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            if (_capsuleDebugRoot == null)
            {
                var root = new GameObject("Capsule Offset Debug Lines")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _capsuleDebugRoot = root.transform;
                _capsuleDebugRoot.SetParent(transform, false);
            }

            var lineObject = new GameObject($"Ring {index}")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = gameObject.layer,
            };
            lineObject.transform.SetParent(_capsuleDebugRoot, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = _capsuleDebugMaterial;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = CapsuleRingSegments;
            line.startWidth = CapsuleDebugLineWidth;
            line.endWidth = CapsuleDebugLineWidth;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            _capsuleDebugRings[index] = line;
            return line;
        }

        private void OnDestroy()
        {
            if (_capsuleDebugMaterial == null) return;
            if (Application.isPlaying) Destroy(_capsuleDebugMaterial);
            else DestroyImmediate(_capsuleDebugMaterial);
        }
#endif

#if UNITY_EDITOR

        private void OnDrawGizmos()
        {
            DrawCapsuleOffsetGizmos();
            if (!drawTraversalMotionDebug || !Application.isPlaying) return;
            TraversalWarpPlanDiagnostics diagnostics = ActiveTraversalPlanDiagnostics;
            if (!_debugTraversalPlan.IsValid)
            {
                // docs/23 §H-1 ⑤：「什麼都不畫」會被誤讀成「沒資料」。
                // Traversal 正在執行但沒有任何 warp 時，明說出來。
                if (!diagnostics.HasResult || diagnostics.Mode != TraversalPlanMode.Failed) return;
                Vector3 marker = transform.position + Vector3.up * 2f;
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(marker, 0.12f);
                // 唯一保留的 Scene View 文字：**一行**，而且只在「traversal 跑了但完全沒有 warp」時出現。
                // 這是唯一沒有東西可以畫出來的失敗態；細節在 Traversal Debug 視窗。
                UnityEditor.Handles.Label(
                    marker + Vector3.up * 0.15f, $"NO WARP PLAN ({diagnostics.EndpointRejection})");
                return;
            }

            const int Segments = 24;
            Vector3 previousOriginal = default;
            Vector3 previousWarped = default;
            bool hasPrevious = false;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                if (!TraversalWarpPlan.TryEvaluateOriginal(
                        _debugTraversalPlan.Bake,
                        _debugTraversalPlan.StartPosition,
                        _debugTraversalPlan.StartForward,
                        t,
                        out Vector3 original,
                        out _) ||
                    !_debugTraversalPlan.TryEvaluate(t, out Vector3 warped, out _))
                {
                    continue;
                }
                if (hasPrevious)
                {
                    Gizmos.color = new Color(0.35f, 0.65f, 1f, 0.9f);
                    Gizmos.DrawLine(previousOriginal, original);
                    Gizmos.color = new Color(0.2f, 1f, 0.55f, 0.95f);
                    Gizmos.DrawLine(previousWarped, warped);
                }
                previousOriginal = original;
                previousWarped = warped;
                hasPrevious = true;
            }

            // ⭐ docs/23 §H-2：當前這一格的 original ● 與 warped ◎ 同時畫並連線。
            //    這條線的長度就是實際套用的 correction；修正前的 root／pose 去同步
            //    （Climb1m 0.82m／Vault1m 1.02m）會直接長成一條看得見的線。
            float currentNormalized = ActiveTraversalNormalizedTime;
            if (TraversalWarpPlan.TryEvaluateOriginal(
                    _debugTraversalPlan.Bake,
                    _debugTraversalPlan.StartPosition,
                    _debugTraversalPlan.StartForward,
                    currentNormalized,
                    out Vector3 currentOriginalRoot,
                    out _) &&
                _debugTraversalPlan.TryEvaluate(
                    currentNormalized, out Vector3 currentWarpedRoot, out _))
            {
                Gizmos.color = new Color(0.35f, 0.65f, 1f, 0.9f);
                Gizmos.DrawWireSphere(currentOriginalRoot, 0.05f);
                Gizmos.color = new Color(0.2f, 1f, 0.55f, 0.95f);
                Gizmos.DrawWireSphere(currentWarpedRoot, 0.05f);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(currentOriginalRoot, currentWarpedRoot);
            }

            if (_debugTraversalPlan.IsPiecewise)
            {
                DrawTraversalKnot(_debugTraversalPlan.EntryKnot, Color.white);
                DrawTraversalKnot(_debugTraversalPlan.LeftContactKnot, Color.yellow);
                DrawTraversalKnot(_debugTraversalPlan.RightContactKnot, new Color(1f, 0.55f, 0.1f));
                DrawTraversalKnot(_debugTraversalPlan.TransferKnot, Color.cyan);
                DrawTraversalKnot(_debugTraversalPlan.ExitKnot, Color.green);
                DrawTraversalKnot(_debugTraversalPlan.RecoveryKnot, Color.magenta);
            }
            else
            {
                // docs/23 §H-1 ⑤：endpoint plan 沒有 knot，但仍要看得到窗的兩端與終點，
                // 否則「沒有 knot 球」會被誤讀成繪製壞掉。
                DrawEndpointWindowMarker(
                    diagnostics.HasResult ? diagnostics.WarpStartNormalizedTime : 0f,
                    "ENDPOINT start", Color.white);
                DrawEndpointWindowMarker(
                    diagnostics.HasResult ? diagnostics.WarpEndNormalizedTime : 1f,
                    diagnostics.WarpEndIsDataDerived
                        ? "ENDPOINT correction complete [from bake]"
                        : "ENDPOINT correction complete [authored]",
                    Color.green);
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(_debugTraversalPlan.TargetPosition, 0.06f);
            }

            TraversalContactTargets contacts = _debugTraversalPlan.ContactTargets;
            if (contacts.HasLeftHand)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(contacts.LeftHandWorldTarget, 0.05f);
                DrawHandContact("Left", in contacts.LeftHandMeasurement, Color.yellow);
            }
            if (contacts.HasRightHand)
            {
                Gizmos.color = new Color(1f, 0.55f, 0.1f);
                Gizmos.DrawWireSphere(contacts.RightHandWorldTarget, 0.05f);
                DrawHandContact(
                    "Right",
                    in contacts.RightHandMeasurement,
                    new Color(1f, 0.55f, 0.1f));
            }

            if (characterController != null)
            {
                Vector3 center = transform.TransformPoint(characterController.center);
                float half = Mathf.Max(characterController.height * 0.5f, characterController.radius);
                float segment = Mathf.Max(0f, half - characterController.radius);
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(center + transform.up * segment, characterController.radius);
                Gizmos.DrawWireSphere(center - transform.up * segment, characterController.radius);
            }

            TraversalExecutionResult result = LastTraversalExecutionResult;
            if (result.HasResult)
            {
                Vector3 basePoint = transform.position + Vector3.up * 0.1f;
                Gizmos.color = Color.white;
                Gizmos.DrawLine(basePoint, basePoint + result.RequestedDisplacement);
                Gizmos.color = Color.red;
                Gizmos.DrawLine(basePoint, basePoint + result.BlockedDisplacement);
                float originalY = 0f;
                float warpedY = 0f;
                if (TraversalWarpPlan.TryEvaluateOriginal(
                        _debugTraversalPlan.Bake,
                        _debugTraversalPlan.StartPosition,
                        _debugTraversalPlan.StartForward,
                        result.NormalizedTime,
                        out Vector3 currentOriginal,
                        out _))
                {
                    originalY = currentOriginal.y;
                }
                if (_debugTraversalPlan.TryEvaluate(
                        result.NormalizedTime, out Vector3 currentWarped, out _))
                {
                    warpedY = currentWarped.y;
                }

                // ⛔ docs/22 §16：**Scene View 一個字都不印。**
                //    所有數值都在 Window ▸ Project ▸ Traversal Debug；在這裡重印只會蓋住
                //    正在觀察的角色本身——那正是 2026-09-13 影片裡文字疊成一團的成因。
                _ = originalY;
                _ = warpedY;
            }
        }

        private void DrawEndpointWindowMarker(float normalizedTime, string label, Color color)
        {
            if (!_debugTraversalPlan.TryEvaluate(normalizedTime, out Vector3 position, out _)) return;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(position, 0.05f);
            _ = label; // 標記只畫球，不印字（見本檔 OnDrawGizmos 的說明）。
        }

        private void DrawTraversalKnot(in TraversalWarpKnot knot, Color color)
        {
            if (!_debugTraversalPlan.IsPiecewise ||
                !_debugTraversalPlan.TryEvaluate(knot.NormalizedTime, out Vector3 position, out _))
            {
                return;
            }
            Gizmos.color = color;
            Gizmos.DrawWireSphere(position, 0.04f);
            // 六個 knot 各印三行字＝十八行疊在同一小塊區域，等於什麼都看不到。
            // 這裡只畫球與「原始→修正後」的連線；哪個球是哪個 knot 由顏色分辨，
            // 對應表在 Traversal Debug 視窗。
            if (TraversalWarpPlan.TryEvaluateOriginal(
                    _debugTraversalPlan.Bake,
                    _debugTraversalPlan.StartPosition,
                    _debugTraversalPlan.StartForward,
                    knot.NormalizedTime,
                    out Vector3 original,
                    out _))
            {
                Gizmos.DrawLine(original, position);
            }
        }

        private static void DrawHandContact(
            string label,
            in TraversalHandContactMeasurement measurement,
            Color color)
        {
            if (!measurement.IsValid) return;
            Gizmos.color = new Color(0.35f, 0.65f, 1f, 0.9f);
            Gizmos.DrawWireSphere(measurement.OriginalAnimatedHandWorld, 0.025f);
            Gizmos.color = color;
            Gizmos.DrawWireSphere(measurement.AnimatedHandWorld, 0.03f);
            // 誤差向量本身就是那條線——線的長度即誤差。再用三行 Vector3 重述一次
            // 只會蓋住手部本身（2026-09-13 影片）。數值在 Traversal Debug 視窗。
            Gizmos.DrawLine(measurement.AnimatedHandWorld, measurement.WorldTarget);
            _ = label;
        }
#endif

        private void ExecuteBakedCurveMovementAtTimes(
            MotionBakeData bakeData,
            float currentTime,
            float previousTime,
            bool usePlayheadDeltaSpeed,
            PlayerRuntimeData data)
        {
            float currentSpeed = bakeData.GetSpeedAt(currentTime);
            float worldSpeed = currentSpeed;

            if (usePlayheadDeltaSpeed)
            {
                float clipDeltaTime = Mathf.Max(0f, currentTime - previousTime);
                float previousSpeed = bakeData.GetSpeedAt(previousTime);
                float averageClipSpeed = 0.5f * (previousSpeed + currentSpeed);
                worldSpeed = clipDeltaTime > 0f
                    ? averageClipSpeed * (clipDeltaTime / Time.deltaTime)
                    : 0f;
            }

            Vector3 horizontalVelocity = transform.forward * worldSpeed;

            float currentYaw = bakeData.GetRotationAt(currentTime);
            float previousYaw = bakeData.GetRotationAt(previousTime);
            float deltaYaw = currentYaw - previousYaw;

            if (Mathf.Abs(deltaYaw) > 0.0001f)
            {
                transform.Rotate(Vector3.up, deltaYaw, Space.World);
            }

            Vector3 finalMovement = horizontalVelocity + GetGravityThisFrame(data);
            characterController.Move(finalMovement * Time.deltaTime);
        }

        /// <summary>
        /// 消費當帧 request 並以序列化速率執行旋轉。方向與是否送出 request 已由唯一 facing authority
        /// 決定；此處不得再依角差或 movement 狀態建立第二套 facing 政策（ADR-007 D2／D3）。
        /// </summary>
        private void ApplyFacingRequest()
        {
            if (_facingRequestFrame != Time.frameCount)
            {
                _requestedFacingDirection = Vector3.zero;
                _facingRequestFrame = -1;
                return;
            }

            Vector3 requestedDirection = _requestedFacingDirection;
            _requestedFacingDirection = Vector3.zero;
            _facingRequestFrame = -1;

            if (requestedDirection.sqrMagnitude <= 0f) return;

            Quaternion currentRotation = transform.rotation;
            Quaternion targetRotation = ComputeFacingTarget(
                currentRotation, requestedDirection, out bool hasTarget);
            if (hasTarget)
            {
                transform.rotation = Quaternion.Slerp(
                    currentRotation, targetRotation, aimFacingTurnSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// 將 request 壓平並換成目標 rotation。這個純函數只做執行前的幾何轉換；
        /// 它不判斷角差、不決定是否該轉，讓 MotionDriver 維持 facing executor 的單一責任。
        /// </summary>
        internal static Quaternion ComputeFacingTarget(
            Quaternion currentRotation,
            Vector3 worldDirection,
            out bool hasTarget)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0f)
            {
                hasTarget = false;
                return currentRotation;
            }

            worldDirection.Normalize();
            hasTarget = true;
            return Quaternion.LookRotation(worldDirection, Vector3.up);
        }

        /// <summary>
        /// 🚀 工業級實作：進階烘焙補償速度疊加位移（動態吸附/校準）
        /// </summary>
        public void ApplyBakedCompensation(MotionBakeData bakeData, Vector3 actualTarget, float normalizedTime, PlayerRuntimeData data)
        {
            if (bakeData == null) return;
            if (IsTimeFrozen) return; // 同 ExecuteBaseMovement，見 IsTimeFrozen 的說明

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
        /// 重力積分路徑先透過 SyncGroundedState 發布一致的接觸快照；committed 垂直曲線
        /// 不取重力，但會直接呼叫同一個同步方法。MotionDriver 仍是唯一寫入者。
        /// </summary>
        private Vector3 GetGravityThisFrame(PlayerRuntimeData data)
        {
            int currentFrame = Time.frameCount;
            if (_gravityFrame == currentFrame) return _cachedGravity;

            _gravityFrame = currentFrame;

            SyncGroundedState(data);

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

        /// <summary>
        /// 將 CharacterController 上一次 Move 的接觸狀態與當前權威垂直速度發布成一致黑板快照。
        /// 重力路徑與 committed 垂直曲線路徑共用；MotionDriver 仍是唯一寫入者。
        /// </summary>
        private void SyncGroundedState(PlayerRuntimeData data)
        {
            // 🆕（M2）單幀落地/離地邊沿：MotionDriver 是這兩個旗標的唯一觸發源（set true）；
            // 復位（set false）由管線末尾 PlayerRuntimeData.ResetTransientState() 負責，非此處。
            bool grounded = characterController.isGrounded;
            data.JustLanded = !_wasGrounded && grounded;
            data.JustLeftGround = _wasGrounded && !grounded;
            data.IsGrounded = grounded;
            _wasGrounded = grounded;

            // 必須在 reboundForce 貼地夾持前發布；否則落地幀的 impact velocity 會被銷毀，
            // 所有落地都只會拿到貼地力並被誤分為 Normal。
            data.VerticalVelocity = _verticalVelocity;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
