using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Footstep;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 落腳偵測的確定性 EditMode 測試。
    ///
    /// ⚠️ **全程以顯式 <c>deltaTime</c> 驅動，不依賴 <c>Time.deltaTime</c>**——它在 EditMode 不可控，
    /// 依賴它會讓這些測試的成敗取決於編輯器狀態而非演算法。這正是
    /// <c>FootPlantTracker.Advance</c> 與 <c>FootstepDetector.Detect</c> 收顯式時間步長的理由。
    ///
    /// 也不需要 Animator、Physics 或任何 raycast——偵測只吃「腳底世界位置 ＋ 地面高度」序列。
    ///
    /// 🔄（Stage 1）新增的測項直接編碼**磁碟上真實動畫量到的數值**（見 `docs/35` §2），
    /// 而不是憑感覺挑的假數字。因此它們同時是「不許再退回去」的回歸鎖。
    /// </summary>
    public class FootstepDetectorTests
    {
        private const float Dt = 1f / 60f;

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private static FootstepDetectionSettings Settings() => new()
        {
            ArmDescentSpeed = 0.25f,
            FireDescentSpeed = 0.05f,
            ReArmLift = 0.025f,
            NormalStepLift = 0.060f,
            ContactClearance = 0.045f,
            PlantHorizontalSpeedMax = 2.0f,
            GroundStepTolerance = 0.06f,
        };

        // =====================================================================
        // 驅動輔助：高度序列 →（地面恆為 0 的）世界位置序列
        // =====================================================================

        /// <summary>一個抬腳→落腳循環：由 0 抬到 peak 再落回 0，各 steps 帧，末尾停兩帧讓速度歸零。</summary>
        private static List<float> Stride(float peak, int steps)
        {
            var heights = new List<float>();
            for (int i = 1; i <= steps; i++) heights.Add(peak * i / steps);
            for (int i = steps - 1; i >= 0; i--) heights.Add(peak * i / steps);
            heights.Add(0f);
            heights.Add(0f);
            return heights;
        }

        /// <summary>把高度序列餵進 tracker（腳水平不動、地面恆 0），回傳每帧的分類。</summary>
        private static List<FootstepKind> Feed(IEnumerable<float> heights, FootstepDetectionSettings settings,
            float dt = Dt, float horizontalStep = 0f)
        {
            var tracker = new FootPlantTracker();
            var result = new List<FootstepKind>();
            float x = 0f;
            foreach (float h in heights)
            {
                result.Add(tracker.Advance(new Vector3(x, h, 0f), true, 0f, 0f, dt, settings, out _));
                x += horizontalStep;
            }
            return result;
        }

        private static int Count(IEnumerable<FootstepKind> kinds, FootstepKind wanted)
        {
            int n = 0;
            foreach (FootstepKind k in kinds) if (k == wanted) n++;
            return n;
        }

        private static int CountAny(IEnumerable<FootstepKind> kinds)
        {
            int n = 0;
            foreach (FootstepKind k in kinds) if (k != FootstepKind.None) n++;
            return n;
        }

        // =====================================================================
        // 核心偵測
        // =====================================================================

        [Test]
        public void Stride_ProducesExactlyOneNormalStep()
        {
            List<FootstepKind> kinds = Feed(Stride(0.18f, 8), Settings());
            Assert.AreEqual(1, CountAny(kinds), "一次完整的抬腳→落腳只能產生一次事件");
            Assert.AreEqual(1, Count(kinds, FootstepKind.Normal), "行程遠大於 NormalStepLift，必須判為 Normal");
        }

        [Test]
        public void FirstSample_DoesNotProduceEvent()
        {
            var tracker = new FootPlantTracker();
            FootstepKind k = tracker.Advance(Vector3.zero, true, 0f, 0f, Dt, Settings(), out FootstepSuppressReason r);
            Assert.AreEqual(FootstepKind.None, k, "首次採樣沒有前一帧，算不出速度");
            Assert.AreEqual(FootstepSuppressReason.Warmup, r);
        }

        [Test]
        public void RepeatedStrides_ProduceOneEventEach()
        {
            var all = new List<float>();
            for (int i = 0; i < 4; i++) all.AddRange(Stride(0.18f, 8));
            Assert.AreEqual(4, CountAny(Feed(all, Settings())), "四次跨步必須剛好四次事件——不多不少");
        }

        [Test]
        public void FrozenTime_DoesNotAdvanceStateOrTrigger()
        {
            var tracker = new FootPlantTracker();
            FootstepDetectionSettings s = Settings();

            foreach (float h in Stride(0.18f, 8))
            {
                Assert.AreEqual(FootstepKind.None, tracker.Advance(new Vector3(0f, h, 0f), true, 0f, 0f, 0f, s, out _),
                    "deltaTime = 0 時不得產生事件");
            }

            int fired = 0;
            foreach (float h in Stride(0.18f, 8))
            {
                if (tracker.Advance(new Vector3(0f, h, 0f), true, 0f, 0f, Dt, s, out _) != FootstepKind.None) fired++;
            }
            Assert.AreEqual(1, fired, "凍結期間不得污染跨帧狀態——解除後偵測必須照常運作");
        }

        // =====================================================================
        // ⭐ Stage 1 回歸鎖 ①：擺盪中低點不是落腳
        // =====================================================================

        /// <summary>
        /// **真實缺陷的回歸鎖。** 數值取自 `WalkFwdLoop` 左腳的實測軌跡：
        /// 踩地(≈0) → 抬到 0.179 → **降到 0.044 再抬回 0.059** → 才真正踩到 −0.007。
        ///
        /// 舊版只看「下降停止」，因此在 0.044 那個中低點就擊發；更糟的是擊發後行程基準被重設，
        /// 之後只剩 0.015 m 的行程打不到當時的 `MinLiftExcursion 0.03`，
        /// **真正的踩地反而被吃掉** ⇒ 走路時每一聲都出現在錯的相位上。
        ///
        /// 新版用**水平速度**分辨：中低點時腳仍以 4.81 m/s 掠過（實測），真正踩地時只有 0.03 m/s。
        /// </summary>
        [Test]
        public void MidSwingDip_IsNotAPlant_AndTheRealPlantStillFires()
        {
            FootstepDetectionSettings s = Settings();
            var tracker = new FootPlantTracker();
            var fired = new List<int>();

            // (height, horizontalSpeed) —— 依實測軌跡取樣
            var profile = new List<(float h, float vxz)>();
            void Segment(float from, float to, int frames, float vxz)
            {
                for (int i = 1; i <= frames; i++) profile.Add((Mathf.Lerp(from, to, i / (float)frames), vxz));
            }

            profile.Add((0f, 0.03f));
            Segment(0f, 0.179f, 23, 3.0f);      // 抬起（擺盪中，水平快）
            Segment(0.179f, 0.044f, 12, 4.81f); // 降到中低點——⚠️ 這裡舊版會誤擊發
            Segment(0.044f, 0.059f, 4, 2.60f);  // 又抬回去（擺盪還沒結束）
            Segment(0.059f, -0.007f, 18, 0.60f);// 真正落下，水平速度收斂
            for (int i = 0; i < 4; i++) profile.Add((-0.007f, 0.03f)); // 踩住

            float x = 0f;
            for (int i = 0; i < profile.Count; i++)
            {
                x += profile[i].vxz * Dt;
                FootstepKind k = tracker.Advance(new Vector3(x, profile[i].h, 0f), true, 0f, 0f, Dt, s, out _);
                if (k != FootstepKind.None) fired.Add(i);
            }

            Assert.AreEqual(1, fired.Count,
                "一次跨步只能擊發一次；中低點（index≈35）不得算數");
            Assert.Greater(fired[0], 50,
                "擊發必須落在真正的踩地段（序列末端），不是中低點");
        }

        /// <summary>水平速度超過上限時，即使高度與下降都合格也不得擊發。</summary>
        [Test]
        public void HighHorizontalSpeed_SuppressesPlant()
        {
            FootstepDetectionSettings s = Settings();
            // 每帧前進 0.05 m ⇒ 3.0 m/s，超過 2.0 上限
            List<FootstepKind> kinds = Feed(Stride(0.18f, 8), s, Dt, horizontalStep: 3.0f * Dt);
            Assert.AreEqual(0, CountAny(kinds), "腳仍以擺盪速度水平掠過時不得判為落腳");
        }

        // =====================================================================
        // ⭐ Stage 1 回歸鎖 ②：收步碎步
        // =====================================================================

        /// <summary>
        /// 停步 settling 的實測行程：`RunFwdStop_RU` 左腳踩地後出現 0.017 → 0.003 → 0.003 的連續小抖動。
        /// 這些都必須完全靜音（連 Shuffle 都不是）。
        /// </summary>
        [Test]
        public void StopSettlingChatter_ProducesNothing()
        {
            FootstepDetectionSettings s = Settings();
            var heights = new List<float> { 0f };
            void Bump(float peak, int up, int down)
            {
                for (int i = 1; i <= up; i++) heights.Add(peak * i / up);
                for (int i = down - 1; i >= 0; i--) heights.Add(peak * i / down);
                heights.Add(0f);
            }
            Bump(0.017f, 6, 8);   // 實測最大的純 settling 行程
            Bump(0.003f, 4, 5);
            Bump(0.003f, 4, 5);
            Bump(0.0015f, 3, 4);  // Idle 呼吸幅度

            Assert.AreEqual(0, CountAny(Feed(heights, s)),
                "settling 與 Idle 微震的行程都在 ReArmLift(0.025) 以下，必須完全靜音");
        }

        /// <summary>
        /// 走路時**支撐腳**的腳跟離地／腳掌滾動，實測抬升 0.0149 m。
        /// 這會發生在每一個步伐週期，若判成事件就等於步頻加倍。
        /// </summary>
        [Test]
        public void SupportFootToeRoll_ProducesNothing()
        {
            var heights = new List<float> { 0f };
            for (int i = 1; i <= 9; i++) heights.Add(0.0149f * i / 9f);
            for (int i = 8; i >= 0; i--) heights.Add(0.0149f * i / 9f);
            heights.Add(0f);
            heights.Add(0f);

            Assert.AreEqual(0, CountAny(Feed(heights, Settings())),
                "支撐腳的腳掌滾動（實測 0.0149 m）不得產生任何事件");
        }

        /// <summary>
        /// 實測的停步腳部調整（`WalkFwdStop_RU` 右腳：行程 0.033 m、峰值下降速度 0.40 m/s）
        /// 應判為 **Shuffle**——既不是完整腳步、也不該完全靜音。
        /// ⚠️ 下降速度必須照實測給：0.40 m/s 會通過上膛門檻(0.25)，這正是它與純 settling 的差別。
        /// </summary>
        [Test]
        public void StopAdjustment_IsClassifiedAsShuffle()
        {
            FootstepDetectionSettings s = Settings();
            var heights = new List<float> { 0f };
            for (int i = 1; i <= 16; i++) heights.Add(0.033f * i / 16f);   // 緩慢抬起
            for (int i = 4; i >= 0; i--) heights.Add(0.033f * i / 5f);     // 5 帧落下 ⇒ ≈0.40 m/s
            heights.Add(0f);
            heights.Add(0f);

            List<FootstepKind> kinds = Feed(heights, s);
            Assert.AreEqual(1, CountAny(kinds), "0.033 m 的調整應產生剛好一次事件");
            Assert.AreEqual(1, Count(kinds, FootstepKind.Shuffle),
                "行程介於 ReArmLift(0.025) 與 NormalStepLift(0.060) 之間 ⇒ Shuffle，不得用正常腳步音");
        }

        // =====================================================================
        // ⭐ Stage 1 回歸鎖 ③：樓梯
        // =====================================================================

        /// <summary>
        /// **上樓時 root 與腳的世界 Y 同時上升**，因此「腳的世界高度」完全不能代表接地關係。
        /// 本測試讓腳的世界 Y **全程單調上升**（從 1.0 升到 1.25），但腳下地面同步上升，
        /// 離地高度仍走出正常的抬起→落下 ⇒ 必須正確偵測到一次落腳。
        /// 以世界高度為判準的舊演算法在這裡永遠不會擊發（因為 Y 從不下降）。
        /// </summary>
        [Test]
        public void ClimbingStairs_DetectsPlantDespiteRisingWorldHeight()
        {
            FootstepDetectionSettings s = Settings();
            var tracker = new FootPlantTracker();
            int fired = 0;

            float groundY = 1.0f;            // 這一階的高度
            List<float> clearance = Stride(0.18f, 8);

            for (int i = 0; i < clearance.Count; i++)
            {
                groundY += 0.004f;           // root／地面持續上升（走上斜向的一階）
                float worldY = groundY + clearance[i];
                if (tracker.Advance(new Vector3(0f, worldY, 0f), true, groundY, groundY, Dt, s, out _)
                    != FootstepKind.None) fired++;
            }

            Assert.AreEqual(1, fired, "上樓時腳的世界 Y 全程上升，但相對地面的軌跡是一次完整的落腳");
        }

        /// <summary>
        /// 跨過階梯**級緣**時，腳下地面在單帧內跳一整個級高（場景實測 0.194 m）。
        /// 那一帧的離地高度與前一帧不可比 ⇒ 不得擊發，也不得把跳變灌進抬腳行程。
        /// </summary>
        [Test]
        public void StairEdgeCrossing_DoesNotFireOrInflateLift()
        {
            FootstepDetectionSettings s = Settings();
            var tracker = new FootPlantTracker();
            int fired = 0;

            // 腳貼著地面幾乎不動（站在級緣上輕微晃動），但 ray 在兩階之間來回跳。
            float low = 1.0f, high = 1.194f;
            for (int i = 0; i < 40; i++)
            {
                bool onHigh = (i / 2) % 2 == 0;      // 每兩帧換一階
                float groundY = onHigh ? high : low;
                float worldY = groundY + 0.004f * ((i % 3) - 1); // ±4 mm 抖動
                if (tracker.Advance(new Vector3(0f, worldY, 0f), true, groundY, groundY, Dt, s, out _)
                    != FootstepKind.None) fired++;
            }

            Assert.AreEqual(0, fired,
                "站在級緣上讓 ray 在兩階之間跳動，不得產生任何腳步——這是樓梯誤觸發的主要來源");
        }

        /// <summary>
        /// **真實缺陷的回歸鎖。** 角色被 `CharacterController` 的 step offset 抬上一階時，
        /// **整個 Root 平面在單帧內跳一個級高**，腳的世界位置跟著整包上移。
        /// 若只擋「腳下地面跳變」而不擋「Root 平面跳變」，差分會算出假的巨大速度。
        ///
        /// 實測（走真實場景樓梯 300 帧）：只擋前者時，每隔 8~9 帧就會在正常步距（26~34 帧）之外
        /// 多擊發一次 ⇒ 這正是「樓梯腳步過密」。補上後者之後，擊發變成完整的左右交替。
        /// </summary>
        [Test]
        public void RootPlaneJump_IsTreatedAsDiscontinuity()
        {
            FootstepDetectionSettings s = Settings();
            var tracker = new FootPlantTracker();

            // 腳穩穩踩在地上，地面觀測與 Root 平面一致。
            tracker.Advance(new Vector3(0f, 1.0f, 0f), true, 1.0f, 1.0f, Dt, s, out _);

            // 下一帧角色被抬上一階（級高 0.194）：Root 平面與腳一起上移，
            // 但腳下的 ray 這一帧還打在舊的一階（例如腳懸在級緣外）。
            FootstepKind k = tracker.Advance(new Vector3(0f, 1.194f, 0f), true, 1.0f, 1.194f, Dt, s,
                out FootstepSuppressReason reason);

            Assert.AreEqual(FootstepKind.None, k, "Root 平面跳變的那一帧不得擊發");
            Assert.AreEqual(0f, tracker.VerticalSpeed,
                "Root 平面跳一個級高時，前後兩帧的離地高度不可比 ⇒ 速度必須視為 0");
            Assert.AreNotEqual(FootstepSuppressReason.Descending, reason,
                "假速度不得讓 tracker 憑空上膛");
        }

        /// <summary>
        /// **真實缺陷的回歸鎖。** 行程是相對「上次落腳的高度」量的，但初始化時那個基準
        /// 只是「當下腳在哪」。若此時腳正懸在半空，基準就被錨在空中，之後永遠量不到足夠行程。
        ///
        /// 實測：`RunRtLoop` 的右腳全程離地 0.030~0.280 m，以第一帧的 0.272 當基準時
        /// 最大行程只有 0.008 m ⇒ **整隻右腳永久靜音**（`RunLtLoop` 同樣症狀）。
        /// 修正後兩者都回到「每個週期各一步」。
        /// </summary>
        [Test]
        public void FootStartingAirborne_StillProducesSteps()
        {
            FootstepDetectionSettings s = Settings();
            var tracker = new FootPlantTracker();
            int fired = 0;

            // 依實測重現：腳從 0.272 開始（半空），最低只降到 0.030，最高 0.280。
            var cycle = new List<float>();
            for (int i = 1; i <= 18; i++) cycle.Add(Mathf.Lerp(0.272f, 0.030f, i / 18f)); // 下降到最低點
            for (int i = 0; i < 4; i++) cycle.Add(0.030f);                                 // 踩住
            for (int i = 1; i <= 21; i++) cycle.Add(Mathf.Lerp(0.030f, 0.280f, i / 21f));  // 再抬起
            for (int i = 1; i <= 3; i++) cycle.Add(Mathf.Lerp(0.280f, 0.272f, i / 3f));

            var tr = tracker;
            for (int c = 0; c < 4; c++)
            {
                foreach (float h in cycle)
                {
                    if (tr.Advance(new Vector3(0f, h, 0f), true, 0f, 0f, Dt, s, out _) != FootstepKind.None) fired++;
                }
            }

            Assert.GreaterOrEqual(fired, 3,
                "腳一開始懸在半空時，落腳基準必須能向下修正——否則這隻腳永遠不會出聲");
        }

        // =====================================================================
        // 地面觀測缺席時的退化
        // =====================================================================

        [Test]
        public void WithoutGroundObservation_FallsBackToRootPlane()
        {
            FootstepDetectionSettings s = Settings();
            var tracker = new FootPlantTracker();
            int fired = 0;

            float rootY = 5.0f;
            foreach (float h in Stride(0.18f, 8))
            {
                // hasGround = false ⇒ 必須改用 rootPlaneY 當基準
                if (tracker.Advance(new Vector3(0f, rootY + h, 0f), false, 0f, rootY, Dt, s, out _)
                    != FootstepKind.None) fired++;
            }

            Assert.AreEqual(1, fired, "沒有地面觀測時退回 Root 平面，平地行為必須與有觀測時一致");
        }

        [Test]
        public void GroundAvailabilityChange_IsTreatedAsDiscontinuity()
        {
            FootstepDetectionSettings s = Settings();
            var tracker = new FootPlantTracker();

            // 有地面觀測：離地 0.0
            tracker.Advance(new Vector3(0f, 1.0f, 0f), true, 1.0f, 1.0f, Dt, s, out _);

            // 下一帧觀測消失、退回 Root 平面 0.2 ⇒ 離地瞬間變成 0.8。
            // 若直接差分，會算出 0.8 / (1/60) = 48 m/s 的假速度。
            FootstepKind k = tracker.Advance(new Vector3(0f, 1.0f, 0f), false, 0f, 0.2f, Dt, s,
                out FootstepSuppressReason reason);

            Assert.AreEqual(FootstepKind.None, k, "地面來源改變的那一帧不得擊發");
            Assert.AreEqual(0f, tracker.VerticalSpeed,
                "地面來源改變的那一帧，前後兩帧的離地高度不可比 ⇒ 速度必須視為 0，不得算出假的巨大速度");
            Assert.AreNotEqual(FootstepSuppressReason.Descending, reason,
                "假速度不得讓 tracker 誤以為腳正在高速下降（那會憑空上膛）");
        }

        // =====================================================================
        // Detector 層：雙腳獨立、Landing 收束、契約
        // =====================================================================

        /// <summary>
        /// 建立「Root ＋ Model 子物件（含 FootIKRig）」的最小階層。
        /// ⚠️ **先組好子物件再掛 Detector**：Detector 的 Awake（若被派送）會 GetComponentInChildren&lt;FootIKRig&gt;，
        /// 找不到就 LogError，而 Unity Test Framework 把 LogError 當測試失敗。
        /// 本測試驅動的是 Detect(pose,…)（輸入由參數傳入），Rig 只是為了讓組裝期安靜通過。
        /// </summary>
        private FootstepDetector CreateDetector()
        {
            var root = new GameObject("FootstepDetectorUnderTest");
            _created.Add(root);

            var model = new GameObject("Model");
            _created.Add(model);
            model.transform.SetParent(root.transform);
            model.AddComponent<FootIKRig>(); // [RequireComponent(Animator)] 會一併補上 Animator

            return root.AddComponent<FootstepDetector>();
        }

        private static FootIKPoseData Pose(float leftBottom, float rightBottom)
        {
            // FootBottomHeight 取 0，讓 FootPosition.y 直接等於腳底高度，測試意圖一目了然。
            return new FootIKPoseData
            {
                IsWarm = true,
                LeftFootPosition = new Vector3(0f, leftBottom, 0f),
                RightFootPosition = new Vector3(0f, rightBottom, 0f),
                LeftFootBottomHeight = 0f,
                RightFootBottomHeight = 0f,
            };
        }

        /// <summary>地面恆為 0 的觀測（平地）。</summary>
        private static FootIKGroundData FlatGround()
        {
            return new FootIKGroundData
            {
                IsWarm = true,
                Left = new FootGroundContact { HasGround = true, GroundY = 0f },
                Right = new FootGroundContact { HasGround = true, GroundY = 0f },
            };
        }

        private static List<PresentationEventData> Drive(
            FootstepDetector detector, IReadOnlyList<float> left, IReadOnlyList<float> right, bool justLanded = false)
        {
            FootIKGroundData ground = FlatGround();
            var results = new List<PresentationEventData>();
            for (int i = 0; i < left.Count; i++)
            {
                results.Add(detector.Detect(Pose(left[i], right[i]), ground, 0f, justLanded, Dt));
            }
            return results;
        }

        [Test]
        public void ColdPose_ProducesNothing()
        {
            FootstepDetector detector = CreateDetector();
            var cold = new FootIKPoseData(); // IsWarm = false

            PresentationEventData e = detector.Detect(cold, FlatGround(), 0f, false, Dt);

            Assert.IsFalse(e.LeftFootPlanted, "快照尚未被 Rig 寫過時不得消費全零數據");
            Assert.IsFalse(e.RightFootPlanted, "同上");
            Assert.DoesNotThrow(() => detector.Detect(null, FlatGround(), 0f, false, Dt),
                "pose 為 null（缺 Rig）必須安全靜默");
            Assert.DoesNotThrow(() => detector.Detect(Pose(0f, 0f), null, 0f, false, Dt),
                "ground 為 null（缺 FootIKController）必須安全退化，不得丟例外");
        }

        [Test]
        public void ColdGround_FallsBackAndStillDetects()
        {
            FootstepDetector detector = CreateDetector();
            List<float> stride = Stride(0.18f, 8);
            var flat = new List<float>();
            for (int i = 0; i < stride.Count; i++) flat.Add(0f);

            int fired = 0;
            for (int i = 0; i < stride.Count; i++)
            {
                // ground = null ⇒ 必須退回 rootPlaneY（此處 0）
                if (detector.Detect(Pose(stride[i], flat[i]), null, 0f, false, Dt).LeftFootPlanted) fired++;
            }
            Assert.AreEqual(1, fired, "缺少地面觀測時，平地上的偵測必須與有觀測時等價");
        }

        [Test]
        public void LeftAndRightFeet_AreTrackedIndependently()
        {
            FootstepDetector detector = CreateDetector();
            List<float> stride = Stride(0.18f, 8);
            var flat = new List<float>();
            for (int i = 0; i < stride.Count; i++) flat.Add(0f); // 右腳全程貼地不動

            List<PresentationEventData> events = Drive(detector, stride, flat);

            int leftCount = 0, rightCount = 0;
            foreach (PresentationEventData e in events)
            {
                if (e.LeftFootPlanted) leftCount++;
                if (e.RightFootPlanted) rightCount++;
            }

            Assert.AreEqual(1, leftCount, "左腳走完一次跨步應回報一次");
            Assert.AreEqual(0, rightCount, "右腳全程不動，不得被左腳帶出事件");
        }

        [Test]
        public void JustLanded_SuppressesFootstepEvent()
        {
            FootstepDetector detector = CreateDetector();
            List<float> stride = Stride(0.18f, 8);

            List<PresentationEventData> events = Drive(detector, stride, stride, justLanded: true);

            foreach (PresentationEventData e in events)
            {
                Assert.IsFalse(e.LeftFootPlanted, "落地帧不得額外回報腳步（Landing 是更高階的語意）");
                Assert.IsFalse(e.RightFootPlanted, "同上");
            }
        }

        /// <summary>
        /// 🔄（Stage 1）落地不只是「本帧不回報」，還必須把雙腳**收束回 Planted**。
        /// 否則殘留的上膛狀態會讓落地後緊接著多播一兩聲普通腳步。
        /// </summary>
        [Test]
        public void JustLanded_ForcesBothFeetBackToPlanted()
        {
            FootstepDetector detector = CreateDetector();
            FootIKGroundData ground = FlatGround();

            // 先讓雙腳抬起並進入快速下降（上膛），但還沒踩到。
            float[] descent = { 0.20f, 0.16f, 0.12f, 0.08f };
            foreach (float h in descent) detector.Detect(Pose(h, h), ground, 0f, false, Dt);

            // 落地帧
            detector.Detect(Pose(0.04f, 0.04f), ground, 0f, true, Dt);

            // 落地後貼地不動：若上膛狀態沒被收束，這幾帧會冒出腳步。
            int spurious = 0;
            for (int i = 0; i < 20; i++)
            {
                PresentationEventData e = detector.Detect(Pose(0f, 0f), ground, 0f, false, Dt);
                if (e.LeftFootPlanted || e.RightFootPlanted) spurious++;
            }

            Assert.AreEqual(0, spurious, "落地後必須重新抬腳才算下一步，不得殘留上膛狀態多播腳步");
        }

        [Test]
        public void Detector_HoldsNoBlackboardReference()
        {
            FieldInfo[] fields = typeof(FootstepDetector)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (FieldInfo f in fields)
            {
                Assert.AreNotEqual(typeof(PlayerRuntimeData), f.FieldType,
                    $"FootstepDetector.{f.Name} 持有黑板引用——黑板必須每帧由參數傳入（IPresentationEventSource 契約）");
            }
        }
    }
}
