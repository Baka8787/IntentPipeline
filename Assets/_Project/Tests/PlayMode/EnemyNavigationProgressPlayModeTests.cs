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
    /// 🆕（2026-10-14，`docs/56` §12／§13，P5a／P5b；ADR-012 Trial）敵人導航進展與交戰站位的回歸測試——在真 NavMesh、真 Y Bot、真 Runner 上：
    /// <list type="number">
    /// <item><b>A1</b> 目標不可達 ⇒ 停在最接近的可達點待命，不推牆（P5-0 基線：被允許移動的帧 67% 沒有進展）</item>
    /// <item><b>ADR-012</b> 出拳／受擊中不回報卡住（基線 27–156 帧誤報）</item>
    /// <item><b>ADR-012</b> 被 NavMesh 不知道的障礙擋住時仍回報卡住（真陽性不能跟著消失）</item>
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
        public IEnumerator Stuck_IsStillReported_WhenPushingAnObstacleTheNavMeshDoesNotKnow()
        {
            // 🔄（2026-10-14，P5b）原本用「牆角裡 Retreat 推牆」當真陽性；P5b 讓敵人走出牆角之後那個情境不再推牆。
            //    改用一個**沒有烘進 NavMesh** 的障礙（只有碰撞體）：路徑以為暢通、角色被擋住——被允許照意圖移動卻沒有進展。
            Rig rig = Build("StuckOnHiddenObstacle", new (Vector3, Vector3)[0], Vector3.zero, new Vector3(0f, 0f, 7f));
            GameObject hidden = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(hidden);
            hidden.transform.position = new Vector3(0f, 1f, 2f);
            hidden.transform.localScale = new Vector3(8f, 2f, 0.6f);   // 不在 NavMesh 來源裡
            Physics.SyncTransforms();
            int stuckFrames = 0;
            for (int frame = 0; frame < 300; frame++)   // 5 s
            {
                yield return null;
                if (Navigator(rig).IsStuck) stuckFrames++;
            }
            string summary = $"[ADR-012] hidden obstacle push: stuck {stuckFrames}/300, z={rig.Enemy.transform.position.z:F2}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.IsTrue(rig.Data.CombatContext.InCombat, "前提：交戰中（追擊）");
            Assert.Less(rig.Enemy.transform.position.z, 2f, "前提：角色被障礙擋住");
            Assert.Greater(stuckFrames, 60, "被允許照意圖移動、卻被擋住沒有進展 ⇒ 必須判定卡住（ADR-012 真陽性）");
        }

        // =====================================================================
        // P5b（docs/56 §13）：交戰帶內的站位
        // =====================================================================

        private static readonly (Vector3, Vector3)[] CornerWalls =
        {
            (new Vector3(5f, 1f, 8.3f), new Vector3(8f, 2f, 0.6f)), (new Vector3(8.3f, 1f, 5f), new Vector3(0.6f, 2f, 8f)),
        };

        [UnityTest]
        public IEnumerator P5b_PinnedInTheCorner_MovesOutToAValidSlot_InsteadOfPushingTheWall()
        {
            Vector3 start = new Vector3(7.3f, 0f, 7.3f);
            float k(float d) => d / Mathf.Sqrt(2f);
            Rig rig = Build("P5bCorner", CornerWalls, start, new Vector3(start.x - k(1.6f), 0f, start.z - k(1.6f)));
            int pushFrames = 0, pressedFrames = 0;
            Vector3 previous = rig.Enemy.transform.position;
            for (int frame = 0; frame < 600; frame++)   // 10 s：1 s 後把目標壓到離起點 0.5 m（CornerPinned 的 T2 情境）
            {
                float d = frame < 60 ? 1.6f : 0.5f;
                rig.Target.transform.position = new Vector3(start.x - k(d), 0f, start.z - k(d));
                yield return null;
                if (frame >= 120)
                {
                    pressedFrames++;
                    Vector3 step = rig.Enemy.transform.position - previous;
                    step.y = 0f;
                    if (rig.Data.MovementIntent.HasMovementRequest && step.magnitude < 0.002f && rig.Runner.CurrentState == StateType.Move)
                        pushFrames++;
                }
                previous = rig.Enemy.transform.position;
            }
            Vector3 end = rig.Enemy.transform.position;
            Vector3 target = rig.Target.transform.position;
            float distance = Horizontal(end - target);
            bool reach = NavMesh.SamplePosition(end, out NavMeshHit e, 0.6f, NavMesh.AllAreas)
                         && NavMesh.SamplePosition(target, out NavMeshHit t, 2.5f, NavMesh.AllAreas)
                         && !NavMesh.Raycast(e.position, t.position, out _, NavMesh.AllAreas);
            float edge = NavMesh.FindClosestEdge(end, out NavMeshHit edgeHit, NavMesh.AllAreas) ? edgeHit.distance : -1f;
            string summary = $"[P5b corner] push {pushFrames}/{pressedFrames} end distance {distance:F2} reach {reach} edge {edge:F2} " +
                             $"slotChanges {rig.Movement.TacticalSlotChanges} situation {rig.Movement.TacticalSituation}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.Less(pushFrames, pressedFrames / 10, "不得持續朝牆角推（T2）");
            Assert.That(distance, Is.InRange(rig.BandMin, rig.Reach), "最後要站在交戰帶內（能出手的位置）");
            Assert.IsTrue(reach, "最後的位置要有觸及線");
            Assert.GreaterOrEqual(edge, 0.25f, "不得停在貼牆的位置（T1）");
            Assert.LessOrEqual(rig.Movement.TacticalSlotChanges, 6, "換槽次數有上限（不在牆角來回振盪）");
        }

        [UnityTest]
        public IEnumerator P5b_PillarBetween_EndsWithAReachLine_NotBehindThePillar()
        {
            var pillar = new[] { (new Vector3(0f, 1f, 2.5f), new Vector3(1.4f, 2f, 1.4f)) };
            Vector3 targetPosition = new Vector3(0f, 0f, 4.5f);
            Rig rig = Build("P5bPillar", pillar, Vector3.zero, targetPosition);
            int blockedInBand = 0, attacksBlocked = 0;
            StateType previousState = StateType.None;
            for (int frame = 0; frame < 900; frame++)   // 15 s
            {
                yield return null;
                Vector3 self = rig.Enemy.transform.position;
                float d = Horizontal(self - targetPosition);
                bool reach = NavMesh.SamplePosition(self, out NavMeshHit e, 0.6f, NavMesh.AllAreas)
                             && NavMesh.SamplePosition(targetPosition, out NavMeshHit t, 2.5f, NavMesh.AllAreas)
                             && !NavMesh.Raycast(e.position, t.position, out _, NavMesh.AllAreas);
                if (d >= rig.BandMin && d <= rig.Reach && !reach) blockedInBand++;
                StateType state = rig.Runner.CurrentState;
                if (state == StateType.Action && previousState != StateType.Action && !reach) attacksBlocked++;
                previousState = state;
            }
            string summary = $"[P5b pillar] in band but blocked {blockedInBand} frames, attacks across the pillar {attacksBlocked}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.LessOrEqual(blockedInBand, 30, "不得長時間待在攻擊距離內、卻被柱子擋住的位置（A2）");
            Assert.AreEqual(0, attacksBlocked, "不得隔柱出拳（P5-0 基線曾出現 1 次）");
        }

        [UnityTest]
        public IEnumerator P5b_OpenGround_HoldsAtTheSlot_InsteadOfStrafingForever()
        {
            Vector3 targetPosition = new Vector3(0f, 0f, 4f);
            Rig rig = Build("P5bOpen", new (Vector3, Vector3)[0], Vector3.zero, targetPosition);
            float travelledLate = 0f;
            int lateFrames = 0, holdFrames = 0;
            Vector3 previous = rig.Enemy.transform.position;
            for (int frame = 0; frame < 900; frame++)   // 15 s；最後 10 s 當作穩態
            {
                yield return null;
                Vector3 step = rig.Enemy.transform.position - previous;
                step.y = 0f;
                previous = rig.Enemy.transform.position;
                if (frame < 300) continue;
                lateFrames++;
                travelledLate += step.magnitude;
                if (rig.Movement.TacticalSituation == TacticalSituation.Slot && !rig.Data.MovementIntent.HasMovementRequest) holdFrames++;
            }
            float d = Horizontal(rig.Enemy.transform.position - targetPosition);
            string summary = $"[P5b open] last 10 s travelled {travelledLate:F2} m, hold {holdFrames}/{lateFrames}, distance {d:F2}, slotChanges {rig.Movement.TacticalSlotChanges}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.Less(travelledLate, 2f, "目標不動、位置有效 ⇒ 不為了「看起來在動」而一直側移（D3；基線約 1.2 m／s）");
            Assert.Greater(holdFrames, lateFrames / 2, "大部分時間在槽上待命");
            Assert.That(d, Is.InRange(rig.BandMin, rig.Reach), "待命在交戰帶內（打得到的距離）");
            Assert.LessOrEqual(rig.Movement.TacticalSlotChanges, 2, "開闊平地不換槽");
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
            public float BandMin;   // prefab 的實際交戰帶（Y Bot：[1.25, 1.6]），不寫死
            public float Reach;
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
            float bandMin = (float)typeof(AIMovementSource).GetField("minimumEngagementDistance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(movement);
            float reach = (float)typeof(AIInputSource).GetField("attackRange", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy.GetComponent<AIInputSource>());
            return new Rig { Enemy = enemy, Target = target, Movement = movement, Runner = runner, Data = runner.RuntimeData,
                             BandMin = bandMin, Reach = reach };
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
