using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Core.Survivability;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🔬（2026-10-13，`docs/56` §11，P5-0）**戰鬥站位的外部行為量測。** 只量、不判——不是回歸測試。
    ///
    /// <para><b>為什麼是外部指標</b></para>
    /// 站位元件自己的評分不能拿來證明站位變好（答題的人不能改自己的考卷）。這裡只量
    /// 「從外面看得到」的事：多少時間站在打得到的位置、出手時是否隔牆、推牆帧數、走了多遠、
    /// 模式切換次數、路徑請求數，以及出拳／受擊期間 <c>IsStuck</c> 的誤報。
    ///
    /// <para><b>情境</b></para>
    /// 每個情境在執行期建一個獨立的小場景（地面＋方塊障礙＋執行期烘焙的 NavMesh），放真 Y Bot prefab，
    /// 目標是一個由測試逐帧移動的空 GameObject（沒有生命值 ⇒ 視為活著；敵人的拳打不到任何東西）。
    /// 不載出貨場景、不改資產。亂數以固定種子初始化（側移方向、攻擊冷卻變異）。
    ///
    /// <para><b>帧對齊</b></para>
    /// 測試協程在 Update 之後、LateUpdate（MotionDriver 位移）之前恢復 ⇒ 第 N 次恢復時看到的位置是第 N−1 帧
    /// 位移後的結果。所以「這一步有沒有進展」用**上一次恢復時**記下的意圖、速度與 FSM 狀態來配對。
    ///
    /// <para><b>重跑</b></para>
    /// <c>bash docs/artifacts/editor-memory/safe_unity_test.sh --mode PlayMode --include-explicit --filter EnemyCombatPositioningMetrics</c>。
    /// 每個情境一行寫進 <c>Logs/p5-metrics/metrics.tsv</c>（含 commit 與變體標籤），同時印在 Console／TestContext。
    /// </summary>
    [Category("Probe")]
    public sealed class EnemyCombatPositioningMetrics
    {
        private const float FixedDelta = 1f / 60f;
        private const float BandMin = 1.25f;          // AIMovementSource.minimumEngagementDistance（Y Bot）
        private const float AttackRange = 2f;         // AIInputSource.attackRange（W10：＝ maximumEngagementDistance）
        private const int Seed = 1234;

        private static readonly FieldInfo EngagementField = typeof(AIMovementSource).GetField("_engagementMovement", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo StrafeBlockedField = typeof(AIMovementSource).GetField("_strafeBlocked", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo StrafeSignField = typeof(AIMovementSource).GetField("_strafeDirectionSign", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NavigatorField = typeof(AIMovementSource).GetField("_navigator", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MotorSpeedField = typeof(MotionDriver).GetField("moveSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

        private Scene _scene;
        private NavMeshDataInstance _navMesh;
        private readonly List<Object> _created = new List<Object>();

        public enum Scenario { OpenStatic, Corner, Pillar, ThinWall, Unreachable, MovingSlow, Kiting, AttackAndHurt }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (Object o in _created) if (o != null) Object.Destroy(o);
            _created.Clear();
            if (_navMesh.valid) NavMesh.RemoveNavMeshData(_navMesh);
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTest, Explicit("P5-0 量測探針：只量不判，不進回歸套件")]
        public IEnumerator Measure([Values] Scenario scenario)
        {
            yield return Run(scenario, "current");
        }

        // =====================================================================
        // 情境定義
        // =====================================================================

        private struct Setup
        {
            public Vector3 EnemyStart;
            public Vector3[] BoxCenters;
            public Vector3[] BoxSizes;
            public float Seconds;
            public float DamageInterval;   // 0 ＝ 不打敵人
            public System.Func<float, Vector3> Target;
        }

        private static Setup Describe(Scenario scenario)
        {
            var none = new Vector3[0];
            switch (scenario)
            {
                case Scenario.OpenStatic:
                    return new Setup { EnemyStart = Vector3.zero, BoxCenters = none, BoxSizes = none, Seconds = 20f,
                        Target = t => new Vector3(0f, 0f, 4f) };
                case Scenario.Corner:
                    // 兩道牆的內面在 x＝8、z＝8；目標離兩面各 0.4 m（落在 NavMesh 侵蝕帶外——貼牆的玩家）。
                    return new Setup { EnemyStart = new Vector3(3f, 0f, 3f), Seconds = 20f,
                        BoxCenters = new[] { new Vector3(5f, 1f, 8.3f), new Vector3(8.3f, 1f, 5f) },
                        BoxSizes = new[] { new Vector3(8f, 2f, 0.6f), new Vector3(0.6f, 2f, 8f) },
                        Target = t => new Vector3(7.6f, 0f, 7.6f) };
                case Scenario.Pillar:
                    return new Setup { EnemyStart = Vector3.zero, Seconds = 20f,
                        BoxCenters = new[] { new Vector3(0f, 1f, 2.5f) },
                        BoxSizes = new[] { new Vector3(1.4f, 2f, 1.4f) },
                        Target = t => new Vector3(0f, 0f, 4.5f) };
                case Scenario.ThinWall:
                    // 敵人與目標隔著一道薄牆、水平 2.0 m（在攻擊距離上）；牆寬 ±3 m，繞過去才有觸及線。
                    return new Setup { EnemyStart = new Vector3(0f, 0f, 1f), Seconds = 20f,
                        BoxCenters = new[] { new Vector3(0f, 1f, 2f) },
                        BoxSizes = new[] { new Vector3(6f, 2f, 0.3f) },
                        Target = t => new Vector3(0f, 0f, 3f) };
                case Scenario.Unreachable:
                    // 封閉圍欄（內部是獨立 NavMesh 島）；外側最近可走點離目標水平約 3.3 m > 攻擊距離。
                    return new Setup { EnemyStart = new Vector3(0f, 0f, -1f), Seconds = 20f,
                        BoxCenters = new[] { new Vector3(0f, 1f, 8.5f), new Vector3(0f, 1f, 3.5f), new Vector3(-2.5f, 1f, 6f), new Vector3(2.5f, 1f, 6f) },
                        BoxSizes = new[] { new Vector3(5.6f, 2f, 0.6f), new Vector3(5.6f, 2f, 0.6f), new Vector3(0.6f, 2f, 5.6f), new Vector3(0.6f, 2f, 5.6f) },
                        Target = t => new Vector3(0f, 0f, 6f) };
                case Scenario.MovingSlow:
                    // 以 1.0 m/s（低於敵人約 1.64 m/s）繞 (0,0,4) 半徑 2.5 的圓。
                    return new Setup { EnemyStart = Vector3.zero, BoxCenters = none, BoxSizes = none, Seconds = 25f,
                        Target = t => Circle(new Vector3(0f, 0f, 4f), 2.5f, 1.0f, t) };
                case Scenario.Kiting:
                    // 以 4.0 m/s（遠高於敵人）繞原點半徑 5 的圓：敵人追不上。
                    return new Setup { EnemyStart = Vector3.zero, BoxCenters = none, BoxSizes = none, Seconds = 20f,
                        Target = t => Circle(Vector3.zero, 5f, 4.0f, t) };
                default: // AttackAndHurt
                    return new Setup { EnemyStart = Vector3.zero, BoxCenters = none, BoxSizes = none, Seconds = 20f,
                        DamageInterval = 2f, Target = t => new Vector3(0f, 0f, 4f) };
            }
        }

        private static Vector3 Circle(Vector3 center, float radius, float speed, float t)
        {
            float angle = speed / radius * t;
            return center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
        }

        // =====================================================================
        // 執行與量測
        // =====================================================================

        /// <summary>單一情境、單一變體。<paramref name="variant"/> 只是標籤（P5a 之後用來分開／開啟站位）。</summary>
        internal IEnumerator Run(Scenario scenario, string variant, System.Action<AIMovementSource> configure = null)
        {
            Setup setup = Describe(scenario);
            Time.captureDeltaTime = FixedDelta;
            _scene = SceneManager.CreateScene("P5Metrics_" + scenario);
            SceneManager.SetActiveScene(_scene);
            BuildArena(setup);

            Random.InitState(Seed);
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Y Bot.prefab");
            Vector3 spawn = setup.EnemyStart + Vector3.up * 0.05f;
            GameObject enemy = Object.Instantiate(prefab, spawn, Quaternion.identity);
            _created.Add(enemy);
            foreach (Animator animator in enemy.GetComponentsInChildren<Animator>(true))
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var target = new GameObject("P5Target");
            _created.Add(target);
            target.transform.position = setup.Target(0f);

            var movement = enemy.GetComponent<AIMovementSource>();
            movement.SetTargetForTests(target.transform);
            // Leash 放到很大：這裡量的是交戰中的站位，不是脫戰。
            enemy.GetComponent<EnemyTerritory>().ConfigureForTests(spawn, roam: 30f, soft: 40f, hard: 50f);
            configure?.Invoke(movement);

            var runner = enemy.GetComponent<CharacterPipelineRunner>();
            var health = enemy.GetComponent<CharacterHealth>();
            MotionDriver motor = enemy.GetComponentInChildren<MotionDriver>();
            PlayerRuntimeData data = runner.RuntimeData;

            var m = new Metrics { Scenario = scenario.ToString(), Variant = variant };
            int frames = Mathf.RoundToInt(setup.Seconds / FixedDelta);
            int nextDamageFrame = setup.DamageInterval > 0f ? Mathf.RoundToInt(1.5f / FixedDelta) : int.MaxValue;

            Vector3 previous = enemy.transform.position;
            bool hasPrevious = false;
            StateType previousState = StateType.None;
            float previousExpectedStep = 0f;
            bool previousWantsMove = false;
            object previousEngagement = null;
            int previousSign = 0, hurtRun = 0, actionRun = 0;
            float motorSpeed = MotorSpeedField != null && motor != null ? (float)MotorSpeedField.GetValue(motor) : 0f;

            for (int frame = 0; frame < frames; frame++)
            {
                target.transform.position = setup.Target(frame * FixedDelta);
                if (frame == nextDamageFrame)
                {
                    health.ApplyDamage(1f);
                    nextDamageFrame += Mathf.RoundToInt(setup.DamageInterval / FixedDelta);
                }
                yield return null;

                Vector3 position = enemy.transform.position;
                StateType state = runner.CurrentState;
                bool inCombat = data.CombatContext.InCombat;
                Vector3 toTarget = target.transform.position - position;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;
                bool reachClear = IsReachClear(position, target.transform.position);
                bool inBand = distance >= BandMin && distance <= AttackRange;

                // ── 上一帧的位移 vs 上一帧被允許的位移（帧對齊見類別註解）──
                if (hasPrevious)
                {
                    Vector3 step = position - previous;
                    step.y = 0f;
                    float moved = step.magnitude;
                    m.Travelled += moved;
                    bool baseMovementAllowed = previousState == StateType.Idle || previousState == StateType.Move;
                    if (baseMovementAllowed && previousWantsMove && previousExpectedStep > 0.3f * FixedDelta)
                    {
                        m.AllowedMoveFrames++;
                        if (moved < 0.25f * previousExpectedStep) m.NoProgressFrames++;
                    }
                }

                if (inCombat)
                {
                    m.CombatFrames++;
                    m.DistanceSum += distance;
                    if (inBand) m.InBandFrames++;
                    if (inBand && reachClear)
                    {
                        m.AttackValidFrames++;
                        if (m.FirstAttackValidFrame < 0) m.FirstAttackValidFrame = frame;
                    }
                    if (inBand && !reachClear) m.InBandReachBlockedFrames++;
                }

                if (state == StateType.Action && previousState != StateType.Action)
                {
                    m.AttacksStarted++;
                    if (!reachClear || distance > AttackRange) m.AttacksWhileNotValid++;
                }
                if (state == StateType.Hurt) { hurtRun++; m.HurtFrames++; }
                else { if (hurtRun > 0) { m.HurtEpisodes++; m.MaxHurtRun = Mathf.Max(m.MaxHurtRun, hurtRun); } hurtRun = 0; }
                if (state == StateType.Action) { actionRun++; m.ActionFrames++; }
                else { if (actionRun > 0) m.MaxActionRun = Mathf.Max(m.MaxActionRun, actionRun); actionRun = 0; }

                object engagement = EngagementField.GetValue(movement);
                if (previousEngagement != null && !Equals(engagement, previousEngagement)) m.EngagementSwitches++;
                if (engagement.ToString() == "Recover") m.RecoverFrames++;
                if ((bool)StrafeBlockedField.GetValue(movement)) m.StrafeBlockedFrames++;
                int sign = (int)StrafeSignField.GetValue(movement);
                if (previousSign != 0 && sign != previousSign) m.StrafeFlips++;
                previousSign = sign;
                previousEngagement = engagement;

                var navigator = NavigatorField.GetValue(movement) as EnemyNavigator;
                if (navigator != null && navigator.IsStuck)
                {
                    m.StuckFrames++;
                    if (state == StateType.Action || state == StateType.Hurt) m.StuckDuringActionOrHurtFrames++;
                }

                previous = position;
                hasPrevious = true;
                previousState = state;
                previousWantsMove = data.MovementIntent.HasMovementRequest && data.MoveDirection.sqrMagnitude > 0.001f;
                previousExpectedStep = data.MoveSpeed * motorSpeed * FixedDelta;
            }

            var finalNavigator = NavigatorField.GetValue(movement) as EnemyNavigator;
            m.PathRequests = finalNavigator != null ? finalNavigator.PathRequestCount : -1;
            m.Frames = frames;

            string line = m.Summary();
            Debug.Log(line);
            TestContext.WriteLine(line);
            AppendTsv(m);

            Assert.Greater(m.CombatFrames, frames / 2, "前提：大部分時間必須在交戰中（aggro 8 m）");
            Assert.Greater(motorSpeed, 0f, "前提：讀得到 MotionDriver 的 max-speed");
        }

        private static bool IsReachClear(Vector3 self, Vector3 target)
        {
            // 目標投影到 NavMesh 再做直線檢查：貼牆的目標本身落在侵蝕帶外，直接 Raycast 會誤判隔牆。
            if (!NavMesh.SamplePosition(self, out NavMeshHit from, 0.6f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(target, out NavMeshHit to, 2.5f, NavMesh.AllAreas)) return false;
            return !NavMesh.Raycast(from.position, to.position, out _, NavMesh.AllAreas);
        }

        private void BuildArena(Setup setup)
        {
            var sources = new List<NavMeshBuildSource>();
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            AddSource(sources, ground);
            for (int i = 0; i < setup.BoxCenters.Length; i++)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.position = setup.BoxCenters[i];
                box.transform.localScale = setup.BoxSizes[i];
                AddSource(sources, box);
            }
            NavMeshData navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Vector3.zero, new Vector3(70f, 10f, 70f)), Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(navData, "測試用 NavMesh 建置失敗");
            _navMesh = NavMesh.AddNavMeshData(navData);
        }

        private static void AddSource(List<NavMeshBuildSource> sources, GameObject box)
        {
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = box.transform.localScale,
                transform = Matrix4x4.TRS(box.transform.position, Quaternion.identity, Vector3.one),
                area = 0,
            });
        }

        // =====================================================================
        // 輸出
        // =====================================================================

        private sealed class Metrics
        {
            public string Scenario, Variant;
            public int Frames, CombatFrames, InBandFrames, AttackValidFrames, InBandReachBlockedFrames, FirstAttackValidFrame = -1;
            public int AttacksStarted, AttacksWhileNotValid;
            public int AllowedMoveFrames, NoProgressFrames, RecoverFrames, StrafeBlockedFrames;
            public int EngagementSwitches, StrafeFlips, PathRequests;
            public int StuckFrames, StuckDuringActionOrHurtFrames;
            public int ActionFrames, MaxActionRun, HurtFrames, HurtEpisodes, MaxHurtRun;
            public float Travelled, DistanceSum;

            private float Seconds(int f) => f * FixedDelta;
            public float AttackValidRatio => CombatFrames > 0 ? (float)AttackValidFrames / CombatFrames : 0f;
            public float MetersPerValidSecond => AttackValidFrames > 0 ? Travelled / Seconds(AttackValidFrames) : float.PositiveInfinity;

            public string Summary() =>
                $"[P5 metrics] {Scenario}/{Variant} combat={Seconds(CombatFrames):F1}s attackValid={AttackValidRatio:P1} " +
                $"inBand={Seconds(InBandFrames):F1}s inBandBlocked={Seconds(InBandReachBlockedFrames):F1}s firstValid={Seconds(FirstAttackValidFrame):F2}s " +
                $"meanDist={(CombatFrames > 0 ? DistanceSum / CombatFrames : 0f):F2}m travelled={Travelled:F2}m m/validSec={MetersPerValidSecond:F2} " +
                $"attacks={AttacksStarted} attacksNotValid={AttacksWhileNotValid} noProgress={NoProgressFrames}/{AllowedMoveFrames} " +
                $"recover={RecoverFrames} strafeBlocked={StrafeBlockedFrames} modeSwitches={EngagementSwitches} strafeFlips={StrafeFlips} " +
                $"pathRequests={PathRequests} stuck={StuckFrames} stuckInActionHurt={StuckDuringActionOrHurtFrames} " +
                $"action={ActionFrames}f(max {MaxActionRun}) hurt={HurtFrames}f/{HurtEpisodes}ep(max {MaxHurtRun})";

            public static string Header =>
                "utc\tcommit\tscenario\tvariant\tframes\tcombatFrames\tattackValidRatio\tinBandFrames\tinBandBlockedFrames\tfirstValidFrame\t" +
                "meanDistance\ttravelled\tmetersPerValidSecond\tattacks\tattacksNotValid\tnoProgressFrames\tallowedMoveFrames\t" +
                "recoverFrames\tstrafeBlockedFrames\tmodeSwitches\tstrafeFlips\tpathRequests\tstuckFrames\tstuckInActionHurt\t" +
                "actionFrames\tmaxActionRun\thurtFrames\thurtEpisodes\tmaxHurtRun";

            public string Row(string utc, string commit) => string.Join("\t", new[]
            {
                utc, commit, Scenario, Variant, Frames.ToString(), CombatFrames.ToString(), AttackValidRatio.ToString("F4"),
                InBandFrames.ToString(), InBandReachBlockedFrames.ToString(), FirstAttackValidFrame.ToString(),
                (CombatFrames > 0 ? DistanceSum / CombatFrames : 0f).ToString("F3"), Travelled.ToString("F3"),
                MetersPerValidSecond.ToString("F3"), AttacksStarted.ToString(), AttacksWhileNotValid.ToString(),
                NoProgressFrames.ToString(), AllowedMoveFrames.ToString(), RecoverFrames.ToString(), StrafeBlockedFrames.ToString(),
                EngagementSwitches.ToString(), StrafeFlips.ToString(), PathRequests.ToString(), StuckFrames.ToString(),
                StuckDuringActionOrHurtFrames.ToString(), ActionFrames.ToString(), MaxActionRun.ToString(),
                HurtFrames.ToString(), HurtEpisodes.ToString(), MaxHurtRun.ToString(),
            });
        }

        private static void AppendTsv(Metrics m)
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "p5-metrics"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "metrics.tsv");
            bool exists = File.Exists(path);
            using (var writer = new StreamWriter(path, append: true))
            {
                if (!exists) writer.WriteLine(Metrics.Header);
                writer.WriteLine(m.Row(System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), ReadCommit()));
            }
        }

        private static string ReadCommit()
        {
            try
            {
                string git = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".git"));
                string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
                if (!head.StartsWith("ref: ")) return head.Substring(0, Mathf.Min(7, head.Length));
                string refPath = Path.Combine(git, head.Substring(5).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(refPath)) return File.ReadAllText(refPath).Trim().Substring(0, 7);
                foreach (string line in File.ReadAllLines(Path.Combine(git, "packed-refs")))
                    if (line.EndsWith(head.Substring(5))) return line.Substring(0, 7);
            }
            catch (System.Exception) { }
            return "unknown";
        }
    }
}
