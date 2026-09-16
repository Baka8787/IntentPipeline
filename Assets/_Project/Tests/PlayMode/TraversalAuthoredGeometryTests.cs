using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Environment;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🔒 **Traversal 必須使用 authored capsule geometry，不得讀執行期被偏移的 `center`。**
    ///
    /// <para><b>這組測試的由來（真實 bug，不是防禦性程式碼）</b></para>
    /// 2026-09-15：玩家開啟 dynamic capsule offset 後「按著 W 完全無法起攀」。
    /// 當時的歸因是「站位退後 0.12 m 吃掉 entry 合法帶」——**那個歸因是錯的**
    /// （`docs/27` §10 第 0 條）：Climb 的 entry 合法帶是 `[0.06, 0.81] m`，
    /// 退後後的 0.34 不但在帶內，還比 0.22 更接近理想的 0.462。
    ///
    /// 真正的機制是 <c>TraversalProbe</c> 沿 corridor 取樣時用的是**當下（已偏移的）**
    /// <c>characterController.center</c>：整條檢查膠囊被往前推一個 offset ⇒
    /// <c>IsCapsuleSegmentClear</c> 在 entry→clearance 段撞牆 ⇒ <c>CorridorBlocked</c>，
    /// 而那是 <c>TraversalEntryPolicy.Evaluate</c> 的**第一個** return，距離根本沒被評估到。
    ///
    /// <para><b>當時的探針怎麼證明的（本組測試就是它的正式化）</b></para>
    /// 三個情境：①基準 ②偏移開啟 ③**對照組：站位退後同樣的距離，但 center 不偏移**。
    /// ③ 通過而 ② 失敗 ⇒ **站位不是原因，`center` 才是**。
    /// 三個半徑（0.12／0.22／0.32）行為完全一致 ⇒ 與 radius 無關。
    ///
    /// ⛔ **不要把這組刪掉**：它守的不變量是「traversal 的環境查詢問的是角色的**基準身體**，
    /// 不是角色現在被動畫推到哪」。任何第二個寫 <c>center</c> 的人都會再踩到。
    /// </summary>
    public sealed class TraversalAuthoredGeometryTests
    {
        private const float Height = 1.826619f;
        private const float CenterY = 0.9134095f;
        private const float SkinWidth = 0.03f;
        private const float StepOffset = 0.3f;
        private const float WallFrontZ = 1.0f;

        /// <summary>X Bot 的 `MaxOffset`。取一個「會壞」的量級，不是隨便一個小數。</summary>
        private const float RuntimeOffset = 0.18f;

        private static readonly TraversalProbeSettings ProbeSettings = new TraversalProbeSettings(
            obstacleMask: 1 << 0,
            scanStep: 0.1f,
            scanCeiling: 2.7f,
            forwardScanDistance: 1.25f,
            vault1mMaxDepth: 0.6f,
            climb1mMaxHeight: 1.25f,
            climb2mMaxHeight: 2.1f,
            heightHysteresis: 0.1f,
            depthHysteresis: 0.08f,
            probeMinSpeed: 0.1f,
            destinationClearanceMargin: 0.05f);

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        /// <summary>
        /// 🔴 **TR1 — runtime center 被偏移時，traversal candidate 必須與 authored baseline 完全一致。**
        ///
        /// 三個半徑各跑三個情境；斷言的核心是：
        /// <b>offset-on 的結果必須等於 baseline，而不是等於 CorridorBlocked。</b>
        /// </summary>
        [UnityTest]
        public IEnumerator TR1_RuntimeCenterOffset_DoesNotChangeTraversalCandidate(
            [Values(0.12f, 0.22f, 0.32f)] float radius)
        {
            // ① baseline：站在「沒有偏移時 CharacterController 會停下的位置」。
            yield return Probe(radius, offset: 0f, extraStandoff: 0f);
            TraversalCandidate baseline = _lastCandidate;
            Assert.AreNotEqual(TraversalKind.None, baseline.Kind,
                $"radius={radius}：治具本身有問題——基準情境就沒有產生 candidate，" +
                "後面的比較不成立");

            // ② offset-on：站位自然退後，且 center 被水平偏移（＝ dynamic capsule 生效時的真實狀態）。
            yield return Probe(radius, offset: RuntimeOffset, extraStandoff: 0f);
            TraversalCandidate offsetOn = _lastCandidate;

            // ③ control：站位退後同樣的距離，但 center **不**偏移。
            yield return Probe(radius, offset: 0f, extraStandoff: RuntimeOffset);
            TraversalCandidate control = _lastCandidate;

            Assert.AreEqual(baseline.Kind, control.Kind,
                $"radius={radius}：對照組（只退後、不偏移 center）本來就該通過——" +
                "它證明『站位』不是失敗原因");

            Assert.AreEqual(baseline.Kind, offsetOn.Kind,
                $"radius={radius}：🔴 runtime center 偏移改變了 traversal 結果。" +
                $"baseline={baseline.Kind}／offset-on={offsetOn.Kind}（{offsetOn.RejectReason}／" +
                $"corridor {offsetOn.Corridor.RejectReason}）。" +
                "⇒ TraversalProbe 又開始讀 live center 了；它必須讀 authored geometry。");

            Assert.AreEqual(TraversalCorridorRejectReason.None, offsetOn.Corridor.RejectReason,
                $"radius={radius}：corridor 檢查膠囊被偏移推走了");
        }

        /// <summary>
        /// **TR2 — authored geometry 是不變量：偏移之後再問，仍然回傳 authored 值。**
        /// TR1 驗行為，這條驗**欄位本身**——兩者一起才擋得住「快照時機不對」這一類錯誤。
        /// </summary>
        [UnityTest]
        public IEnumerator TR2_AuthoredGeometry_NeverFollowsRuntimeCenter()
        {
            CreateGround();
            CreateObstacle(height: 2f, depth: 1f);
            TraversalProbe probe = CreateProbe(0.32f, offsetZ: 0f, rootZ: 0.5f);
            Physics.SyncTransforms();
            yield return null;

            var controller = probe.GetComponent<CharacterController>();
            Vector3 authored = probe.AuthoredCenter;
            float authoredRadius = probe.AuthoredRadius;
            float authoredHeight = probe.AuthoredHeight;

            // 模擬 dynamic capsule offset 與 traversal collision profile 兩種執行期寫入。
            controller.center = authored + new Vector3(0.05f, 0f, RuntimeOffset);
            controller.radius = authoredRadius * 0.5f;
            controller.height = authoredHeight * 0.6f;
            yield return null;

            Assert.AreEqual(authored, probe.AuthoredCenter, "🔒 AuthoredCenter 不得跟著 runtime center 跑");
            Assert.AreEqual(authoredRadius, probe.AuthoredRadius, "🔒 AuthoredRadius 同理");
            Assert.AreEqual(authoredHeight, probe.AuthoredHeight, "🔒 AuthoredHeight 同理");
        }

        // =====================================================================
        // 治具
        // =====================================================================

        private TraversalCandidate _lastCandidate;

        private IEnumerator Probe(float radius, float offset, float extraStandoff)
        {
            CreateGround();
            CreateObstacle(height: 2f, depth: 1f);

            // 站位 ＝ CharacterController 實際會停下的位置。
            float rootZ = WallFrontZ - (radius + SkinWidth + offset + extraStandoff);
            TraversalProbe probe = CreateProbe(radius, offset, rootZ);

            Physics.SyncTransforms();
            yield return null;

            probe.Tick(new PlayerRuntimeData
            {
                IsGrounded = true,
                MoveSpeed = 1f,
                MoveDirection = Vector3.forward,
            });

            _lastCandidate = probe.Candidate;
            DestroyCreated();
        }

        private TraversalProbe CreateProbe(float radius, float offsetZ, float rootZ)
        {
            var host = new GameObject("TraversalAuthoredGeometry-Host");
            _created.Add(host);
            host.layer = 2;   // 不在 obstacleMask 內 ⇒ 不會掃到自己
            host.transform.position = new Vector3(0f, 0f, rootZ);

            var controller = host.AddComponent<CharacterController>();
            controller.height = Height;
            controller.radius = radius;
            controller.skinWidth = SkinWidth;
            controller.stepOffset = StepOffset;
            controller.slopeLimit = 45f;
            // ⚠️ authored center 必須在 probe 的 Awake **之前**就位——Awake 會快照它。
            controller.center = new Vector3(0f, CenterY, 0f);

            TraversalProbe probe = host.AddComponent<TraversalProbe>();   // ← 此時 Awake 執行、快照 authored

            // 這一行才是被測的東西：dynamic capsule offset 在執行期把 center 推走。
            if (offsetZ != 0f) controller.center = new Vector3(0f, CenterY, offsetZ);

            SetPrivateField(probe, "settings", ProbeSettings);
            SetPrivateField(probe, "drawTraversalRuntimeLines", false);
            SetPrivateField(probe, "drawTraversalSceneGizmos", false);
            probe.SetDerivedRangeRequirements(1.402f, 0.6f);
            return probe;
        }

        private void CreateGround()
        {
            GameObject ground = CreateCube("Ground");
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
        }

        private void CreateObstacle(float height, float depth)
        {
            GameObject obstacle = CreateCube("Obstacle");
            obstacle.transform.position = new Vector3(0f, height * 0.5f, WallFrontZ + depth * 0.5f);
            obstacle.transform.localScale = new Vector3(4f, height, depth);
        }

        private GameObject CreateCube(string name)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.layer = 0;
            _created.Add(cube);
            return cube;
        }

        private void DestroyCreated()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到測試設定欄位 {fieldName}");
            field.SetValue(target, value);
        }
    }
}
