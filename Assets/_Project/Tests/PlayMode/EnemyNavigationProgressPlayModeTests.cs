using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Core.Survivability;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🆕（2026-10-14，`docs/56` §12，P5a；ADR-012 Trial）敵人導航進展的回歸測試——在真 NavMesh、真 Y Bot、真 Runner 上：
    /// <list type="number">
    /// <item><b>A1</b> 目標不可達 ⇒ 停在最接近的可達點待命，不推牆（P5-0 基線：被允許移動的帧 67% 沒有進展）</item>
    /// <item><b>ADR-012</b> 出拳／受擊中不回報卡住（基線 27–156 帧誤報）</item>
    /// <item><b>ADR-012</b> 真的推牆時仍回報卡住（真陽性不能跟著消失）</item>
    /// </list>
    /// 情境幾何與 <c>EnemyCombatPositioningMetrics</c> 的 Unreachable／AttackAndHurt／CornerPinned 相同。
    /// </summary>
    public sealed class EnemyNavigationProgressPlayModeTests
    {
        private const float FixedDelta = 1f / 60f;
        private static readonly FieldInfo NavigatorField =
            typeof(AIMovementSource).GetField("_navigator", BindingFlags.Instance | BindingFlags.NonPublic);

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

        // =====================================================================
        // A1：目標不可達
        // =====================================================================

        [UnityTest]
        public IEnumerator A1_UnreachableTarget_HoldsAtClosestReachablePoint_WithoutPushing()
        {
            var walls = new[]
            {
                (new Vector3(0f, 1f, 8.5f), new Vector3(5.6f, 2f, 0.6f)), (new Vector3(0f, 1f, 3.5f), new Vector3(5.6f, 2f, 0.6f)),
                (new Vector3(-2.5f, 1f, 6f), new Vector3(0.6f, 2f, 5.6f)), (new Vector3(2.5f, 1f, 6f), new Vector3(0.6f, 2f, 5.6f)),
            };
            Vector3 targetPosition = new Vector3(0f, 0f, 6f);   // 封閉圍欄內（獨立的 NavMesh 島）
            Rig rig = Build("A1Unreachable", walls, new Vector3(0f, 0f, -1f), targetPosition);

            var probePath = new NavMeshPath();
            Assert.IsTrue(NavMesh.SamplePosition(rig.Enemy.transform.position, out NavMeshHit from, 0.6f, NavMesh.AllAreas));
            Assert.IsTrue(NavMesh.SamplePosition(targetPosition, out NavMeshHit to, 2.5f, NavMesh.AllAreas));
            NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, probePath);
            Assert.AreEqual(NavMeshPathStatus.PathPartial, probePath.status, "前置：目標必須不可達（部分路徑）");
            Vector3 closest = probePath.corners[probePath.corners.Length - 1];

            int enteredFrame = -1, attacks = 0, stuckInHold = 0, intentInHold = 0, holdFrames = 0;
            StateType previousState = StateType.None;
            Vector3 holdStart = default;
            const int Frames = 720;               // 12 s
            const int HoldWindowStart = 480;      // 最後 4 s 當作穩態
            for (int frame = 0; frame < Frames; frame++)
            {
                rig.Target.transform.position = targetPosition;
                yield return null;
                if (enteredFrame < 0 && rig.Movement.TacticalSituation == TacticalSituation.Unreachable) enteredFrame = frame;
                StateType state = rig.Runner.CurrentState;
                if (state == StateType.Action && previousState != StateType.Action) attacks++;
                previousState = state;
                if (frame == HoldWindowStart) holdStart = rig.Enemy.transform.position;
                if (frame >= HoldWindowStart)
                {
                    holdFrames++;
                    if (Navigator(rig).IsStuck) stuckInHold++;
                    if (rig.Data.MovementIntent.HasMovementRequest) intentInHold++;
                }
            }

            Vector3 end = rig.Enemy.transform.position;
            float drift = Horizontal(end - holdStart);
            float fromClosest = Horizontal(end - closest);
            string summary = $"[P5a A1] entered@{enteredFrame} drift(last 4 s)={drift:F3} m fromClosest={fromClosest:F2} m " +
                             $"intent {intentInHold}/{holdFrames} stuck {stuckInHold}/{holdFrames} attacks {attacks}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);

            Assert.IsTrue(rig.Data.CombatContext.InCombat, "前提：全程交戰中");
            Assert.That(enteredFrame, Is.InRange(0, 300), "5 s 內必須判定目標不可達");
            Assert.LessOrEqual(fromClosest, EnemyTacticalPositioning.ArriveExitRadius + 0.1f, "必須待在最接近目標的可達點");
            Assert.Less(drift, 0.1f, "待命期間不得推牆或來回走");
            Assert.LessOrEqual(intentInHold, holdFrames / 10, "待命期間幾乎不發布移動意圖");
            Assert.AreEqual(0, stuckInHold, "待命不是卡住");
            Assert.AreEqual(0, attacks, "最接近點仍在攻擊距離外，不得出手");
        }

        // =====================================================================
        // ADR-012：出拳／受擊中不回報卡住
        // =====================================================================

        [UnityTest]
        public IEnumerator Stuck_IsNotReported_WhilePunchingOrHurt()
        {
            Rig rig = Build("NoStuckInAction", new (Vector3, Vector3)[0], Vector3.zero, new Vector3(0f, 0f, 4f));
            var health = rig.Enemy.GetComponent<CharacterHealth>();
            int actionFrames = 0, hurtFrames = 0, stuckInActionOrHurt = 0, stuckFrames = 0;
            for (int frame = 0; frame < 900; frame++)   // 15 s
            {
                if (frame >= 90 && frame % 120 == 90) health.ApplyDamage(1f);   // 1.5 s 起每 2 s 受擊一次
                yield return null;
                StateType state = rig.Runner.CurrentState;
                bool stuck = Navigator(rig).IsStuck;
                if (state == StateType.Action) actionFrames++;
                if (state == StateType.Hurt) hurtFrames++;
                if (stuck) stuckFrames++;
                if (stuck && (state == StateType.Action || state == StateType.Hurt)) stuckInActionOrHurt++;
            }
            string summary = $"[ADR-012] action {actionFrames} hurt {hurtFrames} stuck {stuckFrames} stuckInActionOrHurt {stuckInActionOrHurt}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);

            Assert.Greater(actionFrames, 60, "前提：有出拳");
            Assert.Greater(hurtFrames, 60, "前提：有受擊");
            Assert.AreEqual(0, stuckInActionOrHurt, "合法的攻擊與受擊不得算作卡住（ADR-012 D5）");
            Assert.AreEqual(0, stuckFrames, "開闊平地、沒有任何阻擋：整段都不該卡住");
        }

        // =====================================================================
        // ADR-012：真的推牆時仍回報卡住
        // =====================================================================

        [UnityTest]
        public IEnumerator Stuck_IsStillReported_WhenRetreatPushesIntoTheCorner()
        {
            var walls = new[]
            {
                (new Vector3(5f, 1f, 8.3f), new Vector3(8f, 2f, 0.6f)), (new Vector3(8.3f, 1f, 5f), new Vector3(0.6f, 2f, 8f)),
            };
            Vector3 start = new Vector3(7.3f, 0f, 7.3f);
            float k(float d) => d / Mathf.Sqrt(2f);
            Rig rig = Build("StuckInCorner", walls, start, new Vector3(start.x - k(1.6f), 0f, start.z - k(1.6f)));
            int stuckFrames = 0, stuckWhilePressed = 0;
            for (int frame = 0; frame < 600; frame++)   // 10 s：1 s 後把目標壓到離起點 0.5 m（背貼兩牆時仍 < 1.25 m ⇒ Retreat 推牆）
            {
                float d = frame < 60 ? 1.6f : 0.5f;
                rig.Target.transform.position = new Vector3(start.x - k(d), 0f, start.z - k(d));
                yield return null;
                bool stuck = Navigator(rig).IsStuck;
                if (stuck) stuckFrames++;
                if (stuck && frame >= 120) stuckWhilePressed++;
            }
            string summary = $"[ADR-012] corner push: stuck {stuckFrames} (while pressed {stuckWhilePressed})";
            Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.Greater(stuckWhilePressed, 60, "Retreat 朝牆角推、沒有進展 ⇒ 必須仍被判定卡住（真陽性不得消失）");
        }

        // =====================================================================
        // helpers
        // =====================================================================

        private struct Rig
        {
            public GameObject Enemy;
            public GameObject Target;
            public AIMovementSource Movement;
            public CharacterPipelineRunner Runner;
            public PlayerRuntimeData Data;
        }

        private Rig Build(string name, (Vector3 center, Vector3 size)[] boxes, Vector3 enemyStart, Vector3 targetStart)
        {
            Time.captureDeltaTime = FixedDelta;
            _scene = SceneManager.CreateScene(name);
            SceneManager.SetActiveScene(_scene);

            var sources = new List<NavMeshBuildSource>();
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(ground);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            sources.Add(Source(ground));
            foreach (var (center, size) in boxes)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _created.Add(box);
                box.transform.position = center;
                box.transform.localScale = size;
                sources.Add(Source(box));
            }
            NavMeshData navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Vector3.zero, new Vector3(70f, 10f, 70f)), Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(navData, "前置失敗：NavMesh 建置失敗");
            _navMesh = NavMesh.AddNavMeshData(navData);

            Random.InitState(1234);
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Y Bot.prefab");
            Vector3 spawn = enemyStart + Vector3.up * 0.05f;
            GameObject enemy = Object.Instantiate(prefab, spawn, Quaternion.identity);
            _created.Add(enemy);
            foreach (Animator animator in enemy.GetComponentsInChildren<Animator>(true))
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var target = new GameObject(name + "Target");
            _created.Add(target);
            target.transform.position = targetStart;

            var movement = enemy.GetComponent<AIMovementSource>();
            movement.SetTargetForTests(target.transform);
            enemy.GetComponent<EnemyTerritory>().ConfigureForTests(spawn, roam: 30f, soft: 40f, hard: 50f);
            var runner = enemy.GetComponent<CharacterPipelineRunner>();
            return new Rig { Enemy = enemy, Target = target, Movement = movement, Runner = runner, Data = runner.RuntimeData };
        }

        private static NavMeshBuildSource Source(GameObject box) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = box.transform.localScale,
            transform = Matrix4x4.TRS(box.transform.position, Quaternion.identity, Vector3.one),
            area = 0,
        };

        private static EnemyNavigator Navigator(Rig rig)
        {
            var navigator = NavigatorField.GetValue(rig.Movement) as EnemyNavigator;
            Assert.IsNotNull(navigator, "前置失敗：讀不到 AIMovementSource._navigator");
            return navigator;
        }

        private static float Horizontal(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}
