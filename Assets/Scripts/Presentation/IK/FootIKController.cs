using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Presentation.IK
{
    /// <summary>
    /// （M3；M3.1 修正反饋迴路＝成熟基線；🆕 M3.5 最終形＝**字面回歸 M3.1 演算法**）
    /// Foot IK 決策端——第二個 <see cref="IPresentationController"/> 實例，由 PresentationPipeline
    /// 於管線順序 6.5（LateUpdate、MotionDriver 之後）驅動，Runner 零改動。
    ///
    /// 雙管道資料流（M3.1 裁決；🔄 M3.x-A 修正擁有權）：
    /// 黑板（IsGrounded／BlockIK）─讀→ 本類別 ─寫→ <see cref="FootIKTargetData"/> ─讀→ FootIKRig
    /// FootIKRig ─寫→ <see cref="FootIKPoseData"/>（動畫原始 pose 快照）─讀→ 本類別
    ///
    /// **擁有權規則（M3.x-A）：管道的 lifetime owner ＝ 該管道的唯一 Writer。**
    /// Target 由本類別寫故由本類別擁有、注入給 Rig 讀；Pose 由 Rig 寫故**由 Rig 擁有**，本類別只是 Reader。
    /// Target 維持單寫單讀；**Pose 自此為單寫多讀**——新增 Reader 向 owner 取得引用即可，零改動。
    ///
    /// 演算法（M3.1＋L1 v4）：每腳「快照 goal → ankle ray → 泰勒斯修正後的腳踝目標＋法線對齊旋轉 →
    /// Heel/Toe 真實世界端點採樣只補向上戳穿殘差（A/B 可關）→
    /// 單因子 Pose 權重（二態系統：窄帶外恆 0 或 1）→ MoveTowards 平滑」＋骨盆補償（低腳差夾限）。
    /// M3.2~M3.4 的實驗機制（fade 族／Slope Gate／濾波／Reach Clamp）已全數移除——實驗結論、
    /// 教訓與復刻指引見 changelog v0.18.2~v0.18.6 與 WORKLOG「Foot IK 品質路線圖」；
    /// 品質升級走輸入資訊量路線（Heel/Toe 雙點、CapsuleCast、Foot Contact），不再往單點權重堆補丁。
    ///
    /// 對 Animator 零依賴（M3.1）：pose 一律讀快照——骨骼 Transform 現值是上一幀 IK 的輸出，
    /// 採樣即反饋迴路（dev-spec §3.5.2 反饋禁令）。
    /// </summary>
    public class FootIKController : MonoBehaviour, IPresentationController
    {
        [SerializeField] private FootIKSettings settings = new();

        private FootIKTargetData _targetData;
        private FootIKPoseData _poseData;

        /// <summary>供除錯／測試檢視。執行期 Target 唯一 Writer 是本類別、Pose 唯一 Writer 是 Rig。</summary>
        public FootIKTargetData TargetData => _targetData;

        /// <summary>
        /// 供除錯／測試檢視。🔄（M3.x-A）本類別只是 Pose 的 **Reader**——
        /// 其 lifetime owner 是唯一 Writer <c>FootIKRig</c>，此處僅持有引用。
        /// ⚠️ 其他 Reader **不應**從這裡取得實例（那會構成 Controller 互相引用，違反
        /// <c>IPresentationController</c> 契約）；正確途徑是向 owner 取：<c>FootIKRig.PoseData</c>。
        /// </summary>
        public FootIKPoseData PoseData => _poseData;

        // 單幀採樣暫存（struct，棧上語義、零 GC）。
        private struct FootSample
        {
            public bool HasHit;
            public Vector3 HitPoint;
            public Vector3 Normal;
            public Vector3 SoleNormal;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public float GroundY;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // O-2 只快取「本幀真正採樣過」的幾何。Drawer 不得重發 physics query。
            public bool DebugWasSampled;
            public Vector3 DebugAnkleOrigin;
            public bool DebugTwoPointAttempted;
            public Vector3 DebugTwoPointBase;
            public Vector3 DebugHeelOrigin;
            public Vector3 DebugToeOrigin;
            public Vector3 DebugFootForward;
            public bool DebugHeelHasHit;
            public bool DebugToeHasHit;
            public Vector3 DebugHeelHitPoint;
            public Vector3 DebugToeHitPoint;
            public float DebugResidualLift;
#endif
        }

        private void Awake()
        {
            // 🔄（M3.x-A）**只建立自己寫的那條管道**。擁有權跟著寫入權走：
            //     Target 由本類別寫 → 本類別擁有其生命週期，注入給 Rig 讀；
            //     Pose 由 Rig 寫 → **Rig 擁有其生命週期**，本類別只是它的 Reader 之一。
            //     先前本類別同時 new 出 Pose（一份自己不寫的資料），是加第二個 Reader 時才浮現的所有權錯置。
            _targetData = new FootIKTargetData(); // 一次性配置，執行期零 GC

            // === 組裝期注入（僅此一次）：此後 Controller 與 Rig 之間只剩兩條單向共享數據，無任何方法呼叫 ===
            // Humanoid Avatar 的有效性由 AnimancerFacade.ValidateHierarchy 的既有 Fail-Fast 防線把關，此處不重複。
            var rig = GetComponentInChildren<FootIKRig>();
            if (rig == null)
            {
                Debug.LogError($"[{gameObject.name}] FootIKController 找不到 FootIKRig！" +
                    "Rig 必須掛在 Model 子物件（與 Animator 同物件——OnAnimatorIK 回呼的 Unity 硬性限制）。", this);
            }
            else
            {
                rig.Bind(_targetData);

                // Pose 是向 owner 取得引用，不是自己建立。Rig 以欄位初始式持有它，
                // 早於所有 Awake，故此處不需要關心 Rig 的 Awake 有沒有先跑。
                _poseData = rig.PoseData;
            }

            // 缺 Rig 時 _poseData 維持 null，由 Tick 開頭的既有防禦線接住（行為與變更前一致）。
        }

        private void Start()
        {
            // 開啟主層的 Animator IK pass（OnAnimatorIK 觸發前提）。經 Facade 而非直呼動畫系統。
            // 放在 Start：確保 AnimancerFacade.Awake（animancer 引用補洞＋層初始化）已完成。
            var facade = GetComponent<AnimationFacadeBase>();
            if (facade == null)
            {
                Debug.LogError($"[{gameObject.name}] FootIKController 找不到 AnimationFacadeBase，無法開啟 IK pass！", this);
            }
            else
            {
                facade.SetApplyAnimatorIK(0, true);
            }
        }

        public void Tick(PlayerRuntimeData data)
        {
            // IsWarm：快照尚未被 Rig 寫過（IK pass 未開／Animator 未評估）前不消費全零數據。
            if (_targetData == null || _poseData == null || !_poseData.IsWarm)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _debugLeftFoot = default;
                _debugRightFoot = default;
                UpdateRuntimeDebugLines();
#endif
                return;
            }

            // Root 原點＝腳底＝膠囊底（ADR-001＋CapsuleFitter §0.3 規則 6），可直接作為「地面平面」基準。
            float rootY = transform.position.y;

            // Q4：不特判狀態——空中 IsGrounded=false 自然關閉；Roll 中腳部蜷起由 pose 權重自然降低。
            // BlockIK 為讀取契約先行。🆕（輪 4）writer 已存在（ArbiterPipeline，順序 4.5），
            // 但目前沒有任何 IArbiterSource 要求 BlockIK，故現值仍恆 false，直到死亡等來源進場——
            // 屆時本檔零改動即生效。
            bool ikAllowed = data.IsGrounded && !data.Arbitration.BlockIK;

            // === ① 各腳採樣 ===
            FootSample left = SampleGround(_poseData.LeftFootPosition, _poseData.LeftFootRotation,
                _poseData.LeftFootBottomHeight, ikAllowed);
            FootSample right = SampleGround(_poseData.RightFootPosition, _poseData.RightFootRotation,
                _poseData.RightFootBottomHeight, ikAllowed);

            // === ② 骨盆補償：沉向較低的腳（夾限＋平滑）===
            float pelvisTarget = (ikAllowed && left.HasHit && right.HasHit)
                ? ComputePelvisOffset(left.GroundY, right.GroundY, rootY, settings.MaxPelvisOffset)
                : 0f;
            _targetData.PelvisOffsetY = Mathf.MoveTowards(_targetData.PelvisOffsetY, pelvisTarget, settings.PelvisSmoothSpeed * Time.deltaTime);

            // === ③ 各腳最終目標與權重 ===
            ResolveFoot(in left, _poseData.LeftFootPosition, _poseData.LeftFootRotation, _poseData.LeftFootBottomHeight,
                rootY, ikAllowed,
                ref _targetData.LeftFootPosition, ref _targetData.LeftFootRotation,
                ref _targetData.LeftFootPositionWeight, ref _targetData.LeftFootRotationWeight);

            ResolveFoot(in right, _poseData.RightFootPosition, _poseData.RightFootRotation, _poseData.RightFootBottomHeight,
                rootY, ikAllowed,
                ref _targetData.RightFootPosition, ref _targetData.RightFootRotation,
                ref _targetData.RightFootPositionWeight, ref _targetData.RightFootRotationWeight);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RecordFootDebug(
                in left,
                _poseData.LeftFootPosition,
                _targetData.LeftFootPosition,
                _targetData.LeftFootRotation,
                _targetData.LeftFootPositionWeight,
                _poseData.LeftFootBottomHeight,
                ref _debugLeftFoot);
            RecordFootDebug(
                in right,
                _poseData.RightFootPosition,
                _targetData.RightFootPosition,
                _targetData.RightFootRotation,
                _targetData.RightFootPositionWeight,
                _poseData.RightFootBottomHeight,
                ref _debugRightFoot);
            UpdateRuntimeDebugLines();
#endif
        }

        /// <summary>
        /// ① 單腳地面採樣：ankle ray 是高度與法線的唯一權威；L1 v2 的 Heel/Toe ray
        /// 只量測腳底平面的戳穿殘差。任一額外 ray 落空或 A/B 關閉時，保留 ankle ray 的幾何結果、
        /// 不做殘差抬升。全部使用 Physics.Raycast out 多載，無堆配置。
        /// </summary>
        private FootSample SampleGround(Vector3 posePosition, Quaternion poseRotation,
            float footBottomHeight, bool ikAllowed)
        {
            FootSample sample = default;
            if (!ikAllowed) return sample;

            sample = SampleSingleGround(posePosition, poseRotation, footBottomHeight);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            sample.DebugWasSampled = true;
            sample.DebugAnkleOrigin = posePosition + Vector3.up * settings.RaycastUpOffset;
#endif
            if (!sample.HasHit || !settings.UseTwoPointSampling) return sample;

            float heelOffset = Mathf.Max(0f, settings.HeelOffset);
            float toeOffset = Mathf.Max(0f, settings.ToeOffset);
            Vector3 localHeel = Vector3.forward * -heelOffset - Vector3.up * footBottomHeight;
            Vector3 localToe = Vector3.forward * toeOffset - Vector3.up * footBottomHeight;
            Vector3 worldHeel = sample.TargetPosition + sample.TargetRotation * localHeel;
            Vector3 worldToe = sample.TargetPosition + sample.TargetRotation * localToe;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            sample.DebugTwoPointAttempted = true;
            sample.DebugTwoPointBase = sample.TargetPosition;
            sample.DebugHeelOrigin = worldHeel + Vector3.up * settings.RaycastUpOffset;
            sample.DebugToeOrigin = worldToe + Vector3.up * settings.RaycastUpOffset;
            // Heel／Toe offset 實際使用的是 corrected foot basis 的 local +Z；直接記錄該次計算值供顯示。
            sample.DebugFootForward = sample.TargetRotation * Vector3.forward;
#endif

            bool heelHasHit = RaycastGround(
                worldHeel + Vector3.up * settings.RaycastUpOffset, out RaycastHit heelHit);
            bool toeHasHit = RaycastGround(
                worldToe + Vector3.up * settings.RaycastUpOffset, out RaycastHit toeHit);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            sample.DebugHeelHasHit = heelHasHit;
            sample.DebugToeHasHit = toeHasHit;
            sample.DebugHeelHitPoint = heelHit.point;
            sample.DebugToeHitPoint = toeHit.point;
#endif

            // 落空代表額外資訊不足，不是關 IK 的理由：保留已算好的 ankle-only 結果。
            if (!heelHasHit || !toeHasHit) return sample;

            sample.GroundY = ComputeSoleHeight(
                sample.TargetPosition,
                sample.SoleNormal,
                footBottomHeight,
                worldHeel,
                heelHit.point,
                worldToe,
                toeHit.point,
                out float lift);
            sample.TargetPosition.y += lift;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            sample.DebugResidualLift = lift;
#endif

            // 🔴 2026-09-08 probe 實測定位：舊版以「誰穿得深就取誰的地面 Y」作 GroundY。
            // lift 的 max() 本身連續，但 argmax 切換後再取另一個屬性（heel/toe ground Y）不連續；
            // 交叉時會跳約「heel-toe 跨距 × 坡度梯度」，平地因兩端等高才看不出來。
            // 骨盆真正需要的是解算後腳底平面高度，現由同一份連續 TargetPosition＋lift 導出，
            // 不以遲滯掩蓋斷點，也不再讓端點選擇決定骨盆高度。
            return sample;
        }

        private FootSample SampleSingleGround(Vector3 posePosition, Quaternion poseRotation,
            float footBottomHeight)
        {
            FootSample sample = default;
            Vector3 origin = posePosition + Vector3.up * settings.RaycastUpOffset;
            if (!RaycastGround(origin, out RaycastHit hit)) return sample;

            sample.HasHit = true;
            sample.HitPoint = hit.point;
            sample.Normal = hit.normal;
            sample.SoleNormal = ClampGroundNormal(hit.normal, settings.MaxFootAlignAngle);
            sample.TargetPosition = ComputeAnkleTarget(origin, hit.point, sample.SoleNormal, footBottomHeight);
            sample.TargetRotation = Quaternion.FromToRotation(Vector3.up, sample.SoleNormal) * poseRotation;
            sample.GroundY = (sample.TargetPosition - sample.SoleNormal * footBottomHeight).y;
            return sample;
        }

        private bool RaycastGround(Vector3 origin, out RaycastHit hit)
        {
            return Physics.Raycast(origin, Vector3.down, out hit, settings.RaycastDistance,
                settings.GroundLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// ③ 單腳最終求解（M3.1）：目標＋法線對齊旋轉＋單因子 Pose 權重 → MoveTowards 平滑。
        /// </summary>
        private void ResolveFoot(in FootSample sample, Vector3 posePosition, Quaternion poseRotation,
            float footBottomHeight, float rootY, bool ikAllowed,
            ref Vector3 targetPosition, ref Quaternion targetRotation,
            ref float positionWeight, ref float rotationWeight)
        {
            float goalWeight = 0f;
            if (ikAllowed && sample.HasHit)
            {
                // 位置由 ankle ray 的泰勒斯修正決定；Heel/Toe（若完整命中）只追加垂直戳穿殘差。
                // 因此腳踝維持動畫 XZ，不會被斜面法線水平推離原本的垂直 ray。
                targetPosition = sample.TargetPosition;

                // 腳掌對齊地面法線；基準是動畫原始 goal 旋轉（非骨骼現值）——無反饋、不累積。
                // 保留俯仰式（v1 凍結基線）：FromToRotation(worldUp, n) 只把世界 up 轉到法線，
                // 動畫腳踝自身的俯仰／roll 原樣保留——契合設計哲學「腳踝自由旋轉、不強制壓平」（design-doc §4.6）。
                // A/B 歸檔（v0.18.7）：軸對齊式（FromToRotation(poseUp, n)＝主動壓平腳底）實測與本式無感差
                // （踩地相動畫俯仰本就小、平地夾角 ~2°），依哲學回歸本式、軸對齊式棄用（見 changelog v0.18.7／roadmap L6）。
                // L1 v3：soleNormal 只限制真正超出踝關節上限的地面對齊量；位置、腳底平面與旋轉共用
                // 同一法線。超額坡度交由既有戳穿殘差抬升，形成一端接觸、另一端自然浮空。
                targetRotation = sample.TargetRotation;

                // 單因子權重＝Pose Heuristic（二態系統：窄帶外恆 0 或 1，腳不是全 IK 就是全動畫）。
                goalWeight = ComputeFootWeight(posePosition.y - rootY, settings.FootGroundedHeightMin, settings.FootGroundedHeightMax);
            }

            float step = settings.WeightSmoothSpeed * Time.deltaTime;
            positionWeight = Mathf.MoveTowards(positionWeight, goalWeight, step);
            rotationWeight = Mathf.MoveTowards(rotationWeight, goalWeight, step);
        }

        /// <summary>
        /// （純函數：Tick 與 EditMode 測試共用，比照 MotionBakeData.ComputeAverageSpeed 先例）
        /// Q3 Pose Heuristic：動畫腳部 goal 相對 Root 平面的高度 → 貼地權重。
        /// ≤ groundedMin 回 1（踩地）、≥ groundedMax 回 0（抬腳）、之間線性遞減。
        /// groundedMax ≤ groundedMin 的異常配置退化為以 groundedMin 硬切（防呆，不拋例外）。
        /// </summary>
        public static float ComputeFootWeight(float footHeightAboveRoot, float groundedMin, float groundedMax)
        {
            if (groundedMax <= groundedMin) return footHeightAboveRoot <= groundedMin ? 1f : 0f;
            return 1f - Mathf.InverseLerp(groundedMin, groundedMax, footHeightAboveRoot);
        }

        /// <summary>
        /// （純函數）把腳底對齊量限制在世界 up 起算的踝關節角度內。
        /// 夾限內與 180° A/B 模式原樣回傳，避免對 v2 路徑引入不必要的數值誤差。
        /// </summary>
        public static Vector3 ClampGroundNormal(Vector3 hitNormal, float maxAngleDegrees)
        {
            if (hitNormal.sqrMagnitude <= Mathf.Epsilon) return Vector3.up;
            if (maxAngleDegrees >= 180f) return hitNormal;

            float clampedMaxAngle = Mathf.Max(0f, maxAngleDegrees);
            if (Vector3.Angle(Vector3.up, hitNormal) <= clampedMaxAngle) return hitNormal;

            return Vector3.RotateTowards(Vector3.up, hitNormal,
                clampedMaxAngle * Mathf.Deg2Rad, 0f);
        }

        /// <summary>
        /// （純函數）由 ankle ray 命中與腳底高計算腳踝目標。移植 ozz-animation foot_ik 的
        /// UpdateAnklesTarget 幾何：沿法線保留腳底間隙，同時以泰勒斯修正抵消水平位移，
        /// 使結果留在原本的垂直 ray 上。平地或退化幾何回到命中點沿法線抬升。
        /// </summary>
        public static Vector3 ComputeAnkleTarget(Vector3 rayStart, Vector3 hitPoint,
            Vector3 hitNormal, float footBottomHeight)
        {
            float abLength = Vector3.Dot(rayStart - hitPoint, hitNormal);
            Vector3 b = rayStart - hitNormal * abLength;
            Vector3 ib = b - hitPoint;
            float ibLength = ib.magnitude;

            if (Mathf.Abs(abLength) <= Mathf.Epsilon || ibLength <= Mathf.Epsilon)
                return hitPoint + hitNormal * footBottomHeight;

            float ihLength = ibLength * footBottomHeight / abLength;
            Vector3 h = hitPoint + ib * (ihLength / ibLength);
            return h + hitNormal * footBottomHeight;
        }

        /// <summary>
        /// （純函數）量測 Heel/Toe 真實世界端點相對各自地面高度的最大戳穿量。
        /// 正值表示端點在地面下；只回傳向上的修正，不會因端點懸空而下壓。
        /// </summary>
        public static float ComputePenetrationLift(Vector3 worldHeel, Vector3 heelGroundPoint,
            Vector3 worldToe, Vector3 toeGroundPoint)
        {
            float heelPenetration = heelGroundPoint.y - worldHeel.y;
            float toePenetration = toeGroundPoint.y - worldToe.y;
            return Mathf.Max(0f, Mathf.Max(heelPenetration, toePenetration));
        }

        /// <summary>
        /// （純函數）由兩端戳穿量抬升腳踝後，回傳解算後的實際腳底平面高度。
        /// <c>max()</c> 對輸入連續，因此本結果在 heel／toe penetration 交叉時也連續；
        /// 不可改回以 argmax 選另一個端點的 ground Y——斜坡上兩端不等高，會產生跨距 × 坡度梯度的跳變。
        /// <paramref name="lift"/> 同行回傳，讓 TargetPosition 與 GroundY 消費同一份抬升真相且不重算。
        /// </summary>
        public static float ComputeSoleHeight(
            Vector3 ankleTargetBeforeLift,
            Vector3 soleNormal,
            float footBottomHeight,
            Vector3 worldHeel,
            Vector3 heelGroundPoint,
            Vector3 worldToe,
            Vector3 toeGroundPoint,
            out float lift)
        {
            lift = ComputePenetrationLift(
                worldHeel, heelGroundPoint, worldToe, toeGroundPoint);
            ankleTargetBeforeLift.y += lift;
            return (ankleTargetBeforeLift - soleNormal * footBottomHeight).y;
        }

        /// <summary>
        /// （純函數）Q2 骨盆補償：取雙腳地面命中點中較低者相對 Root 平面的差（恆 ≤0），
        /// 夾在 [-maxOffset, 0]——不可無限下降。
        /// 高於 Root 平面（上坡側）不上抬——骨盆只下沉、不上頂，抬升交給 CharacterController 的地面跟隨。
        /// </summary>
        public static float ComputePelvisOffset(float leftGroundY, float rightGroundY, float rootY, float maxOffset)
        {
            float lowest = Mathf.Min(leftGroundY, rightGroundY) - rootY;
            return Mathf.Clamp(lowest, -Mathf.Abs(maxOffset), 0f);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // O-2 Foot IK world-space observability（docs/18 §1.2）。
        // Game View 主通道＝runtime LineRenderer（不依賴 Gizmos 開關）；Scene View 詳查＝OnDrawGizmos。
        // 兩條通道都只讀 Tick 已完成的同一份快照，不發 Raycast、不重算 IK 結果。
        private struct FootDebugSnapshot
        {
            public FootSample Sample;
            public Vector3 PosePosition;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public float PositionWeight;
            public float FootBottomHeight;
        }

        [SerializeField] private bool drawFootIKRuntimeLines = true;
#if UNITY_EDITOR
        [SerializeField] private bool drawFootIKSceneGizmos = true;
#endif

        /// <summary>
        /// 🆕（2026-09-15）供 <c>RuntimeDebugPanel</c> 遠端切換 Foot IK 的可視化。
        ///
        /// ⚠️ **旗標的真相仍住在本元件**——Panel 只是遙控器，不持有狀態。
        /// Runtime lines（Game View）與 Scene gizmos（Scene View）**一起切**：
        /// 它們回答的是同一個問題（「腳為什麼擺成這樣」），只是畫在不同視窗。
        /// ⛔ 本屬性不計算任何東西，只是轉發 <c>bool</c>。
        /// </summary>
        internal bool DebugDrawFootIK
        {
            get => drawFootIKRuntimeLines;
            set
            {
                drawFootIKRuntimeLines = value;
#if UNITY_EDITOR
                drawFootIKSceneGizmos = value;
#endif
            }
        }

        private FootDebugSnapshot _debugLeftFoot;
        private FootDebugSnapshot _debugRightFoot;
        private readonly LineRenderer[] _debugRuntimeLines = new LineRenderer[48];
        private Transform _debugRuntimeRoot;
        private Material _debugRuntimeMaterial;

        private static readonly Color DebugAnkleRayColor = new Color(0.3f, 0.9f, 1f, 0.95f);
        private static readonly Color DebugHeelRayColor = new Color(1f, 0.65f, 0.2f, 0.95f);
        private static readonly Color DebugToeRayColor = new Color(0.45f, 0.65f, 1f, 0.95f);
        private static readonly Color DebugRawNormalColor = new Color(1f, 0.9f, 0.15f, 0.95f);
        private static readonly Color DebugSoleNormalColor = new Color(0.25f, 1f, 0.45f, 0.95f);
        private static readonly Color DebugTargetColor = new Color(1f, 0.3f, 0.9f, 0.95f);
        private static readonly Color DebugMissColor = new Color(1f, 0.2f, 0.15f, 0.95f);
        private static readonly Color DebugHeelToeAxisColor = new Color(0.12f, 0.12f, 0.12f, 0.95f);
        private static readonly Color DebugFootForwardColor = new Color(0.1f, 1f, 0.75f, 0.95f);
        private static readonly Color DebugBasePointColor = new Color(1f, 1f, 1f, 0.95f);

        private const float DebugPointRadius = 0.025f;
        private const float DebugNormalLength = 0.22f;
        private const float DebugPoseAxisLength = 0.12f;
        private const float DebugFootForwardLength = 0.25f;
        private const float DebugArrowHeadLength = 0.055f;
        private const float DebugArrowHeadWidth = 0.035f;
        private const float DebugLabelHeight = 0.04f;
        private const float DebugRuntimeLineWidth = 0.018f;

        private static void RecordFootDebug(
            in FootSample sample,
            Vector3 posePosition,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float positionWeight,
            float footBottomHeight,
            ref FootDebugSnapshot snapshot)
        {
            snapshot.Sample = sample;
            snapshot.PosePosition = posePosition;
            snapshot.TargetPosition = targetPosition;
            snapshot.TargetRotation = targetRotation;
            snapshot.PositionWeight = positionWeight;
            snapshot.FootBottomHeight = footBottomHeight;
        }

        /// <summary>
        /// Game View 主通道：建立真正的 Renderer 幾何，因此正常 Play 時不依賴 Gizmos 開關。
        /// LineRenderer 與 Material 只在首次需要時建立，之後逐幀覆寫兩個端點，沒有持續配置。
        /// </summary>
        private void UpdateRuntimeDebugLines()
        {
            int lineCount = 0;
            if (drawFootIKRuntimeLines)
            {
                AppendFootRuntimeLines(in _debugLeftFoot, ref lineCount);
                AppendFootRuntimeLines(in _debugRightFoot, ref lineCount);
            }

            for (int i = lineCount; i < _debugRuntimeLines.Length; i++)
            {
                if (_debugRuntimeLines[i] != null) _debugRuntimeLines[i].enabled = false;
            }
        }

        private void AppendFootRuntimeLines(in FootDebugSnapshot snapshot, ref int lineCount)
        {
            FootSample sample = snapshot.Sample;
            if (!sample.DebugWasSampled) return;

            AppendRuntimeProbe(sample.DebugAnkleOrigin, sample.HasHit, sample.HitPoint,
                DebugAnkleRayColor, ref lineCount);
            if (sample.HasHit)
            {
                AppendRuntimeVector(sample.HitPoint, sample.Normal, DebugRawNormalColor, ref lineCount);
                AppendRuntimeVector(sample.HitPoint, sample.SoleNormal, DebugSoleNormalColor, ref lineCount);
            }

            if (sample.DebugTwoPointAttempted)
            {
                AppendRuntimeLine(sample.DebugHeelOrigin, sample.DebugToeOrigin,
                    DebugHeelToeAxisColor, ref lineCount);
                AppendRuntimeArrow(sample.DebugTwoPointBase, sample.DebugFootForward,
                    DebugFootForwardColor, ref lineCount);

                AppendRuntimeProbe(sample.DebugHeelOrigin, sample.DebugHeelHasHit,
                    sample.DebugHeelHitPoint, DebugHeelRayColor, ref lineCount);
                AppendRuntimeProbe(sample.DebugToeOrigin, sample.DebugToeHasHit,
                    sample.DebugToeHitPoint, DebugToeRayColor, ref lineCount);
            }

            // Ankle miss 時沒有本幀 target；不可把上一幀正在淡出的 TargetData 畫成新決策。
            if (!sample.HasHit) return;

            Color targetColor = DebugTargetColor;
            targetColor.a = Mathf.Lerp(0.2f, DebugTargetColor.a, snapshot.PositionWeight);
            AppendRuntimeLine(snapshot.PosePosition, snapshot.TargetPosition, targetColor, ref lineCount);

            Vector3 soleCenter = snapshot.TargetPosition -
                                 snapshot.TargetRotation * Vector3.up * snapshot.FootBottomHeight;
            Vector3 targetRight = snapshot.TargetRotation * Vector3.right * DebugPoseAxisLength;
            Vector3 targetForward = snapshot.TargetRotation * Vector3.forward * DebugPoseAxisLength;
            AppendRuntimeLine(soleCenter - targetRight, soleCenter + targetRight, targetColor, ref lineCount);
            AppendRuntimeLine(soleCenter - targetForward, soleCenter + targetForward, targetColor, ref lineCount);

            if (sample.DebugResidualLift > 0f)
            {
                AppendRuntimeLine(
                    snapshot.TargetPosition - Vector3.up * sample.DebugResidualLift,
                    snapshot.TargetPosition,
                    targetColor,
                    ref lineCount);
            }
        }

        private void AppendRuntimeProbe(
            Vector3 origin,
            bool hasHit,
            Vector3 hitPoint,
            Color rayColor,
            ref int lineCount)
        {
            Vector3 end = hasHit ? hitPoint : origin + Vector3.down * settings.RaycastDistance;
            Color pointColor = hasHit ? rayColor : DebugMissColor;
            AppendRuntimeLine(origin, end, pointColor, ref lineCount);

            // Runtime 通道沒有 Gizmos sphere，以十字明確標出實際 hit point；miss 則標在完整 query 終點。
            Vector3 horizontal = Vector3.right * DebugPointRadius;
            Vector3 vertical = Vector3.up * DebugPointRadius;
            AppendRuntimeLine(end - horizontal, end + horizontal, pointColor, ref lineCount);
            AppendRuntimeLine(end - vertical, end + vertical, pointColor, ref lineCount);
        }

        private void AppendRuntimeVector(
            Vector3 origin,
            Vector3 direction,
            Color color,
            ref int lineCount)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon) return;
            AppendRuntimeLine(origin, origin + direction.normalized * DebugNormalLength, color, ref lineCount);
        }

        private void AppendRuntimeArrow(
            Vector3 origin,
            Vector3 direction,
            Color color,
            ref int lineCount)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon) return;

            Vector3 normalized = direction.normalized;
            Vector3 end = origin + normalized * DebugFootForwardLength;
            Vector3 side = Vector3.Cross(normalized, Vector3.up);
            if (side.sqrMagnitude <= Mathf.Epsilon) side = Vector3.Cross(normalized, Vector3.right);
            side = side.normalized * DebugArrowHeadWidth;
            Vector3 back = -normalized * DebugArrowHeadLength;

            AppendRuntimeLine(origin, end, color, ref lineCount);
            AppendRuntimeLine(end, end + back + side, color, ref lineCount);
            AppendRuntimeLine(end, end + back - side, color, ref lineCount);
        }

        private void AppendRuntimeLine(Vector3 start, Vector3 end, Color color, ref int lineCount)
        {
            if (lineCount >= _debugRuntimeLines.Length) return;

            LineRenderer line = GetOrCreateRuntimeLine(lineCount);
            if (line == null) return;
            lineCount++;

            line.enabled = true;
            line.startColor = color;
            line.endColor = color;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        private LineRenderer GetOrCreateRuntimeLine(int index)
        {
            LineRenderer existing = _debugRuntimeLines[index];
            if (existing != null) return existing;

            if (_debugRuntimeMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) return null;

                _debugRuntimeMaterial = new Material(shader)
                {
                    name = "Foot IK Debug Lines (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            if (_debugRuntimeRoot == null)
            {
                var root = new GameObject("Foot IK Debug Lines")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _debugRuntimeRoot = root.transform;
                _debugRuntimeRoot.SetParent(transform, false);
            }

            var lineObject = new GameObject($"Line {index}")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = gameObject.layer,
            };
            lineObject.transform.SetParent(_debugRuntimeRoot, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = _debugRuntimeMaterial;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = DebugRuntimeLineWidth;
            line.endWidth = DebugRuntimeLineWidth;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            _debugRuntimeLines[index] = line;
            return line;
        }

        private void OnDestroy()
        {
            if (_debugRuntimeMaterial == null) return;

            if (Application.isPlaying) Destroy(_debugRuntimeMaterial);
            else DestroyImmediate(_debugRuntimeMaterial);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawFootIKSceneGizmos || !Application.isPlaying) return;

            DrawFootGizmos(in _debugLeftFoot, "L BASE", "L HEEL", "L TOE");
            DrawFootGizmos(in _debugRightFoot, "R BASE", "R HEEL", "R TOE");
        }

        private void DrawFootGizmos(
            in FootDebugSnapshot snapshot,
            string baseLabel,
            string heelLabel,
            string toeLabel)
        {
            FootSample sample = snapshot.Sample;
            if (!sample.DebugWasSampled) return;

            DrawProbeGizmo(sample.DebugAnkleOrigin, sample.HasHit, sample.HitPoint, DebugAnkleRayColor);
            if (sample.HasHit)
            {
                DrawVectorGizmo(sample.HitPoint, sample.Normal, DebugRawNormalColor);
                DrawVectorGizmo(sample.HitPoint, sample.SoleNormal, DebugSoleNormalColor);
            }

            if (sample.DebugTwoPointAttempted)
            {
                DrawHeelToeOriginsGizmo(in sample, baseLabel, heelLabel, toeLabel);

                DrawProbeGizmo(sample.DebugHeelOrigin, sample.DebugHeelHasHit,
                    sample.DebugHeelHitPoint, DebugHeelRayColor);
                DrawProbeGizmo(sample.DebugToeOrigin, sample.DebugToeHasHit,
                    sample.DebugToeHitPoint, DebugToeRayColor);
            }

            // Ankle miss 時沒有本幀 target；不可把上一幀正在淡出的 TargetData 畫成新決策。
            if (!sample.HasHit) return;

            // 從動畫的 pre-IK goal 連到實際交給 Rig 的 target；權重越低，顯示越透明。
            Color targetColor = DebugTargetColor;
            targetColor.a = Mathf.Lerp(0.2f, DebugTargetColor.a, snapshot.PositionWeight);
            Gizmos.color = targetColor;
            Gizmos.DrawLine(snapshot.PosePosition, snapshot.TargetPosition);
            Gizmos.DrawWireSphere(snapshot.TargetPosition, DebugPointRadius * 1.35f);

            Vector3 soleCenter = snapshot.TargetPosition -
                                 snapshot.TargetRotation * Vector3.up * snapshot.FootBottomHeight;
            Vector3 targetRight = snapshot.TargetRotation * Vector3.right * DebugPoseAxisLength;
            Vector3 targetForward = snapshot.TargetRotation * Vector3.forward * DebugPoseAxisLength;
            Gizmos.DrawLine(soleCenter - targetRight, soleCenter + targetRight);
            Gizmos.DrawLine(soleCenter - targetForward, soleCenter + targetForward);

            if (sample.DebugResidualLift > 0f)
            {
                Gizmos.DrawLine(
                    snapshot.TargetPosition - Vector3.up * sample.DebugResidualLift,
                    snapshot.TargetPosition);
            }
        }

        private static void DrawHeelToeOriginsGizmo(
            in FootSample sample,
            string baseLabel,
            string heelLabel,
            string toeLabel)
        {
            // 實心點＝production 計算真正使用的 base／ray origins；既有 wire sphere 則是 hit／miss end。
            Gizmos.color = DebugBasePointColor;
            Gizmos.DrawSphere(sample.DebugTwoPointBase, DebugPointRadius * 0.8f);
            Gizmos.color = DebugHeelRayColor;
            Gizmos.DrawSphere(sample.DebugHeelOrigin, DebugPointRadius * 0.65f);
            Gizmos.DrawLine(sample.DebugTwoPointBase, sample.DebugHeelOrigin);
            Gizmos.color = DebugToeRayColor;
            Gizmos.DrawSphere(sample.DebugToeOrigin, DebugPointRadius * 0.65f);
            Gizmos.DrawLine(sample.DebugTwoPointBase, sample.DebugToeOrigin);

            Gizmos.color = DebugHeelToeAxisColor;
            Gizmos.DrawLine(sample.DebugHeelOrigin, sample.DebugToeOrigin);
            DrawArrowGizmo(sample.DebugTwoPointBase, sample.DebugFootForward, DebugFootForwardColor);

            Vector3 labelOffset = Vector3.up * DebugLabelHeight;
            UnityEditor.Handles.Label(sample.DebugTwoPointBase + labelOffset, baseLabel);
            UnityEditor.Handles.Label(sample.DebugHeelOrigin + labelOffset, heelLabel);
            UnityEditor.Handles.Label(sample.DebugToeOrigin + labelOffset, toeLabel);
        }

        private void DrawProbeGizmo(Vector3 origin, bool hasHit, Vector3 hitPoint, Color rayColor)
        {
            Vector3 end = hasHit ? hitPoint : origin + Vector3.down * settings.RaycastDistance;
            Gizmos.color = hasHit ? rayColor : DebugMissColor;
            Gizmos.DrawLine(origin, end);
            Gizmos.DrawWireSphere(end, DebugPointRadius);

            if (!hasHit)
            {
                Vector3 cross = Vector3.one * DebugPointRadius;
                Gizmos.DrawLine(end - cross, end + cross);
                cross.x = -cross.x;
                Gizmos.DrawLine(end - cross, end + cross);
            }
        }

        private static void DrawVectorGizmo(Vector3 origin, Vector3 direction, Color color)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon) return;
            Gizmos.color = color;
            Gizmos.DrawLine(origin, origin + direction.normalized * DebugNormalLength);
        }

        private static void DrawArrowGizmo(Vector3 origin, Vector3 direction, Color color)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon) return;

            Vector3 normalized = direction.normalized;
            Vector3 end = origin + normalized * DebugFootForwardLength;
            Vector3 side = Vector3.Cross(normalized, Vector3.up);
            if (side.sqrMagnitude <= Mathf.Epsilon) side = Vector3.Cross(normalized, Vector3.right);
            side = side.normalized * DebugArrowHeadWidth;
            Vector3 back = -normalized * DebugArrowHeadLength;

            Gizmos.color = color;
            Gizmos.DrawLine(origin, end);
            Gizmos.DrawLine(end, end + back + side);
            Gizmos.DrawLine(end, end + back - side);
        }
#endif
#endif
    }
}
