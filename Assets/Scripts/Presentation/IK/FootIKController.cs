using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Animation;

namespace Project.Presentation.IK
{
    /// <summary>
    /// 🆕（M3）Foot IK 決策端——第二個 <see cref="IPresentationController"/> 實例，
    /// 由 PresentationPipeline 於管線順序 6.5（LateUpdate、MotionDriver 之後）驅動，Runner 零改動。
    /// 資料流：黑板（IsGrounded／BlockIK）→ 本類別（採樣＋計算，<see cref="FootIKRuntimeData"/> 唯一 Writer）
    /// → FootIKRig（Model 端 Thin Executor，唯一 Reader）→ OnAnimatorIK 套用。
    ///
    /// 職責邊界（M3 裁決）：本類別負責 raycast 地面採樣、地面法線、權重計算與平滑、骨盆補償；
    /// 絕不寫入 Animator／骨骼（SetIK* 全在 Rig）。骨骼 Transform 僅初始化取得引用、執行期**唯讀採樣**
    /// ——這是 Q3（Runtime Pose Heuristic：依混合後 Pose 的腳骨高度決定權重）的必要輸入，
    /// 屬 ADR-001 預期的「IK 功能引用 Model 骨骼」情境；讀取不是修改，Model 寫入權仍只在動畫系統與 Rig。
    ///
    /// ⚠️ 已知時序（詳 dev-spec §3.5）：OnAnimatorIK 發生於 Animator 評估流程（早於 LateUpdate），
    /// 因此順序 6.5 算出的目標**下一幀**的 IK pass 才生效——一幀延遲屬 Unity Humanoid IK 正常行為，
    /// 權重平滑（weightSmoothSpeed）可降低視覺影響。
    /// </summary>
    public class FootIKController : MonoBehaviour, IPresentationController
    {
        [Header("Ground Detection")]
        [Tooltip("地面偵測的 Layer 遮罩。務必包含地形所在 Layer；留 Nothing 會使所有 raycast 落空、IK 權重恆 0。")]
        [SerializeField] private LayerMask groundLayers = ~0;

        [Tooltip("raycast 起點＝腳骨位置正上方此高度（公尺），向下打。需大於腳部單步抬升與台階落差。")]
        [SerializeField] private float raycastUpOffset = 0.5f;

        [Tooltip("raycast 總長度（公尺）。至少涵蓋 raycastUpOffset＋預期最大向下落差（斜坡／台階）。")]
        [SerializeField] private float raycastDistance = 1.1f;

        [Tooltip("腳踝骨到腳底的垂直距離（公尺）：IK 目標＝地面命中點＋此高度，避免腳掌陷入地面。")]
        [SerializeField] private float footHeight = 0.1f;

        [Header("Foot Weight（Q3：Runtime Pose Heuristic）")]
        [Tooltip("腳骨相對 Root 平面的高度 ≤ 此值 → 目標權重 1（踩地相，IK 全接管）。")]
        [SerializeField] private float footGroundedHeightMin = 0.08f;

        [Tooltip("腳骨相對 Root 平面的高度 ≥ 此值 → 目標權重 0（抬腳相，動畫全接管）。兩值之間線性過渡。")]
        [SerializeField] private float footGroundedHeightMax = 0.25f;

        [Tooltip("權重朝目標值的收斂速率（每秒）。愈大愈跟手、愈小愈柔——用於掩蓋一幀延遲與抬腳／踩地切換。")]
        [SerializeField] private float weightSmoothSpeed = 8f;

        [Header("Pelvis Compensation（Q2）")]
        [Tooltip("骨盆最大下沉量（公尺）。雙腳地面高差超過此值時低腳將搆不到地，屬設計極限。")]
        [SerializeField] private float maxPelvisDrop = 0.35f;

        [Tooltip("骨盆偏移的收斂速率（每秒）。")]
        [SerializeField] private float pelvisSmoothSpeed = 5f;

        private FootIKRuntimeData _data;
        private Transform _leftFoot;   // 唯讀 pose 採樣（初始化快取，絕不寫入）
        private Transform _rightFoot;

        /// <summary>供除錯／測試檢視。執行期唯一 Writer 仍是本類別（單一寫入者原則）。</summary>
        public FootIKRuntimeData Data => _data;

        private void Awake()
        {
            _data = new FootIKRuntimeData(); // 一次性配置，執行期零 GC

            // === 組裝期注入（僅此一次）：此後 Controller 與 Rig 之間只剩共享數據，無任何方法呼叫 ===
            var rig = GetComponentInChildren<FootIKRig>();
            if (rig == null)
            {
                Debug.LogError($"[{gameObject.name}] FootIKController 找不到 FootIKRig！" +
                    "Rig 必須掛在 Model 子物件（與 Animator 同物件——OnAnimatorIK 回呼的 Unity 硬性限制）。", this);
            }
            else
            {
                rig.Bind(_data);
            }

            // === 腳骨引用：經 Model 的 Humanoid Animator 一次性查詢（GetBoneTransform 是唯讀查詢，非修改）===
            var animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError($"[{gameObject.name}] FootIKController 需要 Model 子物件上的 Humanoid Animator 以取得腳骨引用！", this);
            }
            else
            {
                _leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                _rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                if (_leftFoot == null || _rightFoot == null)
                {
                    Debug.LogError($"[{gameObject.name}] Humanoid Avatar 缺少 LeftFoot／RightFoot 骨骼映射，Foot IK 無法運作！", this);
                }
            }
        }

