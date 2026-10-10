using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Environment;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🆕（docs/22 §22，2026-10-10 第三輪）**出口有效性：Candidate／Eligibility／Plan Validity 的邊界。**
    ///
    /// 真實 <see cref="TraversalProbe"/>（X Bot 的膠囊與 probe 設定）＋ 真實 <see cref="FullBodyStateMachine"/>
    /// （出貨的 PlayerStateMachineConfig）＋ 真實碰撞體。每個案例都從「按下跳躍那一帧」走完整條准入鏈，
    /// 再看 FSM 實際進了哪個狀態、各階段留下什麼理由。
    ///
    /// 案例來源：docs/52 的 live Editor 實測（B2／B3）與 docs/52 J1／J2 裁決。
    /// 正常路徑的出口深度是「不得產生非預期終點變化」的回歸基準（數值取自 docs/52 §21.4 的 live 量測）。
    /// </summary>
    public class TraversalExitValidityPlayModeTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/X Bot.prefab";
        private const string ConfigPath = "Assets/ScriptableObjects/StateMachine/PlayerStateMachineConfig.asset";
        private const float ExitTolerance = 0.01f;

        private sealed class FakeMovementModel : IMovementModel
        {
            public bool IsProducingMotion { get; set; }
            public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime) { }
            public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data) { }
        }

        private sealed class FakeAnimationFacade : AnimationFacadeBase
        {
            public override void Play(string stateKey) { }
            public override void PlayWithCallback(string stateKey, Action onComplete) { }
            public override void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f) { }
            public override void SetFloat(string key, float value) { }
            public override void SetBool(string key, bool value) { }
            public override bool IsPlaying(string stateKey) => false;
            public override float GetNormalizedTime() => 0f;
        }

        private sealed class Rig
        {
            public GameObject Host;
            public TraversalProbe Probe;
            public MotionDriver Driver;
            public FullBodyStateMachine Machine;
            public PlayerRuntimeData Data;
        }

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // ── 正常路徑：不得產生非預期終點變化 ────────────────────────────────────────

        [UnityTest]
        public IEnumerator Normal_Vault_LongGroundBehind_CommitsPiecewiseExitOnVerifiedSurface()
        {
            BuildWall(height: 1f, depth: 0.3f, backGroundLength: 8f);
            Rig rig = BuildRig(rootDistanceFromWall: 1.40f);
            yield return null;

            Attempt(rig);

            TraversalState traversal = AssertEnteredTraversal(rig, TraversalKind.Vault1m);
            Assert.IsTrue(traversal.WarpPlan.IsPiecewise);
            Assert.AreEqual(1.100f, PlanExitDepth(traversal), ExitTolerance);
        }

        [UnityTest]
        public IEnumerator Normal_Climb2m_DeepPlatform_CommitsPiecewiseExitAtNaturalDepth()
        {
            BuildWall(height: 1.8f, depth: 3f, backGroundLength: 0f);
            Rig rig = BuildRig(rootDistanceFromWall: 0.46f);
            yield return null;

            Attempt(rig);

            TraversalState traversal = AssertEnteredTraversal(rig, TraversalKind.Climb2m);
            Assert.IsTrue(traversal.WarpPlan.IsPiecewise);
            Assert.AreEqual(0.213f, PlanExitDepth(traversal), ExitTolerance);
        }

        [UnityTest]
        public IEnumerator Normal_Climb1m_DeepPlatform_CommitsEndpointExit()
        {
            BuildWall(height: 1.1f, depth: 3f, backGroundLength: 0f);
            Rig rig = BuildRig(rootDistanceFromWall: 0.45f);
            yield return null;

            Attempt(rig);

            TraversalState traversal = AssertEnteredTraversal(rig, TraversalKind.Climb1m);
            Assert.IsFalse(traversal.WarpPlan.IsPiecewise, "出貨 Climb1m 沒有 Traversal block ⇒ endpoint");
            Assert.AreEqual(0.918f, PlanExitDepth(traversal), ExitTolerance);
        }

        // ── J1：committed exit 越出已驗證落腳面 ⇒ 不得承諾 ────────────────────────────

        [UnityTest]
        public IEnumerator J1_Vault_NarrowGroundBehind_DoesNotCommitTraversal()
        {
            // docs/52 B2：牆後地面只到牆背 0.35 m。Probe 只驗證到 0.600，destination 卻被推到 0.970。
            BuildWall(height: 1f, depth: 0.3f, backGroundLength: 0.35f);
            Rig rig = BuildRig(rootDistanceFromWall: 1.40f);
            yield return null;

            Attempt(rig);

            Assert.AreEqual(TraversalKind.Vault1m, rig.Probe.Candidate.Kind,
                "候選本身存在（牆與牆後地面都量得到）——否決屬於 Plan Validity，不是 Candidate");
            Assert.IsTrue(rig.Probe.EntryEvaluation.Executable, "站位合法——否決不屬於 Eligibility");
            Assert.AreNotEqual(StateType.Traversal, rig.Machine.CurrentState.Type,
                "committed exit 會越出已驗證的落腳面，不得承諾 traversal");
            Assert.AreEqual(TraversalPlanValidityReason.ExitBeyondVerifiedSurface, rig.Probe.PlanValidity.Reason);
            Assert.Greater(rig.Probe.PlanValidity.ExitDepth, rig.Probe.PlanValidity.VerifiedDepth);
            Assert.AreEqual(StateType.Jump, rig.Machine.CurrentState.Type, "D3：traversal 不成立時照舊交回 Jump（仲裁不變）");
        }

        // ── J2：牆後沒有任何落腳面 ⇒ 不是可通過的候選 ────────────────────────────────

        [UnityTest]
        public IEnumerator J2_Vault_NoGroundBehind_IsNotACandidate()
        {
            // docs/52 B3：懸崖邊的矮牆。舊行為：仍分類為 Vault1m、准入、plan Failed、走未 warp 的曲線。
            BuildWall(height: 1f, depth: 0.3f, backGroundLength: 0f);
            Rig rig = BuildRig(rootDistanceFromWall: 1.40f);
            yield return null;

            Attempt(rig);

            Assert.AreEqual(TraversalKind.None, rig.Probe.Candidate.Kind,
                "量不到任何落腳面時，候選不存在");
            Assert.AreEqual(TraversalRejectReason.NoLandingSurface, rig.Probe.Candidate.RejectReason);
            Assert.AreEqual(TraversalPlanValidityReason.NotEvaluated, rig.Probe.PlanValidity.Reason,
                "候選不存在時不得走到 Plan Validity——那是三個不同的答案");
            Assert.AreNotEqual(StateType.Traversal, rig.Machine.CurrentState.Type);
        }

        // ── Plan Validity：建不出有效 plan ⇒ 不得承諾 ───────────────────────────────

        [UnityTest]
        public IEnumerator PlanValidity_NoBuildablePlan_DoesNotCommitTraversal()
        {
            // 以 binding 的修正上限為 1 mm 的**測試副本**（不改資產）讓 piecewise 與 endpoint 都建不起來。
            BuildWall(height: 1.1f, depth: 3f, backGroundLength: 0f);
            Rig rig = BuildRig(rootDistanceFromWall: 0.45f, tinyCorrectionBudget: true);
            yield return null;

            Attempt(rig);

            Assert.AreEqual(TraversalKind.Climb1m, rig.Probe.Candidate.Kind);
            Assert.IsTrue(rig.Probe.EntryEvaluation.Executable);
            Assert.AreNotEqual(StateType.Traversal, rig.Machine.CurrentState.Type,
                "沒有有效 plan 時不得承諾 traversal（舊行為：承諾後走未 warp 的曲線）");
            Assert.AreEqual(TraversalPlanValidityReason.NoValidPlan, rig.Probe.PlanValidity.Reason);
            Assert.IsTrue(float.IsNaN(rig.Probe.PlanValidity.ExitDepth));
        }

        // ── 邊界：三個答案互不混淆 ───────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Boundary_StandingTooFar_IsAnEligibilityRejection_NotAPlanRejection()
        {
            BuildWall(height: 1.8f, depth: 3f, backGroundLength: 0f);
            Rig rig = BuildRig(rootDistanceFromWall: 2.0f);
            yield return null;

            Attempt(rig);

            Assert.AreEqual(TraversalKind.Climb2m, rig.Probe.Candidate.Kind, "候選存在");
            Assert.IsFalse(rig.Probe.EntryEvaluation.Executable);
            Assert.AreEqual(TraversalEntryRejectReason.TooFar, rig.Probe.EntryEvaluation.RejectReason);
            Assert.AreEqual(TraversalPlanValidityReason.NotEvaluated, rig.Probe.PlanValidity.Reason,
                "進場不合法時不得走到 Plan Validity");
            Assert.AreNotEqual(StateType.Traversal, rig.Machine.CurrentState.Type);
        }

        [UnityTest]
        public IEnumerator Boundary_NormalPath_ReportsValidPlanWithExitInsideVerifiedSurface()
        {
            BuildWall(height: 1f, depth: 0.3f, backGroundLength: 8f);
            Rig rig = BuildRig(rootDistanceFromWall: 1.40f);
            yield return null;

            Attempt(rig);

            TraversalPlanValidity validity = rig.Probe.PlanValidity;
            Assert.AreEqual(TraversalPlanValidityReason.Valid, validity.Reason);
            Assert.IsFalse(validity.UsesEndpoint);
            Assert.LessOrEqual(validity.ExitDepth, validity.VerifiedDepth + 0.001f);
            Assert.AreEqual(TraversalLandingEvidence.Found, rig.Probe.Candidate.LandingEvidence);
        }

        // ── J3′：Climb1m 出口與全域 Entry 距離的耦合（characterization，證明問題存在） ──────────

        /// <summary>
        /// 🆕（docs/22 §22.4，J3′）**證明耦合存在。**同一面 1.1 m 平台、同一個站位（0.50 m，兩組設定下都合法），
        /// 只改全域 <c>EntryPolicySettings.DesiredWallDistance</c>（0.45 → 0.55）：
        /// 出貨 Climb1m 沒有 Traversal block，理想站位退回這個全域值，endpoint 出口因此 **1:1** 移動 0.10 m。
        ///
        /// ⚠️ 這是 characterization——它鎖的是「目前的耦合」，不是「這樣是對的」。
        /// J3′ 定案後必須改寫成「出口與全域值無關」，而不是刪掉。
        /// </summary>
        [UnityTest]
        public IEnumerator J3Prime_Climb1mEndpointExit_MovesOneToOneWithGlobalDesiredWallDistance()
        {
            BuildWall(height: 1.1f, depth: 3f, backGroundLength: 0f);
            Rig nearIdeal = BuildRig(rootDistanceFromWall: 0.50f, globalDesiredWallDistance: 0.45f);
            Rig farIdeal = BuildRig(rootDistanceFromWall: 0.50f, globalDesiredWallDistance: 0.55f);
            yield return null;

            Attempt(nearIdeal);
            Attempt(farIdeal);

            float exitNear = PlanExitDepth(AssertEnteredTraversal(nearIdeal, TraversalKind.Climb1m));
            float exitFar = PlanExitDepth(AssertEnteredTraversal(farIdeal, TraversalKind.Climb1m));
            Assert.AreEqual(0.10f, exitNear - exitFar, 0.002f,
                "J3′ 耦合：Climb1m 出口隨全域 DesiredWallDistance 1:1 移動（站位、牆、動畫都相同）");
        }

        /// <summary>
        /// 對照組：Climb2m 有 Traversal block，理想站位由 bake 推導——全域值改變時出口**不動**。
        /// 證明 J3′ 的耦合只存在於「沒有 block、退回全域值」的那條路徑，不是 exit 規則本身的問題。
        /// </summary>
        [UnityTest]
        public IEnumerator J3Prime_Contrast_Climb2mExitIgnoresGlobalDesiredWallDistance()
        {
            BuildWall(height: 1.8f, depth: 3f, backGroundLength: 0f);
            Rig nearIdeal = BuildRig(rootDistanceFromWall: 0.46f, globalDesiredWallDistance: 0.45f);
            Rig farIdeal = BuildRig(rootDistanceFromWall: 0.46f, globalDesiredWallDistance: 0.55f);
            yield return null;

            Attempt(nearIdeal);
            Attempt(farIdeal);

            float exitNear = PlanExitDepth(AssertEnteredTraversal(nearIdeal, TraversalKind.Climb2m));
            float exitFar = PlanExitDepth(AssertEnteredTraversal(farIdeal, TraversalKind.Climb2m));
            Assert.AreEqual(exitNear, exitFar, 0.0005f);
        }

        // ── J3′ 研究探針（THROWAWAY，[Explicit]：不進回歸套件） ─────────────────────────

        /// <summary>
        /// 🔬 THROWAWAY 量測探針（docs/22 §22.4，J3′ 研究；CLAUDE.md Spike／Probe 例外）。
        /// 從出貨的 Climb1m 進場站位（全域 0.45 m）實際執行一次 endpoint traversal（真實 MotionDriver＋CharacterController），
        /// 量測：approach 段被牆擋掉多少、root 開始上升時離牆多遠、最終 root 停在邊緣內多深。
        /// 只輸出數字、不斷言行為；J3′ 定案後刪除。
        /// </summary>
        [UnityTest, Explicit("J3′ research probe — measurement only"), Category("Probe")]
        public IEnumerator Probe_Climb1mEndpointExecution_FromGlobalEntryDistance(
            [Values(0, 1, 2, 3, 4)] int scenario)
        {
            // 0＝出貨（理想 0.45、站 0.45）；1＝出貨理想、站在合法帶最遠處（0.78；0.80 量起來是 0.82，超出合法帶）；
            // 2–4＝模擬「理想進場距離由 clip 推導」：以三個候選 landmark 估計的邊緣（1.00／1.13／1.22）同時當理想與站位。
            float[] stances = { 0.45f, 0.78f, 1.00f, 1.13f, 1.22f };
            float[] ideals = { float.NaN, float.NaN, 1.00f, 1.13f, 1.22f };
            float stance = stances[scenario];
            float ideal = ideals[scenario];
            BuildWall(height: 1.1f, depth: 3f, backGroundLength: 0f);
            Rig rig = BuildRig(rootDistanceFromWall: stance, globalDesiredWallDistance: ideal);
            SteppingFacade facade = rig.Host.AddComponent<SteppingFacade>();
            yield return null;

            Attempt(rig);
            TraversalState traversal = AssertEnteredTraversal(rig, TraversalKind.Climb1m);
            float edgeZ = traversal.CommittedCandidate.LedgeFrame.EdgeOrigin.z;
            Vector3 target = traversal.WarpPlan.TargetPosition;
            rig.Data.Intent.JumpRequested = false;

            float maxBlocked = 0f;
            float totalBlocked = 0f;
            float rootZAtRise = float.NaN;
            float nAtRise = float.NaN;
            float wallFaceZ = 0f;
            const int Steps = 90;
            for (int i = 1; i <= Steps; i++)
            {
                yield return null;
                facade.NormalizedTime = i / (float)Steps;
                traversal.OnUpdateMotion(rig.Driver, facade, rig.Data);
                maxBlocked = Mathf.Max(maxBlocked, rig.Driver.LastTraversalExecutionResult.BlockedDisplacement.magnitude);
                totalBlocked += rig.Driver.LastTraversalExecutionResult.BlockedDisplacement.magnitude;
                Vector3 p = rig.Host.transform.position;
                if (float.IsNaN(rootZAtRise) && p.y > 0.05f) { rootZAtRise = p.z; nAtRise = facade.NormalizedTime; }
            }

            Vector3 end = rig.Host.transform.position;
            Debug.Log(string.Format(
                "[J3′ probe] Climb1m stance={9:F2} ideal={10} : totalBlocked={11:F3} m maxBlockedPerFrame={0:F3} m | rise starts n={1:F3} at rootZ={2:F3} (wall face z={3:F3}, root–wall {4:F3} m) | " +
                "end root depth past edge={5:F3} (plan target depth {6:F3}) | end y={7:F3} | maxRootDivergence={8:F3}",
                maxBlocked, nAtRise, rootZAtRise, wallFaceZ, wallFaceZ - rootZAtRise,
                end.z - edgeZ, target.z - edgeZ, end.y, rig.Driver.LastTraversalMaxRootDivergence,
                stance, float.IsNaN(ideal) ? "global0.45" : ideal.ToString("F2"), totalBlocked));
            Assert.Pass("measurement only");
        }

        private sealed class SteppingFacade : AnimationFacadeBase
        {
            public float NormalizedTime;
            public override void Play(string stateKey) { }
            public override void PlayWithCallback(string stateKey, Action onComplete) { }
            public override void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f) { }
            public override void SetFloat(string key, float value) { }
            public override void SetBool(string key, bool value) { }
            public override bool IsPlaying(string stateKey) => true;
            public override float GetNormalizedTime() => NormalizedTime;
        }

        // ── 共用 ───────────────────────────────────────────────────────────────

        private static TraversalState AssertEnteredTraversal(Rig rig, TraversalKind kind)
        {
            Assert.AreEqual(StateType.Traversal, rig.Machine.CurrentState.Type,
                $"應進入 Traversal（candidate {rig.Probe.Candidate.Kind}/{rig.Probe.Candidate.RejectReason}，" +
                $"entry {rig.Probe.EntryEvaluation.RejectReason}，selection {rig.Probe.SelectionEvaluation.Reason}）");
            var traversal = (TraversalState)rig.Machine.CurrentState;
            Assert.AreEqual(kind, traversal.CommittedKind);
            Assert.IsTrue(traversal.WarpPlan.IsValid);
            return traversal;
        }

        /// <summary>plan 實際的終點自 ledge edge 沿 +z 的深度（piecewise 取 Exit knot，endpoint 取 target）。</summary>
        private static float PlanExitDepth(TraversalState traversal)
        {
            TraversalWarpPlan plan = traversal.WarpPlan;
            Vector3 exit;
            if (plan.IsPiecewise)
                Assert.IsTrue(plan.TryEvaluate(plan.ExitKnot.NormalizedTime, out exit, out _));
            else
                exit = plan.TargetPosition;
            return exit.z - traversal.CommittedCandidate.LedgeFrame.EdgeOrigin.z;
        }

        private static void Attempt(Rig rig)
        {
            rig.Data.IsGrounded = true;
            rig.Data.MoveSpeed = 0f;
            rig.Data.MoveDirection = Vector3.forward;
            rig.Data.Intent.JumpRequested = true;
            Physics.SyncTransforms();
            rig.Probe.Tick(rig.Data);
            rig.Machine.Tick(rig.Data, 0.016f);
        }

        /// <summary>牆的前面在 z=0；前方地面 z∈[-10,0]；牆後地面 z∈[depth, depth+backGroundLength]（0 ＝ 沒有）。</summary>
        private void BuildWall(float height, float depth, float backGroundLength)
        {
            CreateBox("Front Ground", new Vector3(0f, -0.5f, -5f), new Vector3(10f, 1f, 10f));
            CreateBox("Wall", new Vector3(0f, height * 0.5f, depth * 0.5f), new Vector3(10f, height, depth));
            if (backGroundLength > 0f)
            {
                CreateBox("Back Ground",
                    new Vector3(0f, -0.5f, depth + backGroundLength * 0.5f),
                    new Vector3(10f, 1f, backGroundLength));
            }
        }

        private void CreateBox(string name, Vector3 centre, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = $"ExitValidity-{name}";
            box.layer = 0;
            box.transform.position = centre;
            box.transform.localScale = size;
            _created.Add(box);
        }

        private Rig BuildRig(
            float rootDistanceFromWall,
            bool tinyCorrectionBudget = false,
            float globalDesiredWallDistance = float.NaN)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab, $"找不到 {PlayerPrefabPath}");
            TraversalProbe sourceProbe = prefab.GetComponentInChildren<TraversalProbe>(true);
            Assert.IsNotNull(sourceProbe, "X Bot 必須掛 TraversalProbe");
            CharacterController sourceController = sourceProbe.GetComponent<CharacterController>();

            var host = new GameObject("ExitValidity Rig");
            _created.Add(host);
            host.layer = 2; // Ignore Raycast：rig 不會打到自己
            host.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, -rootDistanceFromWall), Quaternion.identity);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.center = sourceController.center;
            controller.height = sourceController.height;
            controller.radius = sourceController.radius;
            controller.skinWidth = sourceController.skinWidth;
            controller.stepOffset = sourceController.stepOffset;
            controller.slopeLimit = sourceController.slopeLimit;
            controller.minMoveDistance = 0f;

            TraversalProbe probe = host.AddComponent<TraversalProbe>();
            FieldInfo settings = typeof(TraversalProbe).GetField(
                "settings", BindingFlags.NonPublic | BindingFlags.Instance);
            settings.SetValue(probe, settings.GetValue(sourceProbe));
            SetPrivateField(probe, "drawTraversalRuntimeLines", false);

            MotionDriver driver = host.AddComponent<MotionDriver>();
            host.AddComponent<FakeAnimationFacade>();

            var shipped = AssetDatabase.LoadAssetAtPath<StateMachineConfigSO>(ConfigPath);
            Assert.IsNotNull(shipped, $"找不到 {ConfigPath}");
            StateMachineConfigSO config = UnityEngine.Object.Instantiate(shipped);
            _created.Add(config);
            if (tinyCorrectionBudget) InstallTraversalParamsCopy(config, SetTinyCorrectionBudget);
            if (!float.IsNaN(globalDesiredWallDistance))
                InstallTraversalParamsCopy(config, p => SetGlobalDesiredWallDistance(p, globalDesiredWallDistance));

            var data = new PlayerRuntimeData();
            var machine = new FullBodyStateMachine();
            machine.Initialize(config, data, new FakeMovementModel(),
                traversalProbe: probe, motionDriver: driver);
            return new Rig { Host = host, Probe = probe, Driver = driver, Machine = machine, Data = data };
        }

        /// <summary>只改測試副本：三格 binding 的 max correction 設為 1 mm，讓任何 plan 都超限。</summary>
        private static void SetTinyCorrectionBudget(TraversalStateParamsSO clone)
        {
            foreach (string bindingField in new[] { "vault1m", "climb1m", "climb2m" })
            {
                FieldInfo field = typeof(TraversalStateParamsSO).GetField(
                    bindingField, BindingFlags.NonPublic | BindingFlags.Instance);
                object binding = field.GetValue(clone);
                // ⚠️ 不能設 0：binding 的 getter 把 ≤0 視為「未設定」而退回預設 1.5／0.75 m。
                //    1 mm 小於任何真實 plan 需要的修正（Climb1m 理想站位也要水平 2 cm、垂直 8.7 cm）。
                SetPrivateField(binding, "maxHorizontalCorrection", 0.001f);
                SetPrivateField(binding, "maxVerticalCorrection", 0.001f);
                field.SetValue(clone, binding);
            }
        }

        /// <summary>只改測試副本：全域 Entry 理想距離（沒有 Traversal block 的 clip 會退回它）。</summary>
        private static void SetGlobalDesiredWallDistance(TraversalStateParamsSO clone, float distance)
        {
            FieldInfo field = typeof(TraversalStateParamsSO).GetField(
                "entryPolicy", BindingFlags.NonPublic | BindingFlags.Instance);
            object entryPolicy = field.GetValue(clone);
            SetPrivateField(entryPolicy, "desiredWallDistance", distance);
            field.SetValue(clone, entryPolicy);
        }

        /// <summary>把 config 的 TraversalStateParams 換成一份經 <paramref name="tweak"/> 修改過的**副本**（不改資產）。</summary>
        private void InstallTraversalParamsCopy(StateMachineConfigSO config, Action<TraversalStateParamsSO> tweak)
        {
            config.Initialize();
            var current = config.GetStateParams<TraversalStateParamsSO>(StateType.Traversal);
            Assert.IsNotNull(current);
            TraversalStateParamsSO clone = UnityEngine.Object.Instantiate(current);
            _created.Add(clone);
            tweak(clone);

            FieldInfo mappingsField = typeof(StateMachineConfigSO).GetField(
                "paramsMappings", BindingFlags.NonPublic | BindingFlags.Instance);
            var mappings = (List<StateParamsMapping>)mappingsField.GetValue(config);
            for (int i = 0; i < mappings.Count; i++)
            {
                if (mappings[i].State != StateType.Traversal) continue;
                StateParamsMapping mapping = mappings[i];
                mapping.Params = clone;
                mappings[i] = mapping;
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName}");
            field.SetValue(target, value);
        }
    }
}
