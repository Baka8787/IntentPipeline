using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Presentation.IK;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// M3 Foot IK 純函數的確定性 EditMode 單元測試（不需場景／Animator／Physics）。
    /// 驗證 docs/02-dev-spec.md §3.5 的兩個決策核心：
    /// Q3 Pose Heuristic 權重曲線（ComputeFootWeight）與 Q2 骨盆補償（ComputePelvisOffset）。
    /// IK 的視覺效果（貼地／法線對齊／一幀延遲平滑）屬 Play 實測範疇，不在此涵蓋。
    /// </summary>
    public class FootIKTests
    {
        private const float Epsilon = 1e-5f;

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
