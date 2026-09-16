using Project.Core.Blackboard;
using UnityEngine;

namespace Project.Core.Environment
{
    /// <summary>
    /// TraversalClassifier 的完整值型輸入。量測由 TraversalProbe 建立；分類器只讀本結構，
    /// 因此 EditMode 測試不需要物理場景。
    /// </summary>
    public readonly struct TraversalProbeMeasurement
    {
        public readonly bool IsGrounded;
        public readonly float HorizontalSpeed;
        public readonly bool HasForwardHit;
        public readonly Vector3 ForwardHitPoint;
        public readonly Vector3 ForwardHitNormal;
        public readonly float FirstClearHeight;
        public readonly bool HasTop;
        public readonly Vector3 TopPoint;
        public readonly Vector3 TopNormal;
        public readonly float Height;
        public readonly float Depth;
        public readonly Vector3 DestinationPoint;
        public readonly bool DestinationBlocked;
        public readonly float SlopeLimit;
        public readonly Vector3 Forward;
        public readonly TraversalLedgeFrame LedgeFrame;
        public readonly TraversalCorridorEvidence Corridor;
        public readonly Vector3 RootPosition;
        public readonly Vector3 FacingForward;
        public readonly float EntryCapsuleWallClearance;
        /// <summary>落腳面從邊緣往內連續延伸的最遠深度（m）。見 <see cref="TraversalCandidate.LandingSurfaceDepth"/>。</summary>
        public readonly float LandingSurfaceDepth;
        public readonly TraversalDetectionDirectionSource DirectionSource;

        public TraversalProbeMeasurement(
            bool isGrounded,
            float horizontalSpeed,
            bool hasForwardHit,
            Vector3 forwardHitPoint,
            Vector3 forwardHitNormal,
            float firstClearHeight,
            bool hasTop,
            Vector3 topPoint,
            Vector3 topNormal,
            float height,
            float depth,
            Vector3 destinationPoint,
            bool destinationBlocked,
            float slopeLimit,
            Vector3 forward)
            : this(
                isGrounded, horizontalSpeed, hasForwardHit, forwardHitPoint, forwardHitNormal,
                firstClearHeight, hasTop, topPoint, topNormal, height, depth, destinationPoint,
                destinationBlocked, slopeLimit, forward,
                new TraversalLedgeFrame(
                    topPoint,
                    Vector3.Cross(topNormal, forwardHitNormal).sqrMagnitude > 0.000001f
                        ? Vector3.Cross(topNormal, forwardHitNormal)
                        : Vector3.right,
                    forwardHitNormal.sqrMagnitude > 0.000001f ? forwardHitNormal : -forward,
                    topNormal,
                    -1f,
                    1f),
                TraversalCorridorEvidence.Clear(
                    new Vector3(topPoint.x, 0f, topPoint.z) -
                    (forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.forward) * 0.45f,
                    topPoint + Vector3.up * 0.5f,
                    destinationPoint),
                new Vector3(topPoint.x, 0f, topPoint.z) -
                (forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.forward) * 0.45f,
                forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.forward,
                TraversalDetectionDirectionSource.Move,
                1f)
        {
        }

        public TraversalProbeMeasurement(
            bool isGrounded,
            float horizontalSpeed,
            bool hasForwardHit,
            Vector3 forwardHitPoint,
            Vector3 forwardHitNormal,
            float firstClearHeight,
            bool hasTop,
            Vector3 topPoint,
            Vector3 topNormal,
            float height,
            float depth,
            Vector3 destinationPoint,
            bool destinationBlocked,
            float slopeLimit,
            Vector3 forward,
            TraversalLedgeFrame ledgeFrame,
            TraversalCorridorEvidence corridor,
            Vector3 rootPosition,
            Vector3 facingForward,
            TraversalDetectionDirectionSource directionSource,
            float entryCapsuleWallClearance = 1f,
            float landingSurfaceDepth = float.NaN)
        {
            IsGrounded = isGrounded;
            HorizontalSpeed = horizontalSpeed;
            HasForwardHit = hasForwardHit;
            ForwardHitPoint = forwardHitPoint;
            ForwardHitNormal = forwardHitNormal;
            FirstClearHeight = firstClearHeight;
            HasTop = hasTop;
            TopPoint = topPoint;
            TopNormal = topNormal;
            Height = height;
            Depth = depth;
            DestinationPoint = destinationPoint;
            DestinationBlocked = destinationBlocked;
            SlopeLimit = slopeLimit;
            Forward = forward;
            LedgeFrame = ledgeFrame;
            Corridor = corridor;
            RootPosition = rootPosition;
            FacingForward = facingForward;
            EntryCapsuleWallClearance = entryCapsuleWallClearance;
            LandingSurfaceDepth = landingSurfaceDepth;
            DirectionSource = directionSource;
        }
    }

