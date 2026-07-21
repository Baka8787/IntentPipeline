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
    /// 雙管道資料流（M3.1 裁決，各自單寫單讀）：
    /// 黑板（IsGrounded／BlockIK）─讀→ 本類別 ─寫→ <see cref="FootIKTargetData"/> ─讀→ FootIKRig
    /// FootIKRig ─寫→ <see cref="FootIKPoseData"/>（動畫原始 pose 快照）─讀→ 本類別
    ///
    /// 演算法（M3.1）：每腳「快照 goal → raycast → 目標（命中點＋沿法線抬腳底高）＋法線對齊旋轉 →
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
        public FootIKPoseData PoseData => _poseData;

        // 單幀採樣暫存（struct，棧上語義、零 GC）。
        private struct FootSample
        {
            public bool HasHit;
            public Vector3 HitPoint;
            public Vector3 Normal;
            public float GroundY;
        }

        private void Awake()
        {
            _targetData = new FootIKTargetData(); // 兩條管道皆一次性配置，執行期零 GC
            _poseData = new FootIKPoseData();

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
                rig.Bind(_targetData, _poseData);
            }
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
            // BlockIK 為讀取契約先行（writer 到 ArbiterPipeline 接入才存在，現值恆 false）。
            bool ikAllowed = data.IsGrounded && !data.Arbitration.BlockIK;

            // === ① 各腳採樣 ===
            FootSample left = SampleGround(_poseData.LeftFootPosition, ikAllowed);
            FootSample right = SampleGround(_poseData.RightFootPosition, ikAllowed);

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

        /// <summary>① 單腳地面採樣：以動畫原始 goal 為基準向下 raycast，無條件接受命中（M3.1）。無堆配置。</summary>
        private FootSample SampleGround(Vector3 posePosition, bool ikAllowed)
        {
            FootSample sample = default;
            if (!ikAllowed) return sample;

            Vector3 origin = posePosition + Vector3.up * settings.RaycastUpOffset;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, settings.RaycastDistance, settings.GroundLayers, QueryTriggerInteraction.Ignore))
            {
                sample.HasHit = true;
                sample.HitPoint = hit.point;
                sample.Normal = hit.normal;
                sample.GroundY = hit.point.y;
            }
            return sample;
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
                // 目標＝地面命中點＋avatar 腳底高，沿地面法線抬升（M3.3 幾何正解，M3.5 保留；
                // 平面上 normal=up 與 M3.1 世界 up 抬升等價）：腳掌對齊斜面後，腳踝到腳底的間隙
                // 同樣垂直於斜面——沿 up 抬會使前腳掌在斜坡上幾何性插入坡面。
                targetPosition = sample.HitPoint + sample.Normal * footBottomHeight;

                // 腳掌對齊地面法線；基準是動畫原始 goal 旋轉（非骨骼現值）——無反饋、不累積。
                // 保留俯仰式（v1 凍結基線）：FromToRotation(worldUp, n) 只把世界 up 轉到法線，
                // 動畫腳踝自身的俯仰／roll 原樣保留——契合設計哲學「腳踝自由旋轉、不強制壓平」（design-doc §4.6）。
                // A/B 歸檔（v0.18.7）：軸對齊式（FromToRotation(poseUp, n)＝主動壓平腳底）實測與本式無感差
                // （踩地相動畫俯仰本就小、平地夾角 ~2°），依哲學回歸本式、軸對齊式棄用（見 changelog v0.18.7／roadmap L6）。
                targetRotation = Quaternion.FromToRotation(Vector3.up, sample.Normal) * poseRotation;

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
