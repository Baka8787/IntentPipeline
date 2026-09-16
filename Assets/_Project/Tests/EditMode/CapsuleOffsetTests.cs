using NUnit.Framework;
using UnityEngine;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// **Dynamic Capsule V2 —— pose-driven solver 與 driver 層**（`docs/27` §12；白話版 `docs/28`）。
    ///
    /// <para><b>這一組能驗到什麼、驗不到什麼</b></para>
    /// ✅ 驗得到：解算的**幾何正確性**（給定姿勢 → 該不該偏、偏多少、偏哪邊）、連續性、
    ///    上限語意、退化輸入、driver 的 authority guard 與狀態清理。全部是確定性純運算。
    /// ⛔ 驗不到：真實動畫姿勢下的手感、貼牆觀感、底部支撐前移的可接受度。
    ///    ⇒ 那些在 PlayMode（`CapsuleOffsetPlayModeTests`）與人類 Play。
    ///
    /// <para><b>🔄 V1 測試去了哪裡</b></para>
    /// C1–C9 驗的是 <c>MoveDirection × MaxOffset × speed</c> 模型，該模型已被實測推翻並移除
    /// （`docs/27` §12.5：純側移需要 0 卻推 0.12 到**反方向**；前跑需要 0.20–0.30 卻只給 0.12）。
    /// **保留那些測試等於把錯誤模型釘死。** 取而代之的是下面的 CP（solver）與 CD（driver）兩組。
    /// C10／C11／C14 的**意圖**仍然成立，以新形狀保留。
    /// </summary>
    public sealed class CapsuleOffsetTests
    {
        private const float Tolerance = 1e-4f;

        // X Bot 的實測 authored 幾何（docs/27 §12.0）。
        private const float Radius = 0.32f;
        private const float Height = 1.826619f;
        private const float CenterY = 0.9134095f;

        // X Bot 的實測 flesh radius（docs/27 §12.4）。
        private const float RChest = 0.2038f;
        private const float RUpperChest = 0.1867f;
        private const float RNeck = 0.0952f;
        private const float RHead = 0.2109f;

        private static AuthoredCapsuleGeometry Geometry() =>
            new AuthoredCapsuleGeometry(new Vector3(0f, CenterY, 0f), Radius, Height);

        /// <summary>
        /// rest pose 的四個 landmark 高度（`docs/27` §12.0 實測）。
        /// <paramref name="forwardLean"/>／<paramref name="lateralLean"/> 的單位是 **Head 的水平位移（公尺）**，
        /// 其餘三點依軀幹柱的比例縮放。實測 Run 的 Head 約 +0.20、Sprint 約 +0.30。
        /// </summary>
        private static CapsuleLandmarkSample[] RestPose(float forwardLean = 0f, float lateralLean = 0f)
        {
            return new[]
            {
                new CapsuleLandmarkSample(new Vector3(lateralLean * 0.20f, 1.2435f, forwardLean * 0.20f), RChest),
                new CapsuleLandmarkSample(new Vector3(lateralLean * 0.45f, 1.3357f, forwardLean * 0.45f), RUpperChest),
                new CapsuleLandmarkSample(new Vector3(lateralLean * 0.80f, 1.5031f, forwardLean * 0.80f), RNeck),
                new CapsuleLandmarkSample(new Vector3(lateralLean * 1.00f, 1.5993f, forwardLean * 1.00f), RHead),
            };
        }

        private static CapsulePoseSolveResult Solve(CapsuleLandmarkSample[] pose, int iterations = 24)
            => CapsulePoseSolver.Solve(pose, pose.Length, Geometry(), iterations);

        /// <summary>解出的 c 是否真的把每一顆肉體球都包進膠囊。這是「答案對不對」的唯一判準。</summary>
        private static float CoverageResidual(CapsuleLandmarkSample[] pose, Vector2 c)
        {
            AuthoredCapsuleGeometry g = Geometry();
            float worst = float.NegativeInfinity;
            foreach (CapsuleLandmarkSample s in pose)
            {
                float allow = g.Radius - s.FleshRadius;
                float dy = Mathf.Max(0f, s.LocalPosition.y - g.TopSphereCenterY);
                float rhoSqr = allow * allow - dy * dy;
                if (allow <= 0f || rhoSqr <= 0f) continue;   // solver 也會略過，見 SkippedLandmarks
                var q = new Vector2(s.LocalPosition.x, s.LocalPosition.z);
                worst = Mathf.Max(worst, (q - c).magnitude - Mathf.Sqrt(rhoSqr));
            }
            return worst;
        }

        // =====================================================================
        // CP —— Pose Solver（純函式）
        // =====================================================================

        /// <summary>
        /// **CP1 — 站直（Walk 姿勢）⇒ 幾乎不偏。**
        /// 實測 Walk 的上半身完全在膠囊內（`docs/27` §12.2 全 0）⇒ 最小必要修正就是 0。
        /// ⚠️ 這是 V1 的 <c>MinimumSpeedNormalized</c> 想達成但用錯手段的效果：
        /// V1 靠「速度低於門檻就歸零」（一個**不連續**的開關），V2 是解出來自然為 0。
        /// </summary>
        [Test]
        public void CP1_UprightPose_ProducesNoOffset()
        {
            CapsulePoseSolveResult r = Solve(RestPose());

            Assert.AreEqual(CapsulePoseSolveStatus.Neutral, r.Status);
            Assert.That(r.Offset.magnitude, Is.LessThan(1e-3f), "站直時不該有任何偏移");
        }

        /// <summary>**CP2 — 前傾（Run 姿勢）⇒ 往前偏，而且真的包住。**</summary>
        [Test]
        public void CP2_ForwardLean_ProducesForwardOffsetThatCovers()
        {
            CapsuleLandmarkSample[] pose = RestPose(forwardLean: 0.20f);   // head +0.20 m
            CapsulePoseSolveResult r = Solve(pose);

            Assert.AreEqual(CapsulePoseSolveStatus.Corrected, r.Status);
            Assert.That(r.Offset.y, Is.GreaterThan(0.01f), "前傾必須往 +Z 偏");
            Assert.That(Mathf.Abs(r.Offset.x), Is.LessThan(0.01f), "純前傾不該產生側向分量");
            Assert.That(CoverageResidual(pose, r.Offset), Is.LessThanOrEqualTo(Tolerance),
                "解出來的 c 必須真的把每顆肉體球包進膠囊——這是 solver 存在的唯一理由");
        }

        /// <summary>
        /// **CP3 — 前傾更多（Sprint 姿勢）⇒ 偏移單調變大。**
        /// 實測 Run 需要 0.231、Sprint 需要 0.296（`docs/27` §12.5）。
        /// </summary>
        [Test]
        public void CP3_MoreLeanRequiresMoreOffset()
        {
            float run = Solve(RestPose(forwardLean: 0.20f)).Offset.magnitude;
            float sprint = Solve(RestPose(forwardLean: 0.30f)).Offset.magnitude;

            Assert.That(sprint, Is.GreaterThan(run), "傾得更多就必須偏更多");
        }

        /// <summary>
        /// 🔴 **CP4 — 純側移的姿勢是「站直」，所以答案是 0，不是「往側邊推」。**
        ///
        /// 這條是 V2 存在的核心理由。V1 用 <c>MoveDirection</c>，往左走就往左推 0.12；
        /// 而實測純側移時軀幹根本沒往左偏（`StrafeLeftLoop` 的核心甚至往**右**傾 0.029）
        /// ⇒ V1 製造出 0.149 m 的誤差去修一個只有 0.099 而且**本來就在膠囊內**的問題。
        /// </summary>
        [Test]
        public void CP4_PureStrafePose_ProducesNoOffset_BecausePoseIsUpright()
        {
            CapsulePoseSolveResult r = Solve(RestPose());

            Assert.That(r.Offset.magnitude, Is.LessThan(1e-3f),
                "側移的姿勢是站直的 ⇒ 沒有東西露在膠囊外 ⇒ 不該偏移。" +
                "⛔ 若這條紅了，很可能是有人把 MoveDirection 接回 solver。");
        }

        /// <summary>
        /// **CP5 — 斜向姿勢 ⇒ 方向由姿勢決定，不是移動方向。**
        /// 實測 RunStrafeLeft45 解出 (Z +0.197, X −0.072)，比例 2.7:1，**不是 45°**（`docs/27` §12.5）。
        /// </summary>
        [Test]
        public void CP5_DiagonalPose_OffsetFollowsPoseNotMoveDirection()
        {
            CapsuleLandmarkSample[] pose = RestPose(forwardLean: 0.20f, lateralLean: -0.07f);
            CapsulePoseSolveResult r = Solve(pose);

            Assert.That(r.Offset.y, Is.GreaterThan(0f), "前向分量");
            Assert.That(r.Offset.x, Is.LessThan(0f), "側向分量跟著姿勢往同一側");
            Assert.That(Mathf.Abs(r.Offset.y), Is.GreaterThan(Mathf.Abs(r.Offset.x)),
                "姿勢的前傾比側傾大 ⇒ 解出的偏移也該如此；45° 移動不代表 45° 偏移");
            Assert.That(CoverageResidual(pose, r.Offset), Is.LessThanOrEqualTo(Tolerance));
        }

        /// <summary>
        /// **CP6 — 最小性：解出的 c 是「能覆蓋的最近點」，不是「推到底」。**
        /// 任何比它更短的同向向量都必須覆蓋不住，否則 solver 推過頭了。
        /// </summary>
        [Test]
        public void CP6_SolutionIsMinimal_NotMaximal()
        {
            CapsuleLandmarkSample[] pose = RestPose(forwardLean: 0.20f);
            Vector2 c = Solve(pose).Offset;

            Assert.That(CoverageResidual(pose, c), Is.LessThanOrEqualTo(Tolerance), "本身要能覆蓋");
            Assert.That(CoverageResidual(pose, c * 0.8f), Is.GreaterThan(Tolerance),
                "縮短 20% 就該覆蓋不住 ⇒ 證明它是最小必要量，不是隨便推滿");
        }

        /// <summary>
        /// 🔴 **CP7 — 連續性：姿勢連續變化時，解也必須連續（不得有 argmax 式的跳變）。**
        ///
        /// 本 repo 已有「不連續選擇餵給連續量」的缺陷紀錄（memory `snap-to-default-defect-class`）。
        /// 凸集投影的性質保證了這件事：即使 active constraint（正在頂住的那個約束）換人，
        /// 投影點仍然連續移動。這條測試把那個性質釘住。
        /// </summary>
        [Test]
        public void CP7_SolutionIsContinuousAcrossPoseSweep()
        {
            const int steps = 200;
            Vector2 previous = Solve(RestPose(forwardLean: 0f)).Offset;
            float worstJump = 0f;

            for (int i = 1; i <= steps; i++)
            {
                float lean = 0.32f * i / steps;
                Vector2 current = Solve(RestPose(forwardLean: lean)).Offset;
                worstJump = Mathf.Max(worstJump, (current - previous).magnitude);
                previous = current;
            }

            // 姿勢每步只走 0.008 的 lean 參數 ⇒ 解的變化必須是同一個量級，不能出現跳階。
            Assert.That(worstJump, Is.LessThan(0.01f),
                $"偵測到不連續跳變（最大單步 {worstJump:F4} m）——" +
                "solver 退化成 argmax 了嗎？");
        }

        /// <summary>
        /// **CP8 — 肉體球比膠囊還粗的 landmark 會被略過，不會讓整個解爆掉。**
        /// 這是 radius／height 的問題（`docs/27` §12.6），水平偏移救不了，
        /// 正確行為是**記錄下來**而不是讓 solver 去追一個追不到的目標。
        /// </summary>
        [Test]
        public void CP8_LandmarkFatterThanCapsule_IsSkippedAndReported()
        {
            var pose = new[]
            {
                new CapsuleLandmarkSample(new Vector3(0f, 1.2435f, 0f), Radius + 0.05f), // 比膠囊粗
                new CapsuleLandmarkSample(new Vector3(0f, 1.5993f, 0.20f), RHead),
            };

            CapsulePoseSolveResult r = CapsulePoseSolver.Solve(pose, pose.Length, Geometry());

            Assert.AreEqual(1, r.SkippedLandmarks, "過粗的 landmark 必須被計入 SkippedLandmarks");
            Assert.AreEqual(CapsulePoseSolveStatus.Corrected, r.Status, "其餘 landmark 仍要正常求解");
        }

        /// <summary>**CP9 — 退化輸入一律安全歸零，不得回傳 NaN 或亂數。**</summary>
        [Test]
        public void CP9_DegenerateInputs_ReturnZero()
        {
            Assert.AreEqual(Vector2.zero, CapsulePoseSolver.Solve(null, 0, Geometry()).Offset);
            Assert.AreEqual(Vector2.zero, CapsulePoseSolver.Solve(RestPose(), 0, Geometry()).Offset);

            var nan = new[] { new CapsuleLandmarkSample(new Vector3(float.NaN, 1.5f, 0f), RHead) };
            CapsulePoseSolveResult r = CapsulePoseSolver.Solve(nan, 1, Geometry());
            Assert.AreEqual(Vector2.zero, r.Offset);
            Assert.AreEqual(1, r.SkippedLandmarks);

            var zeroRadius = new AuthoredCapsuleGeometry(new Vector3(0f, CenterY, 0f), 0f, Height);
            Assert.AreEqual(
                CapsulePoseSolveStatus.InvalidInput,
                CapsulePoseSolver.Solve(RestPose(), 4, zeroRadius).Status);
        }

        /// <summary>
        /// **CP10 — 預設迭代數已經收斂。**
        /// 拿 24 次與 300 次的結果比對；差距大於容許值就代表 <c>DefaultIterations</c> 訂太小。
        /// （離線研究用 300 次，runtime 不能付那個成本，所以必須有這條把它釘住。）
        /// </summary>
        [Test]
        public void CP10_DefaultIterationCountHasConverged()
        {
            foreach (float lean in new[] { 0.10f, 0.20f, 0.30f })
            {
                CapsuleLandmarkSample[] pose = RestPose(forwardLean: lean, lateralLean: 0.06f);
                Vector2 fast = Solve(pose, CapsulePoseSolver.DefaultIterations).Offset;
                Vector2 slow = Solve(pose, 300).Offset;

                Assert.That((fast - slow).magnitude, Is.LessThan(1e-3f),
                    $"lean={lean}：{CapsulePoseSolver.DefaultIterations} 次迭代尚未收斂");
            }
        }

        /// <summary>
        /// **CP11 — MaxOffset 是上限，不是驅動量。**
        /// 低於上限時原樣通過（不得被「放大到上限」），高於上限時只縮長度、不轉方向。
        /// </summary>
        [Test]
        public void CP11_SafetyCap_ClampsLengthOnly_AndNeverAmplifies()
        {
            var under = new Vector2(0f, 0.15f);
            Assert.AreEqual(under, CapsulePoseSolver.ApplySafetyCap(under, 0.18f),
                "低於上限必須原樣通過——⛔ MaxOffset 不是『跑步就推這麼多』");

            var over = new Vector2(0.18f, 0.24f);                 // 長度 0.30
            Vector2 capped = CapsulePoseSolver.ApplySafetyCap(over, 0.18f);
            Assert.That(capped.magnitude, Is.EqualTo(0.18f).Within(Tolerance), "長度夾到上限");
            Assert.That(Vector2.Angle(over, capped), Is.LessThan(0.01f), "方向不得改變");

            Assert.AreEqual(Vector2.zero, CapsulePoseSolver.ApplySafetyCap(over, 0f));
            Assert.AreEqual(Vector2.zero, CapsulePoseSolver.ApplySafetyCap(over, float.NaN));
        }

        /// <summary>
        /// 🔴 **CP12 — 膠囊在高處會變窄，solver 必須知道。**
        ///
        /// 同一個水平位置、同一顆肉體球，放在圓柱段（y=1.35）與放在上半球（y=1.75）
        /// 需要的偏移**不一樣**。若 solver 拿 <c>Radius</c> 當所有高度的可用半徑，
        /// 這兩個情況會解出相同的答案——那正是 `docs/27` §11 犯過的錯。
        /// </summary>
        [Test]
        public void CP12_CapsuleNarrowsWithHeight_SolverAccountsForIt()
        {
            // ⚠️ 高度必須挑「兩者都還塞得下」的：Head 的 allow = 0.32 − 0.2109 = 0.1091，
            //    所以 y > topC + 0.1091 = 1.6158 之後連塞都塞不下（solver 會 skip，那是另一條測試）。
            var low = new[] { new CapsuleLandmarkSample(new Vector3(0f, 1.35f, 0.18f), RHead) };
            var high = new[] { new CapsuleLandmarkSample(new Vector3(0f, 1.60f, 0.18f), RHead) };

            float lowOffset = CapsulePoseSolver.Solve(low, 1, Geometry()).Offset.magnitude;
            float highOffset = CapsulePoseSolver.Solve(high, 1, Geometry()).Offset.magnitude;

            Assert.That(highOffset, Is.GreaterThan(lowOffset + 0.01f),
                "同一個水平位置放得越高，可用半徑越小 ⇒ 需要的偏移越大。" +
                "兩者相同 ⇒ solver 把 0.32 當成所有高度的寬度了");
        }

        // =====================================================================
        // CD —— Driver 層（authority／狀態清理）
        // =====================================================================

        /// <summary>
        /// 🔴 **CD1（前身 C10）— traversal collision profile 生效期間，姿勢偏移完全不得寫 center。**
        ///
        /// 兩者是**同一個欄位的兩個寫入者**。更糟的是 <c>RestoreTraversalCollisionProfile</c>
        /// 之後會把姿勢偏移「還原」成永久值——那時 traversal 已經結束，沒有人會把它改回來。
        /// </summary>
        [Test]
        public void CD1_TraversalProfileActive_SuppressesOffsetEntirely()
        {
            MotionDriver driver = CreateDriver(out CharacterController capsule);
            Vector3 authoredCenter = capsule.center;

            SetPrivateField(driver, "capsuleOffset", EnabledSettings());
            SetPrivateField(driver, "_traversalCollisionProfileActive", true);

            InvokeUpdateCapsuleOffset(driver, Alive());

            Assert.AreEqual(authoredCenter, capsule.center,
                "traversal profile 生效時 center 必須完全由它獨佔——⛔ 不得有第二個寫入者");
        }

        /// <summary>
        /// **CD2（前身 C11）— 停用時不得留下殘值。**
        /// 關掉開關**不等於**回到原狀：若只是「不再更新」，上一幀偏出去的 center 會永遠留在那裡。
        /// 症狀是碰撞體**永久歪一邊**，而 Inspector 上的開關明明是關的。
        /// </summary>
        [Test]
        public void CD2_Disabled_RestoresAuthoredCenterImmediately()
        {
            MotionDriver driver = CreateDriver(out CharacterController capsule);
            Vector3 authoredCenter = capsule.center;

            // ⚠️ **必須先重現 Awake 的基準擷取。** production 在 Awake 抓，那時 center 還是 authored 值。
            //    測試若先把 center 弄髒才第一次呼叫，擷取到的「基準」就會是偏移後的位置
            //    ⇒ 還原當然還原不回去。（2026-09-15 兩次都踩這個，記在這裡。）
            Assert.AreEqual(authoredCenter, driver.CapsuleBaseCenter);

            SetPrivateField(driver, "capsuleOffset", CapsuleOffsetSettings.Disabled);
            SetPrivateField(driver, "_capsuleCurrentOffset", new Vector3(0.12f, 0f, 0.05f));
            SetPrivateField(driver, "_capsuleSmoothedOffset", new Vector3(0.12f, 0f, 0.05f));
            capsule.center = authoredCenter + new Vector3(0.12f, 0f, 0.05f);

            InvokeUpdateCapsuleOffset(driver, Alive());

            Assert.AreEqual(authoredCenter, capsule.center, "停用必須**立刻**還原 authored 基準，不是慢慢收");
            Assert.AreEqual(Vector3.zero, GetPrivateField<Vector3>(driver, "_capsuleCurrentOffset"));
            Assert.AreEqual(Vector3.zero, GetPrivateField<Vector3>(driver, "_capsuleSmoothedOffset"));
        }

        /// <summary>
        /// **CD3 — 沒有 profile 就不作用（安全退化），而不是用預設值亂動碰撞體。**
        /// flesh radius 是**從角色網格量出來的**，沒有它就沒有任何根據去移動膠囊。
        /// </summary>
        [Test]
        public void CD3_EnabledWithoutProfile_IsInert()
        {
            MotionDriver driver = CreateDriver(out CharacterController capsule);
            Vector3 authoredCenter = capsule.center;

            var settings = CapsuleOffsetSettings.Disabled;
            settings.Enabled = true;
            settings.Profile = null;
            SetPrivateField(driver, "capsuleOffset", settings);

            InvokeUpdateCapsuleOffset(driver, Alive());

            Assert.AreEqual(authoredCenter, capsule.center,
                "缺 profile ⇒ 安全退化為 authored center，⛔ 不得用內建預設值");
        }

        /// <summary>
        /// **CD4 — authored 基準在 <c>Awake</c> 擷取，之後不得被偏移改寫。**
        /// V1 是惰性擷取（第一次 Update 才抓），只要有人先動過 center 就會把偏移後的位置誤當基準。
        /// </summary>
        [Test]
        public void CD4_AuthoredBaseCenter_IsCapturedOnceAndNeverDrifts()
        {
            MotionDriver driver = CreateDriver(out CharacterController capsule);
            Vector3 authoredCenter = capsule.center;

            // 觸發一次擷取。
            Assert.AreEqual(authoredCenter, driver.CapsuleBaseCenter);

            // 模擬偏移已經寫進 center，再問一次基準。
            capsule.center = authoredCenter + new Vector3(0f, 0f, 0.18f);
            Assert.AreEqual(authoredCenter, driver.CapsuleBaseCenter,
                "🔒 基準是不變量：任何動態偏移都不得改寫它");
        }

        /// <summary>
        /// **CD5（前身 C14）— 幾何夾持只縮長度、不轉方向。**
        /// 方向是姿勢決定的；讓牆去改變方向等於把幾何體變成偏移方向的第二個決定者。
        /// </summary>
        [Test]
        public void CD5_ClampToAllowedDistance_ScalesWithoutRotating()
        {
            var desired = new Vector3(0.3f, 0f, 0.4f);   // 長度 0.5

            Assert.AreEqual(desired, CapsuleOffsetSolver.ClampToAllowedDistance(desired, 0.9f, 0.5f),
                "沒被擋住就原樣放行，⛔ 不得順便放大");

            Vector3 clamped = CapsuleOffsetSolver.ClampToAllowedDistance(desired, 0.25f, 0.5f);
            Assert.That(clamped.magnitude, Is.EqualTo(0.25f).Within(Tolerance));
            Assert.That(Vector3.Angle(desired, clamped), Is.LessThan(0.01f), "方向不得改變");

            Assert.AreEqual(Vector3.zero, CapsuleOffsetSolver.ClampToAllowedDistance(desired, 0f, 0.5f),
                "起點就重疊 ⇒ 退回基準位置（與導入本機制前的行為相同）");
        }

        // 📌 **「`center.y` 永不改」與「平滑行為」刻意不在這裡驗**：兩者都要走過
        //    `deltaTime > 0` 的守衛，而 EditMode 的 `Time.deltaTime` 不保證非零
        //    ⇒ 寫在這裡會得到「可能恆真但什麼都沒驗到」的測試（PD2 踩過同一類問題）。
        //    ⇒ 移到 PlayMode：`CapsuleOffsetPlayModeTests`。

        // =====================================================================
        // driver 層治具
        // =====================================================================

        private readonly System.Collections.Generic.List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private MotionDriver CreateDriver(out CharacterController capsule)
        {
            var host = new GameObject("CapsuleOffset-Driver");
            _created.Add(host);
            capsule = host.AddComponent<CharacterController>();
            capsule.center = new Vector3(0f, CenterY, 0f);   // authored 基準，刻意不是原點
            capsule.radius = Radius;
            capsule.height = Height;
            MotionDriver driver = host.AddComponent<MotionDriver>();
            // EditMode 不呼叫 Awake ⇒ 手動補上 production 在 Awake 做的 GetComponent 補洞。
            SetPrivateField(driver, "characterController", capsule);
            return driver;
        }

        private static CapsuleOffsetSettings EnabledSettings()
        {
            CapsuleOffsetSettings s = CapsuleOffsetSettings.Disabled;
            s.Enabled = true;
            return s;
        }

        private static Project.Core.Blackboard.PlayerRuntimeData Alive()
            => new Project.Core.Blackboard.PlayerRuntimeData { IsGrounded = true };

        private static void InvokeUpdateCapsuleOffset(
            MotionDriver driver, Project.Core.Blackboard.PlayerRuntimeData data)
        {
            System.Reflection.MethodInfo method = typeof(MotionDriver).GetMethod(
                "UpdateCapsuleOffset",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, "找不到 MotionDriver.UpdateCapsuleOffset（方法名稱可能已變更）");
            method.Invoke(driver, new object[] { data });
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName}（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName}（欄位名稱可能已變更）");
            return (T)field.GetValue(target);
        }
    }
}
