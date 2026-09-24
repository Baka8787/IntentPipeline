using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.IK;

namespace Project.Presentation.Footstep
{
    /// <summary>
    /// 🆕（M3.x-B）落腳偵測器——第一個 <see cref="IPresentationEventSource"/> 實作。
    ///
    /// 讀 <see cref="FootIKPoseData"/>（**pre-IK 動畫原始 pose**，M3.x-A 起由其唯一 Writer
    /// <see cref="FootIKRig"/> 擁有生命週期）＋ <see cref="FootIKGroundData"/>（🆕 M3.x-C，
    /// 由其唯一 Writer <see cref="FootIKController"/> 擁有）→ 自行維持跨帧偵測狀態 → 回傳 value struct。
    ///
    /// **職責邊界（輪 3 裁決，逐條）**：
    /// * **不寫黑板**——回傳值而非寫入，這是型別層的保證，不是紀律。
    /// * **不知道黑板上有沒有對應欄位**、不認識 <c>AudioController</c>／VFX／鏡頭、
    ///   不參與黑板事件的發布生命週期（那屬 <see cref="PresentationPipeline"/>）。
    /// * **不持有 <see cref="PlayerRuntimeData"/> 欄位引用**：黑板每帧由參數傳入。
    /// * **不 raycast、不詢問地面**（🔄 M3.x-C：改為**複用** Foot IK 已經發過的 ray，
    ///   而不是自己再發一次——語意不變，成本從「每步多 1～2 次 raycast」變成 0）。
    /// * 不讀 post-IK 骨骼 Transform。
    ///
    /// ⚠️ **語意：這是「動畫落腳事件」，不是物理 ground contact。**
    /// 因此刻意讀 IK 套用**前**的 pose——斜坡上 IK 會把腳修到別處，但**落腳的時機不變**，
    /// 而玩家聽到的腳步聲要對齊看到的動畫落腳。改讀 post-IK 位置會讓聲音跟著地形修正漂移。
    ///
    /// 🔄 **（Stage 1）地面觀測只用來當「高度的參考平面」，不改變上面這條語意**：
    /// 量的仍然是動畫腳的位置，只是把「相對世界原點」換成「相對腳下的地面」——
    /// 後者在樓梯與斜坡上才是有意義的量（見 <see cref="FootPlantTracker"/> 的說明）。
    /// </summary>
    public class FootstepDetector : MonoBehaviour, IPresentationEventSource
    {
        [SerializeField] private FootstepDetectionSettings settings = new();

        [Header("Debug")]
        [Tooltip("記錄落腳／抑制的環形緩衝（僅 Editor／Development Build）。\n" +
                 "⚠️ 刻意**不**每帧 Debug.Log——只在擊發、或抑制原因改變時寫一筆。\n" +
                 "檢視方式：選取本元件，於 Inspector 底部的「Footstep Trace」面板。")]
        [SerializeField] private bool recordDebugTrace;

        // 🔬 PROTOTYPE（docs/43 §5.7）：與 recordDebugTrace（Inspector 面板）完全獨立的第二條輸出，
        // 純粹為了能用 `unity cmd console` 撈到逐次判定結果，供 Console log 分析用。
        // ⛔ 只多一個輸出管道，不改變任何判定邏輯、不影響 recordDebugTrace 既有行為。
        [Tooltip("把落腳偵測的判定結果也輸出到 Console（獨立於上面的 Inspector 面板 trace）。\n" +
                 "同款「只在擊發或抑制原因改變時寫一筆」的節流，不逐帧輸出。\n" +
                 "🔬 PROTOTYPE，僅供 docs/43 §5 的 Run Up/Down 腳步聲診斷用，純觀測、零行為影響。")]
        [SerializeField] private bool enableFootstepConsoleTrace;

        [Header("Surface（🆕 Stage 2）")]
        [Tooltip("聲學材質對應表。**未綁定時所有事件的 Surface 皆為 Unknown**，消費端會退回它自己的 default。\n" +
                 "一張場景設定一次即可——demo map 實測只需要 6 筆。")]
        [SerializeField] private SurfaceMapSO surfaceMap;