        private void Start()
        {
            // 開啟主層的 Animator IK pass（OnAnimatorIK 觸發前提）。
            // 經 Facade 而非直呼動畫系統（CLAUDE.md 禁的是繞過 Facade 的 Controller→Animation API）。
            // 放在 Start 而非 Awake：確保 AnimancerFacade.Awake（animancer 引用補洞＋層初始化）已完成，
            // 不依賴同幀 Awake 的元件執行順序。
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
            if (_data == null || _leftFoot == null || _rightFoot == null) return;

            // Root 原點＝腳底＝膠囊底（ADR-001＋CapsuleFitter §0.3 規則 6），可直接作為「地面平面」基準。
            float rootY = transform.position.y;

            // Q4：不特判狀態——空中 IsGrounded=false 自然關閉；Roll 中腳部蜷起由 pose 權重自然降低。
            // BlockIK 為讀取契約先行（writer 到 ArbiterPipeline 接入才存在，現值恆 false）。
            bool ikAllowed = data.IsGrounded && !data.Arbitration.BlockIK;

            SolveFoot(_leftFoot, rootY, ikAllowed,
                ref _data.LeftFootPosition, ref _data.LeftFootRotation,
                ref _data.LeftFootPositionWeight, ref _data.LeftFootRotationWeight,
                out float leftGroundY, out bool leftHit);

            SolveFoot(_rightFoot, rootY, ikAllowed,
                ref _data.RightFootPosition, ref _data.RightFootRotation,
                ref _data.RightFootPositionWeight, ref _data.RightFootRotationWeight,
                out float rightGroundY, out bool rightHit);

            // === Pelvis Compensation（Q2）：骨盆沉向較低的腳，讓低腳的 IK 目標在可及範圍內 ===
            float pelvisTarget = (ikAllowed && leftHit && rightHit)
                ? ComputePelvisOffset(leftGroundY, rightGroundY, rootY, maxPelvisDrop)
                : 0f;
            _data.PelvisOffsetY = Mathf.MoveTowards(_data.PelvisOffsetY, pelvisTarget, pelvisSmoothSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 單腳求解：pose 採樣 → raycast → 目標位置／法線對齊旋轉 → 高度權重 → 平滑。
        /// 全程無堆配置（RaycastHit 為 struct、單一命中 Raycast 無 GC）。
        /// </summary>
        private void SolveFoot(Transform foot, float rootY, bool ikAllowed,
            ref Vector3 targetPosition, ref Quaternion targetRotation,
            ref float positionWeight, ref float rotationWeight,
            out float groundY, out bool hasHit)
        {
            Vector3 footPos = foot.position; // 唯讀 pose 採樣：混合後姿勢（Mixer 輸出）即真相（Q3）
            groundY = rootY;
            hasHit = false;

            float goalWeight = 0f;
            if (ikAllowed)
            {
                Vector3 origin = footPos + Vector3.up * raycastUpOffset;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastDistance, groundLayers, QueryTriggerInteraction.Ignore))
                {
                    hasHit = true;
                    groundY = hit.point.y;
                    targetPosition = hit.point + Vector3.up * footHeight;
                    // 腳掌對齊地面法線；保留動畫原始朝向（僅把「垂直向上」轉到法線方向，yaw 不動）。
                    targetRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * foot.rotation;
                    goalWeight = ComputeFootWeight(footPos.y - rootY, footGroundedHeightMin, footGroundedHeightMax);
                }
            }

            float step = weightSmoothSpeed * Time.deltaTime;
            positionWeight = Mathf.MoveTowards(positionWeight, goalWeight, step);
            rotationWeight = Mathf.MoveTowards(rotationWeight, goalWeight, step);
        }

        /// <summary>
        /// （純函數：Tick 與 EditMode 測試共用，比照 MotionBakeData.ComputeAverageSpeed 先例）
        /// Q3 Pose Heuristic：腳骨相對 Root 平面的高度 → 貼地權重。
        /// ≤ groundedMin 回 1（踩地）、≥ groundedMax 回 0（抬腳）、之間線性遞減。
        /// groundedMax ≤ groundedMin 的異常配置退化為以 groundedMin 硬切（防呆，不拋例外）。
        /// </summary>
        public static float ComputeFootWeight(float footHeightAboveRoot, float groundedMin, float groundedMax)
        {
            if (groundedMax <= groundedMin) return footHeightAboveRoot <= groundedMin ? 1f : 0f;
            return 1f - Mathf.InverseLerp(groundedMin, groundedMax, footHeightAboveRoot);
        }

        /// <summary>
        /// （純函數：Tick 與 EditMode 測試共用）
        /// Q2 骨盆補償：取雙腳地面命中點中較低者相對 Root 平面的差（恆 ≤0），夾在 [-maxDrop, 0]。
        /// 高於 Root 平面（上坡側）不上抬——骨盆只下沉、不上頂，抬升交給 CharacterController 的地面跟隨。
        /// </summary>
        public static float ComputePelvisOffset(float leftGroundY, float rightGroundY, float rootY, float maxDrop)
        {
            float lowest = Mathf.Min(leftGroundY, rightGroundY) - rootY;
            return Mathf.Clamp(lowest, -Mathf.Abs(maxDrop), 0f);
        }
    }
}
