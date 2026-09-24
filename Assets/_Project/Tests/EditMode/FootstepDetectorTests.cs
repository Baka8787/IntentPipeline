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
    /// 也不需要 Animator、Physics 或任何 raycast——偵測只吃「腳底水平位置 ＋ 相對 pose 平面的高度」序列
    /// （🔄 2026-09-24：地面觀測不再參與節奏，見 `docs/44` §9.6）。
    ///
    /// 🔄（Stage 1）新增的測項直接編碼**磁碟上真實動畫量到的數值**（見 `docs/35` §2），
    /// 而不是憑感覺挑的假數字。因此它們同時是「不許再退回去」的回歸鎖。
    /// </summary>
    public class FootstepDetectorTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>
        /// 測試預設的「角色實際世界水平速度」。取 3.0（正常跑步）⇒ 水平速度閘**生效**，
        /// 與 Stage 1 的行為一致。貼牆（root 不前進）的情境由專屬測項單獨驗。
        /// </summary>
        private const float MovingRootSpeed = 3.0f;

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
            SwingGateMinRootSpeed = 0.5f,
        };

        // =====================================================================
        // 驅動輔助：高度序列（相對 pose 平面）→ tracker
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

        /// <summary>把高度序列（相對 pose 平面）餵進 tracker，回傳每帧的分類。</summary>
        private static List<FootstepKind> Feed(IEnumerable<float> heights, FootstepDetectionSettings settings,
            float dt = Dt, float horizontalStep = 0f, float rootHorizontalSpeed = MovingRootSpeed)
        {
            var tracker = new FootPlantTracker();
            var result = new List<FootstepKind>();
            float x = 0f;
            foreach (float h in heights)
            {
                result.Add(tracker.Advance(new Vector2(x, 0f), h, rootHorizontalSpeed, dt, settings, out _));
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
            FootstepKind k = tracker.Advance(Vector2.zero, 0f, MovingRootSpeed, Dt, Settings(), out FootstepSuppressReason r);
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
                Assert.AreEqual(FootstepKind.None, tracker.Advance(new Vector2(0f, 0f), h, MovingRootSpeed, 0f, s, out _),
                    "deltaTime = 0 時不得產生事件");
            }

            int fired = 0;
            foreach (float h in Stride(0.18f, 8))
            {
                if (tracker.Advance(new Vector2(0f, 0f), h, MovingRootSpeed, Dt, s, out _) != FootstepKind.None) fired++;
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
                FootstepKind k = tracker.Advance(new Vector2(x, 0f), profile[i].h, MovingRootSpeed, Dt, s, out _);
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
        /// 🔄 實測的停步腳部調整（`WalkFwdStop_RU` 右腳：行程 0.033 m、峰值下降速度 0.40 m/s）
        /// **必須完全靜音**。
        ///
        /// 這一條的期望值在本輪被**翻轉**了：原本判為 `Shuffle` 並播較輕的摩擦聲，
        /// 使用者實測後裁決「突兀且聽感很怪，收步／起步不該有聲音」⇒ 改為一律抑制。
        /// ⚠️ 抑制不等於沒發生——tracker 仍會 `MarkPlanted`，否則下一步的行程基準會錯亂
        /// （`SuppressedAdjustment_DoesNotCorruptNextStep` 守這件事）。
        /// </summary>
        [Test]
        public void StopAdjustment_IsSilent()
        {
            FootstepDetectionSettings s = Settings();
            var heights = new List<float> { 0f };
            for (int i = 1; i <= 16; i++) heights.Add(0.033f * i / 16f);   // 緩慢抬起
            for (int i = 4; i >= 0; i--) heights.Add(0.033f * i / 5f);     // 5 帧落下 ⇒ ≈0.40 m/s
            heights.Add(0f);
            heights.Add(0f);

            List<FootstepKind> kinds = Feed(heights, s);
            Assert.AreEqual(0, CountAny(kinds),
                "行程 0.033 m 低於 NormalStepLift(0.060) ⇒ 不構成完整跨步，必須完全靜音");
            Assert.AreEqual(0, Count(kinds, FootstepKind.Shuffle),
                "⛔ Shuffle 已移除——偵測器不得再回報這個分類");
        }

        /// <summary>
        /// 抑制的那一步仍然必須**收束跨帧狀態**，否則行程基準會累積下去，
        /// 讓後面一次真正的跨步被誤判成更大的行程、或反過來永遠打不到門檻。
        /// </summary>
        [Test]
        public void SuppressedAdjustment_DoesNotCorruptNextStep()
        {
            FootstepDetectionSettings s = Settings();

            var heights = new List<float> { 0f };
            // 先來兩次「行程不足」的小調整（會被抑制）
            for (int round = 0; round < 2; round++)
            {
                for (int i = 1; i <= 16; i++) heights.Add(0.033f * i / 16f);
                for (int i = 4; i >= 0; i--) heights.Add(0.033f * i / 5f);
                heights.Add(0f);
            }
            // 再來一次真正的跨步
            heights.AddRange(Stride(0.18f, 8));

            List<FootstepKind> kinds = Feed(heights, s);
            Assert.AreEqual(1, CountAny(kinds),
                "兩次被抑制的小調整之後，真正的跨步必須照常擊發，且只有它擊發");
        }

        // =====================================================================
        // ⭐ 回歸鎖：貼牆跑不得沒有聲音
        // =====================================================================

        /// <summary>
        /// **真實缺陷的回歸鎖（使用者回報）。**
        ///
        /// 水平速度閘的前提是「踩住的腳在世界空間靜止，因為身體會從它旁邊走過去」。
        /// 貼著牆跑時 `CharacterController` 被擋住、root 完全不前進，但動畫照跑
        /// ⇒ **踩住的腳反而以動畫速度（約 3.5 m/s）往後滑**，直接衝破 2.0 的門檻，
        /// 每一步都被判成 `StillSwinging` ⇒ **完全沒有腳步聲**。
        ///
        /// 此時擺盪腳與踩住腳的世界速度都接近動畫速度，**這個量裡已經沒有可分辨的資訊**，
        /// 唯一正確的處置是在 root 幾乎沒有位移時關掉這道閘。
        /// </summary>
        [Test]
        public void BlockedAgainstWall_StillProducesSteps()
        {
            FootstepDetectionSettings s = Settings();

            // 腳以 3.5 m/s 在世界空間滑動（遠超過 PlantHorizontalSpeedMax 2.0），
            // 但 root 完全沒有位移 ⇒ 角色被牆擋住。
            List<FootstepKind> blocked = Feed(Stride(0.18f, 8), s,
                dt: Dt, horizontalStep: 3.5f * Dt, rootHorizontalSpeed: 0f);

            Assert.AreEqual(1, CountAny(blocked),
                "貼牆跑（root 不前進）時仍然必須有腳步聲——這正是使用者回報的 bug");

            // 對照組：同樣的腳部滑動，但角色**確實在移動** ⇒ 閘生效，判為擺盪不擊發。
            List<FootstepKind> moving = Feed(Stride(0.18f, 8), s,
                dt: Dt, horizontalStep: 3.5f * Dt, rootHorizontalSpeed: MovingRootSpeed);

            Assert.AreEqual(0, CountAny(moving),
                "角色正常移動時，水平速度閘必須照常擋掉擺盪中低點——修 bug 不得把這道閘廢掉");
        }

        // 🔄（2026-09-24）原「Stage 1 回歸鎖 ③：樓梯」三條（上樓世界 Y 上升／級緣跳變／Root 平面跳變）
        //    測的是「以腳下地面為基準時，參考系跳變要擋掉」。節奏改以 pose 平面量測後，參考系不會再跳，
        //    那三種不連續偵測已從 tracker 移除；對應的保證改由 Detector 層的
        //    `Timing_IsInvariantToCommonVerticalTranslation`／`Timing_IgnoresGroundObservation` 鎖住。

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
                    if (tr.Advance(new Vector2(0f, 0f), h, MovingRootSpeed, Dt, s, out _) != FootstepKind.None) fired++;
                }
            }

            Assert.GreaterOrEqual(fired, 3,
                "腳一開始懸在半空時，落腳基準必須能向下修正——否則這隻腳永遠不會出聲");
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

        /// <param name="posePlaneY">與腳位同時擷取的 pose 平面；腳位 ＝ 平面 ＋ 動畫高度（模擬 root 的任意垂直位移）。</param>
        private static FootIKPoseData Pose(float leftBottom, float rightBottom, float posePlaneY = 0f)
        {
            // FootBottomHeight 取 0，讓 FootPosition.y 直接等於腳底高度，測試意圖一目了然。
            return new FootIKPoseData
            {
                IsWarm = true,
                LeftFootPosition = new Vector3(0f, posePlaneY + leftBottom, 0f),
                RightFootPosition = new Vector3(0f, posePlaneY + rightBottom, 0f),
                LeftFootBottomHeight = 0f,
                RightFootBottomHeight = 0f,
                PosePlaneY = posePlaneY,
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
                results.Add(detector.Detect(Pose(left[i], right[i]), ground, MovingRootSpeed, true, justLanded, Dt));
            }
            return results;
        }

        [Test]
        public void ColdPose_ProducesNothing()
        {
            FootstepDetector detector = CreateDetector();
            var cold = new FootIKPoseData(); // IsWarm = false

            PresentationEventData e = detector.Detect(cold, FlatGround(), MovingRootSpeed, true, false, Dt);

            Assert.IsFalse(e.LeftFootPlanted, "快照尚未被 Rig 寫過時不得消費全零數據");
            Assert.IsFalse(e.RightFootPlanted, "同上");
            Assert.DoesNotThrow(() => detector.Detect(null, FlatGround(), MovingRootSpeed, true, false, Dt),
                "pose 為 null（缺 Rig）必須安全靜默");
            Assert.DoesNotThrow(() => detector.Detect(Pose(0f, 0f), null, MovingRootSpeed, true, false, Dt),
                "ground 為 null（缺 FootIKController）必須安全退化，不得丟例外");
        }

        [Test]
        public void ColdGround_StillDetects()
        {
            FootstepDetector detector = CreateDetector();
            List<float> stride = Stride(0.18f, 8);
            var flat = new List<float>();
            for (int i = 0; i < stride.Count; i++) flat.Add(0f);

            int fired = 0;
            for (int i = 0; i < stride.Count; i++)
            {
                // ground = null ⇒ 節奏本來就不看地面觀測，照常偵測（材質退回 Unknown）
                if (detector.Detect(Pose(stride[i], flat[i]), null, MovingRootSpeed, true, false, Dt).LeftFootPlanted) fired++;
            }
            Assert.AreEqual(1, fired, "缺少地面觀測時，偵測必須與有觀測時等價");
        }

        // =====================================================================
        // ⭐ 2026-09-24 回歸鎖：節奏只看動畫（docs/44 §9.5–§9.6，使用者裁決）
        // =====================================================================

        /// <summary>左右交替的步態：每個週期左腳一步、右腳一步，另一腳踩著不動。</summary>
        private static void AlternatingGait(int cycles, out List<float> left, out List<float> right)
        {
            left = new List<float>();
            right = new List<float>();
            List<float> stride = Stride(0.18f, 8);
            for (int c = 0; c < cycles; c++)
            {
                foreach (float h in stride) { left.Add(h); right.Add(0f); }
                foreach (float h in stride) { left.Add(0f); right.Add(h); }
            }
        }

        private List<string> FireFrames(IReadOnlyList<float> left, IReadOnlyList<float> right,
            System.Func<int, float> posePlane, System.Func<int, FootIKGroundData> ground)
        {
            FootstepDetector detector = CreateDetector();
            var fired = new List<string>();
            for (int i = 0; i < left.Count; i++)
            {
                PresentationEventData e = detector.Detect(Pose(left[i], right[i], posePlane(i)), ground(i),
                    MovingRootSpeed, true, false, Dt);
                if (e.LeftFootPlanted) fired.Add(i + "L");
                if (e.RightFootPlanted) fired.Add(i + "R");
            }
            return fired;
        }

        /// <summary>
        /// root 的任何垂直位移都不得改變落腳節奏：pose 平面與腳位同時擷取，位移會相消。
        /// ① 常數高度（站在高處）② 平滑斜坡（每帧 −8 cm＝樓梯移動面上 Run 下樓的實測最大值）
        /// ③ 單帧跳一級 0.194 m（未套用移動面的樓梯，step offset）。
        /// ⭐ 舊版以「腳底 − 腳下地面」量，②③都會讓踩住的腳看起來在移動（`docs/44` §9.5）。
        /// </summary>
        [Test]
        public void Timing_IsInvariantToCommonVerticalTranslation()
        {
            AlternatingGait(3, out List<float> left, out List<float> right);
            List<string> baseline = FireFrames(left, right, _ => 0f, _ => FlatGround());
            Assert.AreEqual(6, baseline.Count, "前提：三個週期、左右各一步 ⇒ 6 次");

            CollectionAssert.AreEqual(baseline, FireFrames(left, right, _ => 3.7f, _ => FlatGround()),
                "常數高度");
            CollectionAssert.AreEqual(baseline, FireFrames(left, right, i => 3f - 0.08f * i, _ => FlatGround()),
                "平滑斜坡（Run 下樓，每帧 −8 cm）");
            CollectionAssert.AreEqual(baseline, FireFrames(left, right, i => 0.194f * (i / 7), _ => FlatGround()),
                "每 7 帧跳一級（step offset）");
        }

        /// <summary>
        /// 地面觀測（踏面）只決定材質，不重新定義節奏：腳下踏面比 pose 平面低 0～0.4 m、
        /// 每 2～3 帧換一階、觀測時有時無——擊發帧必須與平地完全相同。
        /// </summary>
        [Test]
        public void Timing_IgnoresGroundObservation()
        {
            AlternatingGait(3, out List<float> left, out List<float> right);
            List<string> baseline = FireFrames(left, right, _ => 0f, _ => FlatGround());

            FootIKGroundData Stairs(int i) => new FootIKGroundData
            {
                IsWarm = true,
                Left = new FootGroundContact { HasGround = i % 5 != 0, GroundY = -0.194f * ((i / 2) % 3) },
                Right = new FootGroundContact { HasGround = i % 7 != 0, GroundY = -0.194f * ((i / 3) % 2) - 0.02f },
            };

            CollectionAssert.AreEqual(baseline, FireFrames(left, right, _ => 0f, Stairs),
                "踏面高度與有無觀測都不得影響擊發帧");
        }

        /// <summary>
        /// 🐛 **攀爬途中有腳步聲**（2026-09-17 Play 回報）的回歸測試。
        ///
        /// 重現的是機制本身，不是某一段攀爬動畫：
        /// ledge climb 全程 <c>isGrounded == false</c>（見 <c>MotionDriver.ExecuteTraversalWarpedMovement</c>
        /// 的原碼說明）⇒ Foot IK 閘門關閉 ⇒ 沒有地面觀測 ⇒ 高度基準退回 root 平面
        /// ⇒ 動畫的腳在「離地 0」附近進出接觸帶就被判成落腳。
        ///
        /// ⚠️ 刻意用**與平地完全相同的 stride 資料**——差別只有 <c>isGrounded</c>。
        /// 這樣才證明是「空中不該報」，而不是「這段資料本來就不構成一步」。
        /// </summary>
        [Test]
        public void Airborne_WithoutGroundObservation_ProducesNothing()
        {
            List<float> stride = Stride(0.18f, 8);
            var flat = new List<float>();
            for (int i = 0; i < stride.Count; i++) flat.Add(0f);

            // ① 先確認這段資料在地面上**確實會**擊發——否則本測試等於空轉。
            FootstepDetector grounded = CreateDetector();
            int groundedFired = 0;
            for (int i = 0; i < stride.Count; i++)
                if (grounded.Detect(Pose(stride[i], flat[i]), null, MovingRootSpeed, true, false, Dt)
                        .LeftFootPlanted) groundedFired++;
            Assert.AreEqual(1, groundedFired, "前提斷言：同一段資料在地面上必須擊發一次");

            // ② 同一段資料，只把 isGrounded 改成 false ⇒ 一次都不得擊發。
            FootstepDetector airborne = CreateDetector();
            int airborneFired = 0;
            for (int i = 0; i < stride.Count; i++)
                if (airborne.Detect(Pose(stride[i], flat[i]), null, MovingRootSpeed, false, false, Dt)
                        .LeftFootPlanted) airborneFired++;

            Assert.AreEqual(0, airborneFired,
                "離地且沒有地面觀測時，root 平面不是地面 ⇒ 不得回報腳步（攀爬／跳躍／墜落）");
        }

        /// <summary>
        /// ⭐ 上面那道閘門**只在沒有地面觀測時**生效。
        /// 真的有地面觀測（例如未來 Foot IK 在離地時仍開著）就必須照常回報——
        /// 否則這道修法會變成「離地一律靜音」，那是另一條規則。
        /// </summary>
        [Test]
        public void Airborne_WithGroundObservation_StillReports()
        {
            FootstepDetector detector = CreateDetector();
            List<float> stride = Stride(0.18f, 8);
            var flat = new List<float>();
            for (int i = 0; i < stride.Count; i++) flat.Add(0f);

            int fired = 0;
            for (int i = 0; i < stride.Count; i++)
                if (detector.Detect(Pose(stride[i], flat[i]), FlatGround(), MovingRootSpeed, false, false, Dt)
                        .LeftFootPlanted) fired++;

            Assert.AreEqual(1, fired, "有真實地面觀測時，isGrounded 不得否決回報");
        }

        /// <summary>
        /// ⚠️ 抑制必須發生在 tracker **推進之後**（與 justLanded 同一條規則）。
        /// 否則空中那段的跨帧狀態會凍結，落地後第一步的行程基準與上膛狀態都會錯亂。
        /// </summary>
        [Test]
        public void AirborneSuppression_DoesNotCorruptTheNextGroundedStep()
        {
            FootstepDetector detector = CreateDetector();
            var flat = new List<float>();
            List<float> stride = Stride(0.18f, 8);
            for (int i = 0; i < stride.Count; i++) flat.Add(0f);

            // 空中走完一整個 stride（全部被抑制）
            for (int i = 0; i < stride.Count; i++)
                detector.Detect(Pose(stride[i], flat[i]), null, MovingRootSpeed, false, false, Dt);

            // 回到地面後，同樣的一個 stride 必須**正常擊發一次**
            int fired = 0;
            for (int i = 0; i < stride.Count; i++)
                if (detector.Detect(Pose(stride[i], flat[i]), null, MovingRootSpeed, true, false, Dt)
                        .LeftFootPlanted) fired++;

            Assert.AreEqual(1, fired, "空中被抑制的那段不得讓落地後的第一步失準");
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
            foreach (float h in descent) detector.Detect(Pose(h, h), ground, MovingRootSpeed, true, false, Dt);

            // 落地帧
            detector.Detect(Pose(0.04f, 0.04f), ground, MovingRootSpeed, true, true, Dt);

            // 落地後貼地不動：若上膛狀態沒被收束，這幾帧會冒出腳步。
            int spurious = 0;
            for (int i = 0; i < 20; i++)
            {
                PresentationEventData e = detector.Detect(Pose(0f, 0f), ground, MovingRootSpeed, true, false, Dt);
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
