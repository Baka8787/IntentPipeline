using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// M3 Foot IK 純函數的確定性 EditMode 單元測試（不需場景／Animator／Physics）。
    /// 驗證 docs/05-foot-ik.md §3.5 的決策核心：Q3 Pose Heuristic 權重曲線、Q2 骨盆補償，
    /// 以及 L1 v4 的 ankle 泰勒斯修正與 Heel/Toe 真實端點戳穿殘差。
    /// IK 的視覺效果（貼地／法線對齊／一幀延遲平滑）屬 Play 實測範疇，不在此涵蓋。
    /// </summary>
    public class FootIKTests
    {
        private const float Epsilon = 1e-5f;

        // === L1 v3：踝關節角度夾限 ===

        [Test]
        public void ClampGroundNormal_WithinLimit_ReturnsOriginalExactly()
        {
            Vector3 normal = Quaternion.AngleAxis(10f, Vector3.right) * Vector3.up;

            Vector3 actual = FootIKController.ClampGroundNormal(normal, 15f);

            Assert.AreEqual(normal, actual,
                "夾限內必須原樣回傳，確保坡度未超限時與 v2 完全相同。");
        }

        [Test]
        public void ClampGroundNormal_AboveLimit_ClampsToMaximumAngle()
        {
            Vector3 normal = Quaternion.AngleAxis(40f, Vector3.right) * Vector3.up;

            Vector3 actual = FootIKController.ClampGroundNormal(normal, 15f);

            Assert.AreEqual(15f, Vector3.Angle(Vector3.up, actual), Epsilon,
                "超限法線應連續停在踝關節最大對齊角度。");
            Assert.Greater(Vector3.Dot(actual, normal), Vector3.Dot(Vector3.up, normal),
                "夾限後仍應朝命中法線方向旋轉，而不是回到錯誤方向。");
        }

        [Test]
        public void ClampGroundNormal_AtLimit_ReturnsOriginalExactly()
        {
            Vector3 normal = Quaternion.AngleAxis(15f, Vector3.forward) * Vector3.up;

            Vector3 actual = FootIKController.ClampGroundNormal(normal, 15f);

            Assert.AreEqual(normal, actual,
                "閾值本身不應切換路徑或引入數值擾動。");
        }

        [Test]
        public void ClampGroundNormal_ZeroNormal_ReturnsWorldUp()
        {
            Vector3 actual = FootIKController.ClampGroundNormal(Vector3.zero, 15f);

            Assert.AreEqual(Vector3.up, actual, "退化法線必須安全回退到世界 up。");
        }

        [Test]
        public void ClampGroundNormal_FullAlignment_ReturnsOriginalExactly()
        {
            Vector3 normal = Quaternion.AngleAxis(120f, Vector3.right) * Vector3.up;

            Vector3 actual = FootIKController.ClampGroundNormal(normal, 180f);

            Assert.AreEqual(normal, actual, "180° 必須成為逐值等同 v2 的 A/B 對照模式。");
        }

        [Test]
        public void ClampGroundNormal_NegativeLimit_ClampsSafelyToWorldUp()
        {
            Vector3 normal = Quaternion.AngleAxis(30f, Vector3.forward) * Vector3.up;

            Vector3 actual = FootIKController.ClampGroundNormal(normal, -10f);

            Assert.AreEqual(Vector3.up, actual,
                "程式呼叫繞過 Inspector Range 時，負上限不得讓 RotateTowards 反向旋轉。");
        }

        // === L1 v4（ankle 泰勒斯修正＋Heel/Toe 真實端點戳穿殘差）===

        [Test]
        public void AnkleTarget_FlatGround_DegeneratesToNormalOffset()
        {
            Vector3 rayStart = new Vector3(2f, 1f, 3f);
            Vector3 hitPoint = new Vector3(2f, 0f, 3f);
            Vector3 expected = hitPoint + Vector3.up * 0.1f;

            Vector3 actual = FootIKController.ComputeAnkleTarget(rayStart, hitPoint, Vector3.up, 0.1f);

            Assert.AreEqual(expected.x, actual.x, Epsilon,
                "平地退化路徑的 X 必須等同既有法線位移式");
            Assert.AreEqual(expected.y, actual.y, Epsilon,
                "平地退化路徑的 Y 必須等同既有法線位移式");
            Assert.AreEqual(expected.z, actual.z, Epsilon,
                "平地退化路徑的 Z 必須等同既有法線位移式");
        }

        [Test]
        public void AnkleTarget_Slope_KeepsHorizontalComponentsOnRayLine()
        {
            Vector3 rayStart = new Vector3(2f, 2f, 3f);
            Vector3 hitPoint = new Vector3(2f, 0f, 3f);
            Vector3 normal = new Vector3(-0.5f, Mathf.Sqrt(3f) * 0.5f, 0f);

            Vector3 actual = FootIKController.ComputeAnkleTarget(rayStart, hitPoint, normal, 0.1f);

            Assert.AreEqual(rayStart.x, actual.x, Epsilon, "斜坡修正後 ankle X 必須留在原垂直 ray 上");
            Assert.AreEqual(rayStart.z, actual.z, Epsilon, "斜坡修正後 ankle Z 必須留在原垂直 ray 上");
            Assert.AreEqual(0.1f / normal.y, actual.y - hitPoint.y, Epsilon,
                "垂直高度應是保持法線腳底間隙所需的 footBottomHeight / normal.y");
        }

        [Test]
        public void PenetrationLift_FlatGroundToeUp_LiftsPenetratingHeel()
        {
            const float footBottomHeight = 0.1f;
            const float heelOffset = 0.1f;
            Quaternion finalRotation = Quaternion.AngleAxis(-20f, Vector3.right);
            Vector3 ankleTarget = Vector3.up * footBottomHeight;
            Vector3 worldHeel = ankleTarget + finalRotation *
                (Vector3.forward * -heelOffset - Vector3.up * footBottomHeight);
            Vector3 worldToe = ankleTarget + finalRotation *
                (Vector3.forward * 0.15f - Vector3.up * footBottomHeight);

            float lift = FootIKController.ComputePenetrationLift(
                worldHeel, new Vector3(worldHeel.x, 0f, worldHeel.z),
                worldToe, new Vector3(worldToe.x, 0f, worldToe.z));

            float expected = heelOffset * Mathf.Sin(20f * Mathf.Deg2Rad) -
                footBottomHeight * (1f - Mathf.Cos(20f * Mathf.Deg2Rad));
            Assert.AreEqual(expected, lift, Epsilon,
                "toe-up 必須保留最終旋轉，同時只抬起真實腳跟穿地的約 2.8cm");
        }

        [Test]
        public void PenetrationLift_SlopeEndpointsAboveGround_ReturnsZero()
        {
            float lift = FootIKController.ComputePenetrationLift(
                new Vector3(0f, 0.12f, -0.1f), new Vector3(0f, 0.1f, -0.1f),
                new Vector3(0f, 0.28f, 0.15f), new Vector3(0f, 0.25f, 0.15f));

            Assert.AreEqual(0f, lift, Epsilon,
                "兩端點都在斜坡地面上方時不得下壓腳踝");
        }

        [Test]
        public void PenetrationLift_ToeDeeperThanHeel_UsesToeArgmax()
        {
            float lift = FootIKController.ComputePenetrationLift(
                new Vector3(0f, 0.08f, -0.1f), new Vector3(0f, 0.1f, -0.1f),
                new Vector3(0f, 0.19f, 0.15f), new Vector3(0f, 0.25f, 0.15f));

            Assert.AreEqual(0.06f, lift, Epsilon,
                "Heel 穿 2cm、Toe 穿 6cm 時必須由 Toe argmax 決定抬升量");
        }

        // === ComputeFootWeight（Q3：腳骨高度 → 貼地權重）===

        [Test]
        public void FootWeight_BelowGroundedMin_ReturnsOne()
        {
            Assert.AreEqual(1f, FootIKController.ComputeFootWeight(0.05f, 0.1f, 0.3f), Epsilon,
                "腳骨低於踩地閾值時，IK 應全接管（權重 1）");
        }

        [Test]
        public void FootWeight_AboveGroundedMax_ReturnsZero()
        {
            Assert.AreEqual(0f, FootIKController.ComputeFootWeight(0.4f, 0.1f, 0.3f), Epsilon,
                "腳骨高於抬腳閾值時，動畫應全接管（權重 0）");
        }

        [Test]
        public void FootWeight_AtMidpoint_ReturnsHalf_LinearFalloff()
        {
            Assert.AreEqual(0.5f, FootIKController.ComputeFootWeight(0.2f, 0.1f, 0.3f), Epsilon,
                "閾值帶中點應為 0.5——權重在 min~max 之間必須線性遞減");
        }

        [Test]
        public void FootWeight_DegenerateRange_FallsBackToHardCut()
        {
            // min >= max 的異常配置：退化為以 groundedMin 硬切，不拋例外（防呆契約）
            Assert.AreEqual(1f, FootIKController.ComputeFootWeight(0.1f, 0.1f, 0.1f), Epsilon,
                "退化配置下，高度 ≤ min 應回 1");
            Assert.AreEqual(0f, FootIKController.ComputeFootWeight(0.11f, 0.1f, 0.1f), Epsilon,
                "退化配置下，高度 > min 應回 0");
        }

        // === ComputePelvisOffset（Q2：雙腳地面高差 → 骨盆下沉）===

        [Test]
        public void PelvisOffset_FlatGround_ReturnsZero()
        {
            Assert.AreEqual(0f, FootIKController.ComputePelvisOffset(1f, 1f, 1f, 0.35f), Epsilon,
                "平地（雙腳命中點＝Root 平面）不應有骨盆補償");
        }

        [Test]
        public void PelvisOffset_LowerFoot_ReturnsNegativeDelta()
        {
            Assert.AreEqual(-0.2f, FootIKController.ComputePelvisOffset(0.8f, 1.0f, 1f, 0.35f), Epsilon,
                "低腳在 Root 平面下 0.2m 時，骨盆應下沉 0.2m（取較低腳）");
        }

        [Test]
        public void PelvisOffset_ExceedsMaxDrop_IsClamped()
        {
            Assert.AreEqual(-0.35f, FootIKController.ComputePelvisOffset(0.4f, 1.0f, 1f, 0.35f), Epsilon,
                "高差超過 maxPelvisDrop 時應夾在最大下沉量（低腳搆不到屬設計極限）");
        }

        [Test]
        public void PelvisOffset_GroundAboveRoot_ClampsToZero()
        {
            Assert.AreEqual(0f, FootIKController.ComputePelvisOffset(1.1f, 1.2f, 1f, 0.35f), Epsilon,
                "地面高於 Root 平面（上坡側）不得上抬——骨盆只下沉，抬升交給 CharacterController 地面跟隨");
        }

        // 註（M3.5 最終形）：M3.2 的 ComputeHeightFade／ClampReach 純函數已隨實驗機制移除，
        // 對應 7 條測試一併退場（總數回 42）——實驗結論與復刻指引見 changelog v0.18.2~v0.18.6。

        // =====================================================================
        // 🆕（M3.x-A）Pose 管道的擁有權：lifetime owner ＝ 唯一 Writer
        // =====================================================================

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

        private T CreateGameObject<T>(string name, Transform parent = null) where T : Component
        {
            var go = new GameObject(name);
            _created.Add(go);
            if (parent != null) go.transform.SetParent(parent);
            return go.AddComponent<T>();
        }

        [Test]
        public void PoseData_IsAvailableImmediately_WithoutAnyAwakeOrBind()
        {
            // 🎯 這條守的是「不需要關心 Awake 順序」這個結構保證本身。
            // Rig 以**欄位初始式**持有快照——初始式在元件建構時執行、早於所有 Awake，
            // 所以任何讀取方在任何時機取得的都是同一份有效實例。
            // 若哪天有人把它改成在 Awake 裡 new，這條會紅——而那個改動會讓
            // 「誰的 Awake 先跑」重新變成正確性條件。
            FootIKRig rig = CreateGameObject<FootIKRig>("ModelUnderTest");

            Assert.IsNotNull(rig.PoseData,
                "FootIKRig.PoseData 必須在元件一存在時就有效，不得依賴 Awake 或 Bind 先被呼叫");
        }

        [Test]
        public void PoseData_IsTheSameInstance_SharedByOwnerAndReader()
        {
            // 🎯 這條守的是「單寫多讀共享同一份實例」。
            // 若 Reader 自己 new 了一份（M3.x-A 之前 FootIKController 就是這樣做的），
            // 它會靜默讀到永遠不會被寫入的空快照——不報錯、只是沒反應。
            var root = new GameObject("RootUnderTest");
            _created.Add(root);

            // 先組好 Model 子物件再掛 Controller：確保 Controller 的 Awake（若被派送）
            // 一定找得到 Rig，不會因為 LogError 讓測試失敗。
            FootIKRig rig = CreateGameObject<FootIKRig>("ModelUnderTest", root.transform);
            var controller = root.AddComponent<FootIKController>();

            // 顯式驅動組裝期：EditMode 下 Unity 是否派送生命週期訊息不在本測試掌控範圍內，
            // 依賴它會讓成敗取決於引擎行為（同 GamePauseControllerTests 的作法）。
            // 重複執行是安全的：重新 new Target、重新 Bind、重新讀同一份 PoseData。
            MethodInfo awake = typeof(FootIKController)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(awake, "找不到 FootIKController.Awake（方法名稱可能已變更）");
            awake.Invoke(controller, null);

            Assert.AreSame(rig.PoseData, controller.PoseData,
                "Reader 必須共享 owner 的那一份實例；各自 new 會讀到永不更新的空快照");
        }
    }
}
