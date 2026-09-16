using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Project.Core.Blackboard;
using Project.Presentation.Motion;
using Object = UnityEngine.Object;
using UnityEngine.TestTools.Constraints;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// **Dynamic Capsule V2 的執行期行為**（`docs/27` §12；白話版 `docs/28`）。
    ///
    /// <para><b>為什麼這些必須在 PlayMode</b></para>
    /// 平滑與賦值都在 <c>deltaTime &gt; 0</c> 的守衛之後。EditMode 的 <c>Time.deltaTime</c>
    /// 不保證非零 ⇒ 寫在那裡會得到**可能恆真但什麼都沒驗到**的測試。
    /// 解算層（幾何正確性、連續性、上限語意）由 <c>CapsuleOffsetTests.CP*</c> 在 EditMode 驗完；
    /// **這裡只補真的需要幀迴圈的事**：平滑、夾持、釋放、authority、零配置。
    ///
    /// <para><b>治具為什麼直接注入 landmark，而不是建一個 humanoid Animator</b></para>
    /// <c>GetBoneTransform</c> 需要真正的 humanoid Avatar，在測試裡程式化建立一個 Avatar
    /// 成本極高且脆弱。這裡改成**直接注入已解析的 landmark**——被測的是
    /// 「解算 → 上限 → 平滑 → 夾持 → 寫入」這條管線，不是 Unity 的骨骼查表。
    /// Animator 解析路徑由 EditMode 的 prefab wiring 測試（<c>CapsuleProfileWiringTests</c>）覆蓋。
    ///
    /// ⛔ 這組全綠**不代表**驗收完成——實際覆蓋觀感、貼牆手感、底部支撐前移可不可接受，
    /// 仍然是人類 Play。
    /// </summary>
    public sealed class CapsuleOffsetPlayModeTests
    {
        private const float Radius = 0.32f;
        private const float Height = 1.826619f;
        private const float CenterY = 0.9134095f;

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        /// <summary>
        /// 🔴 **CO1 — 偏移確實作用在水平面上，而 `center.y` 一動也不動。**
        ///
        /// 改變膠囊底面高度 ⇒ 站在台階邊時**失去接地** ⇒ <c>isGrounded</c> false；
        /// 而 <c>FootIKController</c> 的閘門是**二值無淡入** ⇒ 一次誤判就是可見的腳部彈跳。
        /// 本測項同時斷言「**有**作用」與「y **沒有**被動到」——只驗後者的話，
        /// 一條什麼都沒做的實作也會通過。
        /// </summary>
        [UnityTest]
        public IEnumerator CO1_OffsetMovesHorizontallyAndNeverTouchesCenterY()
        {
            Fixture f = CreateFixture();
            SetForwardLeanPose(f, 1.0f);
            yield return null;

            yield return DriveFrames(f, 40);

            Assert.AreEqual(f.AuthoredCenter.y, f.Capsule.center.y, 1e-5f,
                "⛔ center.y 永不改——改了會在台階邊緣直接失去接地");
            Assert.Greater(f.Capsule.center.z, f.AuthoredCenter.z + 0.01f,
                "測試前提：偏移必須真的往前推出去，否則上一條斷言什麼都沒驗到");
            Assert.AreEqual(f.AuthoredCenter.x, f.Capsule.center.x, 1e-3f,
                "純前傾 ⇒ 側向不該有偏移");
        }

        /// <summary>
        /// **CO2 — 姿勢回正之後偏移必須收斂回 0。**
        ///
        /// 收不回去的症狀是**角色站著不動、碰撞體卻永遠偏一邊**——Inspector 上看不出異常，
        /// 只有貼牆時才會突然發現站不進去。
        ///
        /// ⚠️ **依時間推進，不是依幀數**。SmoothDamp 是漸近收斂，「40 幀」在不同幀率下等於
        /// 不同的時間長度 ⇒ 幀數＋絕對容差會得到一條頻率相關的測試（PD2 踩過）。
        /// </summary>
        [UnityTest]
        public IEnumerator CO2_UprightPoseReturnsTheCapsuleToNeutral()
        {
            Fixture f = CreateFixture();
            SetForwardLeanPose(f, 1.0f);
            yield return null;

            yield return DriveFrames(f, 40);
            float peak = f.Capsule.center.z - f.AuthoredCenter.z;
            Assert.Greater(peak, 0.01f, "測試前提：先真的偏出去");

            SetForwardLeanPose(f, 0f);
            float elapsed = 0f;
            float window = f.Profile.ReleaseSmoothTime * 5f;
            for (int i = 0; i < 600 && elapsed < window; i++)
            {
                InvokeUpdate(f);
                elapsed += Time.deltaTime;
                yield return null;
            }

            float residual = Mathf.Abs(f.Capsule.center.z - f.AuthoredCenter.z);
            Assert.Less(residual, peak * 0.1f,
                $"姿勢回正後偏移必須收斂（峰值 {peak:F4}，殘留 {residual:F4}，經過 {elapsed:F3}s）");
        }

        /// <summary>
        /// 🔴 **CO3 — 貼牆時偏移被「夾住」而不是「歸零」。**
        ///
        /// 舊版 <c>touchingWall → zero</c> 讓偏移**只在空曠處存在、貼牆時消失**，
        /// 也就是「只在不需要的時候生效」——那正是使用者回報「好像沒用」的主因。
        /// </summary>
        [UnityTest]
        public IEnumerator CO3_WallClampsOffsetPartiallyInsteadOfZeroingIt()
        {
            Fixture f = CreateFixture();
            SetForwardLeanPose(f, 1.0f);

            // ⚠️ 牆要放在 **cast 膠囊**（半徑 = radius − skinWidth）前緣再往前一點。
            //    用 `radius` 算會讓實際間隙大於偏移量 ⇒ 根本掃不到，測試紅的是治具不是程式。
            float castRadius = f.Capsule.radius - f.Capsule.skinWidth;
            float wallZ = f.AuthoredCenter.z + castRadius + 0.05f;
            GameObject wall = CreateWall(wallZ, f.AuthoredCenter.y);
            yield return null;

            yield return DriveFrames(f, 40);

            Assert.Greater(f.Driver.CapsuleDesiredOffset.z, 0.05f,
                "求解層不認識幾何體 ⇒ Desired 必須維持姿勢解出的值");

            string geometry =
                $"（Desired={f.Driver.CapsuleDesiredOffset} Capped={f.Driver.CapsuleCappedOffset} " +
                $"Smoothed={f.Driver.CapsuleSmoothedOffset} Safe={f.Driver.CapsuleSafeOffset} " +
                $"wallZ={wallZ:F3} center={f.Capsule.center}）";

            Assert.Greater(f.Driver.CapsuleSafeOffset.z, 0.005f,
                "⛔ 貼牆不得再把偏移歸零——那正是舊行為讓功能『看起來沒用』的原因" + geometry);
            Assert.Less(f.Driver.CapsuleSafeOffset.z, f.Driver.CapsuleSmoothedOffset.z,
                "牆確實擋住了一部分 ⇒ Safe 必須小於 Smoothed" + geometry);

            float castFrontZ = f.Capsule.center.z + castRadius;
            Assert.LessOrEqual(castFrontZ, wallZ + 1e-3f,
                "夾持之後 cast 膠囊前緣不得越過牆面" + geometry);
            Assert.AreEqual(f.AuthoredCenter.y, f.Capsule.center.y, 1e-5f, "center.y 仍然不得被動到");

            Object.DestroyImmediate(wall);
            _created.Remove(wall);
        }

        /// <summary>
        /// 🔴 **CO4 — 牆消失的當幀就恢復完整偏移（平滑不是第二個 authority）。**
        ///
        /// 這是 V2 把順序改成 <c>Smooth → Clamp</c> 才成立的性質：平滑器追的是 **Capped**，
        /// 夾持只作用在輸出端。若把夾持結果餵回平滑器（V1 的形狀），牆就成了平滑狀態的
        /// 第二個 authority——貼牆時平滑值被壓到 0，牆一消失還要重新慢慢長回來。
        /// </summary>
        [UnityTest]
        public IEnumerator CO4_RemovingWallRestoresOffsetImmediately()
        {
            Fixture f = CreateFixture();
            SetForwardLeanPose(f, 1.0f);
            float castRadius = f.Capsule.radius - f.Capsule.skinWidth;
            GameObject wall = CreateWall(f.AuthoredCenter.z + castRadius + 0.05f, f.AuthoredCenter.y);
            yield return null;

            yield return DriveFrames(f, 40);
            float clampedSafe = f.Driver.CapsuleSafeOffset.z;
            float smoothedBehindWall = f.Driver.CapsuleSmoothedOffset.z;

            Assert.Greater(smoothedBehindWall, clampedSafe + 0.005f,
                "測試前提：牆必須真的夾掉一部分（Smoothed > Safe）");

            Object.DestroyImmediate(wall);
            _created.Remove(wall);
            Physics.SyncTransforms();

            // 只推進**一幀**——若平滑狀態沒被牆污染，這一幀就該回到完整值。
            InvokeUpdate(f);
            yield return null;

            Assert.Greater(f.Driver.CapsuleSafeOffset.z, smoothedBehindWall - 0.01f,
                $"牆消失後應**立刻**恢復（safe={f.Driver.CapsuleSafeOffset.z:F4}，" +
                $"牆存在時的 smoothed={smoothedBehindWall:F4}）。" +
                "還要慢慢長回來 ⇒ 夾持結果被餵回平滑器了");
        }

        /// <summary>
        /// **CO5 — 死亡時停用水平偏移，而且是平滑收回不是硬歸零。**
        /// 倒地姿勢下「上半身佔用位置」與站立膠囊已經不是同一回事，解出來的偏移沒有意義。
        /// </summary>
        [UnityTest]
        public IEnumerator CO5_DeathDisablesOffsetWithoutSnapping()
        {
            Fixture f = CreateFixture();
            SetForwardLeanPose(f, 1.0f);
            yield return null;

            yield return DriveFrames(f, 40);
            Assert.Greater(f.Capsule.center.z, f.AuthoredCenter.z + 0.01f, "測試前提：先真的偏出去");
            float beforeDeath = f.Capsule.center.z;

            f.Data.Survivability.IsDead = true;
            InvokeUpdate(f);
            yield return null;

            Assert.Less(f.Capsule.center.z, beforeDeath,
                "死亡後偏移必須開始收回");
            Assert.Greater(f.Capsule.center.z, f.AuthoredCenter.z,
                "⛔ 但不得**瞬間**歸零——那會讓屍體的碰撞體彈一下");

            float elapsed = 0f;
            for (int i = 0; i < 600 && elapsed < f.Profile.ReleaseSmoothTime * 5f; i++)
            {
                InvokeUpdate(f);
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.Less(Mathf.Abs(f.Capsule.center.z - f.AuthoredCenter.z), 0.01f,
                "最終必須回到 authored 基準");
        }

        /// <summary>
        /// **CO6 — traversal collision profile 生效期間，姿勢偏移不寫 center（執行期版本）。**
        /// EditMode 的 <c>CD1</c> 驗的是單次呼叫；這裡驗的是**連續數十幀**都不會偷偷寫進去。
        /// </summary>
        [UnityTest]
        public IEnumerator CO6_TraversalProfileActive_PoseOffsetNeverWritesCenter()
        {
            Fixture f = CreateFixture();
            SetForwardLeanPose(f, 1.0f);
            SetPrivateField(f.Driver, "_traversalCollisionProfileActive", true);
            yield return null;

            Vector3 sentinel = f.AuthoredCenter + new Vector3(0f, 0f, 0.05f);
            f.Capsule.center = sentinel;   // 假裝 traversal profile 正把 center 放在這裡

            yield return DriveFrames(f, 30);

            Assert.AreEqual(sentinel, f.Capsule.center,
                "traversal 擁有 center 的 authority ⇒ 姿勢偏移一個字都不准寫");
        }

        /// <summary>
        /// **CO7 — 熱路徑零配置。**
        /// solver 每幀跑、landmark 每幀取樣；任何一個地方用了 <c>new T[]</c> 都會在這裡現形。
        /// </summary>
        [UnityTest]
        public IEnumerator CO7_UpdateAllocatesNothingPerFrame()
        {
            Fixture f = CreateFixture();
            SetForwardLeanPose(f, 1.0f);
            yield return null;

            // 先跑幾幀讓一次性的解析／緩衝配置完成，再量穩態。
            yield return DriveFrames(f, 5);

            PlayerRuntimeData data = f.Data;
            System.Action<PlayerRuntimeData> tick = f.Tick;
            Assert.That(() => tick(data), NUnit.Framework.Is.Not.AllocatingGCMemory(),
                "dynamic capsule 的每幀更新必須零配置（solver、landmark 取樣、夾持都在裡面）");
        }

        // =====================================================================
        // 治具
        // =====================================================================

        private sealed class Fixture
        {
            public GameObject Host;
            public CharacterController Capsule;
            public MotionDriver Driver;
            public PlayerRuntimeData Data;
            public CapsulePoseProfileSO Profile;
            public Vector3 AuthoredCenter;
            public Transform[] Bones;
            /// 快取的委派：⚠️ **零 GC 測試不能在量測區間內做反射**（`MethodInfo.Invoke` 自己會配置）。
            public System.Action<PlayerRuntimeData> Tick;
        }

        private Fixture CreateFixture()
        {
            var host = new GameObject("CapsuleV2-Host");
            _created.Add(host);
            // ⚠️ 必須離開 Default(0)：clamp 會排除**角色自身的 layer**。
            //    治具留在 Default 的話測試用的牆會連帶被濾掉，CO3／CO4 變成恆綠但什麼都沒驗。
            host.layer = 6;

            var capsule = host.AddComponent<CharacterController>();
            capsule.center = new Vector3(0f, CenterY, 0f);
            capsule.radius = Radius;
            capsule.height = Height;

            var driver = host.AddComponent<MotionDriver>();
            SetPrivateField(driver, "characterController", capsule);

            CapsulePoseProfileSO profile = CreateProfile();
            var settings = CapsuleOffsetSettings.Disabled;
            settings.Enabled = true;
            settings.Profile = profile;
            SetPrivateField(driver, "capsuleOffset", settings);

            // 四個「骨骼」——直接注入已解析的 landmark，繞過 humanoid Avatar 的需求。
            var bones = new Transform[4];
            float[] heights = { 1.2435f, 1.3357f, 1.5031f, 1.5993f };
            float[] radii = { 0.2038f, 0.1867f, 0.0952f, 0.2109f };
            for (int i = 0; i < 4; i++)
            {
                var bone = new GameObject($"bone{i}").transform;
                bone.SetParent(host.transform, false);
                bone.localPosition = new Vector3(0f, heights[i], 0f);
                bones[i] = bone;
            }
            SetPrivateField(driver, "_capsuleLandmarkTransforms", bones);
            SetPrivateField(driver, "_capsuleLandmarkRadii", radii);
            SetPrivateField(driver, "_capsuleLandmarkSamples", new CapsuleLandmarkSample[4]);
            SetPrivateField(driver, "_capsuleLandmarkCount", 4);
            SetPrivateField(driver, "_capsuleLandmarksResolved", true);

            MethodInfo update = typeof(MotionDriver).GetMethod(
                "UpdateCapsuleOffset", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(update, "找不到 MotionDriver.UpdateCapsuleOffset（方法名稱可能已變更）");

            return new Fixture
            {
                Tick = (System.Action<PlayerRuntimeData>)update.CreateDelegate(
                    typeof(System.Action<PlayerRuntimeData>), driver),
                Host = host,
                Capsule = capsule,
                Driver = driver,
                Profile = profile,
                Bones = bones,
                AuthoredCenter = capsule.center,
                Data = new PlayerRuntimeData { IsGrounded = true },
            };
        }

        private CapsulePoseProfileSO CreateProfile()
        {
            var profile = ScriptableObject.CreateInstance<CapsulePoseProfileSO>();
            _created.Add(profile);
            var entries = new CapsulePoseProfileSO.LandmarkEntry[4];
            HumanBodyBones[] bones =
            {
                HumanBodyBones.Chest, HumanBodyBones.UpperChest,
                HumanBodyBones.Neck, HumanBodyBones.Head,
            };
            float[] radii = { 0.2038f, 0.1867f, 0.0952f, 0.2109f };
            for (int i = 0; i < 4; i++)
                entries[i] = new CapsulePoseProfileSO.LandmarkEntry
                {
                    Enabled = true, Bone = bones[i], FleshRadius = radii[i],
                };
            SetPrivateField(profile, "landmarks", entries);
            SetPrivateField(profile, "maxOffset", 0.18f);
            SetPrivateField(profile, "smoothTime", 0.06f);
            SetPrivateField(profile, "releaseSmoothTime", 0.03f);
            SetPrivateField(profile, "maximumTurnRateDegreesPerSecond", 180f);
            SetPrivateField(profile, "applyWhileDead", false);
            return profile;
        }

        /// <summary>把四個 landmark 擺成「前傾 <paramref name="lean"/> 個單位」的姿勢。</summary>
        private static void SetForwardLeanPose(Fixture f, float lean)
        {
            float[] weights = { 0.20f, 0.45f, 0.80f, 1.00f };
            float[] heights = { 1.2435f, 1.3357f, 1.5031f, 1.5993f };
            for (int i = 0; i < f.Bones.Length; i++)
                f.Bones[i].localPosition = new Vector3(0f, heights[i], lean * 0.20f * weights[i]);
        }

        private GameObject CreateWall(float frontZ, float y)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(wall);
            wall.transform.position = new Vector3(0f, y, frontZ + 0.5f);
            wall.transform.localScale = new Vector3(6f, 3f, 1f);
            wall.layer = 0;   // 必須與角色不同層，否則被 clamp 的自身 layer 排除規則濾掉
            Physics.SyncTransforms();
            return wall;
        }

        private static IEnumerator DriveFrames(Fixture f, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                InvokeUpdate(f);
                yield return null;
            }
        }

        /// <summary>
        /// 直接呼叫私有更新（經快取委派）。⚠️ 走 <c>ExecuteBaseMovement</c> 會連帶跑重力與 <c>Move</c>，
        /// 把「center 被誰寫」混進一堆無關的物理，失敗訊息會變得無法解讀。
        /// </summary>
        private static void InvokeUpdate(Fixture f) => f.Tick(f.Data);

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName}（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }
    }
}