        // Pose 管道：向 owner（唯一 Writer）取得引用，**不是**向其他 Controller 要
        // ——後者會構成 Controller 互相引用，違反 IPresentationController 契約（M3.x-A 明訂）。
        private FootIKPoseData _poseData;

        // 🆕（M3.x-C）Ground 管道：同樣向 owner（唯一 Writer ＝ FootIKController）取得引用。
        // ⚠️ 本元件是 IPresentationEventSource 而非 IPresentationController，取得的也只是
        //    「本帧腳下有什麼」的物理觀測、不是 Foot IK 的任何決策——故不構成 Controller 互相引用。
        //    時序保證（管線步驟①必早於②）見 FootIKGroundData 的說明。
        private FootIKGroundData _groundData;

        // 跨帧偵測狀態：本元件是它的擁有者。與「黑板事件的發布／生命週期」刻意分屬兩個擁有者。
        private FootPlantTracker _leftTracker;
        private FootPlantTracker _rightTracker;

        // 🆕（Stage 2）材質解析的記憶化狀態（每腳一份；左右腳可能踩在不同 collider 上）。
        private SurfaceResolver _leftSurface;
        private SurfaceResolver _rightSurface;

        // 🆕 角色**實際**的世界水平速度。⚠️ 刻意量 Transform 的位移，不讀黑板的 MoveSpeed——
        //    後者是「想移動多快」的意圖，貼牆被擋住時它仍然是滿的；
        //    我們要問的正是「意圖有沒有變成實際位移」，所以只有量位置差才答得出來。
        private Vector3 _previousRootPosition;
        private bool _hasPreviousRootPosition;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 🔬 PROTOTYPE（docs/43 §5.7）：**只給 Console trace 讀 IK 權重當相關性欄位**，
        // 不參與 Detect 的任何判定——偵測邏輯完全不知道這個欄位存在，見類別頂端的職責邊界。
        // ⛔ 刻意包在 UNITY_EDITOR||DEVELOPMENT_BUILD 內，production build 不持有這個參考。
        private FootIKController _diagFootIKController;
        private FootstepSuppressReason _lastConsoleLeftReason = FootstepSuppressReason.None;
        private FootstepSuppressReason _lastConsoleRightReason = FootstepSuppressReason.None;
#endif

        private void Awake()
        {
            var rig = GetComponentInChildren<FootIKRig>();
            if (rig == null)
            {
                Debug.LogError($"[{gameObject.name}] FootstepDetector 找不到 FootIKRig——" +
                    "落腳偵測需要它產出的 pose 快照，本元件將全程靜默。" +
                    "Rig 必須掛在 Model 子物件（與 Animator 同物件）。", this);
            }
            else
            {
                // Rig 以欄位初始式持有快照（早於所有 Awake），故此處不需要關心元件間的 Awake 順序。
                _poseData = rig.PoseData;
            }

            var ikController = GetComponentInChildren<FootIKController>();
            if (ikController == null)
            {
                // 防禦線而非致命錯誤：沒有 Foot IK 時退化為「以 Root 平面為地面基準」，
                // 平地上行為與有 IK 時一致，只有樓梯／斜坡會退步。
                Debug.LogWarning($"[{gameObject.name}] FootstepDetector 找不到 FootIKController——" +
                    "將以角色 Root 平面作為地面基準。平地正常，樓梯與斜坡的落腳判定會退化。", this);
            }
            else
            {
                // Controller 以欄位初始式持有它（早於所有 Awake），故此處同樣不需要關心 Awake 順序。
                _groundData = ikController.GroundData;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _diagFootIKController = ikController; // 只給 §5.7 的 Console trace 讀權重相關性欄位。
#endif
            }

            if (surfaceMap == null)
            {
                Debug.LogWarning($"[{gameObject.name}] FootstepDetector 未綁定 SurfaceMapSO——" +
                    "所有事件的 Surface 將為 Unknown，音效端會一律退回它自己的 default 材質。", this);
            }
            else
            {
                surfaceMap.Initialize();
            }
        }

