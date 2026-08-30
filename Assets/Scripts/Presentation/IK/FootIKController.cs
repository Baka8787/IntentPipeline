using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Animation;

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
            if (_targetData == null || _poseData == null || !_poseData.IsWarm) return;

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
            if (!sample.HasHit || !settings.UseTwoPointSampling) return sample;

            float heelOffset = Mathf.Max(0f, settings.HeelOffset);
            float toeOffset = Mathf.Max(0f, settings.ToeOffset);
            Vector3 localHeel = Vector3.forward * -heelOffset - Vector3.up * footBottomHeight;
            Vector3 localToe = Vector3.forward * toeOffset - Vector3.up * footBottomHeight;
            Vector3 worldHeel = sample.TargetPosition + sample.TargetRotation * localHeel;
            Vector3 worldToe = sample.TargetPosition + sample.TargetRotation * localToe;

            bool heelHasHit = RaycastGround(
                worldHeel + Vector3.up * settings.RaycastUpOffset, out RaycastHit heelHit);
            bool toeHasHit = RaycastGround(
                worldToe + Vector3.up * settings.RaycastUpOffset, out RaycastHit toeHit);

            // 落空代表額外資訊不足，不是關 IK 的理由：保留已算好的 ankle-only 結果。
            if (!heelHasHit || !toeHasHit) return sample;

            float heelPenetration = heelHit.point.y - worldHeel.y;
            float toePenetration = toeHit.point.y - worldToe.y;
            float lift = ComputePenetrationLift(worldHeel, heelHit.point, worldToe, toeHit.point);
            sample.TargetPosition.y += lift;

            // 誰穿得最深誰就是抬升後的真實接觸端點；骨盆讀取該接觸高度。
            sample.GroundY = heelPenetration >= toePenetration ? heelHit.point.y : toeHit.point.y;
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
        /// （純函數）Q2 骨盆補償：取雙腳地面命中點中較低者相對 Root 平面的差（恆 ≤0），
        /// 夾在 [-maxOffset, 0]——不可無限下降。
        /// 高於 Root 平面（上坡側）不上抬——骨盆只下沉、不上頂，抬升交給 CharacterController 的地面跟隨。
        /// </summary>
        public static float ComputePelvisOffset(float leftGroundY, float rightGroundY, float rootY, float maxOffset)
        {
            float lowest = Mathf.Min(leftGroundY, rightGroundY) - rootY;
            return Mathf.Clamp(lowest, -Mathf.Abs(maxOffset), 0f);
        }
    }
}
