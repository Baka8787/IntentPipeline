using Project.Core.Environment;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public readonly struct TraversalRootConstraintResult
    {
        public readonly bool IsValid;
        public readonly Vector3 RootPosition;
        public readonly float RootYaw;
        public readonly float ContactError;

        public TraversalRootConstraintResult(
            bool isValid,
            Vector3 rootPosition,
            float rootYaw,
            float contactError)
        {
            IsValid = isValid;
            RootPosition = rootPosition;
            RootYaw = rootYaw;
            ContactError = contactError;
        }
    }

    /// <summary>Pure commit-time root solve. Runtime execution consumes only its result.</summary>
    public static class TraversalRootConstraintSolver
    {
        public static TraversalRootConstraintResult SolveSingle(
            Vector3 handInRoot,
            Vector3 worldTarget,
            float requiredRootYaw)
        {
            if (!IsFinite(handInRoot) || !IsFinite(worldTarget) || !IsFinite(requiredRootYaw))
                return default;
            Quaternion rotation = Quaternion.Euler(0f, requiredRootYaw, 0f);
            Vector3 root = worldTarget - rotation * handInRoot;
            float error = Vector3.Distance(root + rotation * handInRoot, worldTarget);
            return IsFinite(root) && IsFinite(error)
                ? new TraversalRootConstraintResult(true, root, requiredRootYaw, error)
                : default;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// V3 fixed-shape plan builder. It selects hand targets on the committed ledge interval,
    /// solves root contact constraints once, and emits six allocation-free piecewise knots.
    /// </summary>
    public static class TraversalPlanBuilder
    {
        private const float Epsilon = 0.0001f;

        public static bool TryBuild(
            MotionBakeData bake,
            in TraversalCandidate candidate,
            in TraversalEntryEvaluation entry,
            float ledgeSafetyMargin,
            float maxHorizontalCorrection,
            float maxVerticalCorrection,
            out TraversalWarpPlan plan) =>
            TryBuild(
                bake, in candidate, in entry, ledgeSafetyMargin,
                maxHorizontalCorrection, maxVerticalCorrection, out plan, out _);

        /// <summary>
        /// 🆕（docs/23 §R1）帶理由的 piecewise build。正式資產目前一律停在
        /// <see cref="TraversalWarpPlanRejection.MissingTraversalBlock"/>——那正是 Phase 2 要補的資料缺口，
        /// 現在至少會在畫面上明說，而不是靜默退回 endpoint。
        /// </summary>
        public static bool TryBuild(
            MotionBakeData bake,
            in TraversalCandidate candidate,
            in TraversalEntryEvaluation entry,
            float ledgeSafetyMargin,
            float maxHorizontalCorrection,
            float maxVerticalCorrection,
            out TraversalWarpPlan plan,
            out TraversalWarpPlanRejection rejection)
        {
            plan = default;
            rejection = TraversalWarpPlanRejection.None;
            if (bake == null)
            {
                rejection = TraversalWarpPlanRejection.MissingBake;
                return false;
            }
            if (!candidate.IsValid)
            {
                rejection = TraversalWarpPlanRejection.InvalidCandidate;
                return false;
            }
            if (!entry.Executable)
            {
                rejection = TraversalWarpPlanRejection.EntryNotExecutable;
                return false;
            }
            if (!candidate.LedgeFrame.IsValid)
            {
                rejection = TraversalWarpPlanRejection.InvalidLedgeFrame;
                return false;
            }
            if (!candidate.Corridor.IsClear)
            {
                rejection = TraversalWarpPlanRejection.CorridorBlocked;
                return false;
            }
            if (!bake.Traversal.HasValidBakedData)
            {
                rejection = TraversalWarpPlanRejection.MissingTraversalBlock;
                return false;
            }

            TraversalMotionBakeBlock authored = bake.Traversal;
            TraversalLedgeFrame ledge = candidate.LedgeFrame;
            Vector3 tangent = ledge.Tangent;
            Vector3 wallNormal = ledge.WallNormal;
            Vector3 traversalForward = -Vector3.ProjectOnPlane(wallNormal, ledge.TopNormal);
            if (traversalForward.sqrMagnitude <= Epsilon)
            {
                rejection = TraversalWarpPlanRejection.DegenerateStartForward;
                return false;
            }
            traversalForward.Normalize();
            float targetYaw = Mathf.Atan2(traversalForward.x, traversalForward.z) * Mathf.Rad2Deg;

            float min = ledge.TangentMin + Mathf.Max(0f, ledgeSafetyMargin);
            float max = ledge.TangentMax - Mathf.Max(0f, ledgeSafetyMargin);
            if (min > max)
            {
                float middle = (ledge.TangentMin + ledge.TangentMax) * 0.5f;
                min = middle;
                max = middle;
            }
            float centerCoordinate = Mathf.Clamp(
                Vector3.Dot(entry.DesiredRootPosition - ledge.EdgeOrigin, tangent), min, max);

            // ⭐ Root constraint 只認一個 primary contact（BothHands 時固定為 Left）。
            // Unity MatchTarget 一次只接受一個 AvatarTarget；成熟實作也不會先把兩隻手各解一個
            // 絕對 root 再平均。secondary hand 會在 primary root 已決定後，對 Probe 已 commit 的
            // ledge surface evidence 做查詢，剩餘誤差只交給 Hand IK，不再反向拉動 root。
            //
            // authored lateral / wrist-to-grip offset 仍然存在，但它們只屬 animation/contact fitting；
            // Probe geometry 仍只有真實 edge frame 與 interval，兩者不混成同一份資料。
            Vector3 gripLeft = authored.HasGripEdges
                ? authored.LeftGripEdgeInRoot
                : authored.LeftContactAnchor.LeftHandInRoot;
            Vector3 gripRight = authored.HasGripEdges
                ? authored.RightGripEdgeInRoot
                : authored.RightContactAnchor.RightHandInRoot;

            bool primaryIsLeft = authored.UsesLeftHand;
            TraversalPrimaryHand primaryHand = primaryIsLeft
                ? TraversalPrimaryHand.Left
                : authored.UsesRightHand
                    ? TraversalPrimaryHand.Right
                    : TraversalPrimaryHand.None;
            if (primaryHand == TraversalPrimaryHand.None)
            {
                rejection = TraversalWarpPlanRejection.ContactSolveFailed;
                return false;
            }
            float primaryLateral = primaryIsLeft ? gripLeft.x : gripRight.x;
            Vector3 primaryGripTarget = ledge.PointAt(
                Mathf.Clamp(centerCoordinate + primaryLateral, min, max));

            float leftTime = authored.UsesLeftHand
                ? authored.LeftHandContactNormalizedTime
                : authored.RightHandContactNormalizedTime;
            float rightTime = authored.UsesRightHand
                ? authored.RightHandContactNormalizedTime
                : leftTime;
            rightTime = Mathf.Max(leftTime, rightTime);

            // ⭐ docs/22 §17：**對齊的是「動畫自己的邊緣」，不是手腕。**
            //    抓握時四指扣頂面、拇指按牆面，手腕自然垂在頂面下方（Climb2m 實測 16.3 cm）並退在後方
            //    （10.3 cm）。把手腕釘到真實邊緣＝把角色抬高 16 cm 並往前推 10 cm ⇒ 穿模＋手的高度不準。
            //    改用 grip edge 之後，整隻手的 authored 姿勢會原樣落位，不需要逐指調整。
            //    舊資產沒有 grip edge 時安全退回手腕（行為與先前一致）。
            Vector3 leftSolveOffset = authored.HasGripEdges
                ? authored.LeftGripEdgeInRoot
                : authored.LeftContactAnchor.LeftHandInRoot;
            Vector3 rightSolveOffset = authored.HasGripEdges
                ? authored.RightGripEdgeInRoot
                : authored.RightContactAnchor.RightHandInRoot;

            if (!TraversalWarpPlan.TryEvaluateOriginal(
                    bake, candidate.SensedRootPosition, candidate.SensedFacingForward,
                    leftTime, out Vector3 leftOriginalRoot, out _) ||
                !TraversalWarpPlan.TryEvaluateOriginal(
                    bake, candidate.SensedRootPosition, candidate.SensedFacingForward,
                    rightTime, out Vector3 rightOriginalRoot, out _))
            {
                rejection = TraversalWarpPlanRejection.ContactSolveFailed;
                return false;
            }

            Vector3 primarySolveOffset = primaryIsLeft ? leftSolveOffset : rightSolveOffset;
            TraversalRootConstraintResult primarySolve = TraversalRootConstraintSolver.SolveSingle(
                primarySolveOffset, primaryGripTarget, targetYaw);
            if (!primarySolve.IsValid)
            {
                rejection = TraversalWarpPlanRejection.ContactSolveFailed;
                return false;
            }

            Vector3 primaryOriginalRoot = primaryIsLeft ? leftOriginalRoot : rightOriginalRoot;
            Vector3 primaryRootTarget = primarySolve.RootPosition;

            // Contact precision is a soft constraint. Never let the hand solve pull the standing
            // capsule support plane through the wall: derive the support distance from the
            // committed Entry snapshot (root distance minus measured capsule clearance), then
            // clamp translation only along wall normal. Any remaining hand error is observable
            // and may be handled by the bounded IK quality pass.
            float minimumRootWallDistance = Mathf.Max(
                0f, entry.LongitudinalDistance - entry.CapsuleWallClearance);
            primaryRootTarget = ClampOutsideWall(
                primaryRootTarget, ledge.EdgeOrigin, wallNormal, minimumRootWallDistance);
            Vector3 primaryCorrection = primaryRootTarget - primaryOriginalRoot;
            Vector3 leftRootTarget = leftOriginalRoot + primaryCorrection;
            Vector3 rightRootTarget = rightOriginalRoot + primaryCorrection;

            // ⭐ 終點也要由動畫決定，不能用探測常數。
            //    Probe 的 destination 是「用 Vault1mMaxDepth(0.6m) 探到的落腳面」——那是**找面**用的距離。
            //    直接當 committed 終點，等於要求角色比動畫自然落點多走一段（Climb2m 0.212 → 0.600，
            //    多 0.39 m），那段位移動畫裡沒有 ⇒ 放手後腳在滑。
            //    動畫自然落點比探測點近時就用它；更遠時（Vault 的長跑出）仍用探測點，
            //    因為再遠不保證還在已驗證的落腳面上。
            Vector3 exitTarget = candidate.DestinationPoint;
            if (bake.TryGetNaturalExitDepth(out float naturalDepth) && naturalDepth > Epsilon)
            {
                Vector3 edgeToDestination = Vector3.ProjectOnPlane(
                    candidate.DestinationPoint - ledge.EdgeOrigin, ledge.TopNormal);
                float probeDepth = edgeToDestination.magnitude;
                if (naturalDepth < probeDepth)
                {
                    exitTarget = ledge.EdgeOrigin + traversalForward * naturalDepth;
                    // 高度仍沿用 Probe 驗證過的落腳面。
                    exitTarget += ledge.TopNormal *
                                  Vector3.Dot(candidate.DestinationPoint - exitTarget, ledge.TopNormal);
                }
            }

            if (!TryCreateKnot(
                    bake, candidate.SensedRootPosition, candidate.SensedFacingForward,
                    TraversalWarpKnotRole.Entry, 0f,
                    candidate.SensedRootPosition,
                    FacingYaw(candidate.SensedFacingForward),
                    maxHorizontalCorrection, maxVerticalCorrection,
                    out TraversalWarpKnot entryKnot) ||
                !TryCreateKnot(
                    bake, candidate.SensedRootPosition, candidate.SensedFacingForward,
                    TraversalWarpKnotRole.LeftContact, leftTime,
                    leftRootTarget, targetYaw,
                    maxHorizontalCorrection, maxVerticalCorrection,
                    out TraversalWarpKnot leftKnot) ||
                !TryCreateKnot(
                    bake, candidate.SensedRootPosition, candidate.SensedFacingForward,
                    TraversalWarpKnotRole.RightContact, rightTime,
                    rightRootTarget, targetYaw,
                    maxHorizontalCorrection, maxVerticalCorrection,
                    out TraversalWarpKnot rightKnot) ||
                !TryCreateKnot(
                    bake, candidate.SensedRootPosition, candidate.SensedFacingForward,
                    TraversalWarpKnotRole.Exit, authored.ExitNormalizedTime,
                    exitTarget, targetYaw,
                    maxHorizontalCorrection, maxVerticalCorrection,
                    out TraversalWarpKnot exitKnot) ||
                !TryCreateKnot(
                    bake, candidate.SensedRootPosition, candidate.SensedFacingForward,
                    TraversalWarpKnotRole.Recovery, authored.RecoveryNormalizedTime,
                    exitTarget, targetYaw,
                    maxHorizontalCorrection, maxVerticalCorrection,
                    out TraversalWarpKnot recoveryKnot))
            {
                rejection = TraversalWarpPlanRejection.KnotRejected;
                return false;
            }

            // ⭐ **手抓住之後，root 的 correction 必須凍結到放開為止。**
            //
            // 手一旦植在牆／邊緣上，它的世界位置就是硬約束。舊版把 Transfer knot 指向
            // 另一個空間目標（corridor clearance root），於是 Contact→Transfer 這段——
            // 也就是**手正抓著的整段**——correction 會一路內插過去；沒有 IK 釘住手時，
            // 手就被 root 拖著在牆面上滑。這正是 2026-09-13 Playtest 看到的現象。
            //
            // Transfer 的語意是「最後一隻手放開的時刻」（由 TraversalContactDerivation 測出），
            // 所以這裡直接沿用最後一次接觸的 correction＝抓握期間零額外位移。
            // 真正的位移交還給動畫自己——那支動畫本來就是在手固定的前提下把身體拉上去的。
            // 越過邊緣的 clearance 因此也由動畫提供，不再由一個外加的 transfer 目標硬拉。
            var heldTransferKnot = new TraversalWarpKnot(
                TraversalWarpKnotRole.Transfer,
                Mathf.Clamp01(authored.TransferNormalizedTime),
                rightKnot.PositionCorrection,
                rightKnot.YawCorrection);
            TraversalWarpKnot transferKnot = heldTransferKnot;

            // IK／debug 的目標是**手腕**（Animator IK 的 goal 就是手腕），但 root solve 的 primary
            // contact 與 secondary surface query 都在 grip edge 語意上工作。wrist-to-grip offset 是
            // bake-time animation fitting 資料；它不會回頭污染 Probe geometry。
            Quaternion contactRotation = Quaternion.Euler(0f, targetYaw, 0f);
            Vector3 leftAnimatedGrip = leftRootTarget + contactRotation * leftSolveOffset;
            Vector3 rightAnimatedGrip = rightRootTarget + contactRotation * rightSolveOffset;
            bool foundLeftSurface = TryQueryContactSurface(
                in ledge, min, max, leftAnimatedGrip, out Vector3 queriedLeft);
            bool foundRightSurface = TryQueryContactSurface(
                in ledge, min, max, rightAnimatedGrip, out Vector3 queriedRight);
            bool hasLeftSurface = authored.UsesLeftHand && (primaryIsLeft || foundLeftSurface);
            bool hasRightSurface = authored.UsesRightHand && (!primaryIsLeft || foundRightSurface);
            Vector3 leftGripTarget = primaryIsLeft
                ? primaryGripTarget
                : foundLeftSurface ? queriedLeft : leftAnimatedGrip;
            Vector3 rightGripTarget = !primaryIsLeft
                ? primaryGripTarget
                : foundRightSurface ? queriedRight : rightAnimatedGrip;
            Vector3 leftWristTarget = leftGripTarget + contactRotation *
                (authored.LeftContactAnchor.LeftHandInRoot - leftSolveOffset);
            Vector3 rightWristTarget = rightGripTarget + contactRotation *
                (authored.RightContactAnchor.RightHandInRoot - rightSolveOffset);

            TraversalHandContactMeasurement leftMeasurement = hasLeftSurface
                ? CreateContactMeasurement(
                    bake,
                    candidate.SensedRootPosition,
                    candidate.SensedFacingForward,
                    in leftKnot,
                    authored.LeftContactAnchor.LeftHandInRoot,
                    leftWristTarget)
                : default;
            TraversalHandContactMeasurement rightMeasurement = hasRightSurface
                ? CreateContactMeasurement(
                    bake,
                    candidate.SensedRootPosition,
                    candidate.SensedFacingForward,
                    in rightKnot,
                    authored.RightContactAnchor.RightHandInRoot,
                    rightWristTarget)
                : default;
            Quaternion handTargetRotation = Quaternion.LookRotation(traversalForward, ledge.TopNormal);
            var contacts = new TraversalContactTargets(
                primaryHand,
                hasLeftSurface,
                hasRightSurface,
                leftWristTarget,
                rightWristTarget,
                handTargetRotation,
                handTargetRotation,
                in leftMeasurement,
                in rightMeasurement,
                authored.HasValidHandIK,
                authored.LeftHandIKWindow,
                authored.RightHandIKWindow,
                authored.HandIKRotationWeight);
            return TraversalWarpPlan.TryCreatePiecewise(
                bake,
                candidate.SensedRootPosition,
                candidate.SensedFacingForward,
                exitTarget,
                traversalForward,
                in entryKnot,
                in leftKnot,
                in rightKnot,
                in transferKnot,
                in exitKnot,
                in recoveryKnot,
                in contacts,
                out plan,
                out rejection);
        }

        /// <summary>
        /// Queries the Probe-committed ledge surface without issuing runtime Physics calls.
        /// A secondary contact that falls outside the verified interval is unavailable; the plan
        /// keeps the authored pose instead of fabricating a narrower two-hand stance.
        /// </summary>
        private static bool TryQueryContactSurface(
            in TraversalLedgeFrame ledge,
            float minimumTangent,
            float maximumTangent,
            Vector3 animatedGrip,
            out Vector3 surfacePoint)
        {
            surfacePoint = default;
            if (!ledge.IsValid || !IsFinite(animatedGrip) ||
                !IsFinite(minimumTangent) || !IsFinite(maximumTangent) ||
                minimumTangent > maximumTangent)
            {
                return false;
            }

            float tangentDistance = Vector3.Dot(animatedGrip - ledge.EdgeOrigin, ledge.Tangent);
            if (tangentDistance < minimumTangent - Epsilon ||
                tangentDistance > maximumTangent + Epsilon)
            {
                return false;
            }

            surfacePoint = ledge.PointAt(Mathf.Clamp(
                tangentDistance, minimumTangent, maximumTangent));
            return IsFinite(surfacePoint);
        }

        private static TraversalHandContactMeasurement CreateContactMeasurement(
            MotionBakeData bake,
            Vector3 startPosition,
            Vector3 startForward,
            in TraversalWarpKnot knot,
            Vector3 handInRoot,
            Vector3 worldTarget)
        {
            if (!TraversalWarpPlan.TryEvaluateOriginal(
                    bake, startPosition, startForward, knot.NormalizedTime,
                    out Vector3 originalRoot, out float originalYaw))
            {
                return default;
            }

            Vector3 originalHand = originalRoot +
                                   Quaternion.Euler(0f, originalYaw, 0f) * handInRoot;
            Vector3 warpedRoot = originalRoot + knot.PositionCorrection;
            float warpedYaw = originalYaw + knot.YawCorrection;
            Vector3 animatedHand = warpedRoot +
                                   Quaternion.Euler(0f, warpedYaw, 0f) * handInRoot;
            return new TraversalHandContactMeasurement(
                knot.NormalizedTime, originalHand, animatedHand, worldTarget);
        }

        private static Vector3 ClampOutsideWall(
            Vector3 root,
            Vector3 edgeOrigin,
            Vector3 wallNormal,
            float minimumDistance)
        {
            float distance = Vector3.Dot(root - edgeOrigin, wallNormal);
            if (distance < minimumDistance)
                root += wallNormal * (minimumDistance - distance);
            return root;
        }

        private static bool TryCreateKnot(
            MotionBakeData bake,
            Vector3 startPosition,
            Vector3 startForward,
            TraversalWarpKnotRole role,
            float normalizedTime,
            Vector3 targetPosition,
            float targetYaw,
            float maxHorizontalCorrection,
            float maxVerticalCorrection,
            out TraversalWarpKnot knot)
        {
            knot = default;
            if (!TraversalWarpPlan.TryEvaluateOriginal(
                    bake, startPosition, startForward, normalizedTime,
                    out Vector3 originalPosition, out float originalYaw))
            {
                return false;
            }
            Vector3 correction = targetPosition - originalPosition;
            Vector3 horizontal = Vector3.ProjectOnPlane(correction, Vector3.up);
            if (!IsFinite(correction) || !IsFinite(targetYaw) ||
                horizontal.magnitude > Mathf.Max(0f, maxHorizontalCorrection) + Epsilon ||
                Mathf.Abs(correction.y) > Mathf.Max(0f, maxVerticalCorrection) + Epsilon)
            {
                return false;
            }
            knot = new TraversalWarpKnot(
                role,
                Mathf.Clamp01(normalizedTime),
                correction,
                Mathf.DeltaAngle(originalYaw, targetYaw));
            return true;
        }

        private static float FacingYaw(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > Epsilon
                ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg
                : 0f;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Traversal V2 的固定三種內容綁定。Kind 是資料選擇，不會擴張 FSM topology。
    /// </summary>
    [System.Serializable]
    public struct TraversalMotionBinding
    {
        private const float DefaultWarpEndNormalizedTime = 0.8f;
        private const float DefaultRecoverNormalizedTime = 0.85f;
        private const float DefaultMaxHorizontalCorrection = 1.5f;
        private const float DefaultMaxVerticalCorrection = 0.75f;

        [SerializeField] private string animationKey;
        [SerializeField] private MotionBakeData bake;
        [SerializeField, Range(0f, 1f)] private float warpStartNormalizedTime;
        [Tooltip("勾選才會使用下方的 warp end；不勾就由 MotionBakeData 的 VerticalCurve 推導" +
                 "（動畫自己『爬完了』的時刻）。不勾是建議值。")]
        [SerializeField] private bool overrideWarpWindowEnd;
        [SerializeField, Range(0f, 1f)] private float warpEndNormalizedTime;
        [SerializeField, Range(0f, 1f)] private float recoverNormalizedTime;
        [SerializeField, Min(0f)] private float maxHorizontalCorrection;
        [SerializeField, Min(0f)] private float maxVerticalCorrection;
        [SerializeField] private TraversalCollisionProfile collisionProfile;

        public string AnimationKey => animationKey;
        public MotionBakeData Bake => bake;
        public float WarpStartNormalizedTime => IsFinite(warpStartNormalizedTime)
            ? Mathf.Clamp01(warpStartNormalizedTime)
            : 0f;
        public float WarpEndNormalizedTime
        {
            get
            {
                float start = WarpStartNormalizedTime;
                float authored = IsFinite(warpEndNormalizedTime) ? Mathf.Clamp01(warpEndNormalizedTime) : 0f;
                return authored > start ? authored : Mathf.Max(start, DefaultWarpEndNormalizedTime);
            }
        }

        /// <summary>
        /// 🆕（docs/23 §R1）這一格是否真的 author 過 warp end。
        /// <c>false</c> 時 <see cref="TraversalState"/> 改用
        /// <c>MotionBakeData.GetVerticalSettleNormalizedTime()</c> 由 bake 推導，
        /// 而不是套用涵蓋整支 clip 的常數預設（舊行為：0.8，比實際動作窗寬 2–5 倍）。
        ///
        /// ⚠️ **必須是顯式 opt-in，不能用「值不等於預設」推斷。**
        /// 2026-09-13 實測踩到：struct 的欄位初始式本來就把 <c>warpEndNormalizedTime</c> 設成 0.8，
        /// 而正式 asset 沒有序列化這個欄位 ⇒ 反序列化後仍是 0.8 ⇒ 任何「>0 就算 author 過」的判斷
        /// 都會恆為 true，推導路徑因此完全不會執行（live Editor 顯示 `window=0.000-0.800 derived=False`）。
        /// </summary>
        public bool HasAuthoredWarpEnd =>
            overrideWarpWindowEnd &&
            IsFinite(warpEndNormalizedTime) &&
            Mathf.Clamp01(warpEndNormalizedTime) > WarpStartNormalizedTime;
        public float RecoverNormalizedTime
        {
            get
            {
                float authored = IsFinite(recoverNormalizedTime) ? Mathf.Clamp01(recoverNormalizedTime) : 0f;
                float recover = authored > 0f ? authored : DefaultRecoverNormalizedTime;
                return Mathf.Max(WarpEndNormalizedTime, recover);
            }
        }
        public float MaxHorizontalCorrection =>
            IsFinite(maxHorizontalCorrection) && maxHorizontalCorrection > 0f
                ? maxHorizontalCorrection
                : DefaultMaxHorizontalCorrection;
        public float MaxVerticalCorrection =>
            IsFinite(maxVerticalCorrection) && maxVerticalCorrection > 0f
                ? maxVerticalCorrection
                : DefaultMaxVerticalCorrection;
        public TraversalCollisionProfile CollisionProfile => collisionProfile;

        public TraversalMotionBinding(string animationKey, MotionBakeData bake)
        {
            this.animationKey = animationKey;
            this.bake = bake;
            warpStartNormalizedTime = 0f;
            overrideWarpWindowEnd = false;
            warpEndNormalizedTime = DefaultWarpEndNormalizedTime;
            recoverNormalizedTime = DefaultRecoverNormalizedTime;
            maxHorizontalCorrection = DefaultMaxHorizontalCorrection;
            maxVerticalCorrection = DefaultMaxVerticalCorrection;
            collisionProfile = default;
        }

        public TraversalMotionBinding(
            string animationKey,
            MotionBakeData bake,
            float warpStartNormalizedTime,
            float warpEndNormalizedTime,
            float recoverNormalizedTime,
            float maxHorizontalCorrection,
            float maxVerticalCorrection)
        {
            this.animationKey = animationKey;
            this.bake = bake;
            this.warpStartNormalizedTime = warpStartNormalizedTime;
            // 顯式傳入 warp end 的多載＝作者真的指定了窗（既有 PlayMode rig 走這條）。
            overrideWarpWindowEnd = true;
            this.warpEndNormalizedTime = warpEndNormalizedTime;
            this.recoverNormalizedTime = recoverNormalizedTime;
            this.maxHorizontalCorrection = maxHorizontalCorrection;
            this.maxVerticalCorrection = maxVerticalCorrection;
            collisionProfile = default;
        }

        public TraversalMotionBinding(
            string animationKey,
            MotionBakeData bake,
            float warpStartNormalizedTime,
            float warpEndNormalizedTime,
            float recoverNormalizedTime,
            float maxHorizontalCorrection,
            float maxVerticalCorrection,
            TraversalCollisionProfile collisionProfile)
            : this(
                animationKey,
                bake,
                warpStartNormalizedTime,
                warpEndNormalizedTime,
                recoverNormalizedTime,
                maxHorizontalCorrection,
                maxVerticalCorrection)
        {
            this.collisionProfile = collisionProfile;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// 單一 TraversalState 的 authored content。V2 仍僅有三格，不建立 library 或 dictionary。
    /// </summary>
    [CreateAssetMenu(
        fileName = "TraversalStateParams",
        menuName = "Project/Core/StateParams/TraversalStateParams")]
    public sealed class TraversalStateParamsSO : StateParamsSO
    {
        private const float DefaultNormalJumpReachSafetyMargin = 0.15f;

        [SerializeField] private TraversalMotionBinding vault1m =
            new TraversalMotionBinding("Vault1m", null);
        [SerializeField] private TraversalMotionBinding climb1m =
            new TraversalMotionBinding("Climb1m", null);
        [SerializeField] private TraversalMotionBinding climb2m =
            new TraversalMotionBinding("Climb2m", null);

        [Header("Selection — Normal Jump vs Climb")]
        [Tooltip("普通 Jump 理論 apex 扣除的安全餘量（m）。只影響 Climb；Vault 永遠保留 contextual traversal。")]
        [SerializeField, Min(0f)] private float normalJumpReachSafetyMargin =
            DefaultNormalJumpReachSafetyMargin;

        [Header("Entry Legality — V3")]
        [SerializeField] private TraversalEntryPolicySettings entryPolicy =
            TraversalEntryPolicySettings.Default;

        [Header("Animation Fitting — docs/24 §8")]
        [Tooltip("先讓動畫配合現場尺寸（播放速率），剩下的落差才交給 root warp。關掉＝rate 恆為 1。")]
        [SerializeField] private TraversalAnimationFitSettings animationFit =
            TraversalAnimationFitSettings.Default;

        [Header("Content toggles — 停用的 kind 直接讓給普通 Jump")]
        [Tooltip("停用後，該 kind 的 candidate 仍會被 Probe 分類（debug 看得到），" +
                 "但 selection 會以 KindDisabled 讓給普通 Jump。純內容開關，可隨時勾回。")]
        [SerializeField] private bool vault1mEnabled = true;
        [SerializeField] private bool climb1mEnabled = true;
        [SerializeField] private bool climb2mEnabled = true;

        public float NormalJumpReachSafetyMargin =>
            !float.IsNaN(normalJumpReachSafetyMargin) &&
            !float.IsInfinity(normalJumpReachSafetyMargin) &&
            normalJumpReachSafetyMargin > 0f
                ? normalJumpReachSafetyMargin
                : DefaultNormalJumpReachSafetyMargin;

        public TraversalEntryPolicySettings EntryPolicy => entryPolicy;

        public TraversalAnimationFitSettings AnimationFit => animationFit;

        private static readonly TraversalKind[] AllKinds =
        {
            TraversalKind.Vault1m,
            TraversalKind.Climb1m,
            TraversalKind.Climb2m,
        };

        /// <summary>
        /// 🆕（docs/24 §10）**Probe 的前掃距離必須由動畫需求推導，不能是獨立的 magic number。**
        ///
        /// 取所有**啟用中** kind 的「該支動畫推導出的理想進場距離 ＋ 進場帶的遠端容差」最大值，
        /// 再除以最大容許 facing 誤差的 cos——因為射線是沿角色 forward 打的，
        /// 站歪的時候到牆面的路徑會比垂直距離長。
        ///
        /// 推不出進場距離的 kind（缺 Traversal block）退回全域 <c>DesiredWallDistance</c>，
        /// 與 <see cref="TraversalEntryPolicy"/> 的退化路徑一致。
        ///
        /// ⚠️ 停用的 kind **不**計入：停用是內容決策，不該讓感測範圍替不會發生的動作買單。
        /// </summary>
        public float GetRequiredSensingReach()
        {
            float required = 0f;
            for (int i = 0; i < AllKinds.Length; i++)
            {
                TraversalKind kind = AllKinds[i];
                if (!IsKindEnabled(kind)) continue;
                MotionBakeData bake = GetBinding(kind).Bake;
                float desired = bake != null && bake.TryGetDesiredEntryDistance(out float derived)
                    ? derived
                    : entryPolicy.DesiredWallDistance;
                if (float.IsNaN(desired) || float.IsInfinity(desired) || desired <= 0f) continue;
                required = Mathf.Max(required, desired + entryPolicy.MaximumFarError);
            }

            if (required <= 0f) return 0f;
            float cos = Mathf.Cos(entryPolicy.MaximumFacingAngle * Mathf.Deg2Rad);
            return cos > 0.1f ? required / cos : required;
        }

        /// <summary>
        /// 🆕（docs/24 §10）Probe 的落腳面掃描上限：取所有啟用中 kind 的**自然落點深度**最大值。
        ///
        /// 為什麼要這個：`Vault1mMaxDepth`（0.6 m）是**分類**常數，不是落地預算。
        /// 拿它當 committed 終點，等於要求動畫比它自己的落點提早停下，
        /// 落差只能由 warp 往回拉（Playtest 的「放手後還在位移」，`docs/22` §18.6）。
        /// </summary>
        public float GetRequiredLandingDepth()
        {
            float required = 0f;
            for (int i = 0; i < AllKinds.Length; i++)
            {
                TraversalKind kind = AllKinds[i];
                if (!IsKindEnabled(kind)) continue;
                MotionBakeData bake = GetBinding(kind).Bake;
                if (bake == null || !bake.TryGetNaturalExitDepth(out float depth)) continue;
                if (float.IsNaN(depth) || float.IsInfinity(depth) || depth <= 0f) continue;
                required = Mathf.Max(required, depth);
            }

            return required;
        }

        /// <summary>
        /// 該 kind 是否啟用。⚠️ 這是**內容開關，不是 fallback**：停用後 Probe 仍會分類、debug 仍看得到，
        /// 只是 selection 讓給普通 Jump。因此「停用 ⇒ 那個高度就上不去」是預期結果，不是 bug。
        /// </summary>
        public bool IsKindEnabled(TraversalKind kind)
        {
            switch (kind)
            {
                case TraversalKind.Vault1m:
                    return vault1mEnabled;
                case TraversalKind.Climb1m:
                    return climb1mEnabled;
                case TraversalKind.Climb2m:
                    return climb2mEnabled;
                default:
                    return false;
            }
        }

        public TraversalMotionBinding GetBinding(TraversalKind kind)
        {
            switch (kind)
            {
                case TraversalKind.Vault1m:
                    return vault1m;
                case TraversalKind.Climb1m:
                    return climb1m;
                case TraversalKind.Climb2m:
                    return climb2m;
                default:
                    return default;
            }
        }
    }
}
