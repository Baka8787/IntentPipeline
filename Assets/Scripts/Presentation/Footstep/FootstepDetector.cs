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
            }
        }

        /// <summary>
        /// 【管線順序 6.5 末尾】推進雙腳偵測並回報本帧事件。熱路徑零配置（純數值運算，無 new／無 LINQ／無字串）。
        /// </summary>
        public PresentationEventData Evaluate(PlayerRuntimeData data)
        {
            // 本方法刻意只做「取得本帧輸入」這一件事，判定全部在 Detect —— 見 Detect 的說明。
            return Detect(_poseData, _groundData, transform.position.y,
                data != null && data.JustLanded, Time.deltaTime);
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
        /// <param name="justLanded">本帧是否剛落地。</param>
        /// <param name="deltaTime">本帧時間步長。</param>
        public PresentationEventData Detect(FootIKPoseData pose, FootIKGroundData ground,
            float rootPlaneY, bool justLanded, float deltaTime)
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

            FootstepKind left = _leftTracker.Advance(
                leftBottom, leftHasGround, leftHasGround ? ground.Left.GroundY : 0f,
                rootPlaneY, deltaTime, settings, out FootstepSuppressReason leftReason);

            FootstepKind right = _rightTracker.Advance(
                rightBottom, rightHasGround, rightHasGround ? ground.Right.GroundY : 0f,
                rootPlaneY, deltaTime, settings, out FootstepSuppressReason rightReason);

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
#endif

            events.LeftFootstep = left;
            events.RightFootstep = right;
            return events;
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
#endif
    }
}
