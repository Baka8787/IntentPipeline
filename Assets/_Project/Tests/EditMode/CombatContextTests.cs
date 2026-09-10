using NUnit.Framework;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Combat;
using Project.Core.Facing;
using Project.Core.StateMachine;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Look;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class CombatContextTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void TC1_ActionIntent_EntersCombatImmediately()
        {
            GameObject player = CreatePlayer(out PlayerCombatContextSource source);
            var data = new PlayerRuntimeData();
            data.Intent.RequestedActionSlot = ActionSlot.Slot1;

            source.Tick(data, 10f);

            Assert.IsTrue(data.CombatContext.InCombat);
            Destroy(player);
        }

        [Test]
        public void TC2_Disengage_RequiresDistanceAndTimeTogether()
        {
            Assert.IsFalse(PlayerCombatContextSource.ShouldDisengage(
                false, true, 4f, 0f, 5f), "只有距離成立、時間未到，仍須留在戰鬥");
            Assert.IsFalse(PlayerCombatContextSource.ShouldDisengage(
                true, true, 6f, 0f, 5f), "只有時間成立、仍有合法目標，仍須留在戰鬥");
            Assert.IsTrue(PlayerCombatContextSource.ShouldDisengage(
                false, true, 6f, 0f, 5f), "距離與時間都成立才離開");
        }

        [Test]
        public void TC3_EnterLeaveRadiusHysteresis_DoesNotOscillateBetweenThresholds()
        {
            GameObject player = CreatePlayer(out PlayerCombatContextSource source);
            GameObject enemy = CreateEnemy(new Vector3(0f, 0f, 5.5f));
            var data = new PlayerRuntimeData();
            Physics.SyncTransforms();

            source.Tick(data, 0f);
            Assert.IsTrue(data.CombatContext.InCombat);
            Assert.IsTrue(data.CombatContext.HasTarget);

            enemy.transform.position = new Vector3(0f, 0f, 7.5f);
            Physics.SyncTransforms();
            source.Tick(data, 1f);

            Assert.IsTrue(data.CombatContext.InCombat, "enter 與 leave 半徑之間不得退出");
            Assert.IsTrue(data.CombatContext.HasTarget, "遲滯區內的既有目標仍合法");

            Destroy(player, enemy);
        }

        [Test]
        public void TC4_StickyTarget_NearerEnemyDoesNotStealLegalTarget()
        {
            GameObject player = CreatePlayer(out PlayerCombatContextSource source);
            GameObject retained = CreateEnemy(new Vector3(0f, 0f, 4f));
            GameObject newcomer = CreateEnemy(new Vector3(0f, 0f, 5f));
            var data = new PlayerRuntimeData();
            Physics.SyncTransforms();

            source.Tick(data, 0f);
            Vector3 retainedPoint = data.CombatContext.TargetPosition;

            newcomer.transform.position = new Vector3(0f, 0f, 1f);
            Physics.SyncTransforms();
            source.Tick(data, 1f);

            Assert.Less((data.CombatContext.TargetPosition - retainedPoint).sqrMagnitude, Epsilon,
                "目前目標仍合法時，更近的新敵人不得奪取目標");
            Destroy(player, retained, newcomer);
        }

        [Test]
        public void TC5_InvalidTarget_ReselectsThenClearsWhenNoCandidateRemains()
        {
            GameObject player = CreatePlayer(out PlayerCombatContextSource source);
            GameObject first = CreateEnemy(new Vector3(0f, 0f, 2f));
            GameObject second = CreateEnemy(new Vector3(0f, 0f, 4f));
            var data = new PlayerRuntimeData();
            Physics.SyncTransforms();

            source.Tick(data, 0f);
            first.SetActive(false);
            Physics.SyncTransforms();
            source.Tick(data, 1f);

            Assert.IsTrue(data.CombatContext.HasTarget, "目前目標停用後應重選合法候選");
            Assert.AreEqual(4f, data.CombatContext.TargetPosition.z, Epsilon);

            second.transform.position = new Vector3(0f, 0f, 10f);
            Physics.SyncTransforms();
            source.Tick(data, 2f);

            Assert.IsFalse(data.CombatContext.HasTarget, "所有候選超出 leaveRadius 後不得保留失效目標");
            Assert.IsTrue(data.CombatContext.InCombat, "時間尚未到，距離單獨成立不得立即退出");
            Destroy(player, first, second);
        }

        [Test]
        public void TC6_FacingPriority_CommitmentThenMovementThenNoRequest()
        {
            var data = new PlayerRuntimeData
            {
                CombatContext = new CombatContextData
                {
                    InCombat = true,
                    HasTarget = true,
                    TargetPosition = new Vector3(10f, 3f, 0f)
                },
                MoveDirection = Vector3.left
            };
            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                true, Vector3.forward, data.MoveDirection,
                out Vector3 direction, out bool fromActionCommitment));
            Assert.AreEqual(Vector3.forward, direction, "Action commitment 必須壓過 movement direction");
            Assert.IsTrue(fromActionCommitment, "方向來源必須同行回報，Tick 才能只對 Action 套死區");

            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                false, default, data.MoveDirection, out direction, out fromActionCommitment));
            Assert.AreEqual(Vector3.left, direction,
                "InCombat 且有目標但無 Action commitment 時，facing 仍必須跟隨 MoveDirection");
            Assert.IsFalse(fromActionCommitment, "movement 來源必須被標成不套用站定死區");

            data.MoveDirection = Vector3.zero;
            Assert.IsFalse(CharacterFacingSource.TryResolveFacing(
                false, default, data.MoveDirection, out _, out _),
                "無 Action commitment 且無移動時不得送 request");
        }

        [Test]
        public void TC6A_ActionCommitmentFacing_UsesAngularDeadzone()
        {
            Vector3 currentForward = Quaternion.Euler(0f, 25f, 0f) * Vector3.forward;
            Vector3 inside = Quaternion.Euler(0f, 39f, 0f) * Vector3.forward;
            Vector3 outside = Quaternion.Euler(0f, 40.1f, 0f) * Vector3.forward;

            Assert.IsTrue(CharacterFacingSource.IsWithinFacingDeadzone(
                    currentForward, inside, 15f),
                "Action commitment 與目前朝向只差 14° 時應落在 15° 站定死區內");
            Assert.IsFalse(CharacterFacingSource.ShouldRequestFacing(
                    currentForward, inside, true, 15f),
                "Action commitment 落在死區內時不得送出 request");
            Assert.IsFalse(CharacterFacingSource.IsWithinFacingDeadzone(
                    currentForward, outside, 15f),
                "Action commitment 與目前朝向相差 15.1° 時必須離開死區並開始轉向");
            Assert.IsTrue(CharacterFacingSource.ShouldRequestFacing(
                    currentForward, outside, true, 15f),
                "Action commitment 離開死區後必須送出 request");
        }

        [Test]
        public void TC6B_MovementFacing_DoesNotUseAngularDeadzone()
        {
            Vector3 movement = Quaternion.Euler(0f, 1f, 0f) * Vector3.forward;

            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                false, default, movement, out Vector3 direction, out bool fromActionCommitment));
            Assert.IsFalse(fromActionCommitment,
                "優先序 ③ 的 movement 必須回報自己的來源，Tick 才會無條件送出 request");
            Assert.IsTrue(CharacterFacingSource.IsWithinFacingDeadzone(
                    Vector3.forward, direction, 8f),
                "治具刻意讓 movement 落在 8° 內：即便如此也不得把站定 Action 的死區套到 movement");
            Assert.IsTrue(CharacterFacingSource.ShouldRequestFacing(
                    Vector3.forward, direction, fromActionCommitment, 8f),
                "優先序 ③ movement 即使落在死區角度內也必須一律送出 request");
        }

        // Idle combat-facing 遲滯已隨常駐 combat target 朝向移除；理由見 docs/15 §19.3。

        /// <summary>
        /// 🔄 2026-09-08 取代原本的 `TC6C_HeadLookAngles_ClampWorldSpaceYawAndPitch`。
        /// 原測試斷言「yaw 超出可視角時 clamp 到 ±maxYaw」＝**把缺陷寫成了正確行為**：
        /// clamp 會把 SignedAngle 在正後方的 ±180° wrap 放大成 ±maxYaw 的單幀翻轉。
        /// 新的斷言守的是**連續性**，而不是某個特定回傳值。
        /// </summary>
        [Test]
        public void TC6C_HeadLookAngles_AreContinuousAcrossTheFullSweep()
        {
            const float MaxYaw = 70f;
            const float StepDegrees = 5f;

            bool hadPrevious = false;
            float previousYaw = 0f;
            bool sawRefusal = false;

            // 目標繞角色一整圈——包含正後方那個 wrap 點。
            for (float azimuth = -180f; azimuth <= 180f; azimuth += StepDegrees)
            {
                Vector3 target = Quaternion.Euler(0f, azimuth, 0f) * Vector3.forward;
                bool resolved = HeadLookController.TryComputeLookAngles(
                    Vector3.forward, target, Vector3.up, MaxYaw, 25f, out float yaw, out _);

                if (!resolved)
                {
                    // 超出可視角 ⇒ 視為沒有目標；權重會淡回 0，下一次成立時重新起算。
                    sawRefusal = true;
                    hadPrevious = false;
                    continue;
                }

                Assert.LessOrEqual(Mathf.Abs(yaw), MaxYaw + Epsilon,
                    $"回傳 true 時 yaw 不得超出可視角（azimuth={azimuth}）");

                if (hadPrevious)
                {
                    Assert.LessOrEqual(Mathf.Abs(yaw - previousYaw), StepDegrees + Epsilon,
                        $"連續兩個成立取樣之間不得出現跳變（azimuth={azimuth}）——" +
                        "這正是 clamp 版本會在 ±180° 附近失敗的地方");
                }

                previousYaw = yaw;
                hadPrevious = true;
            }

            Assert.IsTrue(sawRefusal,
                "掃過整圈必須出現「超出可視角 ⇒ 回傳 false」的區段；" +
                "若全程都成立，代表 clamp 行為又回來了");
        }

        [Test]
        public void TC6D_HeadLookPitch_IsStillClampedWithinTheYawWindow()
        {
            // pitch 沒有 wrap 問題（elevation 差由 asin 導出，值域連續），因此維持 clamp。
            Vector3 target = Quaternion.Euler(-40f, 60f, 0f) * Vector3.forward;

            Assert.IsTrue(HeadLookController.TryComputeLookAngles(
                Vector3.forward, target, Vector3.up, 70f, 25f,
                out float yaw, out float pitch));

            Assert.AreEqual(60f, yaw, Epsilon, "可視角內的 yaw 應原值回傳");
            Assert.AreEqual(-25f, pitch, Epsilon, "pitch 仍受 maxPitch 限制");
        }

        [Test]
        public void TC6E_HeadLookYaw_HasAcquireReleaseHysteresis()
        {
            Vector3 target = Quaternion.Euler(0f, 72f, 0f) * Vector3.forward;

            Assert.IsFalse(HeadLookController.TryComputeLookAngles(
                    Vector3.forward, target, Vector3.up, 70f, 25f, out _, out _),
                "尚未看著目標時，72° 超出 70° acquire 門檻，不得取得目標");

            Assert.IsTrue(HeadLookController.TryComputeLookAngles(
                    Vector3.forward, target, Vector3.up, 80f, 25f, out float yaw, out _),
                "已經在看著目標時，72° 仍在 80° release 門檻內，必須保留目標");
            Assert.AreEqual(72f, yaw, Epsilon,
                "遲滯只放寬放棄門檻，不得把遲滯帶內的 yaw clamp 回 acquire 門檻");
        }

        /// <summary>
        /// 🔄 2026-09-08 取代原本的 `TC7_ActionCommitment_PrefersCombatTargetThenAimFallback`。
        /// 原測試斷言「有 combat target 時一律壓過 aim point」——那是 `docs/11` §8.3 已被**推翻**的行為
        /// （等於所有 Action 都是 soft-target）。新測試守的是修正後的角錐語意。
        /// </summary>
        [Test]
        public void TC7_SoftTarget_OnlyCorrectsInsideTheAimCone()
        {
            Vector3 origin = Vector3.zero;
            Vector3 aimPoint = new Vector3(0f, 0f, 10f);          // 鏡頭正前方

            // 約 11° 偏差：在錐內 ⇒ 修正到敵人
            var near = new CombatContextData { HasTarget = true, TargetPosition = new Vector3(2f, 1f, 10f) };
            Assert.IsTrue(ActionState.TrySoftTarget(near, origin, aimPoint, out Vector3 point));
            Assert.AreEqual(near.TargetPosition, point, "角錐內的合法目標必須修正");

            // 約 45° 偏差：在錐外 ⇒ 不修正，維持 camera forward
            var far = new CombatContextData { HasTarget = true, TargetPosition = new Vector3(10f, 0f, 10f) };
            Assert.IsFalse(ActionState.TrySoftTarget(far, origin, aimPoint, out _),
                "角錐外的目標不得改寫承諾——否則等於背對著也會被吸過去");

            // 正後方：最極端的錐外情形
            var behind = new CombatContextData { HasTarget = true, TargetPosition = new Vector3(0f, 0f, -10f) };
            Assert.IsFalse(ActionState.TrySoftTarget(behind, origin, aimPoint, out _));

            // 沒有目標 ⇒ 沒有修正
            Assert.IsFalse(ActionState.TrySoftTarget(default, origin, aimPoint, out _));

            // 目標與角色重合 ⇒ 方向退化，不得產生 NaN 方向
            var overlapping = new CombatContextData { HasTarget = true, TargetPosition = origin };
            Assert.IsFalse(ActionState.TrySoftTarget(overlapping, origin, aimPoint, out _));
        }

        /// <summary>
        /// `docs/11` §8.3 的可預測性論證**完全建立在預設值上**：忘了填要得到「與普通招式一致」的
        /// 行為，而不是某個特例。這條把那個論證釘住——預設值一旦被改，這裡先紅。
        /// </summary>
        [Test]
        public void TC7B_TargetingPolicy_DefaultsToCameraForward()
        {
            var definition = ScriptableObject.CreateInstance<ActionDefinitionSO>();

            Assert.AreEqual(ActionTargetingPolicy.CameraForward, definition.Targeting,
                "未填寫的 Definition 必須得到 CameraForward ⇒ 吸敵永遠是顯性選擇，不是遺漏的產物");

            Destroy(definition);
        }

        private static GameObject CreatePlayer(out PlayerCombatContextSource source)
        {
            var player = new GameObject("Combat-Context-Player");
            source = player.AddComponent<PlayerCombatContextSource>();
            return player;
        }

        private static GameObject CreateEnemy(Vector3 position)
        {
            var enemy = new GameObject("Combat-Context-Enemy");
            enemy.transform.position = position;
            enemy.AddComponent<ActionRequestTarget>();
            enemy.AddComponent<SphereCollider>();
            return enemy;
        }

        private static void Destroy(params Object[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
        }
    }
}
