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

        // 🆕（M3.x-C）地面觀測的 **lifetime owner**：本類別是它的唯一 Writer（擁有權跟著寫入權走）。
        // 以**欄位初始式**建立（比照 FootIKRig 持有 _poseData 的先例）：初始式早於所有 Awake，
        // 因此任何讀取方都不需要關心元件之間的 Awake 順序——「拿得到同一份實例」是結構保證，不是時序紀律。
        private readonly FootIKGroundData _groundData = new FootIKGroundData();

        /// <summary>
        /// 🆕（M3.x-C）本帧地面觀測（唯讀共享）。本類別為**唯一 Writer**；回傳引用而非拷貝。
        /// 現有讀取方＝<c>FootstepDetector</c>。時序保證與讀取方契約見 <see cref="FootIKGroundData"/>。
        /// ⚠️ 這**不是**把 Foot IK 的決策開放出去——公開的只有「本帧腳下有什麼」這個物理觀測，
        /// 權重／目標／骨盆等決策仍然完全私有。
        /// </summary>
        public FootIKGroundData GroundData => _groundData;

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

            /// <summary>🆕（M3.x-C）命中的碰撞體。IK 本身不使用，純粹沿用同一次 raycast 的結果轉發給 <see cref="FootIKGroundData"/>。</summary>
            public Collider HitCollider;

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

        /// <summary>身體參考面的 8 個世界固定取樣方向（與角色朝向無關，docs/05 §3.5.4.3）。唯讀、只配置一次。</summary>
        private static readonly Vector2[] ReferenceDirections =
        {
            new Vector2(1f, 0f), new Vector2(0.70710678f, 0.70710678f), new Vector2(0f, 1f), new Vector2(-0.70710678f, 0.70710678f),
            new Vector2(-1f, 0f), new Vector2(-0.70710678f, -0.70710678f), new Vector2(0f, -1f), new Vector2(0.70710678f, -0.70710678f),
        };

        /// <summary>root 支撐查詢往下的額外距離（公尺）：起點在 root 上方 RaycastUpOffset，往下打到 root 下方這麼深。</summary>
        private const float RootSupportProbeDepth = 0.2f;

        /// <summary>角色移動真正會碰的世界（與 CharacterController.Move 同一份碰撞矩陣推導）。Awake 快取一次。</summary>
        private int _movementMask;

        // 🧪（T3 原型，docs/45 §6–§7）每隻腳刻意跨帧保存的接觸／鎖腳狀態（只存數值）。原型關閉時每帧重設。
        private FootContactState _leftContact;
        private FootContactState _rightContact;

        // 🧪（站直原型，docs/05 §3.5.4.6）姿勢狀態與它需要的一帧歷史（只存數值）。原型關閉時每帧重設。
        private PostureState _posture;
        private Vector3 _postureRoot;
        private Vector3 _postureLeftHip;
        private Vector3 _postureRightHip;
        private bool _postureHasHistory;

        private void ResetPosture()
        {
            _posture.Reset();
            _postureHasHistory = false;
        }

        private void Awake()
        {
            _movementMask = MotionDriver.MovementCollisionMask(gameObject.layer);

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
                _leftContact.Reset();
                _rightContact.Reset();
                ResetPosture();
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

            // === ①' 轉發地面觀測（🆕 M3.x-C）===
            // 刻意放在採樣之後、任何決策之前：公開的是**觀測**，不是 IK 的結論。
            // 整體覆寫即復位——沒有命中時 SampleGround 回傳 default，HasGround 自然為 false。
            PublishGroundContact(in left, ref _groundData.Left);
            PublishGroundContact(in right, ref _groundData.Right);
            _groundData.IsWarm = true;

            // === ①'' 支撐量（stance）：動畫裡這隻腳離 pose 平面多高 ===
            // ⚠️ 必須用 pose **同一時刻**的平面（PosePlaneY），不是本帧已被 LateUpdate 移動過的 rootY
            //    ——在樓梯移動面上兩者每帧差 5–8 cm（`docs/44` §9.5）。
            //    同一個量同時決定 IK 權重（③）與骨盆貢獻（②），兩者對「哪隻腳在支撐」不會各說各話。
            float posePlaneY = _poseData.PosePlaneY;
            float leftStance = ComputeFootWeight(_poseData.LeftFootPosition.y - posePlaneY,
                settings.FootGroundedHeightMin, settings.FootGroundedHeightMax);
            float rightStance = ComputeFootWeight(_poseData.RightFootPosition.y - posePlaneY,
                settings.FootGroundedHeightMin, settings.FootGroundedHeightMax);

            // === ② 骨盆補償 ===
            // 🔄（2026-09-24，`docs/44` §9.6）取代「雙腳都命中才補、取較低者」：
            //    擺盪腳的 ray 會先探到下一階（樓梯下行時更低），舊規則讓它整個拉動骨盆 ⇒ 一級一個的鋸齒。
            //    現在每隻腳只按它**正在支撐的程度**要求骨盆下沉；擺盪腳仍照常取得踏面做 IK 落點（③不變）。
            //    ⛔ 不是新的 HasGround 閘門：沒有命中的腳只是「沒有下沉需求」，不會讓整個骨盆歸零。
            float pelvisTarget = 0f;
            if (ikAllowed)
            {
                float supportNeed = ComputeSupportedPelvisOffset(
                    left.HasHit, left.GroundY, leftStance,
                    right.HasHit, right.GroundY, rightStance,
                    rootY, settings.MaxPelvisOffset);
                pelvisTarget = supportNeed;

                // 🆕（2026-09-24，docs/05 §3.5.4.3）在代理移動面（樓梯）上，身體改為跟隨參考面包絡。
                //    上面的逐腳需求在樓梯上必然隨步伐週期擺盪（實測：髖骨 dv95 4.7–7.3 m/s），
                //    不論怎麼加權都一樣——需求必須來自地形本身，而不是腳現在在哪。
                if (settings.ReferenceSampleRadius > 0f)
                {
                    float referenceOffset = ComputeReferenceOffset(rootY, out bool rootOnProxy);
                    pelvisTarget = CombinePelvisTarget(referenceOffset, rootOnProxy, supportNeed);
                }
            }
            // PresentationPipeline 直接呼叫 Tick、不看元件是否啟用（Codex Medium 1）：停用時 T3 狀態必須每帧維持重設，
            // 不能只靠 OnDisable 那一次。舊路徑的行為維持原樣。
            bool contact = settings.UseContactPrototype && isActiveAndEnabled;
            // 🧪 站直原型需要 T3（本帧的腳目標與即時接觸）；關閉時骨盆照舊在這裡更新（逐位元不變）。
            bool posture = contact && settings.UseStandingPosturePrototype;
            if (!posture)
            {
                _targetData.PelvisOffsetY = Mathf.MoveTowards(_targetData.PelvisOffsetY, pelvisTarget, settings.PelvisSmoothSpeed * Time.deltaTime);
                ResetPosture();
            }

            // === ③ 各腳最終目標與權重 ===
            ResolveFoot(in left, _poseData.LeftFootRotation, _poseData.LeftFootBottomHeight,
                leftStance, ikAllowed, contact,
                ref _targetData.LeftFootPosition, ref _targetData.LeftFootRotation,
                ref _targetData.LeftFootPositionWeight, ref _targetData.LeftFootRotationWeight);

            ResolveFoot(in right, _poseData.RightFootRotation, _poseData.RightFootBottomHeight,
                rightStance, ikAllowed, contact,
                ref _targetData.RightFootPosition, ref _targetData.RightFootRotation,
                ref _targetData.RightFootPositionWeight, ref _targetData.RightFootRotationWeight);

            // === ③' 🧪 T3：位置目標改由接觸構建（旋轉維持 ③ 的結果）===
            if (contact)
            {
                ResolveContactPosition(ref _leftContact, _poseData.LeftFootPosition, _poseData.LeftFootRotation,
                    _poseData.LeftFootBottomHeight, leftStance, ikAllowed, posePlaneY,
                    _targetData.LeftFootRotation, _targetData.LeftFootRotationWeight,
                    ref _targetData.LeftFootPosition, ref _targetData.LeftFootPositionWeight,
                    out float leftFootSpeed, out bool leftLive);
                ResolveContactPosition(ref _rightContact, _poseData.RightFootPosition, _poseData.RightFootRotation,
                    _poseData.RightFootBottomHeight, rightStance, ikAllowed, posePlaneY,
                    _targetData.RightFootRotation, _targetData.RightFootRotationWeight,
                    ref _targetData.RightFootPosition, ref _targetData.RightFootPositionWeight,
                    out float rightFootSpeed, out bool rightLive);

                // === ④ 🧪 站直原型：必須在 T3 解完本帧兩腳之後（Codex High）——與本帧寫出的腳目標一起在下一帧套用 ===
                if (posture)
                {
                    ResolveStandingPosture(data, ikAllowed, pelvisTarget, leftStance, rightStance,
                        leftFootSpeed, rightFootSpeed, leftLive, rightLive);
                }
            }
            else
            {
                _leftContact.Reset();
                _rightContact.Reset();
            }

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
        /// ①' 把單腳採樣轉成對外的地面觀測。純欄位搬運，無任何判斷——
        /// 「腳踩到了沒有」的解讀屬各 Reader 自己的事（同 <c>FootIKRig</c> 對 Pose 快照的立場）。
        /// </summary>
        private static void PublishGroundContact(in FootSample sample, ref FootGroundContact contact)
        {
            contact.HasGround = sample.HasHit;
            contact.Point = sample.HitPoint;
            contact.Normal = sample.Normal;
            contact.GroundY = sample.GroundY;
            contact.Collider = sample.HitCollider;
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
            sample.HitCollider = hit.collider; // 🆕（M3.x-C）IK 不用，轉發給 FootIKGroundData（Stage 2 Surface Resolver 的接點）
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
        private void ResolveFoot(in FootSample sample, Quaternion poseRotation,
            float footBottomHeight, float stanceWeight, bool ikAllowed, bool contactOwnsPosition,
            ref Vector3 targetPosition, ref Quaternion targetRotation,
            ref float positionWeight, ref float rotationWeight)
        {
            float goalWeight = 0f;
            if (ikAllowed && sample.HasHit)
            {
                // 位置由 ankle ray 的泰勒斯修正決定；Heel/Toe（若完整命中）只追加垂直戳穿殘差。
                // 因此腳踝維持動畫 XZ，不會被斜面法線水平推離原本的垂直 ray。
                // 🧪 T3 開啟時位置由 ResolveContactPosition 決定（本函式只負責旋轉，Codex High 2）。
                if (!contactOwnsPosition) targetPosition = sample.TargetPosition;

                // 腳掌對齊地面法線；基準是動畫原始 goal 旋轉（非骨骼現值）——無反饋、不累積。
                // 保留俯仰式（v1 凍結基線）：FromToRotation(worldUp, n) 只把世界 up 轉到法線，
                // 動畫腳踝自身的俯仰／roll 原樣保留——契合設計哲學「腳踝自由旋轉、不強制壓平」（design-doc §4.6）。
                // A/B 歸檔（v0.18.7）：軸對齊式（FromToRotation(poseUp, n)＝主動壓平腳底）實測與本式無感差
                // （踩地相動畫俯仰本就小、平地夾角 ~2°），依哲學回歸本式、軸對齊式棄用（見 changelog v0.18.7／roadmap L6）。
                // L1 v3：soleNormal 只限制真正超出踝關節上限的地面對齊量；位置、腳底平面與旋轉共用
                // 同一法線。超額坡度交由既有戳穿殘差抬升，形成一端接觸、另一端自然浮空。
                targetRotation = sample.TargetRotation;

                // 單因子權重＝Pose Heuristic（二態系統：窄帶外恆 0 或 1，腳不是全 IK 就是全動畫）。
                // 🔄（2026-09-24）stance 由 Tick 以 pose 同時刻的平面算好傳入（見 ①''）。
                goalWeight = stanceWeight;
            }

            float step = settings.WeightSmoothSpeed * Time.deltaTime;
            if (!contactOwnsPosition) positionWeight = Mathf.MoveTowards(positionWeight, goalWeight, step);
            rotationWeight = Mathf.MoveTowards(rotationWeight, goalWeight, step);
        }

        /// <summary>
        /// 🧪（T3 原型，docs/45 §6–§7）位置目標的接觸構建。旋轉目標與旋轉權重由 <see cref="ResolveFoot"/> 決定、這裡只讀。
        ///
        /// ① 一帧外插 pose（目標下一帧才套用；位置權重為 1 時不外插會讓擺盪腳落後一帧）。
        /// ② β ＝ 支撐量 × 速度相位；鎖腳狀態轉移 ⇒ 本帧目標水平位置（動畫／鎖定點／釋放混合）。
        /// ③ 擺盪參考：該水平位置下方的**移動面** ＋ 動畫離地高度（樓梯上是踏面前緣連線的包絡，不會穿進台階）。
        /// ④ 帶朝向的最小抬升：以即將顯示的腳朝向（pose 旋轉朝 IK 旋轉目標依旋轉權重插值）取腳跟／腳踝底／腳尖，
        ///    各自下方查 <c>GroundLayers</c>；穿透一律抬起，懸空只依 β 下拉。
        /// 失效（IK 不允許）⇒ 狀態整體重設、位置權重平滑歸零；不連續（瞬移）⇒ 鎖腳重設、這一帧不外插。
        /// </summary>
        private void ResolveContactPosition(ref FootContactState state, Vector3 posePosition, Quaternion poseRotation,
            float footBottomHeight, float stance, bool ikAllowed, float posePlaneY,
            Quaternion rotationTarget, float rotationWeight,
            ref Vector3 targetPosition, ref float positionWeight,
            out float animatedFootSpeed, out bool liveContact)
        {
            float dt = Time.deltaTime;
            float weightStep = settings.WeightSmoothSpeed * dt;
            animatedFootSpeed = 0f;
            liveContact = false;
            if (!ikAllowed)
            {
                state.Reset();
                positionWeight = Mathf.MoveTowards(positionWeight, 0f, weightStep);
                return;
            }

            // ①
            bool continuous = FootContactSolver.PredictPose(ref state, posePosition, posePosition.y - posePlaneY,
                dt, settings.MaxPoseStepPerFrame, out Vector3 predicted, out float predictedHeight, out float footSpeed);
            if (!continuous)
            {
                state.Phase = FootLockPhase.Unlocked;
                state.ReleaseBlend = 0f;
                state.PrevContactValid = false;
            }

            // ②
            float beta = FootContactSolver.ComputeContactBeta(stance, footSpeed,
                settings.ContactPhaseSpeedMin, settings.ContactPhaseSpeedMax);
            FootLockPhase phaseBefore = state.Phase;
            FootContactSolver.StepLock(ref state, beta, predicted, dt,
                settings.LockBeta, settings.UnlockBeta, settings.UnlockRadius, settings.ReleaseTime);
            // 鎖定當帧：支撐尚未選定，先用即時查詢，再把決定抬升的那一點下方的地面記成鎖定支撐。
            bool supportSelected = state.Phase == FootLockPhase.Locked && phaseBefore == FootLockPhase.Locked;
            Vector3 horizontal = FootContactSolver.ResolveHorizontal(in state, predicted);

            // ③ 移動面沒命中（例如腳懸在平台邊緣外）⇒ 退回 pose 平面（＝動畫原本的高度）。
            //    起點用 pose 平面、不加動畫腳高：擺盪腳抬高時起點若跟著抬高，射線會搆不到後腳下方的移動面
            //    （上樓後腳在 root 下方 0.3–0.4 m），落空退回 pose 平面 ⇒ 目標跳 25–55 cm（第一次 Play 量測）。
            //    起點再加上「到 root 的水平距離」（45° 錐）：腳離 root 0.7 m 以上時（後腳、鎖定中的腳），
            //    上坡側的移動面會高過 pose 平面 ＋ 0.5，起點落在斜面裡面 ⇒ 射線從內部出發、打不到它（第二次 Play 量測）。
            float up = settings.RaycastUpOffset;
            Vector3 root = transform.position;
            float reach = new Vector2(horizontal.x - root.x, horizontal.z - root.z).magnitude;
            float probeDistance = up + settings.RaycastDistance;
            float referenceY = posePlaneY;
            if (Physics.Raycast(new Vector3(horizontal.x, posePlaneY + up + reach, horizontal.z), Vector3.down,
                    out RaycastHit referenceHit, probeDistance + 2f * reach, _movementMask, QueryTriggerInteraction.Ignore))
            {
                referenceY = referenceHit.point.y;
            }
            Vector3 ankle = new Vector3(horizontal.x, referenceY + predictedHeight, horizontal.z);

            // ④
            Quaternion shownRotation = Quaternion.Slerp(poseRotation, rotationTarget, rotationWeight);
            float contactDelta = float.NegativeInfinity;
            float supportY = 0f;
            int supportPoint = 0;
            bool hasContact = false; // 有可用於求解的地面（含鎖定支撐）
            bool liveHit = false;    // 本帧真的有射線命中——只有它能決定「接觸是否有效」
            AccumulateContact(in state, supportSelected, 0, ankle, shownRotation, footBottomHeight, -Mathf.Max(0f, settings.HeelOffset),
                probeDistance, ref contactDelta, ref supportY, ref supportPoint, ref hasContact, ref liveHit);
            AccumulateContact(in state, supportSelected, 1, ankle, shownRotation, footBottomHeight, 0f,
                probeDistance, ref contactDelta, ref supportY, ref supportPoint, ref hasContact, ref liveHit);
            AccumulateContact(in state, supportSelected, 2, ankle, shownRotation, footBottomHeight, Mathf.Max(0f, settings.ToeOffset),
                probeDistance, ref contactDelta, ref supportY, ref supportPoint, ref hasContact, ref liveHit);
            if (state.Phase == FootLockPhase.Locked && !supportSelected && hasContact)
            {
                state.LockedSupportY = supportY;
                state.LockedSupportPoint = supportPoint;
            }

            float lift = hasContact ? FootContactSolver.ComputeMinimumLift(contactDelta, beta) : 0f;
            float height = FootContactSolver.ResolveHeight(in state, ankle.y + lift, hasContact, ankle.y + contactDelta);
            if (state.Phase == FootLockPhase.Locked) state.LockedHeight = height;
            targetPosition = new Vector3(ankle.x, height, ankle.z);
            positionWeight = Mathf.MoveTowards(positionWeight, 1f, weightStep);

            state.PrevContactValid = liveHit;
            state.DebugBeta = beta;
            state.DebugLift = lift;
            animatedFootSpeed = footSpeed;
            liveContact = liveHit;
        }

        /// <summary>
        /// 🧪 T3：一個腳底端點下方的地面（<c>GroundLayers</c>；鎖定中另以鎖定支撐為下限），累積最大有號穿透量，
        /// 並記下決定它的那一點的地面高度（鎖定當帧用來選定支撐）。
        /// </summary>
        private void AccumulateContact(in FootContactState state, bool supportSelected, int pointIndex, Vector3 ankle, Quaternion rotation,
            float footBottomHeight, float forwardOffset, float distance,
            ref float contactDelta, ref float supportY, ref int supportPoint, ref bool hasContact, ref bool liveHit)
        {
            Vector3 point = FootContactSolver.SolePoint(ankle, rotation, footBottomHeight, forwardOffset);
            Vector3 origin = new Vector3(point.x, Mathf.Max(point.y, ankle.y) + settings.RaycastUpOffset, point.z);
            bool hasHit = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance,
                settings.GroundLayers, QueryTriggerInteraction.Ignore);
            liveHit |= hasHit;
            if (!FootContactSolver.ContactGround(in state, supportSelected, pointIndex, hasHit, hasHit ? hit.point.y : 0f,
                    out float groundY)) return;
            float delta = groundY - point.y;
            if (delta > contactDelta)
            {
                contactDelta = delta;
                supportY = groundY;
                supportPoint = pointIndex;
            }
            hasContact = true;
        }

        /// <summary>
        /// 🧪（站直原型，docs/05 §3.5.4.6）Moving → Settling → Upright → Return → Moving。
        /// 輸入全是本帧的事實：包絡目標 E（既有）、本帧 T3 腳目標、同時刻的動畫髖位（一帧外插，與腳目標同一套用帧）、
        /// root 與動畫腳速度、兩腳即時接觸、移動意圖（黑板唯讀）。⛔ 不讀 FSM 狀態名稱、不動鎖腳（V1 不重新落定）。
        /// </summary>
        private void ResolveStandingPosture(PlayerRuntimeData data, bool ikAllowed, float envelope,
            float leftStance, float rightStance, float leftFootSpeed, float rightFootSpeed, bool leftLive, bool rightLive)
        {
            float dt = Time.deltaTime;
            Vector3 root = transform.position;
            Vector3 leftHip = _poseData.LeftHipPosition, rightHip = _poseData.RightHipPosition;
            float leftLeg = _poseData.LeftLegLength, rightLeg = _poseData.RightLegLength;

            // 失效：IK 不允許、沒有腿骨事實 ⇒ 既有行為（MoveTowards 到本帧目標；IK 不允許時目標是 0）並重設。
            // 瞬移（root 一帧位移超過上限）⇒ 歷史與彈簧重設，照既有路徑走。
            float maxStep = settings.MaxPoseStepPerFrame;
            bool teleported = _postureHasHistory && (root - _postureRoot).sqrMagnitude > maxStep * maxStep;
            if (!ikAllowed || leftLeg <= 0f || rightLeg <= 0f || teleported)
            {
                ResetPosture();
                _targetData.PelvisOffsetY = Mathf.MoveTowards(_targetData.PelvisOffsetY, envelope, settings.PelvisSmoothSpeed * dt);
                _postureRoot = root;
                _postureLeftHip = leftHip;
                _postureRightHip = rightHip;
                _postureHasHistory = ikAllowed && leftLeg > 0f && rightLeg > 0f;
                return;
            }

            float rootSpeed = 0f;
            Vector3 leftHipNext = leftHip, rightHipNext = rightHip;
            // 沒有上一帧歷史（重設後第一帧）就量不到速度 ⇒ 這一帧不算靜止（不能把「量不到」當成 0）。
            bool haveSpeed = _postureHasHistory && dt > 0f;
            if (haveSpeed)
            {
                rootSpeed = new Vector2(root.x - _postureRoot.x, root.z - _postureRoot.z).magnitude / dt;
                // 骨盆下一帧才套用：與 T3 腳目標同一個一帧外插慣例（離線重播就是這樣配對的）。
                leftHipNext = 2f * leftHip - _postureLeftHip;
                rightHipNext = 2f * rightHip - _postureRightHip;
            }
            _postureRoot = root;
            _postureLeftHip = leftHip;
            _postureRightHip = rightHip;
            _postureHasHistory = true;

            Vector3 leftTarget = _targetData.LeftFootPosition, rightTarget = _targetData.RightFootPosition;
            // R：偏好站姿（兩腳都以偏好伸展比搆到）；C：支撐加權、完全伸直的硬性上限——兩者分開（Codex High）。
            float preferred = Mathf.Min(
                StandingPostureSolver.LegBound(leftHipNext, leftTarget, settings.StandingPreferredExtension * leftLeg),
                StandingPostureSolver.LegBound(rightHipNext, rightTarget, settings.StandingPreferredExtension * rightLeg));
            float hard = Mathf.Min(
                Mathf.Clamp01(1.25f * leftStance) * Mathf.Min(0f, StandingPostureSolver.LegBound(leftHipNext, leftTarget, leftLeg)),
                Mathf.Clamp01(1.25f * rightStance) * Mathf.Min(0f, StandingPostureSolver.LegBound(rightHipNext, rightTarget, rightLeg)));

            bool still = haveSpeed
                && data.MovementIntent.DesiredSpeedNormalized <= 0f
                && rootSpeed < settings.StandingRootSpeedMax
                && leftFootSpeed < settings.StandingFootSpeedMax && rightFootSpeed < settings.StandingFootSpeedMax
                && leftLive && rightLive;

            var input = new PostureInputs { Envelope = envelope, Preferred = preferred, HardLimit = hard, Still = still };
            StandingPostureSolver.Step(ref _posture, in input, dt, ref _targetData.PelvisOffsetY,
                settings.PelvisSmoothSpeed, settings.StandingSettleFrames, settings.StandingRiseOmega, settings.StandingReturnOmega,
                settings.StandingHandbackTolerance, settings.StandingHandbackSpeed);
            _posture.DebugEnvelope = envelope;
            _posture.DebugPreferred = preferred;
            _posture.DebugHardLimit = hard;
        }

        private void OnDisable()
        {
            ResetPosture();
            _leftContact.Reset();
            _rightContact.Reset();
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
        /// 身體參考面包絡 Kref（docs/05 §3.5.4.3）。在 root 周圍 8 個世界固定方向、半徑 <c>ReferenceSampleRadius</c> 處
        /// 往下打「移動世界」射線，取最低者相對 root 的高差（夾限在 [−MaxPelvisOffset, 0]）。
        ///
        /// 「代理面」＝角色移動會碰、但 Foot IK 看不到的面（<c>移動世界 &amp; ~GroundLayers</c>，由遮罩推導、不寫 layer 名）。
        /// - 命中代理面 ⇒ 用命中高度。
        /// - root 站在代理面上時，沒命中代理面的取樣改用 max(非代理命中, root 支撐平面的延伸)：
        ///   樓梯側邊／開放頂端下方的地面被延伸平面蓋掉，不會把身體往下拉；樓梯底的地面與斜面起點同高，數值連續。
        /// - root 不在代理面上 ⇒ 只計代理面命中（一般地形逐位元不變，從平台走向樓梯時自然預先下沉）。
        ///
        /// root 支撐查詢：起點 root 上方 <c>RaycastUpOffset</c>，往下到 root 下方 <see cref="RootSupportProbeDepth"/>；
        /// 沒命中 ⇒ 視為不在代理面上（Codex 審查：刻意的語意邊界）。全部使用 Raycast out 多載，零配置。
        /// </summary>
        private float ComputeReferenceOffset(float rootY, out bool rootOnProxy)
        {
            int proxyMask = _movementMask & ~settings.GroundLayers.value;
            Vector3 root = transform.position;
            float up = settings.RaycastUpOffset;

            rootOnProxy = false;
            Vector3 supportPoint = root;
            Vector3 supportNormal = Vector3.up;
            if (Physics.Raycast(root + Vector3.up * up, Vector3.down, out RaycastHit rootHit,
                    up + RootSupportProbeDepth, _movementMask, QueryTriggerInteraction.Ignore))
            {
                rootOnProxy = IsInMask(proxyMask, rootHit.collider.gameObject.layer);
                supportPoint = rootHit.point;
                supportNormal = rootHit.normal;
            }

            float radius = settings.ReferenceSampleRadius;
            float max = settings.MaxPelvisOffset;
            float sampleDistance = up + radius + max;
            float referenceOffset = 0f;
            for (int k = 0; k < ReferenceDirections.Length; k++)
            {
                Vector2 dir = ReferenceDirections[k];
                float x = root.x + dir.x * radius;
                float z = root.z + dir.y * radius;
                bool hasHit = Physics.Raycast(new Vector3(x, root.y + up, z), Vector3.down, out RaycastHit hit,
                    sampleDistance, _movementMask, QueryTriggerInteraction.Ignore);
                bool hitIsProxy = hasHit && IsInMask(proxyMask, hit.collider.gameObject.layer);
                float planeY = rootOnProxy ? ExtrapolateSupportPlaneY(supportPoint, supportNormal, x, z) : 0f;

                if (TryReferenceSampleHeight(hasHit, hitIsProxy, hasHit ? hit.point.y : 0f, rootOnProxy, planeY,
                        out float height))
                {
                    referenceOffset = Mathf.Min(referenceOffset, Mathf.Clamp(height - rootY, -max, 0f));
                }
            }
            return referenceOffset;
        }

        private static bool IsInMask(int mask, int layer) => (mask & (1 << layer)) != 0;

        /// <summary>
        /// （純函數）單一參考取樣的高度。回傳 false ＝ 這個取樣不計入包絡。
        /// </summary>
        public static bool TryReferenceSampleHeight(bool hasHit, bool hitIsProxy, float hitY,
            bool rootOnProxy, float supportPlaneY, out float height)
        {
            if (hasHit && hitIsProxy)
            {
                height = hitY;
                return true;
            }
            if (rootOnProxy)
            {
                height = hasHit ? Mathf.Max(hitY, supportPlaneY) : supportPlaneY;
                return true;
            }
            height = 0f;
            return false;
        }

        /// <summary>（純函數）root 支撐平面在 (x, z) 的高度。法線近乎水平（退化）時退回支撐點高度。</summary>
        public static float ExtrapolateSupportPlaneY(Vector3 point, Vector3 normal, float x, float z)
        {
            if (normal.y < 0.01f) return point.y;
            return point.y - (normal.x * (x - point.x) + normal.z * (z - point.z)) / normal.y;
        }

        /// <summary>
        /// （純函數）骨盆目標：root 在代理面上 ⇒ 只看參考面包絡；否則取包絡與逐腳支撐需求較深者
        /// （包絡此時只含代理面命中 ⇒ 一般地形上恆為 0，行為不變）。兩個模式的交界由樓梯契約 I6／I7 保證兩邊 ≈ 0。
        /// </summary>
        public static float CombinePelvisTarget(float referenceOffset, bool rootOnProxy, float supportNeed)
            => rootOnProxy ? referenceOffset : Mathf.Min(referenceOffset, supportNeed);

        /// <summary>
        /// （純函數）Q2 骨盆補償，🔄 2026-09-24 改為**支撐加權**（`docs/44` §9.6）。
        ///
        /// 每隻腳的下沉需求 ＝ 支撐量 × 夾限後的高差（腳下地面 − Root，恆 ≤0，下限 −maxOffset）；
        /// 骨盆目標取兩者較深者。
        /// - 支撐量 1（動畫裡正踩著）⇒ 需求等同舊規則；支撐量 0（擺盪中）⇒ 沒有需求，
        ///   即使它的 ray 已經探到更低的下一階。
        /// - 沒有地面命中 ⇒ 這隻腳沒有需求（不是讓整個骨盆歸零）。
        /// - 高於 Root 平面（上坡側）不上抬——骨盆只下沉、不上頂，抬升交給 CharacterController 的地面跟隨。
        ///
        /// 連續性：支撐量對動畫腳高是連續的，<c>min</c> 換邊只產生斜率折點、沒有值的跳變；
        /// 剩下唯一的跳變是「腳下踏面換階」時的 支撐量 × 級高，由呼叫端既有的 MoveTowards 限速。
        /// </summary>
        public static float ComputeSupportedPelvisOffset(
            bool leftHasGround, float leftGroundY, float leftSupport,
            bool rightHasGround, float rightGroundY, float rightSupport,
            float rootY, float maxOffset)
        {
            float max = Mathf.Abs(maxOffset);
            float left = leftHasGround ? Mathf.Clamp01(leftSupport) * Mathf.Clamp(leftGroundY - rootY, -max, 0f) : 0f;
            float right = rightHasGround ? Mathf.Clamp01(rightSupport) * Mathf.Clamp(rightGroundY - rootY, -max, 0f) : 0f;
            return Mathf.Min(left, right);
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
