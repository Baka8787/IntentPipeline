using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Pipeline;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🆕（2026-10-14，`docs/57` §14.2）**交戰中的 AI 移動方向不依賴任何相機**——在真的沒有相機的世界裡驗。
    ///
    /// <para><b>為什麼從 EditMode 搬來</b></para>
    /// 原本的 <c>MovementIntentTests.AIMovementSource_WithoutCamera_ProducesNonZeroWorldDirection</c>
    /// 在 EditMode 建臨時 NavMesh，但 <c>NavMeshAgent</c> 的註冊需要 Play 生命週期，<c>Warp</c> 每次都回 false，
    /// 於是它在所有留有紀錄的全量回歸中 8／8 被 <c>Assert.Ignore</c> 跳過——交戰分支的這個行為從未被驗證過。
    /// 它是 <c>ProduceIntent_WithoutCamera_ProducesZeroWorldDirection</c>（玩家：沒有相機 ⇒ 方向為零）的對偶：
    /// 玩家的方向投影自相機基底；AI 的方向是世界座標，沒有相機也必須是非零的水平單位向量。
    ///
    /// <para><b>它比 A30 多守什麼</b></para>
    /// A30 只靜態掃描 <c>Core/Movement</c> 有沒有出現 <c>CameraTransform</c> 這個字；改成讀 <c>Camera.main</c>、
    /// 或經別的檔案間接取得相機，A30 都看不到。這裡在執行期把相機整個拿掉，任何形式的相機依賴都會讓方向消失或偏掉。
    ///
    /// <para><b>前置條件一律硬性斷言</b></para>
    /// NavMesh 建置、agent 註冊、沒有相機、進入交戰——任何一項不成立都是**失敗**，⛔ 不用 <c>Assert.Ignore</c>：
    /// 前置條件不成立時這個測試什麼都沒驗證，必須讓人看見。
    /// </summary>
    public sealed class AIMovementNoCameraPlayModeTests
    {
        private const float FixedDelta = 1f / 60f;
        private const int RegistrationFrames = 30;
        private const int EngageFrames = 30;
        private const int FirstIntentFrames = 15;
        private const int ObservedFrames = 60;   // 1 s：walk 約 1.64 m/s，進不了 2.5 m 內，但足以量到前進
        private const float MinimumAlignment = 0.95f;   // 平地直線路徑：方向應幾乎正對目標
        private const int ReachBandFrames = 360;        // 5 m → 交戰帶（≤ 2 m）：walk 約 1.64 m/s，留足餘裕
        private const int BandFrames = 90;
        private const int MinimumStrafeFrames = 10;
        private const float MaximumStrafeAlignment = 0.25f;   // 切線：與目標方向幾乎垂直

        // 原 EditMode 測試的情境（目標 1.5 m，落在交戰帶內）實際走的是 Strafe 分支；
        // 這裡讀 producer 自己的模式，逐帧以「該模式的合理方向」檢查，不靠距離猜。
        private static readonly FieldInfo EngagementField =
            typeof(AIMovementSource).GetField("_engagementMovement", BindingFlags.Instance | BindingFlags.NonPublic);

        private Scene _scene;
        private NavMeshDataInstance _navMesh;
        private readonly List<Object> _created = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (Object o in _created) if (o != null) Object.Destroy(o);
            _created.Clear();
            if (_navMesh.valid) NavMesh.RemoveNavMeshData(_navMesh);
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTest]
        public IEnumerator Engaged_WithoutAnyCamera_ProducesFiniteWorldDirectionTowardTarget()
        {
            Time.captureDeltaTime = FixedDelta;
            _scene = SceneManager.CreateScene("AIMovementNoCamera");
            SceneManager.SetActiveScene(_scene);
            AssertNoCameraAnywhere("場景建立後");

            // ── 1. 有效的 NavMesh ───────────────────────────────────────────────────────────────────
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(ground);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = ground.transform.localScale,
                    transform = Matrix4x4.TRS(ground.transform.position, Quaternion.identity, Vector3.one),
                    area = 0,
                },
            };
            NavMeshData navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Vector3.zero, new Vector3(40f, 10f, 40f)), Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(navData, "前置失敗：測試用 NavMesh 建置失敗");
            _navMesh = NavMesh.AddNavMeshData(navData);
            Assert.IsTrue(_navMesh.valid, "前置失敗：NavMesh 加入失敗");
            Vector3 spawn = new Vector3(0f, 0.05f, 0f);
            Vector3 targetPosition = new Vector3(0f, 0f, 5f);   // 8 m aggro 內、交戰帶（≤ 2 m）外 ⇒ Approach
            Assert.IsTrue(NavMesh.SamplePosition(spawn, out _, 0.5f, NavMesh.AllAreas), "前置失敗：出生點不在 NavMesh 上");
            Assert.IsTrue(NavMesh.SamplePosition(targetPosition, out _, 0.5f, NavMesh.AllAreas), "前置失敗：目標不在 NavMesh 上");

            // ── 2. 真 Y Bot prefab（真 Runner、真 AICombatContextSource、真 AIMovementSource）────────────────
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Y Bot.prefab");
            Assert.IsNotNull(prefab, "前置失敗：找不到 Y Bot prefab");
            GameObject enemy = Object.Instantiate(prefab, spawn, Quaternion.identity);
            _created.Add(enemy);
            foreach (Animator animator in enemy.GetComponentsInChildren<Animator>(true))
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var target = new GameObject("NoCameraTarget");
            _created.Add(target);
            target.transform.position = targetPosition;

            var movement = enemy.GetComponent<AIMovementSource>();
            var agent = enemy.GetComponent<NavMeshAgent>();
            var runner = enemy.GetComponent<CharacterPipelineRunner>();
            Assert.IsNotNull(movement, "前置失敗：Y Bot 沒有 AIMovementSource");
            Assert.IsNotNull(agent, "前置失敗：Y Bot 沒有 NavMeshAgent");
            Assert.IsNotNull(runner, "前置失敗：Y Bot 沒有 CharacterPipelineRunner");
            movement.SetTargetForTests(target.transform);
            // Leash 放大：這裡驗的是方向，不是脫戰。
            var territory = enemy.GetComponent<EnemyTerritory>();
            Assert.IsNotNull(territory, "前置失敗：Y Bot 沒有 EnemyTerritory");
            territory.ConfigureForTests(spawn, roam: 20f, soft: 30f, hard: 40f);
            AssertNoCameraAnywhere("敵人生成後（prefab 不得自帶相機）");

            // ── 3. Agent 真的註冊到 NavMesh（EditMode 版做不到的那一步）──────────────────────────────────
            int frame = 0;
            while (!agent.isOnNavMesh && frame < RegistrationFrames)
            {
                yield return null;
                frame++;
            }
            Assert.IsTrue(agent.isOnNavMesh, $"前置失敗：{RegistrationFrames} 帧內 NavMeshAgent 沒有註冊到 NavMesh");
            Assert.IsFalse(agent.updatePosition, "A16：agent 不得擁有位移權（updatePosition 必須為 false）");

            PlayerRuntimeData data = runner.RuntimeData;
            Assert.IsNotNull(data, "前置失敗：Runner 沒有建立黑板");
            Assert.IsNull(data.CameraTransform,
                "前置失敗：黑板上仍有 CameraTransform——Runner 由 playerCamera／Camera.main 填入，沒有相機時必須為 null");

            // ── 4. 進入交戰 ──────────────────────────────────────────────────────────────────────────
            frame = 0;
            while (!(data.CombatContext.InCombat && data.CombatContext.HasTarget) && frame < EngageFrames)
            {
                yield return null;
                frame++;
            }
            Assert.IsTrue(data.CombatContext.InCombat && data.CombatContext.HasTarget,
                $"前置失敗：{EngageFrames} 帧內沒有進入交戰（目標距離 5 m，aggro 8 m）");

            // ── 5. 移動意圖：非零、有限、水平單位向量、正對目標 ─────────────────────────────────────────
            // 路徑非同步計算，前一兩帧意圖為零是既有行為（pathPending ⇒ Wait）；之後必須持續有方向。
            frame = 0;
            while (!data.MovementIntent.HasMovementRequest && frame < FirstIntentFrames)
            {
                yield return null;
                frame++;
            }
            Assert.IsTrue(data.MovementIntent.HasMovementRequest,
                $"交戰後 {FirstIntentFrames} 帧內沒有任何移動意圖——沒有相機時 AI 不得停止產生方向");

            float startDistance = HorizontalDistance(enemy.transform.position, targetPosition);
            float minAlignment = 1f;
            for (int i = 0; i < ObservedFrames; i++)
            {
                yield return null;
                Assert.IsTrue(data.CombatContext.InCombat, $"第 {i} 帧意外脫戰");
                if (HorizontalDistance(enemy.transform.position, targetPosition) <= 2.5f) break;   // 進交戰帶前才檢查 Approach

                MovementIntentData intent = data.MovementIntent;
                Vector3 d = intent.DesiredDirection;
                Assert.IsTrue(IsFinite(d) && IsFinite(intent.DesiredSpeedNormalized),
                    $"第 {i} 帧意圖含 NaN／Infinity：dir={d} speed={intent.DesiredSpeedNormalized}");
                Assert.IsTrue(intent.HasMovementRequest, $"第 {i} 帧移動意圖消失（speed={intent.DesiredSpeedNormalized}）");
                Assert.AreEqual(0f, d.y, 1e-4f, $"第 {i} 帧方向不是水平的：{d}");
                Assert.AreEqual(1f, d.magnitude, 1e-3f, $"第 {i} 帧方向不是單位向量：{d}");

                Vector3 toTarget = targetPosition - enemy.transform.position;
                toTarget.y = 0f;
                float alignment = Vector3.Dot(d, toTarget.normalized);
                minAlignment = Mathf.Min(minAlignment, alignment);
                Assert.GreaterOrEqual(alignment, MinimumAlignment,
                    $"第 {i} 帧方向沒有朝向目標：dir={d} toTarget={toTarget.normalized} dot={alignment:F3}");
            }

            // ── 6. 意圖真的被執行：敵人朝目標靠近（位移仍只由 MotionDriver 結算）──────────────────────────
            float endDistance = HorizontalDistance(enemy.transform.position, targetPosition);
            string summary = $"[AI no-camera] distance {startDistance:F2} -> {endDistance:F2} m, min alignment {minAlignment:F3}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.Less(endDistance, startDistance - 0.5f, "意圖存在但敵人沒有朝目標前進");

            // ── 7. 交戰帶內（原 EditMode 測試的情境）：Strafe／Approach／Retreat 都必須有合理的世界方向 ─────────
            Assert.IsNotNull(EngagementField, "測試需要讀 AIMovementSource._engagementMovement（欄位被改名？）");
            frame = 0;
            while (HorizontalDistance(enemy.transform.position, targetPosition) > 1.95f && frame < ReachBandFrames)
            {
                yield return null;
                frame++;
            }
            Assert.LessOrEqual(HorizontalDistance(enemy.transform.position, targetPosition), 1.95f,
                $"{ReachBandFrames} 帧內沒有走進交戰帶——沒有相機時 AI 追不到目標");

            int strafeFrames = 0, approachFrames = 0, retreatFrames = 0;
            for (int i = 0; i < BandFrames; i++)
            {
                yield return null;
                string mode = EngagementField.GetValue(movement).ToString();
                MovementIntentData intent = data.MovementIntent;
                Vector3 d = intent.DesiredDirection;
                Assert.IsTrue(IsFinite(d) && IsFinite(intent.DesiredSpeedNormalized), $"帶內第 {i} 帧意圖含 NaN／Infinity：{d}");
                Assert.IsTrue(intent.HasMovementRequest, $"帶內第 {i} 帧（{mode}）沒有移動意圖——開闊平地兩側都可走，不該停步");
                Assert.AreEqual(0f, d.y, 1e-4f, $"帶內第 {i} 帧方向不是水平的：{d}");
                Assert.AreEqual(1f, d.magnitude, 1e-3f, $"帶內第 {i} 帧方向不是單位向量：{d}");

                Vector3 toTarget = targetPosition - enemy.transform.position;
                toTarget.y = 0f;
                float alignment = Vector3.Dot(d, toTarget.normalized);
                switch (mode)
                {
                    case "Strafe":
                        strafeFrames++;
                        Assert.LessOrEqual(Mathf.Abs(alignment), MaximumStrafeAlignment,
                            $"帶內第 {i} 帧 Strafe 方向不是切線：dot={alignment:F3}");
                        break;
                    case "Approach":
                        approachFrames++;
                        Assert.GreaterOrEqual(alignment, 0.9f, $"帶內第 {i} 帧 Approach 沒有朝向目標：dot={alignment:F3}");
                        break;
                    case "Retreat":
                        retreatFrames++;
                        Assert.LessOrEqual(alignment, -0.9f, $"帶內第 {i} 帧 Retreat 沒有遠離目標：dot={alignment:F3}");
                        break;
                    default:
                        Assert.Fail($"帶內第 {i} 帧出現非交戰移動模式 {mode}（Hold／Recover 表示沒有方向或離網）");
                        break;
                }
            }
            string band = $"[AI no-camera] in band: strafe {strafeFrames}, approach {approachFrames}, retreat {retreatFrames} frames";
            Debug.Log(band);
            TestContext.WriteLine(band);
            Assert.GreaterOrEqual(strafeFrames, MinimumStrafeFrames,
                $"交戰帶內只有 {strafeFrames} 帧 Strafe——原測試要驗的切線分支沒有被執行到");
            AssertNoCameraAnywhere("驗證結束時");
        }

        private static void AssertNoCameraAnywhere(string when)
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            Assert.AreEqual(0, cameras.Length,
                $"前置失敗（{when}）：世界裡存在 {cameras.Length} 個 Camera——這個測試必須在沒有任何相機時執行");
            Assert.IsNull(Camera.main, $"前置失敗（{when}）：Camera.main 不是 null");
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }
}
