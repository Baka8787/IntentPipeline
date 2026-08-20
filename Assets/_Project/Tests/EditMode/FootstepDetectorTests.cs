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
    /// M3.x-B 落腳偵測的確定性 EditMode 測試。
    ///
    /// ⚠️ **全程以顯式 <c>deltaTime</c> 驅動，不依賴 <c>Time.deltaTime</c>**——它在 EditMode 不可控，
    /// 依賴它會讓這些測試的成敗取決於編輯器狀態而非演算法。這正是
    /// <c>FootPlantTracker.Advance</c> 與 <c>FootstepDetector.Detect</c> 收顯式時間步長的理由。
    ///
    /// 也不需要 Animator、Physics 或任何 raycast——偵測只吃腳底世界高度序列（輪 3 裁決）。
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
            ArmDescentSpeed = 0.35f,
            FireDescentSpeed = 0.05f,
            MinLiftExcursion = 0.03f,
        };

        /// <summary>把一段高度序列餵進 tracker，回傳偵測到的落腳次數。</summary>
        private static int CountPlants(IEnumerable<float> heights, FootstepDetectionSettings settings, float dt = Dt)
        {
            var tracker = new FootPlantTracker();
            int plants = 0;
            foreach (float h in heights)
            {
                if (tracker.Advance(h, dt, settings)) plants++;
            }
            return plants;
        }

        /// <summary>一個完整的抬腳→落腳循環：由 0 抬到 peak 再落回 0，各 steps 帧。</summary>
        private static List<float> Stride(float peak, int steps)
        {
            var heights = new List<float>();
            for (int i = 1; i <= steps; i++) heights.Add(peak * i / steps);      // 抬起
            for (int i = steps - 1; i >= 0; i--) heights.Add(peak * i / steps);  // 落下
            heights.Add(0f);                                                      // 停在地面（速度歸零 → 擊發）
            heights.Add(0f);
            return heights;
        }

        // =====================================================================
        // 核心偵測：下降 → 反轉 → plant
        // =====================================================================

        [Test]
        public void Descent_ThenReversal_ProducesExactlyOnePlant()
        {
            // 0.12m 的抬腳、6 帧落下 ⇒ 平均下降速度約 1.2 m/s，遠高於上膛門檻。
            Assert.AreEqual(1, CountPlants(Stride(0.12f, 6), Settings()),
                "一次完整的抬腳→落腳循環必須恰好產生一次落腳事件");
        }

        [Test]
        public void FirstSample_DoesNotProducePlant()
        {
            var tracker = new FootPlantTracker();
            Assert.IsFalse(tracker.Advance(0f, Dt, Settings()),
                "首次採樣沒有前一帧可比，算不出速度，不得產生事件");
        }

        [Test]
        public void RepeatedStrides_ProduceOnePlantEach()
        {
            var heights = new List<float>();
            for (int i = 0; i < 4; i++) heights.AddRange(Stride(0.12f, 6));

            Assert.AreEqual(4, CountPlants(heights, Settings()),
                "連續四步應產生四次事件——不得漏也不得重複（sprint 的高步頻不能被濾掉）");
        }

        // =====================================================================
        // 去抖：速度雙門檻打時間軸抖動、最小行程打空間假動作
        // =====================================================================

        [Test]
        public void IdleMicroOscillation_DoesNotTrigger()
        {
            // Idle 呼吸／重心轉移：±1mm、慢速來回。速度遠低於上膛門檻。
            var heights = new List<float>();
            for (int i = 0; i < 120; i++) heights.Add(0.001f * Mathf.Sin(i * 0.15f));

            Assert.AreEqual(0, CountPlants(heights, Settings()),
                "原地微幅晃動不得產生腳步聲——速度達不到上膛門檻");
        }

        [Test]
        public void VelocityHoveringNearZero_DoesNotChatter()
        {
            // 🎯 單門檻方案在這裡會連續誤觸發：速度反覆跨越 0。
            // 雙門檻（上膛 0.35 / 擊發 0.05）讓它永遠回不到上膛狀態。
            var heights = new List<float> { 0f };
            for (int i = 0; i < 200; i++) heights.Add((i % 2 == 0) ? 0.0004f : 0f);

            Assert.AreEqual(0, CountPlants(heights, Settings()),
                "速度在 0 附近抖動時不得反覆擊發——這正是 Schmitt trigger 要買的東西");
        }

        [Test]
        public void InsufficientExcursion_DoesNotTrigger()
        {
            // 先一次真實落腳建立基準，接著做「下降夠快、但抬得不夠高」的假動作。
            var heights = new List<float>();
            heights.AddRange(Stride(0.12f, 6));                 // 第一步：真實
            for (int i = 0; i < 6; i++)
            {
                heights.Add(0.01f);                              // 只抬 1cm（< 3cm 行程門檻）
                heights.Add(0f);                                 // 快速落回（速度足以上膛）
                heights.Add(0f);
            }

            Assert.AreEqual(1, CountPlants(heights, Settings()),
                "抬腳行程不足時不得算成新的一步——只有第一次真實落腳應該被計入");
        }

        [Test]
        public void SufficientExcursionAfterAPlant_TriggersAgain()
        {
            var heights = new List<float>();
            heights.AddRange(Stride(0.12f, 6));
            heights.AddRange(Stride(0.12f, 6));

            Assert.AreEqual(2, CountPlants(heights, Settings()),
                "行程足夠時必須能再次觸發——行程門檻不得把真實步伐一起擋掉");
        }

        [Test]
        public void FrozenTime_DoesNotAdvanceStateOrTrigger()
        {
            // deltaTime <= 0（暫停）：沒有時間流逝就沒有速度可算，比照 MotionDriver.IsTimeFrozen。
            var tracker = new FootPlantTracker();
            FootstepDetectionSettings s = Settings();

            foreach (float h in Stride(0.12f, 6))
            {
                Assert.IsFalse(tracker.Advance(h, 0f, s), "deltaTime = 0 時不得產生事件");
            }

            // 狀態也不該被推進：解除凍結後，同一段序列仍能正常偵測到一次落腳。
            int plants = 0;
            foreach (float h in Stride(0.12f, 6))
            {
                if (tracker.Advance(h, Dt, s)) plants++;
            }
            Assert.AreEqual(1, plants, "凍結期間不得污染跨帧狀態——解除後偵測必須照常運作");
        }

        // =====================================================================
        // Detector 層：雙腳獨立、Landing 抑制、抑制不破壞跨帧狀態
        // =====================================================================

        /// <summary>
        /// 建立「Root ＋ Model 子物件（含 FootIKRig）」的最小階層。
        /// ⚠️ **先組好子物件再掛 Detector**：Detector 的 Awake（若被派送）會 GetComponentInChildren&lt;FootIKRig&gt;，
        /// 找不到就 LogError，而 Unity Test Framework 把 LogError 當測試失敗。
        /// 本測試驅動的是 Detect(pose,…)（pose 由參數傳入），Rig 只是為了讓組裝期安靜通過。
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

        /// <summary>用同一段步伐序列驅動 detector，回傳每帧的事件。</summary>
        private static List<PresentationEventData> Drive(
            FootstepDetector detector, IReadOnlyList<float> left, IReadOnlyList<float> right, bool justLanded = false)
        {
            var results = new List<PresentationEventData>();
            for (int i = 0; i < left.Count; i++)
            {
                results.Add(detector.Detect(Pose(left[i], right[i]), justLanded, Dt));
            }
            return results;
        }

        [Test]
        public void ColdPose_ProducesNothing()
        {
            FootstepDetector detector = CreateDetector();
            var cold = new FootIKPoseData(); // IsWarm = false

            PresentationEventData e = detector.Detect(cold, false, Dt);

            Assert.IsFalse(e.LeftFootPlanted, "快照尚未被 Rig 寫過時不得消費全零數據");
            Assert.IsFalse(e.RightFootPlanted, "同上");
            Assert.DoesNotThrow(() => detector.Detect(null, false, Dt), "pose 為 null（缺 Rig）必須安全靜默");
        }

        [Test]
        public void LeftAndRightFeet_AreTrackedIndependently()
        {
            FootstepDetector detector = CreateDetector();
            List<float> stride = Stride(0.12f, 6);
            var flat = new List<float>();
            for (int i = 0; i < stride.Count; i++) flat.Add(0f); // 右腳全程貼地不動

            List<PresentationEventData> results = Drive(detector, stride, flat);

            int leftPlants = 0, rightPlants = 0;
            foreach (PresentationEventData e in results)
            {
                if (e.LeftFootPlanted) leftPlants++;
                if (e.RightFootPlanted) rightPlants++;
            }

            Assert.AreEqual(1, leftPlants, "左腳走了一步，應恰好產生一次左腳事件");
            Assert.AreEqual(0, rightPlants, "右腳全程未動，不得產生任何右腳事件");
        }

        [Test]
        public void JustLanded_SuppressesFootstepEvent()
        {
            FootstepDetector detector = CreateDetector();
            List<float> stride = Stride(0.12f, 6);

            List<PresentationEventData> results = Drive(detector, stride, stride, justLanded: true);

            foreach (PresentationEventData e in results)
            {
                Assert.IsFalse(e.LeftFootPlanted, "JustLanded 當帧必須抑制腳步事件——落地是更高階的語意");
                Assert.IsFalse(e.RightFootPlanted, "同上");
            }
        }

        [Test]
        public void LandingSuppression_DoesNotCorruptTemporalState()
        {
            // 🎯 抑制的是「報告」，不是「發生」。若抑制時把 tracker 一起回滾，
            // 落地後的第一步會因為行程基準錯亂而漏報或誤報。
            FootstepDetector suppressed = CreateDetector();
            FootstepDetector reference = CreateDetector();
            List<float> stride = Stride(0.12f, 6);

            // 第一步：一邊被 JustLanded 抑制，另一邊正常。
            Drive(suppressed, stride, stride, justLanded: true);
            Drive(reference, stride, stride, justLanded: false);

            // 第二步：兩邊都不抑制，行為必須完全一致。
            List<PresentationEventData> afterSuppressed = Drive(suppressed, stride, stride);
            List<PresentationEventData> afterReference = Drive(reference, stride, stride);

            int suppressedPlants = 0, referencePlants = 0;
            for (int i = 0; i < afterSuppressed.Count; i++)
            {
                if (afterSuppressed[i].LeftFootPlanted) suppressedPlants++;
                if (afterReference[i].LeftFootPlanted) referencePlants++;
            }

            Assert.AreEqual(1, referencePlants, "對照組：第二步應正常偵測到一次落腳");
            Assert.AreEqual(referencePlants, suppressedPlants,
                "被抑制過的偵測器，下一步的行為必須與從未抑制過的完全相同——抑制不得回滾跨帧狀態");
        }

        // =====================================================================
        // 契約：Detector 不得持有黑板引用
        // =====================================================================

        [Test]
        public void Detector_HoldsNoBlackboardReference()
        {
            // 黑板是每帧傳入的參數，不是可快取的狀態。持有它就等於打開了「偷偷寫回去」的門。
            FieldInfo[] fields = typeof(FootstepDetector)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (FieldInfo field in fields)
            {
                Assert.AreNotEqual(typeof(PlayerRuntimeData), field.FieldType,
                    $"FootstepDetector.{field.Name} 持有 PlayerRuntimeData 引用——" +
                    "事件來源只能透過參數讀黑板，且永遠不得寫入");
            }
        }
    }
}