    /// <summary>
    /// Traversal V1 的唯一環境 physics query owner。只發布元件內的 Candidate 快取，
    /// 不寫黑板，也不決定狀態轉移。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class TraversalProbe : MonoBehaviour
    {
        private const float DirectionSqrEpsilon = 0.000001f;
        private const float QueryEpsilon = 0.02f;
        private const float DepthEvidenceEpsilon = 0.0001f;
        private const int MaximumScanRayCount = 64;

        [SerializeField] private TraversalProbeSettings settings = TraversalProbeSettings.Default;

        private CharacterController _characterController;
        private TraversalKind _previousKind;
        private float _requiredForwardReach = float.NaN;
        private float _requiredLandingDepth = float.NaN;

        /// <summary>
        /// 🆕（docs/24 §10）**感測範圍與動畫需求的明確關係。**
        ///
        /// `forwardScanDistance` 原本是一個與動畫無關的 authored 常數（1.25 m），而各支動畫由 bake
        /// 推導出來的理想進場距離最大到 1.402 m ⇒ **理想站位落在感測範圍之外**，
        /// Vault 的可執行區間因此只剩約 20 cm（docs/24 §9.3 實測）。
        ///
        /// 這裡不是把常數換成另一個常數，而是讓**擁有動畫資料的那一層**（`TraversalStateParamsSO`／
        /// `TraversalState`）把需求推進來，Probe 取 `max(authored, required)` 當下限。
        /// Probe 仍然只認得 float 與幾何，不反向依賴 StateMachine／Presentation（LayerRule 不變）。
        /// </summary>
        /// <param name="forwardReach">最遠要能偵測到障礙物的水平距離（m）。</param>
        /// <param name="landingDepth">落腳面最遠要驗證到的深度（m）。</param>
        public void SetDerivedRangeRequirements(float forwardReach, float landingDepth)
        {
            _requiredForwardReach = forwardReach;
            _requiredLandingDepth = landingDepth;
        }

        /// <summary>實際使用的前掃距離＝authored 與動畫需求取大。</summary>
        public float EffectiveForwardScanDistance =>
            IsFinitePositive(_requiredForwardReach)
                ? Mathf.Max(settings.ForwardScanDistance, _requiredForwardReach)
                : settings.ForwardScanDistance;

        /// <summary>實際使用的落腳面掃描上限＝分類常數與動畫自然落點取大。</summary>
        public float EffectiveLandingScanDepth =>
            IsFinitePositive(_requiredLandingDepth)
                ? Mathf.Max(settings.Vault1mMaxDepth, _requiredLandingDepth)
                : settings.Vault1mMaxDepth;

        private static bool IsFinitePositive(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

        public TraversalCandidate Candidate { get; private set; }
        public TraversalEntryEvaluation EntryEvaluation { get; private set; }
        /// <summary>
        /// 🆕（docs/23 §A10／R1）最近一次 Jump 意圖的 selection 結果。
        /// Probe 不產生它——由 <c>TraversalState.CanEnter</c> 推入，純觀測用途。
        /// </summary>
        public TraversalSelectionEvaluation SelectionEvaluation { get; private set; }

        /// <summary>
        /// 🆕（docs/24 §8）最近一次的 Animation Fitting 結果。
        /// 與 <see cref="EntryEvaluation"/>／<see cref="SelectionEvaluation"/> 同型：
        /// **Probe 不產生它**，由 <c>TraversalState.CanEnter</c> 推入，純觀測用途。
        /// </summary>
        public TraversalAnimationFit AnimationFit { get; private set; } =
            TraversalAnimationFit.Unfitted(TraversalFitReason.NotFitted);

        // ─────────────────────────────────────────────────────────────────────
        // Authored Capsule Geometry（角色基準碰撞幾何）—— 2026-09-15
        //
        // 🔴 **為什麼一定要快照，不能讀 live 值**
        // `CharacterController.center`／`height`／`radius` 在執行期有兩個合法的暫時寫入者：
        //   ① dynamic capsule pose offset（MotionDriver，水平 center）
        //   ② traversal collision profile（MotionDriver，center/height/radius）
        // 而 traversal 的環境查詢問的是「**這個角色的基準身體**能不能通過」，
        // 不是「這個角色現在被動畫推到哪」。讀 live 值會讓 corridor 檢查膠囊整體被推走，
        // 於是 `IsCapsuleSegmentClear` 在 entry→clearance 段撞牆 ⇒ `CorridorBlocked`
        // ⇒ 起攀完全不觸發。這是 2026-09-15 由 PlayMode 探針證實的真實 bug（`docs/27` §10 第 0 條）：
        // 同一個站位、只差 center 有沒有被偏移，結果一個 `Climb2m`、一個 `CorridorBlocked`。
        //
        // 🔒 **不變量（invariant）**
        //   authored geometry 一旦初始化，就**不得**被任何 locomotion／action 的動態偏移改寫。
        //   它是「作者在 prefab 上設定的那顆膠囊」，生命週期內恆定。
        //   ⇒ 由 EditMode `A45` 與 PlayMode `TR1` 兩條測試守住。
        //
        // ⛔ **刻意不向 MotionDriver 要這個值**：`MotionDriver` 在 Presentation 層，
        //    `TraversalProbe` 在 Core 層。Core 去問 Presentation 拿自己的基準幾何會把依賴方向反轉，
        //    而且會讓「traversal 能不能運作」取決於一個表現層元件存不存在。
        //    兩邊各自在 Awake 快照同一個 authored 常數——那不是兩個 writer，是兩個 reader 讀同一個不變量。
        // ─────────────────────────────────────────────────────────────────────
        private bool _authoredGeometryCaptured;
        private Vector3 _authoredCenter;
        private float _authoredRadius;
        private float _authoredHeight;

        /// <summary>AuthoredCenter（作者設定的基準膠囊中心，local space）。執行期偏移不影響它。</summary>
        public Vector3 AuthoredCenter
        {
            get { CaptureAuthoredGeometry(); return _authoredCenter; }
        }

        /// <summary>AuthoredRadius（作者設定的基準膠囊半徑）。</summary>
        public float AuthoredRadius
        {
            get { CaptureAuthoredGeometry(); return _authoredRadius; }
        }

        /// <summary>AuthoredHeight（作者設定的基準膠囊高度）。</summary>
        public float AuthoredHeight
        {
            get { CaptureAuthoredGeometry(); return _authoredHeight; }
        }

        /// <summary>
        /// 擷取一次 authored 幾何。**冪等**：第二次以後直接 return，因此即使在偏移生效之後
        /// 才第一次被呼叫，也不會把偏移後的值誤當基準——前提是 <see cref="Awake"/> 已經跑過。
        /// EditMode 測試不呼叫 <c>Awake</c>，故此處保留惰性擷取作為後備。
        /// </summary>
        private void CaptureAuthoredGeometry()
        {
            if (_authoredGeometryCaptured) return;
            if (_characterController == null) _characterController = GetComponent<CharacterController>();
            if (_characterController == null) return;

            _authoredCenter = _characterController.center;
            _authoredRadius = _characterController.radius;
            _authoredHeight = _characterController.height;
            _authoredGeometryCaptured = true;
        }

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            // ⚠️ 必須在 Awake 就擷取：任何動態偏移最早也要到第一次 Update／LateUpdate 才會寫 center，
            //    所以 Awake 讀到的必定是 prefab／場景上 authored 的值。
            CaptureAuthoredGeometry();
        }

        /// <summary>管線順序 2.7。量測、快取並以純分類器更新本幀 Candidate。</summary>
        public void Tick(PlayerRuntimeData data)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ResetTraversalDebugSnapshot();
            // Keep the last intent-time evaluation visible long enough to diagnose an attempt.
            // This is debug/state evidence only; traversal admission always evaluates the current
            // Candidate when JumpRequested and never consumes this latched copy.
            _debugSnapshot.EntryEvaluation = EntryEvaluation;
            _debugSnapshot.SelectionEvaluation = SelectionEvaluation;
#endif
            if (data == null || _characterController == null)
            {
                CompleteTick(default);
                return;
            }

            Vector3 up = transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(data.MoveDirection, up);
            TraversalDetectionDirectionSource directionSource = TraversalDetectionDirectionSource.Move;
            if (forward.sqrMagnitude <= DirectionSqrEpsilon)
            {
                forward = Vector3.ProjectOnPlane(transform.forward, up);
                directionSource = TraversalDetectionDirectionSource.Facing;
            }
            if (forward.sqrMagnitude > DirectionSqrEpsilon)
                forward.Normalize();

            Vector3 facingForward = Vector3.ProjectOnPlane(transform.forward, up);
            if (facingForward.sqrMagnitude > DirectionSqrEpsilon)
                facingForward.Normalize();

            // V3: sensing is continuous while grounded. JumpPressed is the intent boundary;
            // stationary characters still receive a current geometry snapshot from facing.
            if (!data.IsGrounded)
            {
                var gatedMeasurement = new TraversalProbeMeasurement(
                    data.IsGrounded,
                    data.MoveSpeed,
                    false,
                    default,
                    default,
                    0f,
                    false,
                    default,
                    default,
                    0f,
                    0f,
                    default,
                    false,
                    _characterController.slopeLimit,
                    forward,
                    default,
                    default,
                    transform.position,
                    facingForward,
                    directionSource,
                    1f);
                CompleteTick(TraversalClassifier.Classify(
                    in gatedMeasurement, in settings, _previousKind));
                return;
            }

            float halfHeight = Mathf.Max(AuthoredHeight * 0.5f, AuthoredRadius);
            Vector3 capsuleCenter = transform.TransformPoint(AuthoredCenter);
            Vector3 capsuleBottom = capsuleCenter - up * halfHeight;

            bool hasForwardHit = false;
            RaycastHit forwardHit = default;
            float firstClearHeight = 0f;
            float ceiling = Mathf.Max(_characterController.stepOffset, settings.ScanCeiling);
            float scanHeight = Mathf.Max(0f, _characterController.stepOffset);

            for (int rayIndex = 0;
                 rayIndex < MaximumScanRayCount && scanHeight <= ceiling + QueryEpsilon;
                 rayIndex++, scanHeight += settings.ScanStep)
            {
                Vector3 origin = capsuleBottom + up * scanHeight;
                bool hasHit = Physics.Raycast(
                    origin,
                    forward,
                    out RaycastHit hit,
                    EffectiveForwardScanDistance,
                    settings.ObstacleMask,
                    QueryTriggerInteraction.Ignore);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                RecordTraversalStepRay(origin, hasHit, hasHit ? hit.point : origin + forward * EffectiveForwardScanDistance);
#endif
                if (hasHit)
                {
                    if (!hasForwardHit)
                    {
                        hasForwardHit = true;
                        forwardHit = hit;
                    }
                    continue;
                }

                if (hasForwardHit) firstClearHeight = scanHeight;
                break;
            }

            bool hasTop = false;
            RaycastHit topHit = default;
            Vector3 topProbeOrigin = default;
            Vector3 topProbeEnd = default;
            if (hasForwardHit && firstClearHeight > 0f)
            {
                Vector3 desiredHeightPoint = capsuleBottom + up * (firstClearHeight + QueryEpsilon);
                float lift = Vector3.Dot(desiredHeightPoint - forwardHit.point, up);
                topProbeOrigin = forwardHit.point + forward * QueryEpsilon + up * Mathf.Max(QueryEpsilon, lift);
                float topProbeDistance = Mathf.Max(settings.ScanCeiling + AuthoredHeight, firstClearHeight + QueryEpsilon);
                hasTop = Physics.Raycast(
                    topProbeOrigin,
                    -up,
                    out topHit,
                    topProbeDistance,
                    settings.ObstacleMask,
                    QueryTriggerInteraction.Ignore);
                topProbeEnd = hasTop ? topHit.point : topProbeOrigin - up * topProbeDistance;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RecordTraversalTopProbe(topProbeOrigin, topProbeEnd, hasTop);
#endif

            float height = hasTop ? Mathf.Max(0f, Vector3.Dot(topHit.point - capsuleBottom, up)) : 0f;
            float depth = 0f;
            float landingSurfaceDepth = float.NaN;
            Vector3 destinationPoint = default;
            bool destinationBlocked = false;
            Vector3 depthProbeOrigin = default;
            Vector3 depthProbeEnd = default;
            bool depthProbeHasHit = false;
            Vector3 capsulePoint1 = default;
            Vector3 capsulePoint2 = default;
            float capsuleRadius = 0f;
            TraversalLedgeFrame ledgeFrame = default;
            TraversalCorridorEvidence corridor = default;
            float entryCapsuleWallClearance = 1f;

            if (hasTop)
            {
                depthProbeOrigin = topHit.point + forward * settings.Vault1mMaxDepth + up * QueryEpsilon;
                float depthProbeDistance = Mathf.Max(
                    settings.ScanCeiling + AuthoredHeight,
                    height + AuthoredHeight);
                depthProbeHasHit = Physics.Raycast(
                    depthProbeOrigin,
                    -up,
                    out RaycastHit depthHit,
                    depthProbeDistance,
                    settings.ObstacleMask,
                    QueryTriggerInteraction.Ignore);
                depthProbeEnd = depthProbeHasHit
                    ? depthHit.point
                    : depthProbeOrigin - up * depthProbeDistance;

                bool surfaceContinues = depthProbeHasHit &&
                                        Mathf.Abs(Vector3.Dot(topHit.point - depthHit.point, up)) <= settings.ScanStep;
                depth = surfaceContinues
                    ? settings.Vault1mMaxDepth + DepthEvidenceEpsilon
                    : Mathf.Max(0f, settings.Vault1mMaxDepth - DepthEvidenceEpsilon);

                // 🆕（docs/24 §10）**落地預算與分類證據分離。**
                //    上面的 `depth` 是**分類**用的二元證據（超過 Vault1mMaxDepth ⇒ 實心平台 ⇒ Climb）。
                //    但那個常數（0.6 m）同時被當成 committed 終點，於是動畫自然落點在 1.86 m 外的
                //    `Vault1m` 每次都被要求提早停下，落差只能由 warp 事後往回拉——
                //    這就是 Playtest 的「放手後還在位移」。
                //    這裡改為往內**續掃到動畫真正需要的深度**（上限由各支 bake 推導後推入，
                //    見 SetDerivedRangeRequirements），回報最遠仍連續的落腳點。
                //    ⚠️ 兩種動作都要掃，而且**連續性要對照落腳面本身**，不是對照障礙物頂面：
                //      • Climb（表面延續）：落腳面就是平台頂面 ⇒ 沿著頂面往內掃。
                //      • Vault（薄牆）：落腳面是**牆另一側的地面** ⇒ 沿著那個地面往外掃。
                //    只掃前者的話，Vault 的 run-out（自然落點 1.86 m）永遠掃不到，
                //    終點會停在探測常數上，落差全部丟給 correction。
                Vector3 landingSurfacePoint = default;
                if (depthProbeHasHit)
                {
                    landingSurfaceDepth = settings.Vault1mMaxDepth;
                    landingSurfacePoint = depthHit.point;
                    float landingScanLimit = EffectiveLandingScanDepth;
                    float landingStep = settings.ScanStep;
                    for (float probeDepth = settings.Vault1mMaxDepth + landingStep;
                         probeDepth <= landingScanLimit + DepthEvidenceEpsilon;
                         probeDepth += landingStep)
                    {
                        Vector3 landingOrigin = topHit.point + forward * probeDepth + up * QueryEpsilon;
                        if (!Physics.Raycast(
                                landingOrigin,
                                -up,
                                out RaycastHit landingHit,
                                depthProbeDistance,
                                settings.ObstacleMask,
                                QueryTriggerInteraction.Ignore))
                        {
                            break;
                        }
                        if (Mathf.Abs(Vector3.Dot(depthHit.point - landingHit.point, up)) > settings.ScanStep)
                            break;
                        landingSurfaceDepth = probeDepth;
                        landingSurfacePoint = landingHit.point;
                    }
                }

                Vector3 destinationSurface = depthProbeHasHit
                    ? depthHit.point
                    : capsuleBottom + forward * settings.Vault1mMaxDepth;
                bool landingScanExtended = IsFinitePositive(landingSurfaceDepth) &&
                                           landingSurfaceDepth > settings.Vault1mMaxDepth + DepthEvidenceEpsilon;
                if (landingScanExtended)
                {
                    // 掃描已經走得比探測常數遠，落點本身就在牆後方很遠，不需要再補膠囊淨空。
                    destinationSurface = landingSurfacePoint;
                }
                else if (!surfaceContinues)
                {
                    // Vault 的落點必須讓角色自身膠囊完整越過薄牆；只落在 depth 探線上時，
                    // 膠囊後緣仍可能與牆重疊，會把合法薄牆誤判為 DestinationBlocked。
                    destinationSurface += forward *
                                          (AuthoredRadius + settings.DestinationClearanceMargin);
                }
                Vector3 rootToCapsuleBottom = capsuleBottom - transform.position;
                // 🔄（docs/23 Phase 1 後續／Playtest「終點位置仍然有誤」）
                //    `DestinationClearanceMargin` 是 **query 用的抬升量**——它存在的理由是讓
                //    CheckCapsule／corridor 取樣不要把落腳面本身算成阻擋物。
                //    舊版把它直接加進 `destinationPoint`，而那個點就是 commit 給 warp plan 的
                //    **終點**（endpoint target ＋ piecewise Exit/Recovery knot），
                //    於是角色每次都停在平台上方 5 cm，再由重力／接地補正拉下來。
                //    現在兩者分離：committed 終點＝腳踩在表面上，抬升只用於查詢。
                Vector3 destinationRoot = destinationSurface - rootToCapsuleBottom;
                Vector3 destinationQueryRoot =
                    destinationRoot + up * settings.DestinationClearanceMargin;
                destinationPoint = destinationRoot;

                Vector3 destinationCenter = destinationQueryRoot + transform.TransformVector(AuthoredCenter);
                float segmentHalfLength = Mathf.Max(0f, halfHeight - AuthoredRadius);
                capsulePoint1 = destinationCenter + up * segmentHalfLength;
                capsulePoint2 = destinationCenter - up * segmentHalfLength;
                capsuleRadius = AuthoredRadius;
                destinationBlocked = Physics.CheckCapsule(
                    capsulePoint1,
                    capsulePoint2,
                    capsuleRadius,
                    settings.ObstacleMask,
                    QueryTriggerInteraction.Ignore);

                Vector3 wallNormal = Vector3.ProjectOnPlane(forwardHit.normal, up);
                if (wallNormal.sqrMagnitude <= DirectionSqrEpsilon) wallNormal = -forward;
                wallNormal.Normalize();
                // Signed standing-capsule clearance against the measured wall plane. This is
                // animation-agnostic Probe evidence: positive is clear/touching, negative is
                // penetration. Consumers must not reconstruct capsule geometry or query again.
                entryCapsuleWallClearance = Vector3.Dot(
                    capsuleCenter - forwardHit.point, wallNormal) -
                    AuthoredRadius + _characterController.skinWidth;
                Vector3 tangent = Vector3.Cross(topHit.normal, wallNormal);
                if (tangent.sqrMagnitude > DirectionSqrEpsilon)
                {
                    tangent.Normalize();
                    if (Vector3.Dot(tangent, transform.right) < 0f) tangent = -tangent;
                }

                MeasureLedgeInterval(
                    topHit,
                    tangent,
                    up,
                    out float tangentMin,
                    out float tangentMax);
                ledgeFrame = new TraversalLedgeFrame(
                    topHit.point,
                    tangent,
                    wallNormal,
                    topHit.normal,
                    tangentMin,
                    tangentMax);

                Vector3 desiredEntryRoot = topHit.point +
                                           wallNormal * (AuthoredRadius +
                                                         _characterController.skinWidth +
                                                         settings.DestinationClearanceMargin);
                desiredEntryRoot += up * Vector3.Dot(transform.position - desiredEntryRoot, up);

                Vector3 clearanceEntryRoot = desiredEntryRoot;
                float clearanceRootAlongUp = Vector3.Dot(
                    topHit.point - rootToCapsuleBottom +
                    up * settings.DestinationClearanceMargin,
                    up);
                clearanceEntryRoot += up *
                    (clearanceRootAlongUp - Vector3.Dot(clearanceEntryRoot, up));
                Vector3 transferRoot = destinationPoint + up *
                    (clearanceRootAlongUp - Vector3.Dot(destinationPoint, up));

                // ⚠️ Corridor 是 **query evidence**，因此最後一段必須用抬升過的落點：
                //    committed 終點現在是「腳正好踩在表面上」，拿它做 CheckCapsule 會被落腳面本身擋下。
                corridor = ValidateFixedCapsuleCorridor(
                    desiredEntryRoot,
                    clearanceEntryRoot,
                    transferRoot,
                    destinationQueryRoot,
                    up,
                    halfHeight,
                    AuthoredRadius);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RecordTraversalDepthProbe(depthProbeOrigin, depthProbeEnd, depthProbeHasHit);
            RecordTraversalDestinationCapsule(
                capsulePoint1, capsulePoint2, capsuleRadius, hasTop, destinationBlocked);
#endif

            var measurement = new TraversalProbeMeasurement(
                data.IsGrounded,
                data.MoveSpeed,
                hasForwardHit,
                hasForwardHit ? forwardHit.point : default,
                hasForwardHit ? forwardHit.normal : default,
                firstClearHeight,
                hasTop,
                hasTop ? topHit.point : default,
                hasTop ? topHit.normal : default,
                height,
                depth,
                destinationPoint,
                destinationBlocked,
                _characterController.slopeLimit,
                forward,
                ledgeFrame,
                corridor,
                transform.position,
                facingForward,
                directionSource,
                entryCapsuleWallClearance,
                landingSurfaceDepth);

            CompleteTick(TraversalClassifier.Classify(in measurement, in settings, _previousKind));
        }

        /// <summary>Records the pure entry decision for debug; never performs an environment query.</summary>
        public void RecordEntryEvaluation(in TraversalEntryEvaluation evaluation)
        {
            EntryEvaluation = evaluation;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSnapshot.EntryEvaluation = evaluation;
            UpdateTraversalRuntimeDebugLines();
#endif
        }

        /// <summary>
        /// 🆕（docs/23 §A10／R1）Records the Jump-vs-Traversal selection verdict **and its reason**.
        /// 舊版 selection 只回 <c>bool</c>，被否決時畫面照樣顯示 `Climb1m` ＋ `Entry: Accept`
        /// 卻什麼都不發生——那是本系統最誤導的狀態。同樣不查環境、不影響執行。
        /// </summary>
        public void RecordSelectionEvaluation(in TraversalSelectionEvaluation evaluation)
        {
            SelectionEvaluation = evaluation;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSnapshot.SelectionEvaluation = evaluation;
            UpdateTraversalRuntimeDebugLines();
#endif
        }

        /// <summary>
        /// 記錄 Animation Fitting 的結果供除錯顯示；**不做任何環境查詢、也不影響 Candidate**。
        /// 由 <c>TraversalState.CanEnter</c> 推入，與 entry／selection 同一條觀測通道。
        /// </summary>
        public void RecordAnimationFit(in TraversalAnimationFit fit)
        {
            AnimationFit = fit;
        }

        private void MeasureLedgeInterval(
            RaycastHit topHit,
            Vector3 tangent,
            Vector3 up,
            out float tangentMin,
            out float tangentMax)
        {
            tangentMin = 0f;
            tangentMax = 0f;
            if (topHit.collider == null || tangent.sqrMagnitude <= DirectionSqrEpsilon) return;

            float step = Mathf.Max(0.05f, settings.ScanStep);
            float maximum = Mathf.Max(0.75f, AuthoredRadius * 2f);
            float rayHeight = Mathf.Max(0.1f, step);
            float rayDistance = rayHeight + settings.ScanStep * 2f;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float extent = 0f;
                for (float distance = step; distance <= maximum + QueryEpsilon; distance += step)
                {
                    Vector3 origin = topHit.point + tangent * (distance * sign) + up * rayHeight;
                    bool hit = Physics.Raycast(
                        origin,
                        -up,
                        out RaycastHit sample,
                        rayDistance,
                        settings.ObstacleMask,
                        QueryTriggerInteraction.Ignore);
                    if (!hit || sample.collider != topHit.collider ||
                        Vector3.Angle(sample.normal, up) > _characterController.slopeLimit)
                    {
                        break;
                    }
                    extent = distance;
                }

                if (sign < 0) tangentMin = -extent;
                else tangentMax = extent;
            }
        }

        private TraversalCorridorEvidence ValidateFixedCapsuleCorridor(
            Vector3 entryRoot,
            Vector3 clearanceRoot,
            Vector3 transferRoot,
            Vector3 exitRoot,
            Vector3 up,
            float halfHeight,
            float radius)
        {
            if (!IsCapsuleSegmentClear(entryRoot, clearanceRoot, up, halfHeight, radius))
            {
                return new TraversalCorridorEvidence(
                    false,
                    TraversalCorridorRejectReason.EntryToClearanceBlocked,
                    entryRoot,
                    clearanceRoot,
                    transferRoot,
                    exitRoot);
            }
            if (!IsCapsuleSegmentClear(clearanceRoot, transferRoot, up, halfHeight, radius))
            {
                return new TraversalCorridorEvidence(
                    false,
                    TraversalCorridorRejectReason.ClearanceToExitBlocked,
                    entryRoot,
                    clearanceRoot,
                    transferRoot,
                    exitRoot);
            }
            if (!IsCapsuleSegmentClear(transferRoot, exitRoot, up, halfHeight, radius))
            {
                return new TraversalCorridorEvidence(
                    false,
                    TraversalCorridorRejectReason.ExitDescentBlocked,
                    entryRoot,
                    clearanceRoot,
                    transferRoot,
                    exitRoot);
            }

            return TraversalCorridorEvidence.Clear(
                entryRoot, clearanceRoot, transferRoot, exitRoot);
        }

        private bool IsCapsuleSegmentClear(
            Vector3 startRoot,
            Vector3 endRoot,
            Vector3 up,
            float halfHeight,
            float radius)
        {
            const int SamplesPerSegment = 8;
            float segmentHalfLength = Mathf.Max(0f, halfHeight - radius);
            for (int i = 1; i <= SamplesPerSegment; i++)
            {
                float t = i / (float)SamplesPerSegment;
                Vector3 root = Vector3.Lerp(startRoot, endRoot, t);
                Vector3 center = root + transform.TransformVector(AuthoredCenter);
                Vector3 point1 = center + up * segmentHalfLength;
                Vector3 point2 = center - up * segmentHalfLength;
                if (Physics.CheckCapsule(
                        point1,
                        point2,
                        radius,
                        settings.ObstacleMask,
                        QueryTriggerInteraction.Ignore))
                {
                    return false;
                }
            }
            return true;
        }

        private void CompleteTick(TraversalCandidate candidate)
        {
            Candidate = candidate;
            _previousKind = candidate.Kind;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSnapshot.Candidate = candidate;
            _debugSnapshot.HasSnapshot = true;
            UpdateTraversalRuntimeDebugLines();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // O-T Traversal world-space observability（docs/18 §1.1）。兩個繪製通道只讀 Tick 快照。
        private struct TraversalDebugRay
        {
            public Vector3 Origin;
            public Vector3 End;
            public bool HasHit;
        }

        private struct TraversalDebugSnapshot
        {
            public bool HasSnapshot;
            public TraversalCandidate Candidate;
            public Vector3 TopProbeOrigin;
            public Vector3 TopProbeEnd;
            public bool TopProbeHasHit;
            public Vector3 DepthProbeOrigin;
            public Vector3 DepthProbeEnd;
            public bool DepthProbeHasHit;
            public Vector3 CapsulePoint1;
            public Vector3 CapsulePoint2;
            public float CapsuleRadius;
            public bool HasDestinationCapsule;
            public bool DestinationBlocked;
            public TraversalEntryEvaluation EntryEvaluation;
            public TraversalSelectionEvaluation SelectionEvaluation;
        }

        [SerializeField] private bool drawTraversalRuntimeLines = true;   // 🔄 2026-09-15 預設開啟（使用者裁決）
        /// <summary>
        /// Game View 的 <c>TextMesh</c> 面板。**預設關閉**——決策鏈是文字，文字屬於
        /// Window ▸ Project ▸ Traversal Debug，不屬於正在觀察的畫面。
        /// 世界空間的線／球不受此旗標影響（那些本來就該畫在空間裡）。
        /// </summary>
        [SerializeField] private bool drawTraversalPanelText = false;
#if UNITY_EDITOR
        [SerializeField] private bool drawTraversalSceneGizmos = true;    // 🔄 2026-09-15 預設開啟
#endif

        // 🆕（2026-09-15）供 RuntimeDebugPanel 遠端切換。
        // ⚠️ 旗標的真相仍住在本元件；Panel 只是遙控器，不持有狀態、不計算任何東西。
        // 三個分開曝露（不像 Foot IK 併成一個）：它們回答**不同**的問題——
        // 線＝空間關係、gizmo＝Scene View 的同一批、文字＝決策鏈，觀察時常常只想開其中一種。

        internal bool DebugDrawRuntimeLines
        {
            get => drawTraversalRuntimeLines;
            set => drawTraversalRuntimeLines = value;
        }

        internal bool DebugDrawPanelText
        {
            get => drawTraversalPanelText;
            set => drawTraversalPanelText = value;
        }

        internal bool DebugDrawSceneGizmos
        {
#if UNITY_EDITOR
            get => drawTraversalSceneGizmos;
            set => drawTraversalSceneGizmos = value;
#else
            // Player build 沒有 Scene View ⇒ 這個頻道不存在，回報 false 而不是假裝有。
            get => false;
            set { }
#endif
        }

        private const int MaximumRuntimeLineCount = 96;
        private const float DebugLineWidth = 0.018f;
        private const float DebugPointRadius = 0.035f;
        private const float DebugNormalLength = 0.3f;

        private readonly TraversalDebugRay[] _debugStepRays = new TraversalDebugRay[MaximumScanRayCount];
        private readonly LineRenderer[] _debugRuntimeLines = new LineRenderer[MaximumRuntimeLineCount];
        private int _debugStepRayCount;
        private TraversalDebugSnapshot _debugSnapshot;
        private Transform _debugRuntimeRoot;
        private Material _debugRuntimeMaterial;
        private TextMesh _debugRuntimeLabel;

        private static readonly Color DebugHitColor = new Color(0.2f, 1f, 0.4f, 0.95f);
        private static readonly Color DebugMissColor = new Color(1f, 0.25f, 0.15f, 0.95f);
        private static readonly Color DebugTopColor = new Color(0.25f, 0.8f, 1f, 0.95f);
        private static readonly Color DebugDepthColor = new Color(1f, 0.75f, 0.15f, 0.95f);
        private static readonly Color DebugClearColor = new Color(0.25f, 1f, 0.75f, 0.95f);
        private static readonly Color DebugBlockedColor = new Color(1f, 0.15f, 0.65f, 0.95f);

        private void ResetTraversalDebugSnapshot()
        {
            _debugStepRayCount = 0;
            _debugSnapshot = default;
        }

        private void RecordTraversalStepRay(Vector3 origin, bool hasHit, Vector3 end)
        {
            if (_debugStepRayCount >= _debugStepRays.Length) return;
            _debugStepRays[_debugStepRayCount++] = new TraversalDebugRay
            {
                Origin = origin,
                End = end,
                HasHit = hasHit,
            };
        }

        private void RecordTraversalTopProbe(Vector3 origin, Vector3 end, bool hasHit)
        {
            _debugSnapshot.TopProbeOrigin = origin;
            _debugSnapshot.TopProbeEnd = end;
            _debugSnapshot.TopProbeHasHit = hasHit;
        }

        private void RecordTraversalDepthProbe(Vector3 origin, Vector3 end, bool hasHit)
        {
            _debugSnapshot.DepthProbeOrigin = origin;
            _debugSnapshot.DepthProbeEnd = end;
            _debugSnapshot.DepthProbeHasHit = hasHit;
        }

        private void RecordTraversalDestinationCapsule(
            Vector3 point1,
            Vector3 point2,
            float radius,
            bool hasCapsule,
            bool blocked)
        {
            _debugSnapshot.CapsulePoint1 = point1;
            _debugSnapshot.CapsulePoint2 = point2;
            _debugSnapshot.CapsuleRadius = radius;
            _debugSnapshot.HasDestinationCapsule = hasCapsule;
            _debugSnapshot.DestinationBlocked = blocked;
        }

        private void UpdateTraversalRuntimeDebugLines()
        {
            int lineCount = 0;
            int shapeCount = 0;

            // 🆕（2026-09-15）**關鍵高度恆畫**，不等 snapshot。
            // 「我現在爬不爬得上這面牆」在還沒偵測到候選時就該看得出來。
            if (drawTraversalRuntimeLines) AppendTraversalKeyHeights(ref lineCount, ref shapeCount);

            if (drawTraversalRuntimeLines && _debugSnapshot.HasSnapshot)
            {
                for (int i = 0; i < _debugStepRayCount; i++)
                {
                    TraversalDebugRay ray = _debugStepRays[i];
                    AppendTraversalRuntimeLine(
                        ray.Origin,
                        ray.End,
                        ray.HasHit ? DebugHitColor : DebugMissColor,
                        ref lineCount);
                }

                AppendTraversalRuntimeProbe(
                    _debugSnapshot.TopProbeOrigin,
                    _debugSnapshot.TopProbeEnd,
                    _debugSnapshot.TopProbeHasHit,
                    DebugTopColor,
                    ref lineCount);
                AppendTraversalRuntimeProbe(
                    _debugSnapshot.DepthProbeOrigin,
                    _debugSnapshot.DepthProbeEnd,
                    _debugSnapshot.DepthProbeHasHit,
                    DebugDepthColor,
                    ref lineCount);

                TraversalCandidate candidate = _debugSnapshot.Candidate;
                if (candidate.ObstacleNormal.sqrMagnitude > DirectionSqrEpsilon)
                    AppendTraversalRuntimeVector(
                        candidate.ObstacleHitPoint, candidate.ObstacleNormal, DebugHitColor, ref lineCount);
                if (candidate.TopNormal.sqrMagnitude > DirectionSqrEpsilon)
                    AppendTraversalRuntimeVector(
                        candidate.TopPoint, candidate.TopNormal, DebugTopColor, ref lineCount);

                if (candidate.LedgeFrame.IsValid)
                {
                    AppendTraversalRuntimeLine(
                        candidate.LedgeFrame.PointAt(candidate.LedgeFrame.TangentMin),
                        candidate.LedgeFrame.PointAt(candidate.LedgeFrame.TangentMax),
                        DebugTopColor,
                        ref lineCount);
                }

                TraversalCorridorEvidence corridor = candidate.Corridor;
                Color corridorColor = corridor.IsClear ? DebugClearColor : DebugBlockedColor;
                AppendTraversalRuntimeLine(corridor.EntryRoot, corridor.ClearanceRoot, corridorColor, ref lineCount);
                AppendTraversalRuntimeLine(corridor.ClearanceRoot, corridor.TransferRoot, corridorColor, ref lineCount);
                AppendTraversalRuntimeLine(corridor.TransferRoot, corridor.ExitRoot, corridorColor, ref lineCount);

                TraversalEntryEvaluation entry = _debugSnapshot.EntryEvaluation;
                AppendEntryRuntimeDebug(in candidate, in entry, ref lineCount);

                if (_debugSnapshot.HasDestinationCapsule)
                {
                    AppendTraversalRuntimeCapsule(
                        _debugSnapshot.CapsulePoint1,
                        _debugSnapshot.CapsulePoint2,
                        _debugSnapshot.CapsuleRadius,
                        _debugSnapshot.DestinationBlocked ? DebugBlockedColor : DebugClearColor,
                        ref lineCount);
                }
                UpdateTraversalRuntimeLabel(in candidate);
            }
            else if (_debugRuntimeLabel != null)
            {
                _debugRuntimeLabel.gameObject.SetActive(false);
            }

            for (int i = lineCount; i < _debugRuntimeLines.Length; i++)
            {
                if (_debugRuntimeLines[i] != null) _debugRuntimeLines[i].enabled = false;
            }

            // 形狀池同樣要關掉沒用到的，否則上一幀的球會留在原地變成幽靈。
            for (int i = shapeCount; i < _debugShapeLines.Length; i++)
            {
                if (_debugShapeLines[i] != null) _debugShapeLines[i].enabled = false;
            }
        }

        /// <summary>
        /// 🆕（2026-09-15，使用者要求：「不需要文字，給我射線以及關鍵高度表示」）
        /// **攀爬門檻的高度尺**——畫在角色右側的一排短刻度，每一條就是一個 authored 門檻。
        ///
        /// <para><b>它回答的問題</b></para>
        /// 「為什麼判定可／不可攀爬」在這之前只能從面板數字腦補：你看得到射線打到牆，
        /// 但看不到**那個高度算高還是算矮**。有了尺，`障礙物頂端的青色刻度`在紅線之上還是之下
        /// 就是答案本身——不需要任何文字。
        ///
        /// <list type="bullet">
        /// <item><b>灰</b>＝地面（root 高度），尺的原點</item>
        /// <item><b>琥珀</b>＝<c>Climb1mMaxHeight</c>：低於它走 Vault／Climb1m</item>
        /// <item><b>紅</b>＝<c>Climb2mMaxHeight</c>：**超過這條就完全爬不上去**</item>
        /// <item><b>暗青</b>＝<c>ScanCeiling</c>：再高就連看都不看</item>
        /// <item><b>亮青（較長）</b>＝**本幀偵測到的障礙物頂端**，並有一條水平連接線指向真正的 TopPoint</item>
        /// <item><b>琥珀（水平）</b>＝<c>ForwardScanDistance</c>：水平搆得到多遠。
        /// `docs/24` §19 指出真正卡住玩家的就是這個值（1.25 m &lt; 理想進場距離 1.402 m）</item>
        /// </list>
        ///
        /// <para><b>⛔ 只讀，不重算</b></para>
        /// 全部取自 authored <c>settings</c> 與**已經算好的** <c>_debugSnapshot</c>。
        /// 這裡沒有任何 <c>Raycast</c>／<c>CapsuleCast</c>——`docs/18` §1「**記錄，不要重算**」：
        /// debug 畫出來的必須是**真正做決定的那份資料**，否則它會變成第二個看起來很權威的來源。
        /// </summary>
        private void AppendTraversalKeyHeights(ref int lineCount, ref int shapeCount)
        {
            Vector3 root = transform.position;
            Vector3 forward = transform.forward;

            // ── 關鍵高度：以角色為中心的水平圓環 ───────────────────────────────────
            // 🔄 2026-09-15 改版：原本畫成角色**側邊**的三根短刻度，使用者回報
            //    「不知道那是什麼、為什麼在側邊」——那個抱怨是對的：一根浮在身側的短線
            //    既不像高度也不像門檻。圓環套在身上，一眼就讀成「我的攀爬上限在這裡」。
            AppendDebugRing(root + Vector3.up * settings.Climb1mMaxHeight,
                KeyHeightRingRadius, DebugDepthColor, ref shapeCount);
            AppendDebugRing(root + Vector3.up * settings.Climb2mMaxHeight,
                KeyHeightRingRadius, DebugMissColor, ref shapeCount);
            AppendDebugRing(root + Vector3.up * settings.ScanCeiling,
                KeyHeightRingRadius, DebugRulerColor, ref shapeCount);

            // 水平搆得到多遠。`docs/24` §19：真正卡住玩家的就是這個值。
            Vector3 reachOrigin = root + Vector3.up * RulerReachHeight;
            AppendTraversalRuntimeLine(
                reachOrigin, reachOrigin + forward * settings.ForwardScanDistance,
                DebugDepthColor, ref lineCount);

            if (!_debugSnapshot.HasSnapshot) return;
            TraversalCandidate candidate = _debugSnapshot.Candidate;

            // ── 偵測到的關鍵點：線框球 ─────────────────────────────────────────────
            // 使用者要求「這幾項要檢測到的點用球體顯示」。球比線好讀的原因是它有**體積**：
            // 你看得出那個點在空間的哪裡，而不是一條可能被牆吃掉一半的線。

            // ① 牆面接觸點：前方掃描打到障礙的地方。
            if (candidate.ObstacleNormal.sqrMagnitude > DirectionSqrEpsilon)
                AppendDebugSphere(candidate.ObstacleHitPoint, ContactSphereRadius,
                    DebugHitColor, ref shapeCount);

            // ② 手抓點：障礙頂端邊緣。**這是攀爬真正要抓的地方**，畫得比其他球大。
            if (candidate.TopNormal.sqrMagnitude > DirectionSqrEpsilon)
            {
                AppendDebugSphere(candidate.TopPoint, GrabSphereRadius, DebugTopColor, ref shapeCount);
                // 連一條垂直線到地面，讓「這個抓點有多高」不必用眼睛估。
                AppendTraversalRuntimeLine(
                    new Vector3(candidate.TopPoint.x, root.y, candidate.TopPoint.z),
                    candidate.TopPoint, DebugTopColor, ref lineCount);
            }

            // ③ 該站在哪：entry 求出的 desired root。站不到就是進不去。
            TraversalEntryEvaluation entry = _debugSnapshot.EntryEvaluation;
            if (entry.DesiredRootPosition != default)
            {
                AppendDebugSphere(entry.DesiredRootPosition, StanceSphereRadius,
                    entry.Executable ? DebugClearColor : DebugBlockedColor, ref shapeCount);
            }

            // ④ 人上去所需的空間：目的地膠囊的兩顆球心各畫一顆，半徑＝膠囊半徑。
            //    既有的膠囊線框保留；球讓「那塊空間有多大」在遠處也看得出來。
            if (_debugSnapshot.HasDestinationCapsule)
            {
                Color clearanceColor = _debugSnapshot.DestinationBlocked
                    ? DebugBlockedColor
                    : DebugClearColor;
                AppendDebugSphere(_debugSnapshot.CapsulePoint1, _debugSnapshot.CapsuleRadius,
                    clearanceColor, ref shapeCount);
                AppendDebugSphere(_debugSnapshot.CapsulePoint2, _debugSnapshot.CapsuleRadius,
                    clearanceColor, ref shapeCount);
            }
        }

        private const float RulerReachHeight = 1.0f;
        private const float KeyHeightRingRadius = 0.55f;
        private const float ContactSphereRadius = 0.06f;
        private const float GrabSphereRadius = 0.11f;
        private const float StanceSphereRadius = 0.09f;
        private static readonly Color DebugRulerColor = new Color(0.6f, 0.6f, 0.66f, 0.8f);

        /// <summary>
        /// 🆕（docs/23 §H-2）Entry 的空間關係全部畫在空間裡：
        /// ① current ● → desired ◎ 拆成 **L 形兩段**（沿 wall normal 的 longitudinal ＋ 沿 tangent 的 lateral），
        ///    一眼看出偏的是哪個軸；② 合法距離帶的近／遠兩條界線；③ 目前 facing 與 desired facing 箭頭。
        /// 這三樣以前完全沒有被視覺化，只能從面板數字腦補。
        /// </summary>
        private void AppendEntryRuntimeDebug(
            in TraversalCandidate candidate,
            in TraversalEntryEvaluation entry,
            ref int lineCount)
        {
            if (entry.CurrentRootPosition == default && entry.DesiredRootPosition == default) return;
            Color verdictColor = entry.Executable ? DebugClearColor : DebugBlockedColor;

            TraversalLedgeFrame ledge = candidate.LedgeFrame;
            if (ledge.IsValid)
            {
                Vector3 up = ledge.TopNormal;
                Vector3 wallNormal = Vector3.ProjectOnPlane(ledge.WallNormal, up);
                Vector3 tangent = Vector3.ProjectOnPlane(ledge.Tangent, up);
                if (wallNormal.sqrMagnitude > DirectionSqrEpsilon &&
                    tangent.sqrMagnitude > DirectionSqrEpsilon)
                {
                    wallNormal.Normalize();
                    tangent.Normalize();

                    // ① L 形拆解：先沿 wall normal 走完距離誤差，再沿 tangent 走完側向誤差。
                    Vector3 current = entry.CurrentRootPosition;
                    Vector3 desired = entry.DesiredRootPosition;
                    Vector3 delta = desired - current;
                    Vector3 corner = current + wallNormal * Vector3.Dot(delta, wallNormal);
                    AppendTraversalRuntimeLine(current, corner, verdictColor, ref lineCount);
                    AppendTraversalRuntimeLine(corner, desired, DebugDepthColor, ref lineCount);

                    // ② 合法距離帶：沿 ledge interval 畫近／遠兩條界線。
                    float rootHeight = Vector3.Dot(current - ledge.EdgeOrigin, up);
                    AppendEntryBandLine(
                        in ledge, wallNormal, tangent, up, rootHeight,
                        entry.MinimumLongitudinalDistance, verdictColor, ref lineCount);
                    AppendEntryBandLine(
                        in ledge, wallNormal, tangent, up, rootHeight,
                        entry.MaximumLongitudinalDistance, verdictColor, ref lineCount);

                    // ③ facing vs desired facing。
                    AppendTraversalRuntimeVector(current, entry.CurrentFacing, DebugTopColor, ref lineCount);
                    AppendTraversalRuntimeVector(current, -wallNormal, verdictColor, ref lineCount);
                    return;
                }
            }

            AppendTraversalRuntimeLine(
                entry.CurrentRootPosition, entry.DesiredRootPosition, verdictColor, ref lineCount);
        }

        private void AppendEntryBandLine(
            in TraversalLedgeFrame ledge,
            Vector3 wallNormal,
            Vector3 tangent,
            Vector3 up,
            float rootHeight,
            float longitudinalDistance,
            Color color,
            ref int lineCount)
        {
            if (float.IsNaN(longitudinalDistance) || float.IsInfinity(longitudinalDistance)) return;
            Vector3 basePoint = ledge.EdgeOrigin + wallNormal * longitudinalDistance + up * rootHeight;
            AppendTraversalRuntimeLine(
                basePoint + tangent * ledge.TangentMin,
                basePoint + tangent * ledge.TangentMax,
                color,
                ref lineCount);
        }

        private void AppendTraversalRuntimeProbe(
            Vector3 origin,
            Vector3 end,
            bool hasHit,
            Color hitColor,
            ref int lineCount)
        {
            if (origin == default && end == default) return;
            AppendTraversalRuntimeLine(origin, end, hasHit ? hitColor : DebugMissColor, ref lineCount);
            Vector3 cross = Vector3.one * DebugPointRadius;
            AppendTraversalRuntimeLine(end - cross, end + cross, hasHit ? hitColor : DebugMissColor, ref lineCount);
            cross.x = -cross.x;
            AppendTraversalRuntimeLine(end - cross, end + cross, hasHit ? hitColor : DebugMissColor, ref lineCount);
        }

        private void AppendTraversalRuntimeVector(
            Vector3 origin,
            Vector3 direction,
            Color color,
            ref int lineCount)
        {
            if (direction.sqrMagnitude <= DirectionSqrEpsilon) return;
            AppendTraversalRuntimeLine(
                origin, origin + direction.normalized * DebugNormalLength, color, ref lineCount);
        }

        private void AppendTraversalRuntimeCapsule(
            Vector3 point1,
            Vector3 point2,
            float radius,
            Color color,
            ref int lineCount)
        {
            Vector3 right = transform.right * radius;
            Vector3 forward = transform.forward * radius;
            AppendTraversalRuntimeLine(point1, point2, color, ref lineCount);
            AppendTraversalRuntimeLine(point1 - right, point1 + right, color, ref lineCount);
            AppendTraversalRuntimeLine(point2 - right, point2 + right, color, ref lineCount);
            AppendTraversalRuntimeLine(point1 - forward, point1 + forward, color, ref lineCount);
            AppendTraversalRuntimeLine(point2 - forward, point2 + forward, color, ref lineCount);
        }

        private void AppendTraversalRuntimeLine(
            Vector3 start,
            Vector3 end,
            Color color,
            ref int lineCount)
        {
            if (lineCount >= _debugRuntimeLines.Length) return;
            LineRenderer line = GetOrCreateTraversalRuntimeLine(lineCount);
            if (line == null) return;
            lineCount++;
            line.enabled = true;
            line.startColor = color;
            line.endColor = color;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        // =====================================================================
        // 形狀池（圓環／線框球）——與 2 點線段池分開
        //
        // 既有的 `_debugRuntimeLines` 每條固定 `positionCount = 2`，畫不出圓。
        // 若用 2 點線段去逼近一個圓，一顆線框球要 3×24 = 72 條，一幀就吃光整個預算。
        // ⇒ 另開一個**可變點數**的池；一個圓環 ＝ 一個 LineRenderer。
        // =====================================================================

        private const int MaximumShapeCount = 32;
        private const int ShapeRingSegments = 24;
        private readonly LineRenderer[] _debugShapeLines = new LineRenderer[MaximumShapeCount];
        private readonly Vector3[] _shapeRingBuffer = new Vector3[ShapeRingSegments];

        /// <summary>水平圓環（法線 ＝ 世界 up）。</summary>
        private void AppendDebugRing(Vector3 center, float radius, Color color, ref int shapeCount)
            => AppendDebugRing(center, radius, Vector3.right, Vector3.forward, color, ref shapeCount);

        /// <summary>線框球 ＝ 三個互相垂直的圓環。⚠️ 佔用 3 個 shape 名額。</summary>
        private void AppendDebugSphere(Vector3 center, float radius, Color color, ref int shapeCount)
        {
            if (radius <= 0f) return;
            AppendDebugRing(center, radius, Vector3.right, Vector3.forward, color, ref shapeCount);
            AppendDebugRing(center, radius, Vector3.right, Vector3.up, color, ref shapeCount);
            AppendDebugRing(center, radius, Vector3.forward, Vector3.up, color, ref shapeCount);
        }

        private void AppendDebugRing(
            Vector3 center, float radius, Vector3 axisA, Vector3 axisB, Color color, ref int shapeCount)
        {
            if (shapeCount >= _debugShapeLines.Length) return;
            LineRenderer ring = GetOrCreateTraversalShapeLine(shapeCount);
            if (ring == null) return;
            shapeCount++;

            for (int i = 0; i < ShapeRingSegments; i++)
            {
                float angle = i * (Mathf.PI * 2f / ShapeRingSegments);
                _shapeRingBuffer[i] =
                    center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
            }

            ring.enabled = true;
            ring.startColor = color;
            ring.endColor = color;
            ring.SetPositions(_shapeRingBuffer);
        }

        private LineRenderer GetOrCreateTraversalShapeLine(int index)
        {
            LineRenderer existing = _debugShapeLines[index];
            if (existing != null) return existing;
            if (!EnsureTraversalRuntimeDebugRoot()) return null;

            var shapeObject = new GameObject("Traversal Debug Shape")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = gameObject.layer,
            };
            shapeObject.transform.SetParent(_debugRuntimeRoot, false);
            var line = shapeObject.AddComponent<LineRenderer>();
            line.sharedMaterial = _debugRuntimeMaterial;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = ShapeRingSegments;
            line.startWidth = DebugLineWidth;
            line.endWidth = DebugLineWidth;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _debugShapeLines[index] = line;
            return line;
        }

        private LineRenderer GetOrCreateTraversalRuntimeLine(int index)
        {
            LineRenderer existing = _debugRuntimeLines[index];
            if (existing != null) return existing;
            if (!EnsureTraversalRuntimeDebugRoot()) return null;

            var lineObject = new GameObject("Traversal Debug Line")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = gameObject.layer,
            };
            lineObject.transform.SetParent(_debugRuntimeRoot, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = _debugRuntimeMaterial;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = DebugLineWidth;
            line.endWidth = DebugLineWidth;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _debugRuntimeLines[index] = line;
            return line;
        }

        private bool EnsureTraversalRuntimeDebugRoot()
        {
            if (_debugRuntimeRoot != null) return true;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;
            _debugRuntimeMaterial = new Material(shader)
            {
                name = "Traversal Debug Lines (Runtime)",
                hideFlags = HideFlags.HideAndDontSave,
            };
            var root = new GameObject("Traversal Debug (Runtime)")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _debugRuntimeRoot = root.transform;
            _debugRuntimeRoot.SetParent(transform, false);
            return true;
        }

        private void UpdateTraversalRuntimeLabel(in TraversalCandidate candidate)
        {
            if (!drawTraversalPanelText)
            {
                if (_debugRuntimeLabel != null) _debugRuntimeLabel.gameObject.SetActive(false);
                return;
            }
            if (_debugRuntimeLabel == null)
            {
                if (!EnsureTraversalRuntimeDebugRoot()) return;
                var labelObject = new GameObject("Traversal Debug Label")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = gameObject.layer,
                };
                labelObject.transform.SetParent(_debugRuntimeRoot, false);
                _debugRuntimeLabel = labelObject.AddComponent<TextMesh>();
                _debugRuntimeLabel.anchor = TextAnchor.LowerCenter;
                _debugRuntimeLabel.alignment = TextAlignment.Center;
                _debugRuntimeLabel.characterSize = 0.04f;
                _debugRuntimeLabel.fontSize = 48;
            }

            _debugRuntimeLabel.gameObject.SetActive(true);
            _debugRuntimeLabel.text = BuildCompactTraversalPanel(in candidate);
            Vector3 labelBase = candidate.TopPoint != default
                ? candidate.TopPoint
                : transform.position;
            _debugRuntimeLabel.transform.position = labelBase + transform.up * 0.25f;
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
                _debugRuntimeLabel.transform.rotation = mainCamera.transform.rotation;
        }

        /// <summary>
        /// 🆕（docs/23 §H-2）Compact panel：**不出現任何 <c>Vector3</c> 全值，也不重印容差常數**。
        /// 空間關係一律由世界空間的線／球／箭頭表示；面板只留「畫不出來的判定結果與純量」。
        /// 三行涵蓋三個決策層：分類 → entry 合法性 → selection。
        /// </summary>
        private string BuildCompactTraversalPanel(in TraversalCandidate candidate)
        {
            TraversalEntryEvaluation entry = _debugSnapshot.EntryEvaluation;
            TraversalSelectionEvaluation selection = _debugSnapshot.SelectionEvaluation;

            string kindLine =
                $"Traversal: {GetTraversalDebugLabel(candidate.Kind, candidate.RejectReason)}" +
                (candidate.DirectionSource == TraversalDetectionDirectionSource.Move
                    ? "   dir Move"
                    : "   dir Facing");

            string entryLine;
            if (entry.DistanceBand == TraversalEntryDistanceBand.Invalid &&
                entry.RejectReason == TraversalEntryRejectReason.None)
            {
                entryLine = "Entry: (not evaluated — press Jump)";
            }
            else if (entry.Executable)
            {
                entryLine = $"Entry: ACCEPT ({entry.DistanceBand})   " +
                            $"wall {entry.LongitudinalDistance:F2} m   " +
                            $"lat {entry.LateralError * 100f:F0} cm   " +
                            $"face {entry.FacingAngleError:F0}°";
            }
            else
            {
                entryLine = $"Entry: REJECT — {entry.RejectReason}   " +
                            DescribeEntryRejectMagnitude(in entry);
            }

            // ⭐ 這一行就是 docs/23 §A10 指認的缺口：以前 selection 否決完全不留痕跡。
            string selectionLine = !selection.HasResult
                ? "Select: (not evaluated — press Jump)"
                : selection.PreferTraversal
                    ? $"Select: TRAVERSAL ({selection.Reason})" + DescribeReach(in selection)
                    : $"Select: NORMAL JUMP ({selection.Reason})" + DescribeReach(in selection);

            return $"{kindLine}\n{entryLine}\n{selectionLine}";
        }

        private static string DescribeReach(in TraversalSelectionEvaluation selection)
        {
            if (!selection.HasReachEvidence) return string.Empty;
            return $"   ledge {selection.CandidateHeight:F2} m vs jump reach {selection.SafeReach:F2} m";
        }

        private static string DescribeEntryRejectMagnitude(in TraversalEntryEvaluation entry)
        {
            switch (entry.RejectReason)
            {
                case TraversalEntryRejectReason.TooFar:
                case TraversalEntryRejectReason.TooCloseUnsafe:
                    return $"wall {entry.LongitudinalDistance:F2} m " +
                           $"(band {entry.MinimumLongitudinalDistance:F2}–" +
                           $"{entry.MaximumLongitudinalDistance:F2})   " +
                           $"capsule {entry.CapsuleWallClearance * 100f:F1} cm";
                case TraversalEntryRejectReason.LateralOutOfRange:
                    return $"lateral {entry.LateralError * 100f:F0} cm";
                case TraversalEntryRejectReason.FacingOutOfRange:
                    return $"facing {entry.FacingAngleError:F0}°";
                default:
                    return string.Empty;
            }
        }

        private static string GetTraversalDebugLabel(
            TraversalKind kind,
            TraversalRejectReason reason)
        {
            switch (kind)
            {
                case TraversalKind.Vault1m: return "Vault1m";
                case TraversalKind.Climb1m: return "Climb1m";
                case TraversalKind.Climb2m: return "Climb2m";
            }

            switch (reason)
            {
                case TraversalRejectReason.NotGrounded: return "None: NotGrounded";
                case TraversalRejectReason.TooSlow: return "None: TooSlow";
                case TraversalRejectReason.NoForwardHit: return "None: NoForwardHit";
                case TraversalRejectReason.TooHigh: return "None: TooHigh";
                case TraversalRejectReason.NoValidTop: return "None: NoValidTop";
                case TraversalRejectReason.TopTooSteep: return "None: TopTooSteep";
                case TraversalRejectReason.DestinationBlocked: return "None: DestinationBlocked";
                case TraversalRejectReason.CorridorBlocked: return "None: CorridorBlocked";
                default: return "None";
            }
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
            if (!drawTraversalSceneGizmos || !Application.isPlaying || !_debugSnapshot.HasSnapshot) return;

            for (int i = 0; i < _debugStepRayCount; i++)
            {
                TraversalDebugRay ray = _debugStepRays[i];
                Gizmos.color = ray.HasHit ? DebugHitColor : DebugMissColor;
                Gizmos.DrawLine(ray.Origin, ray.End);
            }

            DrawTraversalProbeGizmo(
                _debugSnapshot.TopProbeOrigin,
                _debugSnapshot.TopProbeEnd,
                _debugSnapshot.TopProbeHasHit,
                DebugTopColor);
            DrawTraversalProbeGizmo(
                _debugSnapshot.DepthProbeOrigin,
                _debugSnapshot.DepthProbeEnd,
                _debugSnapshot.DepthProbeHasHit,
                DebugDepthColor);

            TraversalCandidate candidate = _debugSnapshot.Candidate;
            DrawTraversalVectorGizmo(candidate.ObstacleHitPoint, candidate.ObstacleNormal, DebugHitColor);
            DrawTraversalVectorGizmo(candidate.TopPoint, candidate.TopNormal, DebugTopColor);
            if (candidate.LedgeFrame.IsValid)
            {
                Gizmos.color = DebugTopColor;
                Gizmos.DrawLine(
                    candidate.LedgeFrame.PointAt(candidate.LedgeFrame.TangentMin),
                    candidate.LedgeFrame.PointAt(candidate.LedgeFrame.TangentMax));
            }

            TraversalCorridorEvidence corridor = candidate.Corridor;
            Gizmos.color = corridor.IsClear ? DebugClearColor : DebugBlockedColor;
            Gizmos.DrawLine(corridor.EntryRoot, corridor.ClearanceRoot);
            Gizmos.DrawLine(corridor.ClearanceRoot, corridor.TransferRoot);
            Gizmos.DrawLine(corridor.TransferRoot, corridor.ExitRoot);

            TraversalEntryEvaluation entry = _debugSnapshot.EntryEvaluation;
            if (entry.CurrentRootPosition != default || entry.DesiredRootPosition != default)
            {
                Gizmos.color = entry.Executable ? DebugClearColor : DebugBlockedColor;
                Gizmos.DrawLine(entry.CurrentRootPosition, entry.DesiredRootPosition);
                Gizmos.DrawWireSphere(entry.DesiredRootPosition, DebugPointRadius * 1.5f);
            }
            if (_debugSnapshot.HasDestinationCapsule)
            {
                DrawTraversalCapsuleGizmo(
                    _debugSnapshot.CapsulePoint1,
                    _debugSnapshot.CapsulePoint2,
                    _debugSnapshot.CapsuleRadius,
                    _debugSnapshot.DestinationBlocked ? DebugBlockedColor : DebugClearColor);
            }

            // ⛔ docs/22 §16：Scene View 不印任何文字。決策鏈在
            //    Window ▸ Project ▸ Traversal Debug，那裡有版面可以排，這裡沒有。
        }

        private static void DrawTraversalProbeGizmo(
            Vector3 origin,
            Vector3 end,
            bool hasHit,
            Color hitColor)
        {
            if (origin == default && end == default) return;
            Gizmos.color = hasHit ? hitColor : DebugMissColor;
            Gizmos.DrawLine(origin, end);
            Gizmos.DrawWireSphere(end, DebugPointRadius);
        }

        private static void DrawTraversalVectorGizmo(Vector3 origin, Vector3 direction, Color color)
        {
            if (direction.sqrMagnitude <= DirectionSqrEpsilon) return;
            Gizmos.color = color;
            Gizmos.DrawLine(origin, origin + direction.normalized * DebugNormalLength);
        }

        private static void DrawTraversalCapsuleGizmo(
            Vector3 point1,
            Vector3 point2,
            float radius,
            Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(point1, radius);
            Gizmos.DrawWireSphere(point2, radius);
            Gizmos.DrawLine(point1, point2);
        }
#endif
#endif
    }
}