        /// <summary>
        /// 【管線順序 6.5 末尾】推進雙腳偵測並回報本帧事件。熱路徑零配置（純數值運算，無 new／無 LINQ／無字串）。
        /// </summary>
        public PresentationEventData Evaluate(PlayerRuntimeData data)
        {
            // 本方法刻意只做「取得本帧輸入」這一件事，判定全部在 Detect —— 見 Detect 的說明。
            float deltaTime = Time.deltaTime;
            Vector3 rootPosition = transform.position;

            float rootHorizontalSpeed = 0f;
            if (_hasPreviousRootPosition && deltaTime > 0f)
            {
                Vector3 rootDelta = rootPosition - _previousRootPosition;
                rootDelta.y = 0f;
                rootHorizontalSpeed = rootDelta.magnitude / deltaTime;
            }
            _previousRootPosition = rootPosition;
            _hasPreviousRootPosition = true;

            // ⚠️ `data` 為 null 時視為 **grounded**：退化方向必須是「什麼都不做」（照常出聲），
            //    而不是把整個角色的腳步靜音。
            return Detect(_poseData, _groundData, rootPosition.y, rootHorizontalSpeed,
                data == null || data.IsGrounded,
                data != null && data.JustLanded, deltaTime);
        }

        /// <summary>
        /// 落腳判定的完整邏輯，**輸入全部顯式傳入**。
        /// 拆出來的唯一理由是可測性：<c>Time.deltaTime</c> 在 EditMode 不可控，
        /// 若把它藏在方法內部，抑制規則與雙腳獨立性就只能靠 Play 模式肉眼驗
        /// （同 <c>FootIKController.ComputeFootWeight</c> 公開為純函數以供測試的先例）。
        /// ⚠️ 推進 tracker 會改動跨帧狀態，故本方法**不是**純函數，不可重複呼叫同一帧。
        /// </summary>
        /// <param name="pose">pre-IK 動畫 pose 快照。</param>
        /// <param name="ground">本帧地面觀測；<c>null</c> 或未 warm 時退化為以 <paramref name="rootPlaneY"/> 為地面。</param>
        /// <param name="rootPlaneY">角色 Root 平面世界 Y（＝膠囊底＝腳底基準）。</param>
        /// <param name="rootHorizontalSpeed">角色**實際**的世界水平速度（量 Transform 位移，不是移動意圖）。</param>
        /// <param name="isGrounded">
        /// 角色本帧是否觸地（黑板 <c>IsGrounded</c>）。⭐ 只用來**否決**沒有地面觀測時的回報，
        /// 見方法內的說明；為 <c>true</c> 時本規則完全不介入。
        /// </param>
        /// <param name="justLanded">本帧是否剛落地。</param>
        /// <param name="deltaTime">本帧時間步長。</param>
        public PresentationEventData Detect(FootIKPoseData pose, FootIKGroundData ground,
            float rootPlaneY, float rootHorizontalSpeed, bool isGrounded, bool justLanded, float deltaTime)
        {
            PresentationEventData events = default;

            // IsWarm：快照尚未被 Rig 寫過（IK pass 未開／Animator 未評估）前不消費全零數據。
            if (pose == null || !pose.IsWarm) return events;

            bool groundWarm = ground != null && ground.IsWarm;

            // 腳底世界位置＝IK goal 世界位置 − 腳踝到腳底的 Avatar 常數（沿世界 up，與 pose 旋轉無關，
            // 與 FootIKController 的 FootGroundedHeight 判準同一個簡化）。
            Vector3 leftBottom = pose.LeftFootPosition;
            leftBottom.y -= pose.LeftFootBottomHeight;
            Vector3 rightBottom = pose.RightFootPosition;
            rightBottom.y -= pose.RightFootBottomHeight;

            bool leftHasGround = groundWarm && ground.Left.HasGround;
            bool rightHasGround = groundWarm && ground.Right.HasGround;

            // 🆕（Stage 2）材質解析：**每帧都解**，不是只在擊發時解。
            // 理由是落地事件也要用它——落地那一瞬間必須已經有值，不能等到擊發才去查。
            // 成本由 SurfaceResolver 的 collider 記憶化吸收（同一塊地板上等同一次參考比對）。
            SurfaceId leftSurface = _leftSurface.Resolve(
                leftHasGround ? ground.Left.Collider : null,
                leftHasGround ? ground.Left.Point : Vector3.zero, surfaceMap);
            SurfaceId rightSurface = _rightSurface.Resolve(
                rightHasGround ? ground.Right.Collider : null,
                rightHasGround ? ground.Right.Point : Vector3.zero, surfaceMap);

            FootstepKind left = _leftTracker.Advance(
                leftBottom, leftHasGround, leftHasGround ? ground.Left.GroundY : 0f,
                rootPlaneY, rootHorizontalSpeed, deltaTime, settings, out FootstepSuppressReason leftReason);

            FootstepKind right = _rightTracker.Advance(
                rightBottom, rightHasGround, rightHasGround ? ground.Right.GroundY : 0f,
                rootPlaneY, rootHorizontalSpeed, deltaTime, settings, out FootstepSuppressReason rightReason);

            // 🆕（2026-09-17）**離地且沒有地面觀測 ⇒ 不回報。**
            //
            // ⚠️ 這不是「把判準調嚴」，是**原本的量測在空中沒有意義**：
            //    `FootIKController` 的閘門是 `data.IsGrounded` ⇒ 離地時它整段關閉 ⇒ 本偵測器
            //    拿不到地面觀測 ⇒ `groundReference` 退回 root 平面。root 平面在地上是腳底基準，
            //    在空中只是一個**跟著角色一起飛的參考點**——腳相對它的起落純粹是動畫內容。
            //
            // ⭐ 觸發本 bug 的實例：ledge climb 全程 `isGrounded == false`
            //    （原碼說明見 `MotionDriver.ExecuteTraversalWarpedMovement`：
            //    exit knot 之後位置凍結 ⇒ `Move(Vector3.zero)` 不回報觸地 ⇒ 整段 traversal 都是 false），
            //    攀爬動畫的腳因此在「離地 0」附近反覆進出接觸帶 ⇒ **攀爬途中出現腳步聲**。
            //
            // ⭐ 退化方向：`hasGround` 為真（真的有地面觀測）時**完全不介入**
            //    ⇒ 沒有 `FootIKController` 的退化配置在地上仍照常出聲（`WithoutGroundObservation_*` 契約不變）。
            // ⚠️ 與 justLanded 相同：抑制只發生在**推進之後**，tracker 的跨帧狀態照常前進。
            //    「不報告」與「沒發生」是兩件事，這裡只做前者。
            // ⛔ 刻意**不**認識 traversal／jump／任何狀態——本元件不該知道有哪些狀態存在。
            if (!isGrounded)
            {
                if (!leftHasGround) { left = FootstepKind.None; leftReason = FootstepSuppressReason.Airborne; }
                if (!rightHasGround) { right = FootstepKind.None; rightReason = FootstepSuppressReason.Airborne; }
            }

            // Landing 優先（輪 3 裁決）：落地是更高階的語意，該帧不再額外報一次腳步。
            // ⚠️ 抑制刻意發生在**推進之後**——tracker 的跨帧狀態照常前進，
            //    否則落地那一步會讓下一步的行程基準與上膛狀態錯亂。
            //    「不報告」與「沒發生」是兩件事，這裡只做前者。
            // 🔄（Stage 1）除了不回報，還**強制雙腳收束到 Planted**：
            //    否則落地後殘留的上膛狀態會在接下來一兩帧多播普通腳步（見 ForcePlanted 的說明）。
            if (justLanded)
            {
                _leftTracker.ForcePlanted();
                _rightTracker.ForcePlanted();
                left = FootstepKind.None;
                right = FootstepKind.None;
                leftReason = FootstepSuppressReason.None;
                rightReason = FootstepSuppressReason.None;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (recordDebugTrace)
            {
                RecordTrace(false, left, leftReason, in _leftTracker, leftHasGround);
                RecordTrace(true, right, rightReason, in _rightTracker, rightHasGround);
            }
            if (enableFootstepConsoleTrace)
            {
                LogFootstepConsoleTrace(false, left, leftReason, in _leftTracker, leftHasGround,
                    isGrounded, justLanded, rootHorizontalSpeed);
                LogFootstepConsoleTrace(true, right, rightReason, in _rightTracker, rightHasGround,
                    isGrounded, justLanded, rootHorizontalSpeed);
            }
#endif

            events.LeftFootstep = left;
            events.RightFootstep = right;
            events.LeftFootPosition = leftBottom;
            events.RightFootPosition = rightBottom;

            // 🆕（Stage 2）落地由本來源**轉報**，而不是讓消費端自己讀黑板的 JustLanded。
            // 唯一理由是**材質必須同源**：消費端讀到的事件快照慢一帧，而 JustLanded 是當帧值，
            // 混用會讓落地拿到「上一帧還在空中、沒有地面觀測」的材質。
            events.Landed = justLanded;

            // 🆕（Stage 2）三種事件共用的單一材質決策。
            // 雙腳踩在不同 collider 上時（跨材質交界、樓梯轉角）取**有事件的那一隻**；
            // 都沒事件就取任一已解析的——這一格描述的是「角色現在站在什麼上面」。
            events.Surface = PickSurface(left, leftSurface, right, rightSurface);
            return events;
        }

        /// <summary>
        /// 選出本帧代表性的材質。
        /// ⚠️ 刻意**不**做「左右各自一個材質」——那會讓 Landing 無所適從，
        /// 也會在材質交界處產生左右腳交替變聲的抖動。單一決策是使用者要的「同一次接觸共用材質」。
        /// </summary>
        private static SurfaceId PickSurface(FootstepKind left, SurfaceId leftSurface,
            FootstepKind right, SurfaceId rightSurface)
        {
            bool leftFired = left != FootstepKind.None;
            bool rightFired = right != FootstepKind.None;

            if (leftFired && leftSurface != SurfaceId.Unknown) return leftSurface;
            if (rightFired && rightSurface != SurfaceId.Unknown) return rightSurface;
            if (leftSurface != SurfaceId.Unknown) return leftSurface;
            return rightSurface;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>一筆偵測紀錄。固定大小、值型別，寫入不配置。</summary>
        public struct TraceEntry
        {
            public float Time;
            public bool IsRight;
            public FootstepKind Kind;
            public FootstepSuppressReason Reason;
            public FootPlantPhase Phase;
            public bool Armed;
            public bool HasGround;
            public float Clearance;
            public float VerticalSpeed;
            public float HorizontalSpeed;
            public float LiftSinceLastPlant;
        }

        private const int TraceCapacity = 64;
        private readonly TraceEntry[] _trace = new TraceEntry[TraceCapacity];
        private int _traceCount;
        private int _traceHead;
        private FootstepSuppressReason _lastLeftReason = FootstepSuppressReason.None;
        private FootstepSuppressReason _lastRightReason = FootstepSuppressReason.None;

        /// <summary>供 Editor 面板讀取。<c>index</c> 0 ＝最舊。</summary>
        public int TraceCount => _traceCount;

        /// <summary>供 Editor 面板讀取。</summary>
        public TraceEntry GetTrace(int index)
        {
            int start = _traceCount == TraceCapacity ? _traceHead : 0;
            return _trace[(start + index) % TraceCapacity];
        }

        /// <summary>供 Editor 面板清空。</summary>
        public void ClearTrace()
        {
            _traceCount = 0;
            _traceHead = 0;
        }

        private void RecordTrace(bool isRight, FootstepKind kind, FootstepSuppressReason reason,
            in FootPlantTracker tracker, bool hasGround)
        {
            // 只在「有事件」或「抑制原因改變」時寫——否則站著不動每帧都會寫一筆 NotReArmed。
            FootstepSuppressReason last = isRight ? _lastRightReason : _lastLeftReason;
            bool interesting = kind != FootstepKind.None || reason != last;
            if (isRight) _lastRightReason = reason; else _lastLeftReason = reason;
            if (!interesting) return;

            _trace[_traceHead] = new TraceEntry
            {
                Time = Time.realtimeSinceStartup,
                IsRight = isRight,
                Kind = kind,
                Reason = reason,
                Phase = tracker.Phase,
                Armed = tracker.IsArmed,
                HasGround = hasGround,
                Clearance = tracker.Clearance,
                VerticalSpeed = tracker.VerticalSpeed,
                HorizontalSpeed = tracker.HorizontalSpeed,
                LiftSinceLastPlant = tracker.LiftSinceLastPlant,
            };
            _traceHead = (_traceHead + 1) % TraceCapacity;
            if (_traceCount < TraceCapacity) _traceCount++;
        }

        /// <summary>
        /// 🔬 PROTOTYPE（docs/43 §5.7）：`RecordTrace` 的 Console 版本，**獨立的 last-reason 狀態**
        /// （`_lastConsoleLeftReason`／`_lastConsoleRightReason`），不與 Inspector 面板共用，兩條
        /// 輸出互不影響。同款「只在擊發或抑制原因改變時寫一筆」節流。
        /// ⚠️ 純觀測：只讀已經算好的 <paramref name="kind"/>／<paramref name="reason"/>／
        /// <paramref name="tracker"/>，不影響任何判定；<c>ikWeight</c>／<c>ikHasGround</c> 只是
        /// 額外讀 Foot IK 的相關性欄位供人工比對，偵測邏輯本身完全不知道它們存在。
        /// </summary>
        private void LogFootstepConsoleTrace(bool isRight, FootstepKind kind, FootstepSuppressReason reason,
            in FootPlantTracker tracker, bool hasGround, bool isGrounded, bool justLanded, float rootHorizontalSpeed)
        {
            FootstepSuppressReason last = isRight ? _lastConsoleRightReason : _lastConsoleLeftReason;
            bool interesting = kind != FootstepKind.None || reason != last || justLanded;
            if (isRight) _lastConsoleRightReason = reason; else _lastConsoleLeftReason = reason;
            if (!interesting) return;

            float ikWeight = float.NaN;
            bool ikHasGround = false;
            if (_diagFootIKController != null)
            {
                var target = _diagFootIKController.TargetData;
                ikWeight = isRight ? target.RightFootPositionWeight : target.LeftFootPositionWeight;
                var ground = _diagFootIKController.GroundData;
                ikHasGround = isRight ? ground.Right.HasGround : ground.Left.HasGround;
            }

            Debug.Log("[FootstepTrace] frame=" + Time.frameCount +
                " t=" + Time.time.ToString("F3") +
                " foot=" + (isRight ? "R" : "L") +
                " kind=" + kind +
                " fired=" + (kind != FootstepKind.None) +
                " reason=" + reason +
                " isGrounded=" + isGrounded +
                " phase=" + tracker.Phase +
                " armed=" + tracker.IsArmed +
                " clearance=" + tracker.Clearance.ToString("F5") +
                " vSpeed=" + tracker.VerticalSpeed.ToString("F4") +
                " hSpeed=" + tracker.HorizontalSpeed.ToString("F4") +
                " liftSinceLastPlant=" + tracker.LiftSinceLastPlant.ToString("F4") +
                " forcePlanted=" + justLanded +
                " rootHorizontalSpeed=" + rootHorizontalSpeed.ToString("F3") +
                " detectorHasGround=" + hasGround +
                " ikHasGround=" + ikHasGround +
                " ikWeight=" + ikWeight.ToString("F3"));
        }
#endif
    }
}
