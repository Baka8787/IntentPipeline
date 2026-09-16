using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Project.Presentation.Motion;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🆕（2026-09-17，正本 `docs/38`）**Valid Standing Support 的行為測試。**
    ///
    /// 把 spike 量到的數字固化成斷言。守的是三件事：
    /// ①平台邊緣的懸空量真的被壓下來 ②樓梯不誤判 ③斜坡不誤判。
    ///
    /// ⚠️ **刻意不斷言精確數值**——那會讓測試在 PhysX 版本或 fixedDeltaTime 變動時脆裂。
    /// 斷言的是**關係**：「比碰撞判定嚴格」「樓梯／斜坡上不得否決」。
    ///
    /// ⭐ 為什麼測 <see cref="StandingSupportProbe"/> 而不是整條 MotionDriver：
    /// 否決層的正確性就是這個純函式的正確性；把 MotionDriver 拉進來只會引入
    /// prefab 接線與狀態機時序這些與本問題無關的變因。
    /// MotionDriver 那一行（`isGrounded && HasValidStandingSupportThisFrame()`）
    /// 是接線，由 `PrefabWiringTests` 與人工 Play 覆蓋。
    /// </summary>
    public class StandingSupportTests
    {
        const float Radius = 0.32f;
        const float Height = 1.8266189f;
        const float CenterY = 0.9134095f;
        const float SupportRadius = 0.14f;   // 出貨值
        const float SearchDepth = 0.30f;     // 出貨值
        const float ProbeLift = 0.05f;
        const float SlopeLimit = 45f;
        const float ToeExtent = 0.114f;      // 實測：ToeBase 距 root 水平距離

        GameObject _platform, _stairs, _slope, _character;
        CharacterController _cc;
        RaycastHit[] _buffer;

        [SetUp]
        public void SetUp()
        {
            _buffer = new RaycastHit[StandingSupportProbe.BufferSize];

            _platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _platform.transform.position = new Vector3(0f, -0.5f, -5f);
            _platform.transform.localScale = new Vector3(10f, 1f, 10f);

            _stairs = new GameObject("Stairs");
            _stairs.transform.position = new Vector3(20f, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.transform.SetParent(_stairs.transform);
                step.transform.localPosition = new Vector3(0f, 0.18f * i + 0.09f, 0.30f * i);
                step.transform.localScale = new Vector3(4f, 0.18f, 0.30f);
            }

            _slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _slope.transform.position = new Vector3(40f, 0f, 0f);
            _slope.transform.localScale = new Vector3(6f, 0.4f, 8f);
            _slope.transform.rotation = Quaternion.Euler(-30f, 0f, 0f);

            _character = new GameObject("Character");
            _cc = _character.AddComponent<CharacterController>();
            _cc.radius = Radius;
            _cc.height = Height;
            _cc.center = new Vector3(0f, CenterY, 0f);
            _cc.skinWidth = 0.03f;
            _cc.stepOffset = 0.3f;
            _cc.slopeLimit = SlopeLimit;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in new[] { _character, _platform, _stairs, _slope })
                if (go != null) Object.Destroy(go);
        }

        Vector3 FootWorld => _character.transform.TransformPoint(
            _cc.center - new Vector3(0f, _cc.height * 0.5f, 0f));

        bool HasSupport() => StandingSupportProbe.HasValidStandingSupport(
            FootWorld, _character.transform.up, SupportRadius, SearchDepth, ProbeLift,
            SlopeLimit, ~0, _character.transform.root, _buffer, out _);

        /// <summary>
        /// ⚠️ <paramref name="snapToSurface"/> 的兩難，兩邊都踩過：
        /// <list type="bullet">
        /// <item><b>不 snap</b>：從高處放下來只往下走 12×0.03 = 0.36 m，樓梯／斜坡**根本落不了地**
        /// ⇒ 取樣數 0，回歸測試等於沒跑。</item>
        /// <item><b>snap</b>：邊緣外的 XZ 探不到表面，會退回高空起點而永遠落不了地
        /// ⇒ **量測工具在待測區域失效**。</item>
        /// </list>
        /// ⇒ 樓梯／斜坡用 snap，平台邊緣掃描**必須**不用。
        /// </summary>
        IEnumerator Settle(float x, float startY, float z, bool snapToSurface = false)
        {
            float y = startY;
            if (snapToSurface && Physics.Raycast(new Vector3(x, startY, z), Vector3.down,
                    out RaycastHit surf, 50f, ~0, QueryTriggerInteraction.Ignore))
                y = surf.point.y + 0.04f;

            _character.transform.position = new Vector3(x, y, z);
            Physics.SyncTransforms();
            for (int i = 0; i < 12; i++)
            {
                _cc.Move(Vector3.down * 0.03f);
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>
        /// ⭐ 核心契約：**support probe 必須比 `isGrounded` 嚴格。**
        /// 存在一段區間，碰撞判定說「還站著」而 support probe 說「沒有支撐」——
        /// 那段區間正是使用者看到的「腳懸空還站著」。
        /// </summary>
        [UnityTest]
        public IEnumerator Ledge_SupportProbeIsStricterThanCollision()
        {
            float lastGrounded = float.NegativeInfinity;
            float lastSupported = float.NegativeInfinity;

            for (float z = -0.40f; z <= 0.45f; z += 0.01f)
            {
                yield return Settle(0f, 0.04f, z);
                if (_character.transform.position.y < -0.3f) break;
                if (_cc.isGrounded) lastGrounded = z;
                if (_cc.isGrounded && HasSupport()) lastSupported = z;
            }

            Assert.Greater(lastGrounded, float.NegativeInfinity, "前提：角色必須真的站上過平台");
            Assert.Less(lastSupported, lastGrounded,
                $"support probe 必須比碰撞判定嚴格（supported 到 {lastSupported:F3}、" +
                $"grounded 到 {lastGrounded:F3}）——否則這一層等於沒作用。");

            // 視覺契約：腳尖懸空必須明顯改善。基準實測 0.434，這裡要求至少砍到 0.30 以下。
            float toeOverhang = lastSupported + ToeExtent;
            Assert.Less(toeOverhang, 0.30f,
                $"腳尖懸空 {toeOverhang:F3} m 仍過大（基準 0.434）——supportRadius 可能設得太接近碰撞半徑。");
        }

        /// <summary>
        /// 樓梯回歸：只要碰撞判定認為站著，support probe **就不得否決**。
        /// ⚠️ spike 實測 supportRadius 0.10 會在 6 個取樣點中誤判 2 次 ⇒ 出貨值不得低於 0.14。
        /// </summary>
        [UnityTest]
        public IEnumerator Stairs_NeverVetoesWhileCollisionSaysGrounded()
        {
            int checkedSamples = 0;
            for (float z = -0.10f; z <= 0.95f; z += 0.10f)
            {
                yield return Settle(20f, 3.0f, z, snapToSurface: true);
                if (!_cc.isGrounded) continue;
                checkedSamples++;
                Assert.IsTrue(HasSupport(),
                    $"樓梯 z={z:F2} 被誤判為無支撐——角色會在樓梯上憑空掉下去。");
            }

            Assert.Greater(checkedSamples, 3, "前提：樓梯上必須有足夠的有效取樣點");
        }

        /// <summary>
        /// 斜坡回歸。
        /// ⚠️ 這條抓過一個真實缺陷：搜尋深度取 0.05 時 11/11 全部誤判——
        /// 膠囊停在 30° 斜面上時底部離正下方斜面 r(1/cos30−1)≈0.05，剛好搆不到。
        /// 出貨值 0.30（對齊 UE 的 max(MAX_FLOOR_DIST, MaxStepHeight)）才正確。
        /// </summary>
        [UnityTest]
        public IEnumerator Slope30Degrees_NeverVetoesWhileCollisionSaysGrounded()
        {
            int checkedSamples = 0;
            for (float z = -2.0f; z <= 2.0f; z += 0.40f)
            {
                yield return Settle(40f, 6.0f, z, snapToSurface: true);
                if (!_cc.isGrounded) continue;
                checkedSamples++;
                Assert.IsTrue(HasSupport(),
                    $"30° 斜坡 z={z:F2} 被誤判為無支撐——搜尋深度可能小於 step height。");
            }

            Assert.Greater(checkedSamples, 5, "前提：斜坡上必須有足夠的有效取樣點");
        }

        /// <summary>
        /// ⭐ 守住「只有否決權、沒有授予權」：平台正中央（碰撞與支撐都成立）不得有任何差異。
        /// </summary>
        [UnityTest]
        public IEnumerator FlatGround_SupportAlwaysAgreesWithCollision()
        {
            for (float z = -3f; z <= -1f; z += 0.5f)
            {
                yield return Settle(0f, 0.04f, z);
                Assert.IsTrue(_cc.isGrounded, $"前提：平台正中央 z={z:F2} 應該站得住");
                Assert.IsTrue(HasSupport(), $"平地 z={z:F2} 不得被否決");
            }
        }

        /// <summary>設定退化時**不得否決**——這一層的失敗方向必須是「什麼都不做」。</summary>
        [UnityTest]
        public IEnumerator DegenerateSettings_DoNotVeto()
        {
            yield return Settle(0f, 0.04f, -3f);

            Assert.IsTrue(StandingSupportProbe.HasValidStandingSupport(
                FootWorld, Vector3.up, 0f, SearchDepth, ProbeLift, SlopeLimit, ~0,
                _character.transform.root, _buffer, out _), "supportRadius = 0 不得否決");

            Assert.IsTrue(StandingSupportProbe.HasValidStandingSupport(
                FootWorld, Vector3.up, SupportRadius, 0f, ProbeLift, SlopeLimit, ~0,
                _character.transform.root, _buffer, out _), "searchDepth = 0 不得否決");

            Assert.IsTrue(StandingSupportProbe.HasValidStandingSupport(
                FootWorld, Vector3.up, SupportRadius, SearchDepth, ProbeLift, SlopeLimit, ~0,
                _character.transform.root, null, out _), "buffer 為 null 不得否決");
        }
    }
}
