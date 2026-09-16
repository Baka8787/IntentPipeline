using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// ⚠️ **THROWAWAY SPIKE（用完即刪，不得被 runtime 引用）**
    ///
    /// 回答一個問題：**在不動 CharacterController radius 的前提下，
    /// 「另外做一次 support query」能不能把懸空量壓到與視覺相符，而且不誤判樓梯／斜坡？**
    ///
    /// <para><b>Spike A — Unreal 式較小有效半徑</b></para>
    /// 對應 <c>UCharacterMovementComponent::ComputePerchResult(GetValidPerchRadius(), …)</c>：
    /// 碰撞膠囊維持 0.32，但**用一個較小的半徑重跑一次 floor query**，
    /// 失敗就把 floor 判為不可站（UE 原碼：<c>OutFloorResult.bWalkableFloor = false</c>）。
    ///
    /// <para><b>Spike B — footprint 多點採樣</b></para>
    /// 不用單一 cast，改用少量 support sample（center／front／back）判「有效支撐區」。
    ///
    /// ⛔ 兩者都**只量測、不接線**：不寫黑板、不碰 MotionDriver、不碰 JumpState。
    /// </summary>
    public class ValidStandingSupportSpike
    {
        // 與 X Bot 實際 prefab 一致
        const float Radius = 0.32f;
        const float Height = 1.8266189f;
        const float CenterY = 0.9134095f;
        const float SkinWidth = 0.03f;
        const float StepOffset = 0.3f;

        // 實測：腳尖（ToeBase）距 root 水平 0.114 m
        const float ToeExtent = 0.114f;

        // floor query 的容許距離。對齊 UE 的 MAX_FLOOR_DIST 概念（UE 用 2.4 unreal units，
        // 其單位是 cm ⇒ 0.024 m）。這裡放寬到 0.05 m，涵蓋 skinWidth 與貼地誤差。
        // 🐛 第一版用 0.05 ⇒ **斜坡 11/11 全部誤判**。原因：膠囊停在 30° 斜面上時，
        //    它的底部離「正下方的斜面」比離接觸點遠 r(1/cos30−1) ≈ 0.05 m，
        //    0.05 的探測距離剛好搆不到 ⇒ 探不到地面≠沒有地面。
        //    UE 的作法是 FloorSweepTraceDist = max(MAX_FLOOR_DIST, MaxStepHeight + adjust)，
        //    即**用 step height 當地板搜尋深度**。這裡照做（= stepOffset 0.3）。
        //    ⚠️ 這不會讓平台邊緣變寬——邊緣外沒有地面，探多深都一樣沒有。
        const float MaxFloorDist = 0.30f;
        const float ProbeLift = 0.10f;

        static GameObject _platform, _stairs, _slope;
        static GameObject _character;
        static CharacterController _cc;

        static Vector3 FootWorld => _character.transform.position
                                    + new Vector3(0f, CenterY - Height * 0.5f, 0f);

        // ---- Spike A：較小半徑的 support query -------------------------------
        static bool HasSupport_SmallerRadius(float supportRadius, out float slopeDeg)
        {
            slopeDeg = 0f;
            Vector3 origin = FootWorld + Vector3.up * (supportRadius + ProbeLift);
            if (!Physics.SphereCast(origin, supportRadius, Vector3.down, out RaycastHit hit,
                    ProbeLift + MaxFloorDist, ~0, QueryTriggerInteraction.Ignore))
                return false;
            slopeDeg = Vector3.Angle(hit.normal, Vector3.up);
            return slopeDeg <= 45f; // walkable normal，對齊 slopeLimit
        }

        // ---- Spike B：footprint 多點採樣 -------------------------------------
        /// <param name="samples">回傳命中的點數</param>
        static bool HasSupport_Footprint(float extent, int required, out int samples)
        {
            Vector3 fwd = _character.transform.forward;
            Vector3 foot = FootWorld;
            Vector3[] offsets =
            {
                Vector3.zero,
                fwd * extent,
                -fwd * extent,
            };

            samples = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 origin = foot + offsets[i] + Vector3.up * ProbeLift;
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                        ProbeLift + MaxFloorDist, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (Vector3.Angle(hit.normal, Vector3.up) > 45f) continue;
                samples++;
            }
            return samples >= required;
        }

        static void BuildWorld()
        {
            // 平台：頂面 y=0，邊緣在 z=0，平台佔 z<0
            _platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _platform.name = "SpikePlatform";
            _platform.transform.position = new Vector3(0f, -0.5f, -5f);
            _platform.transform.localScale = new Vector3(10f, 1f, 10f);

            // 樓梯：4 階，每階 0.18 高 × 0.30 深，放在 x=20 以免干擾
            _stairs = new GameObject("SpikeStairs");
            _stairs.transform.position = new Vector3(20f, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.transform.SetParent(_stairs.transform);
                step.transform.localPosition = new Vector3(0f, 0.18f * i + 0.09f, 0.30f * i);
                step.transform.localScale = new Vector3(4f, 0.18f, 0.30f);
            }

            // 斜坡：30°，放在 x=40
            _slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _slope.name = "SpikeSlope";
            _slope.transform.position = new Vector3(40f, 0f, 0f);
            _slope.transform.localScale = new Vector3(6f, 0.4f, 8f);
            _slope.transform.rotation = Quaternion.Euler(-30f, 0f, 0f);

            _character = new GameObject("SpikeCharacter");
            _cc = _character.AddComponent<CharacterController>();
            _cc.radius = Radius;
            _cc.height = Height;
            _cc.center = new Vector3(0f, CenterY, 0f);
            _cc.skinWidth = SkinWidth;
            _cc.stepOffset = StepOffset;
            _cc.slopeLimit = 45f;
        }

        /// <summary>
        /// ⚠️ 第一版只往下走 6×0.02 = 0.12 m，從 y=1.2／3.0 丟下來時**根本沒落地**
        /// ⇒ 樓梯與斜坡兩段取樣數為 0，回歸測試等於沒跑。
        /// 現在先往下探到表面再放上去，然後才做短距離沉降。
        /// </summary>
        static IEnumerator Settle(Vector3 xzAnchor, bool snapToSurface = true)
        {
            // 先找該 XZ 正下方的表面，直接把角色放到它上面一點點
            Vector3 from = new Vector3(xzAnchor.x, xzAnchor.y, xzAnchor.z);
            // ⚠️ snapToSurface 在**邊緣掃描時必須關掉**：邊緣外的 XZ 探不到地面，
            //    會退回 xzAnchor.y 的高空起點而永遠落不了地 ⇒ 整段量測歸零（第二版踩過）。
            float startY = xzAnchor.y;
            if (snapToSurface && Physics.Raycast(from, Vector3.down, out RaycastHit surf, 50f, ~0,
                    QueryTriggerInteraction.Ignore))
                startY = surf.point.y + 0.04f;

            _character.transform.position = new Vector3(xzAnchor.x, startY, xzAnchor.z);
            Physics.SyncTransforms();

            for (int i = 0; i < 12; i++)
            {
                _cc.Move(Vector3.down * 0.03f);
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator Spike_MeasureSupportModels()
        {
            BuildWorld();
            yield return new WaitForFixedUpdate();

            var sb = new StringBuilder();
            sb.AppendLine("================ VALID STANDING SUPPORT SPIKE ================");
            sb.AppendLine($"CC radius={Radius} height={Height} skin={SkinWidth} step={StepOffset}");
            sb.AppendLine($"腳尖外伸 ToeExtent={ToeExtent}  MaxFloorDist={MaxFloorDist}");
            sb.AppendLine();

            // ---------- 1) 平台邊緣：各模型的最大 root overhang ----------
            sb.AppendLine("---- ① 平台邊緣（邊緣在 z=0；root.z>0 ＝ 已超出）----");
            float[] supportRadii = { 0.10f, 0.14f, 0.18f, 0.22f };
            float ccLast = float.NegativeInfinity;
            var aLast = new float[supportRadii.Length];
            for (int i = 0; i < aLast.Length; i++) aLast[i] = float.NegativeInfinity;
            float bLast3 = float.NegativeInfinity, bLast2 = float.NegativeInfinity;

            for (float z = -0.40f; z <= 0.45f; z += 0.01f)
            {
                yield return Settle(new Vector3(0f, 0.04f, z), snapToSurface: false);
                if (_character.transform.position.y < -0.3f) break;

                if (_cc.isGrounded) ccLast = z;
                for (int i = 0; i < supportRadii.Length; i++)
                    if (HasSupport_SmallerRadius(supportRadii[i], out _)) aLast[i] = z;
                if (HasSupport_Footprint(ToeExtent, 3, out _)) bLast3 = z;
                if (HasSupport_Footprint(ToeExtent, 2, out _)) bLast2 = z;
            }

            sb.AppendLine($"  基準 CharacterController.isGrounded : root 最遠 {ccLast,6:F3}  ⇒ 腳尖懸空 {ccLast + ToeExtent,6:F3}");
            for (int i = 0; i < supportRadii.Length; i++)
                sb.AppendLine($"  A) supportRadius={supportRadii[i]:F2}            : root 最遠 {aLast[i],6:F3}  ⇒ 腳尖懸空 {aLast[i] + ToeExtent,6:F3}");
            sb.AppendLine($"  B) footprint 3/3 點(±{ToeExtent})       : root 最遠 {bLast3,6:F3}  ⇒ 腳尖懸空 {bLast3 + ToeExtent,6:F3}");
            sb.AppendLine($"  B) footprint 2/3 點(±{ToeExtent})       : root 最遠 {bLast2,6:F3}  ⇒ 腳尖懸空 {bLast2 + ToeExtent,6:F3}");
            sb.AppendLine();

            // ---------- 2) 樓梯：會不會誤判成沒支撐 ----------
            sb.AppendLine("---- ② 樓梯回歸（4 階 × 0.18 高 / 0.30 深）----");
            sb.AppendLine("  z      ccGrounded  A(0.10) A(0.14) A(0.18) A(0.22)  B3/3  B2/3");
            int aFalse = 0, b3False = 0, b2False = 0, stairSamples = 0;
            for (float z = -0.30f; z <= 1.10f; z += 0.10f)
            {
                yield return Settle(new Vector3(20f, 3.0f, z));
                bool cc = _cc.isGrounded;
                if (!cc) continue; // 只在「CC 認為站著」時比較，才是誤判
                stairSamples++;

                bool[] a = new bool[supportRadii.Length];
                for (int i = 0; i < supportRadii.Length; i++) a[i] = HasSupport_SmallerRadius(supportRadii[i], out _);
                bool b3 = HasSupport_Footprint(ToeExtent, 3, out _);
                bool b2 = HasSupport_Footprint(ToeExtent, 2, out _);
                if (!a[1]) aFalse++;          // 以 0.14 當代表
                if (!b3) b3False++;
                if (!b2) b2False++;

                sb.AppendLine($"  {z,5:F2}  {cc,-10}  {a[0],-7} {a[1],-7} {a[2],-7} {a[3],-8} {b3,-5} {b2}");
            }
            sb.AppendLine($"  ⇒ 樓梯上 CC 認為站著的 {stairSamples} 個取樣點中：" +
                          $"A(0.14) 誤判 {aFalse}、B3/3 誤判 {b3False}、B2/3 誤判 {b2False}");
            sb.AppendLine();

            // ---------- 3) 斜坡 ----------
            sb.AppendLine("---- ③ 斜坡回歸（30°）----");
            int aFalseSlope = 0, b3FalseSlope = 0, b2FalseSlope = 0, slopeSamples = 0;
            for (float z = -2.0f; z <= 2.0f; z += 0.40f)
            {
                yield return Settle(new Vector3(40f, 6.0f, z));
                bool cc = _cc.isGrounded;
                if (!cc) continue;
                slopeSamples++;
                if (!HasSupport_SmallerRadius(0.14f, out float sd)) aFalseSlope++;
                if (!HasSupport_Footprint(ToeExtent, 3, out _)) b3FalseSlope++;
                if (!HasSupport_Footprint(ToeExtent, 2, out _)) b2FalseSlope++;
                sb.AppendLine($"  z={z,5:F2}  ccGrounded=True  量到的坡度={sd,5:F1}°");
            }
            sb.AppendLine($"  ⇒ 斜坡上 {slopeSamples} 個取樣點中：" +
                          $"A(0.14) 誤判 {aFalseSlope}、B3/3 誤判 {b3FalseSlope}、B2/3 誤判 {b2FalseSlope}");

            Debug.Log("[ValidStandingSupportSpike]\n" + sb);

            Object.Destroy(_character);
            Object.Destroy(_platform);
            Object.Destroy(_stairs);
            Object.Destroy(_slope);
            yield return null;
            Assert.Pass("量測 spike——見 Console 的 [ValidStandingSupportSpike]");
        }
    }
}
