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

    public enum TraversalExecutionModel
    {
        Absolute = 0,
        Segmented = 1,
    }

    /// <summary>
    /// 🆕（docs/52 T-01／G-1）一次 traversal 的 committed exit：plan 實際要停的位置，以及它與
    /// Probe 已驗證落腳面的關係。由 <see cref="TraversalExitResolver"/> 產生，是 piecewise plan、
    /// endpoint plan 與 Animation Fitting 報表**共用的唯一答案**。
    /// </summary>
    public readonly struct TraversalCommittedExit
    {
        private const float DepthTolerance = 0.001f;

        public readonly bool IsValid;
        public readonly Vector3 Position;

        /// <summary>自 ledge edge 起算的落點深度（m）。</summary>
        public readonly float Depth;

        /// <summary>Probe 連續驗證到的落腳面深度（m）；<c>NaN</c> ＝ 沒有任何已驗證的面。</summary>
        public readonly float VerifiedDepth;

        /// <summary><c>true</c> ＝ 落點取自動畫自然落點；<c>false</c> ＝ 取 Probe 的 destination。</summary>
        public readonly bool UsesNaturalDepth;

        public TraversalCommittedExit(
            Vector3 position, float depth, float verifiedDepth, bool usesNaturalDepth)
        {
            IsValid = true;
            Position = position;
            Depth = depth;
            VerifiedDepth = verifiedDepth;
            UsesNaturalDepth = usesNaturalDepth;
        }

        /// <summary>
        /// ⚠️ committed exit 比 Probe 驗證過的面更遠（或根本沒有已驗證的面）。
        /// 既有規則是「比驗證過的面遠 ⇒ 踩空」（`docs/22` §18／`docs/30` §C）；
        /// 目前 Probe 會為了淨空查詢把薄牆的 destination 往前推一個膠囊半徑，因而可能違反它。
        /// 是否改為否決屬 gameplay 裁決（`docs/52` J1／J2），此處只讓違反**看得見**。
        /// </summary>
        public bool BeyondVerifiedSurface =>
            IsValid && !(VerifiedDepth >= Depth - DepthTolerance);
    }

    /// <summary>
    /// 🆕（docs/52 T-01／T-02）committed exit 的**唯一規則**：
    /// 動畫自然落點比 Probe destination 近就用自然落點，否則用 destination（`docs/22` §18.4、`docs/30` §C「兩者取近」）。
    ///
    /// 過去這條規則只寫在 <see cref="TraversalPlanBuilder"/> 裡；Fitting 另用一條規則報「committed」，
    /// endpoint 路徑則完全沒有套用——於是 Climb1m 的終點被 Probe 的跨 kind 掃描上限（Vault 的自然落點）決定。
    /// 純函式：不查 Physics、不讀時間，只讀已 commit 的 candidate。
    /// </summary>
    public static class TraversalExitResolver
    {
        private const float Epsilon = 0.0001f;

        /// <param name="forward">落點沿這個水平方向自 ledge edge 量起（piecewise 用牆法線、endpoint 用 candidate 方向）。</param>
        /// <param name="naturalDepth">動畫自然落點深度；非有限或 ≤0 ⇒ 直接用 Probe destination。</param>
        public static TraversalCommittedExit Resolve(
            in TraversalCandidate candidate, Vector3 forward, float naturalDepth)
        {
            TraversalLedgeFrame ledge = candidate.LedgeFrame;
            Vector3 destination = candidate.DestinationPoint;
            if (!candidate.IsValid || !IsFinite(destination) || !IsFinite(ledge.EdgeOrigin) ||
                !IsFinite(ledge.TopNormal))
            {
                return default;
            }

            float verified = candidate.LandingSurfaceDepth;
            Vector3 edgeToDestination = Vector3.ProjectOnPlane(
                destination - ledge.EdgeOrigin, ledge.TopNormal);
            float probeDepth = edgeToDestination.magnitude;

            forward = Vector3.ProjectOnPlane(forward, ledge.TopNormal);
            bool hasForward = forward.sqrMagnitude > Epsilon;
            if (hasForward) forward.Normalize();
            // ⚠️ 取近的比較沿用舊 piecewise 的無號距離（行為保持）；但**回報的深度**要有號——
            //    destination 落在邊緣後方（牆後沒有地面時 Probe 構造的點）必須顯示成負的，
            //    否則面板又多一個誤導值（獨立審查 F3）。
            bool useNatural = IsFinite(naturalDepth) && naturalDepth > Epsilon &&
                              naturalDepth < probeDepth && hasForward;
            if (!useNatural)
            {
                float reportedDepth = hasForward
                    ? Vector3.Dot(edgeToDestination, forward)
                    : probeDepth;
                return new TraversalCommittedExit(destination, reportedDepth, verified, false);
            }

            Vector3 exit = ledge.EdgeOrigin + forward * naturalDepth;
            // 高度仍沿用 Probe 驗證過的落腳面。
            exit += ledge.TopNormal * Vector3.Dot(destination - exit, ledge.TopNormal);
            return new TraversalCommittedExit(exit, naturalDepth, verified, true);
        }

        /// <summary>piecewise plan 的 traversal forward：牆法線反向，投影到頂面上。</summary>
        public static bool TryGetLedgeForward(in TraversalLedgeFrame ledge, out Vector3 forward)
        {
            forward = -Vector3.ProjectOnPlane(ledge.WallNormal, ledge.TopNormal);
            if (!IsFinite(forward) || forward.sqrMagnitude <= Epsilon)
            {
                forward = Vector3.zero;
                return false;
            }
            forward.Normalize();
            return true;
        }

        /// <summary>
        /// endpoint plan 的自然落點深度。endpoint 的 target 對應的是 **clip 結尾**的 root，
        /// 因此量在結尾（piecewise 的 Exit knot 則量在 exit marker，見
        /// <see cref="MotionBakeData.TryGetNaturalExitDepth"/>）。進場距離用 Entry 實際採用的理想距離，
        /// 與 piecewise 一樣以「站在理想位置時動畫會停在哪」為準；站偏的差由 warp 吸收。
        /// </summary>
        public static float GetEndpointNaturalDepth(MotionBakeData bake, float idealEntryDistance)
        {
            if (bake == null || !IsFinite(idealEntryDistance) || idealEntryDistance <= 0f)
                return float.NaN;
            float duration = bake.Duration;
            if (!IsFinite(duration) || duration <= 0f) return float.NaN;
            float travelled = bake.GetHorizontalDisplacementAt(duration);
            if (!IsFinite(travelled)) return float.NaN;
            return travelled - idealEntryDistance;
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
                maxHorizontalCorrection, maxVerticalCorrection,
                out plan, out _, out _);

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
            out TraversalWarpPlanRejection rejection) =>
            TryBuild(
                bake, in candidate, in entry, ledgeSafetyMargin,
                maxHorizontalCorrection, maxVerticalCorrection,
                out plan, out rejection, out _);

        public static bool TryBuild(
            MotionBakeData bake,
            in TraversalCandidate candidate,
            in TraversalEntryEvaluation entry,
            float ledgeSafetyMargin,
            float maxHorizontalCorrection,
            float maxVerticalCorrection,
            out TraversalWarpPlan plan,
            out TraversalWarpPlanRejection rejection,
            out float contactSupportPlaneClampDistance,
            bool completeVerticalAdaptationByTransfer = false)
        {
            plan = default;
            rejection = TraversalWarpPlanRejection.None;
            contactSupportPlaneClampDistance = 0f;
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

            // Contact precision is a soft constraint. Never let the hand solve place the standing
            // capsule below the surface it is standing on: clamp the root up against the committed
            // Entry support plane. Any remaining hand error stays observable to the bounded IK pass.
            float unclampedRootY = primaryRootTarget.y;
            primaryRootTarget.y = Mathf.Max(
                primaryRootTarget.y, candidate.SensedRootPosition.y);
            contactSupportPlaneClampDistance = primaryRootTarget.y - unclampedRootY;
            Vector3 primaryCorrection = primaryRootTarget - primaryOriginalRoot;
            Vector3 leftRootTarget = leftOriginalRoot + primaryCorrection;
            Vector3 rightRootTarget = rightOriginalRoot + primaryCorrection;

            // ⭐ 終點也要由動畫決定，不能用探測常數。
            //    Probe 的 destination 是「用 Vault1mMaxDepth(0.6m) 探到的落腳面」——那是**找面**用的距離。
            //    直接當 committed 終點，等於要求角色比動畫自然落點多走一段（Climb2m 0.212 → 0.600，
            //    多 0.39 m），那段位移動畫裡沒有 ⇒ 放手後腳在滑。
            //    動畫自然落點比探測點近時就用它；更遠時（Vault 的長跑出）仍用探測點，
            //    因為再遠不保證還在已驗證的落腳面上。
            //    🔄（docs/52 T-01）規則本體移到 TraversalExitResolver，Fitting 報表與 endpoint 路徑共用。
            float naturalDepth = bake.TryGetNaturalExitDepth(out float bakedNaturalDepth)
                ? bakedNaturalDepth
                : float.NaN;
            TraversalCommittedExit committedExit =
                TraversalExitResolver.Resolve(in candidate, traversalForward, naturalDepth);
            Vector3 exitTarget = committedExit.IsValid
                ? committedExit.Position
                : candidate.DestinationPoint;

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

            // ⭐ 2026-09-22 Play 回報：**垂直修正全部凍結到 Transfer 之後，root 會先衝過頭再沉下來。**
            //
            // 上面那段「correction 凍結到放開為止」對**水平**是對的（手釘在牆上，root 橫移就是滑手），
            // 但對**垂直**是錯的：把垂直也凍結，等於要求角色不管牆多高都先爬完**動畫自己的**高度
            // （Climb2m 是 1.978 m），越過真實邊緣之後再由 Transfer→Exit 把多出來的部分沉回去。
            //
            // 實測（1.75 m 牆，ledge top 1.750）：
            //   n=0.263 root 1.660 → **n=0.350 root 1.978（高出邊緣 22.8 cm）** → n=0.702 才降回 1.780。
            // 畫面上就是「爬上去之後整個人浮在半空、再慢慢沉下去」，越矮的牆越明顯。
            //
            // ⚠️ 這個缺陷**量不到**：它不會被碰撞擋住（往下沉是朝著平台，沒有東西擋），
            //    所以 blocked displacement ≈ 0；手也早就放開了，所以 hand residual 也乾淨。
            //    這正是 `docs/39` F1 那一類「代理量沒有涵蓋失效模式」的錯誤。
            //
            // 修法：**抓握期間要鬆開的，只有「支撐面夾持」那一份。**
            //
            // Contact 的垂直修正由兩部分組成，語意完全不同：
            //   ① **grip-driven**：把 root 擺到「動畫的手剛好落在真實邊緣」的高度。
            //      手在抓握期間是硬約束 ⇒ 這一份**必須凍結**，動了就是滑手。
            //   ② **support-plane clamp**：角色在接觸瞬間站在地上，不能把 root 解到地板下面，
            //      所以我們往上夾了一段。**那是對 root 高度說的一個謊**，而它只在腳還踩著地時成立。
            //      角色一離地，這個謊就該還回去——否則它會一路撐到 Transfer 之後，
            //      變成「先爬過頭、再沉下來」。
            //
            // 所以 Transfer 的垂直 ＝ Contact 的垂直 **減掉夾持量**。
            // 剩下的（grip 幾何與落腳面之間的差，實測 6–8 cm）留給 Transfer→Exit，
            // 那段手已經放開，且 2.05 m 牆本來就是這樣跑而沒有任何過衝。
            //
            // ⛔ 早一版曾改成 `transferCorrection.y = exitKnot.PositionCorrection.y`（垂直全部補完）。
            //    低牆確實不再過衝，但在 **2.05 m 牆**上抓握期間多沉了 6.1 cm，
            //    滿權重手臂 ratio 從 0.866 升到 **0.976** ⇒ 用高牆的手換低牆的 root。不可接受。
            if (completeVerticalAdaptationByTransfer)
            {
                Vector3 transferCorrection = rightKnot.PositionCorrection;
                transferCorrection.y -= contactSupportPlaneClampDistance;
                transferKnot = new TraversalWarpKnot(
                    TraversalWarpKnotRole.Transfer,
                    Mathf.Clamp01(authored.TransferNormalizedTime),
                    transferCorrection,
                    rightKnot.YawCorrection);
            }

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
            // ⭐ 2026-09-22：**朝向和位置一樣要 commit，而且 commit 的是「骨骼朝向」。**
            //
            // 接觸瞬間手的朝向偏離 authored 抓握姿勢：authored 高度 11.0°、coverage 下緣 **49.3°**
            // ⇒ 五根手指插進頂面下方 8.3 cm。腕點目標在所有高度上**完全相同**
            // （相對邊緣 −10.2 / −8.2 cm）⇒ 位置 solve 一直是對的，缺的就是這個。
            // 這是 `gripEdgeInRoot`（位置）那次修法的**旋轉版**：同樣是「把資料轉到正確的基底再 solve」。
            //
            // ⚠️ **這裡產出的是 bone-space 世界旋轉，不是 Animator 的 IK goal 旋轉。**
            //    兩者差一個 avatar 決定的基底轉換（實測量級 ≈98°）。
            //    轉換由 `TraversalHandIKRig` 負責——只有那一層同時看得到骨骼與 `GetIKRotation`。
            //    ⛔ 不要在這裡先乘上去：Core 層沒有 Animator，也不該知道 avatar 的內部慣例。
            Quaternion ledgeFrameRotation =
                Quaternion.LookRotation(traversalForward, ledge.TopNormal);
            Quaternion contactYawRotation = Quaternion.Euler(0f, targetYaw, 0f);
            Quaternion leftHandTargetRotation = authored.LeftContactAnchor.HasHandRotations
                ? contactYawRotation * authored.LeftContactAnchor.LeftHandRotationInRoot
                : ledgeFrameRotation;
            Quaternion rightHandTargetRotation = authored.RightContactAnchor.HasHandRotations
                ? contactYawRotation * authored.RightContactAnchor.RightHandRotationInRoot
                : ledgeFrameRotation;
            var contacts = new TraversalContactTargets(
                primaryHand,
                hasLeftSurface,
                hasRightSurface,
                leftWristTarget,
                rightWristTarget,
                leftHandTargetRotation,
                rightHandTargetRotation,
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

        [Header("Execution model")]
        [Tooltip("Absolute uses the production per-frame absolute-pose path and is the revert " +
                 "target. Segmented re-bases carried execution error only at authored phase " +
                 "boundaries.")]
        [SerializeField] private TraversalExecutionModel executionModel;

        // ⭐ 2026-09-20：**動畫覆蓋範圍（上下限）。**
        //
        // 為什麼放在 binding 而不是 TraversalProbeSettings：這是**這支 clip** 的性質，
        // 不是環境幾何的性質。Probe 回答「這個障礙物多高」；binding 回答「我這支動畫撐得住多高」。
        // 放進 ProbeSettings 會讓 Core/Environment 開始擁有內容決策，撞 §F I10。
        //
        // 為什麼需要它：`docs/39` §2 Q7 量到 Climb2m 的 classifier band 是 (1.25, 2.1]，
        // 但動畫實際只在約 1.62–2.05 m 成立——低於 1.62 m 時 contact 殘差超過 IK guard，
        // 手會在接觸當幀失控再瞬移。**幾何上可攀爬 ≠ 這支動畫適用。**
        // 超出範圍時**不硬 warp**，讓給普通 Jump，並留下可觀測的理由（§F I8）。
        //
        // 0 ＝ 不限制（維持舊行為）。下限與 jump safeReach 的關係由 selection policy 疊加：
        // 兩者都要過，所以 authored 下限低於 jump 搆得到的高度時不會有害，只是不生效。
        //
        // ⚠️（docs/52 T-03）反方向也要知道：下限**高於** jump apex 時，jump-reach 判斷整段被遮蔽，
        //    真正的 Jump／Climb 分界就是這個 authored 值。出貨值是人對照別人的事實選的——
        //    Climb1m 1.00 ← 普通 Jump apex 0.9535；Climb2m 1.65 ← Hand IK residual guard 0.35 m（docs/40 §13）。
        //    改 Jump 倍率或 guard 時要回來重看；Climb1m 那一條由
        //    TraversalAnimationFitTests.R4 守住（下限不得低於 apex，否則重新出現「有時跳有時爬」的模糊帶）。
        [Header("Animation coverage — 這支 clip 實際撐得住的高度帶（0 ＝ 不限制）")]
        [Tooltip("下限（m）。高於普通 Jump apex 時，它就是實際的 Jump／Climb 分界（jump-reach 判斷被遮蔽）。" +
                 "出貨值對照 Jump apex（Climb1m）或 Hand IK residual guard（Climb2m）選定——改那兩者時要回來重看。docs/52 T-03")]
        [SerializeField, Min(0f)] private float coverageMinHeight;
        [Tooltip("上限（m）。超出讓給普通 Jump（OutsideAnimationCoverage）。")]
        [SerializeField, Min(0f)] private float coverageMaxHeight;

        public string AnimationKey => animationKey;
        /// <summary>動畫覆蓋下限（m）。0 或非有限值 ＝ 不限制。</summary>
        public float CoverageMinHeight =>
            IsFinite(coverageMinHeight) && coverageMinHeight > 0f ? coverageMinHeight : 0f;
        /// <summary>動畫覆蓋上限（m）。0 或非有限值 ＝ 不限制。</summary>
        public float CoverageMaxHeight =>
            IsFinite(coverageMaxHeight) && coverageMaxHeight > 0f ? coverageMaxHeight : 0f;
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
        public TraversalExecutionModel ExecutionModel => executionModel;

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
            executionModel = TraversalExecutionModel.Absolute;
            coverageMinHeight = 0f;
            coverageMaxHeight = 0f;
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
            executionModel = TraversalExecutionModel.Absolute;
            coverageMinHeight = 0f;
            coverageMaxHeight = 0f;
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
