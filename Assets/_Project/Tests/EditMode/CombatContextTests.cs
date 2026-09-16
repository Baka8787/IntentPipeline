using System.Reflection;
using NUnit.Framework;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Combat;
using Project.Core.Facing;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Core.StateMachine.Actions;
using Project.Core.Survivability;
using Project.Presentation.Look;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.AI;

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

        /// <summary>
        /// 🆕（`docs/26` §I，2026-09-14）**受擊 seam migration 的回歸守門。**
        ///
        /// `docs/15` §4.1 明文把「受到傷害」列為戰鬥語境的進入條件。它原本讀
        /// `ActionRequestTarget.HasPendingRequest`——因為當時受擊是一個 Action。
        /// 受擊改為 `StateType.Hurt` 之後那個 mailbox 不再承載受擊，
        /// **若沒有一併遷移，「被打」會安靜地停止刷新交戰計時 ⇒ 被圍毆時反而提早脫離戰鬥語境，
        /// 而且不會有任何錯誤訊息。**
        ///
        /// ⇒ 這條測試的存在理由就是「那種安靜的退化」。
        /// </summary>
        [Test]
        public void TC1B_TakingDamage_EntersCombatImmediately()
        {
            GameObject player = CreatePlayer(out PlayerCombatContextSource source);
            var data = new PlayerRuntimeData();

            // 沒有出手意圖、沒有目標——唯一的訊號是「我被打了」。
            data.Survivability.JustTookDamage = true;
            source.Tick(data, 10f);

            Assert.IsTrue(data.CombatContext.InCombat,
                "受到傷害必須算交戰互動（docs/15 §4.1），且不得再依賴已退役的 Reaction mailbox");
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
        public void TC6_FacingPriority_CommitmentThenCombatThenMovementThenNoRequest()
        {
            Vector3 combatFacing = new Vector3(10f, 3f, 0f);
            Vector3 moveDirection = Vector3.left;

            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                true, Vector3.forward, true, combatFacing, moveDirection,
                out Vector3 direction, out bool fromActionCommitment));
            Assert.AreEqual(Vector3.forward, direction, "Action commitment 必須壓過 combat facing");
            Assert.IsTrue(fromActionCommitment, "方向來源必須同行回報，Tick 才能只對 Action 套死區");

            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                false, default, true, combatFacing, moveDirection,
                out direction, out fromActionCommitment));
            Assert.AreEqual(Vector3.right, direction,
                "無 Action commitment 時，combat facing 必須壓過 MoveDirection");
            Assert.IsFalse(fromActionCommitment, "combat facing 不得被標成 Action 而吃到站定死區");

            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                false, default, false, combatFacing, moveDirection,
                out direction, out fromActionCommitment));
            Assert.AreEqual(Vector3.left, direction,
                "無 Action commitment 且未啟用 combat facing 時，必須退回 MoveDirection");
            Assert.IsFalse(fromActionCommitment, "movement 來源必須被標成不套用站定死區");

            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                false, default, true, Vector3.zero, moveDirection,
                out direction, out fromActionCommitment));
            Assert.AreEqual(Vector3.left, direction,
                "combat target 與自身重合時沒有可用朝向，必須退回 MoveDirection");
            Assert.IsFalse(fromActionCommitment);

            Assert.IsFalse(CharacterFacingSource.TryResolveFacing(
                false, default, false, default, Vector3.zero, out _, out _),
                "Action／combat／movement 全無時不得送 request");
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
        public void TC6B_CombatFacing_DoesNotUseAngularDeadzone()
        {
            Vector3 combatFacing = Quaternion.Euler(0f, 1f, 0f) * Vector3.forward;

            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                false, default, true, combatFacing, Vector3.left,
                out Vector3 direction, out bool fromActionCommitment));
            Assert.IsFalse(fromActionCommitment,
                "優先序 ② 的 combat facing 必須被標成不套用站定死區");
            Assert.IsTrue(CharacterFacingSource.IsWithinFacingDeadzone(
                    Vector3.forward, direction, 8f),
                "治具刻意讓 combat facing 落在 8° 內，幾何上確實落在死區內");
            Assert.IsTrue(CharacterFacingSource.ShouldRequestFacing(
                    Vector3.forward, direction, fromActionCommitment, 8f),
                "combat facing 是 continuous dynamics，任意小角差也必須送出 request");
        }

        [Test]
        public void TC6F_DisabledPersistentCombatFacing_PreservesPreviousPriorityCases()
        {
            Vector3 ignoredCombatFacing = Vector3.right;

            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                true, Vector3.forward, false, ignoredCombatFacing, Vector3.left,
                out Vector3 direction, out bool fromActionCommitment));
            Assert.AreEqual(Vector3.forward, direction,
                "actor policy 關閉時，既有 Action commitment 分支必須完全不變");
            Assert.IsTrue(fromActionCommitment);

            Assert.IsTrue(CharacterFacingSource.TryResolveFacing(
                false, default, false, ignoredCombatFacing, Vector3.left,
                out direction, out fromActionCommitment));
            Assert.AreEqual(Vector3.left, direction,
                "actor policy 關閉時，非零 combat 資料也不得影響原有 MoveDirection 分支");
            Assert.IsFalse(fromActionCommitment);

            Assert.IsFalse(CharacterFacingSource.TryResolveFacing(
                false, default, false, ignoredCombatFacing, Vector3.zero, out _, out _),
                "actor policy 關閉時，原有全無方向分支必須繼續不送 request");
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

        /// <summary>ActionState 只消費 producer 已選好的 soft-target snapshot，不重算 cone。</summary>
        [Test]
        public void TC7_SoftTarget_UsesOnlyTheDedicatedCandidateSnapshot()
        {
            Vector3 origin = Vector3.zero;
            Vector3 candidatePoint = new Vector3(2f, 1f, 10f);
            var context = new CombatContextData
            {
                HasTarget = true,
                TargetPosition = new Vector3(-4f, 0f, 2f),
                HasSoftTarget = true,
                SoftTargetPosition = candidatePoint
            };

            Assert.IsTrue(ActionState.TrySoftTarget(context, origin, out Vector3 point));
            Assert.AreEqual(candidatePoint, point,
                "Action commitment 必須使用 dedicated soft target，不得退回黏性的 combat target");

            context.HasSoftTarget = false;
            Assert.IsFalse(ActionState.TrySoftTarget(context, origin, out _),
                "candidate 消失後不得殘留 stale combat target");

            context.HasSoftTarget = true;
            context.SoftTargetPosition = origin;
            Assert.IsFalse(ActionState.TrySoftTarget(context, origin, out _),
                "candidate 與角色重合時不得產生退化方向");
        }

        [Test]
        public void TC7C_SoftTarget_RejectsSideBehindAndOutOfRangeCandidates()
        {
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;

            Assert.IsTrue(PlayerCombatContextSource.TryScoreSoftTarget(
                origin, forward, new Vector3(2f, 0f, 10f), 12f, 25f, out _, out _));
            Assert.IsFalse(PlayerCombatContextSource.TryScoreSoftTarget(
                origin, forward, new Vector3(10f, 0f, 1f), 12f, 25f, out _, out _),
                "側面候選即使更近也不得吸附");
            Assert.IsFalse(PlayerCombatContextSource.TryScoreSoftTarget(
                origin, forward, new Vector3(0f, 0f, -2f), 12f, 25f, out _, out _),
                "背後候選不得吸附");
            Assert.IsFalse(PlayerCombatContextSource.TryScoreSoftTarget(
                origin, forward, new Vector3(0f, 0f, 12.1f), 12f, 25f, out _, out _),
                "超出 12m playtest range 的候選不得吸附");
        }

        [Test]
        public void TC7D_SoftTarget_ScoresAimCenterBeforeDistance()
        {
            Assert.IsTrue(PlayerCombatContextSource.IsBetterSoftTarget(
                    0.999f, 100f, 0.97f, 9f),
                "更接近 camera aim center 的遠目標必須勝過偏角較大的近目標");
            Assert.IsTrue(PlayerCombatContextSource.IsBetterSoftTarget(
                    0.99f, 9f, 0.99f, 16f),
                "alignment 等價時才以距離消歧");
        }

        [Test]
        public void TC7E_SoftTarget_RecomputesWithoutStickyState()
        {
            GameObject player = CreatePlayer(out PlayerCombatContextSource source);
            GameObject camera = new GameObject("Soft-Target-Camera");
            GameObject centeredFar = CreateEnemy(new Vector3(0f, 0f, 8f));
            GameObject sideNear = CreateEnemy(new Vector3(4f, 0f, 2f));
            var data = new PlayerRuntimeData { CameraTransform = camera.transform };
            Physics.SyncTransforms();

            source.Tick(data, 0f);
            Assert.IsTrue(data.CombatContext.HasSoftTarget);
            Assert.That(data.CombatContext.SoftTargetPosition.z, Is.GreaterThan(7f),
                "camera-centered 遠目標必須勝過側面的近目標");

            centeredFar.transform.position = new Vector3(8f, 0f, 0f);
            Physics.SyncTransforms();
            source.Tick(data, 1f);

            Assert.IsFalse(data.CombatContext.HasSoftTarget,
                "最後一個 cone 內候選離開後，下一幀必須清除 soft target snapshot");
            Assert.AreEqual(Vector3.zero, data.CombatContext.SoftTargetPosition);

            Destroy(player, camera, centeredFar, sideNear);
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

        // =====================================================================
        // TC8／TC9 —— 敵人的接戰語境（`docs/19` §3.1）
        // producer 位置由 ADR-007 D3 補充條款承載：「CombatContextData 的唯一寫入者
        // ＝每角色唯一 active 的 combat context producer」；進出門檻在該條款明列為**不凍結**。
        // =====================================================================

        /// <summary>
        /// CombatContext 的進出必須是**黏性半徑**，⛔ 不得只看「target 欄位有沒有填」。
        ///
        /// 為什麼這條值得一個測試：`target` 是 Inspector 上恆為非 null 的序列化欄位，
        /// 若 `InCombat` 只由 `target != null` 決定，敵人會**從場景開始就永久 InCombat**
        /// ⇒ 持續 target-facing 永遠不解除 ⇒ `docs/19` §7.2 的
        /// 「脫離 combat 回到 facing 跟隨 MoveDirection」**永遠驗不過**，而且畫面上看起來
        /// 只是「敵人有點黏」，不會有人聯想到是語境判定缺了半徑。
        /// </summary>
        [Test]
        public void TC8_EnemyCombatEngagement_UsesStickyEnterLeaveRadii()
        {
            const float enterRadius = 8f;
            const float leaveRadius = 12f;

            // 未進場：必須真的走進 enter 半徑才算進戰鬥。
            Assert.IsFalse(AIMovementSource.ResolveCombatEngagement(false, 8.01f, enterRadius, leaveRadius),
                "尚未進場且在 enter 半徑外 ⇒ 不得 InCombat");
            Assert.IsTrue(AIMovementSource.ResolveCombatEngagement(false, 8f, enterRadius, leaveRadius),
                "走到 enter 半徑上 ⇒ 進場");

            // 已進場：黏性——enter 與 leave 之間維持在戰鬥中，不逐幀翻轉 facing 來源。
            Assert.IsTrue(AIMovementSource.ResolveCombatEngagement(true, 10f, enterRadius, leaveRadius),
                "已在戰鬥中且仍在 leave 半徑內 ⇒ 維持 InCombat（黏性核心）");
            Assert.IsFalse(AIMovementSource.ResolveCombatEngagement(true, 12.01f, enterRadius, leaveRadius),
                "超出 leave 半徑 ⇒ 脫離戰鬥");

            // 脫離後要再進場必須回到較小的 enter 半徑——不是 leave 半徑，否則遲滯等於沒有。
            Assert.IsFalse(AIMovementSource.ResolveCombatEngagement(false, 10f, enterRadius, leaveRadius),
                "脫離後位於 enter 與 leave 之間 ⇒ 尚未重新進場");

            // 退化輸入：leave 小於 enter 時夾成 enter，兩個門檻仍不得反向。
            Assert.IsTrue(AIMovementSource.ResolveCombatEngagement(true, 8f, enterRadius, 6f));
            Assert.IsFalse(AIMovementSource.ResolveCombatEngagement(true, 8.01f, enterRadius, 6f));
        }

        /// <summary>
        /// **NavMesh 無效 ≠ 沒有 target ／ 不在 combat**（2026-09-10 使用者裁決）。
        ///
        /// 兩者是不同層次：
        /// **CombatContext** 描述「還在不在跟這個目標交戰」——那是目標關係的性質；
        /// **EngagementMovement** 描述「**在可導航前提下**要怎麼移動」——那是導航的性質。
        /// ⇒ 離網時 combat truth 必須保留，但遲滯狀態機**不得繼續推進**：否則回到 NavMesh 的
        /// 那一幀會拿到一個沒有任何一幀真正推導過的模式。
        ///
        /// 📌 本測項刻意讓 `_agent` 為 null 來表示「agent 無效」，因此**不需要臨時 NavMesh**，
        /// 也就不受 `MovementIntentTests` 那條 EditMode NavMesh 限制影響（守衛的兩半
        /// `_agent == null || !_agent.isOnNavMesh` 走同一個分支）。
        /// </summary>
        /// <remarks>
        /// 🔄 **2026-09-15 baseline 更新（同一工作包內取代，非暫停）。**
        /// 本測項原本斷言離網時 movement mode 必須是 <c>Hold</c>。那條不變量**是缺陷本體**：
        /// 全 repo 沒有任何一行會把離網的 agent 放回去（<c>isOnNavMesh</c> 只有一處被讀）
        /// ⇒ <c>Hold</c> 是沒有出口的終態，敵人永久靜止（2026-09-15 錄影：連續 11 秒零位移）。
        ///
        /// 新 baseline 保留原測項真正在守的兩件事——**combat truth 不受導航影響**、
        /// **遲滯狀態機不得推進**（模式不得從注入的 <c>Approach</c> 被推導成別的交戰模式）——
        /// 只把「因此不動」換成「因此走回可導航區」。
        /// </remarks>
        [Test]
        public void TC9_NavMeshUnavailable_KeepsCombatTruthAndRecoversInsteadOfFreezing()
        {
            var enemy = new GameObject("Combat-Context-Enemy-Source");
            var targetObject = new GameObject("Combat-Context-Enemy-Target");
            try
            {
                enemy.transform.position = Vector3.zero;
                targetObject.transform.position = new Vector3(0f, 0f, 1f);   // 在 enter 半徑內

                // 先掛 NavMeshAgent 再把 _agent 打成 null：即使某天 EditMode 開始呼叫 Awake，
                // 也不會在 Awake 內對 null agent 設定 updatePosition。
                enemy.AddComponent<NavMeshAgent>();
                AIMovementSource source = enemy.AddComponent<AIMovementSource>();
                SetPrivateField(source, "target", targetObject.transform);
                SetPrivateField(source, "_agent", null);
                SetPrivateField(source, "_engagementMovement", AIMovementSource.EngagementMovement.Approach);

                var data = new PlayerRuntimeData();
                InputData input = default;
                source.ProduceIntent(ref input, data);

                Assert.IsTrue(data.CombatContext.InCombat,
                    "NavMesh 無效不代表脫離戰鬥——combat truth 由目標關係決定，不由導航決定");
                Assert.IsTrue(data.CombatContext.HasTarget,
                    "目標仍然有效，HasTarget 不得因為導航失效而被抹掉");
                Assert.AreEqual(targetObject.transform.position, data.CombatContext.TargetPosition,
                    "TargetPosition 必須仍是本幀的目標位置");

                Assert.AreEqual(
                    AIMovementSource.EngagementMovement.Recover,
                    GetPrivateField<AIMovementSource.EngagementMovement>(source, "_engagementMovement"),
                    "離網必須進入 Recover：遲滯狀態機依舊凍結（不得由注入的 Approach 被推導成 "
                    + "Approach／Strafe／Retreat 任何一者），但模式本身要能表達『正在自救』");

                // 🔴 這兩條是本次修正的核心：離網**不得**等於靜止。
                // EditMode 沒有 NavMesh ⇒ SamplePosition 失敗 ⇒ 走「朝目標」的最後手段，
                // 而那正是設計上刻意保留的下限：**永遠解得出一個方向**。
                Assert.Greater(data.MovementIntent.DesiredSpeedNormalized, 0f,
                    "離網時必須仍輸出移動意圖——不然 Hold/Recover 就只是換個名字的終態");
                Assert.Greater(
                    Vector3.Dot(data.MovementIntent.DesiredDirection, Vector3.forward), 0.99f,
                    "取樣不到可導航點時的最後手段是朝目標（目標依定義站在可走的地方）");
            }
            finally
            {
                Destroy(enemy, targetObject);
            }
        }

        /// <summary>
        /// 還沒進入 combat 的敵人必須保持 Idle。producer 必須在距離帶解析前直接返回，
        /// 不能讓未交戰的 target 產生 Approach／Strafe／Retreat。
        /// </summary>
        [Test]
        public void TC10_OutOfCombat_ProducesNoMovementIntentAndHoldsEngagementMode()
        {
            var enemy = new GameObject("Out-Of-Combat-Enemy-Source");
            var targetObject = new GameObject("Out-Of-Combat-Enemy-Target");
            try
            {
                enemy.transform.position = Vector3.zero;
                targetObject.transform.position = new Vector3(0f, 0f, 20f);

                enemy.AddComponent<NavMeshAgent>();
                AIMovementSource source = enemy.AddComponent<AIMovementSource>();
                SetPrivateField(source, "target", targetObject.transform);
                SetPrivateField(source, "aggroEnterRadius", 8f);
                SetPrivateField(source, "aggroLeaveRadius", 12f);
                SetPrivateField(source, "_agent", null);
                SetPrivateField(source, "_engagementMovement", AIMovementSource.EngagementMovement.Approach);

                var data = new PlayerRuntimeData
                {
                    MovementIntent = new MovementIntentData
                    {
                        DesiredDirection = Vector3.forward,
                        DesiredSpeedNormalized = 1f,
                        WalkModeActive = true,
                    }
                };
                InputData input = default;
                source.ProduceIntent(ref input, data);

                Assert.IsFalse(data.CombatContext.InCombat,
                    "目標仍在 enter 半徑外，測試前提必須維持 !InCombat");
                Assert.AreEqual(default(MovementIntentData), data.MovementIntent,
                    "!InCombat 時不得產生 MovementIntent；既有的非零值也必須被整體覆寫為 default");
                Assert.AreEqual(
                    AIMovementSource.EngagementMovement.Hold,
                    GetPrivateField<AIMovementSource.EngagementMovement>(source, "_engagementMovement"),
                    "!InCombat 時 engagement mode 必須回到零移動的 Hold fallback");
            }
            finally
            {
                Destroy(enemy, targetObject);
            }
        }

        // =====================================================================
        // TC11 —— 接戰帶的上界（`AIInputSource.attackRange` 契約的左半邊）
        // =====================================================================

        /// <summary>
        /// **敵人「停下來不再前進」的最大距離，恆等於 <c>maximumEngagementDistance</c>。**
        ///
        /// <para>這條測項存在的理由不是覆蓋率，而是**釘住 W21 那條跨元件契約的根據**：</para>
        /// W21 斷言 <c>attackRange ≥ maximumEngagementDistance</c>。那個門檻不是隨便挑的數字，
        /// 而是本函數的一個性質——**距離一旦超過 maximum，不論前一幀是哪個模式、
        /// 不論 hysteresis 多大，結果一定是 Approach**；反過來在 maximum 上則存在
        /// 不前進的結果（Strafe）⇒ maximum 就是 hold 帶的**緊上界**。
        ///
        /// <para>⚠️ hysteresis 只會把實際停住的點**往內拉**（<c>maximum − deadZone</c>），
        /// 永遠不會往外推。所以契約用 maximum 當門檻是安全的上界，不是近似值。</para>
        ///
        /// 🐛 這條對應 2026-09-14 使用者 Play 回報的
        /// 「敵人攻擊距離比迂迴距離短，玩家得自己往前靠」：
        /// 當時 <c>attackRange = 1.8 &lt; maximumEngagementDistance = 2.0</c>
        /// ⇒ 1.8–2.0 m 是一段「站得住但打不到」的死區，而失敗模式是**完全靜默的**
        /// ——敵人看起來只是「在猶豫」，不會有任何錯誤訊息。
        /// </summary>
        [Test]
        public void TC11_HoldBandUpperBound_IsExactlyMaximumEngagementDistance()
        {
            const float minimum = 1.25f;
            const float maximum = 1.6f;
            const float hysteresis = 0.15f;

            var allPriorModes = new[]
            {
                AIMovementSource.EngagementMovement.Hold,
                AIMovementSource.EngagementMovement.Approach,
                AIMovementSource.EngagementMovement.Strafe,
                AIMovementSource.EngagementMovement.Retreat,
            };

            // ① 超過外側門檻 ⇒ 必定 Approach，與前一幀模式、與 hysteresis 大小都無關。
            foreach (AIMovementSource.EngagementMovement prior in allPriorModes)
            {
                foreach (float largeHysteresis in new[] { 0f, hysteresis, 99f })
                {
                    Assert.AreEqual(
                        AIMovementSource.EngagementMovement.Approach,
                        AIMovementSource.ResolveEngagementMovement(
                            prior, maximum + 0.01f, minimum, maximum, largeHysteresis),
                        $"距離超過 maximum 時必定前進（prior={prior}, hysteresis={largeHysteresis}）");
                }
            }

            // ② 剛好落在外側門檻上 ⇒ 存在「不前進」的結果 ⇒ maximum 是緊上界，不是保守估計。
            Assert.AreEqual(
                AIMovementSource.EngagementMovement.Strafe,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Strafe, maximum, minimum, maximum, hysteresis),
                "距離等於 maximum 時敵人就會停下來側移——attackRange 必須涵蓋到這裡");

            // ③ hysteresis 把實際停住的點往**內**拉，不往外推。
            //
            // ⚠️ 這兩條刻意各留 0.01 的餘裕，**不**斷言剛好落在 `maximum − deadZone` 上。
            //    測試若要重現實作的浮點運算（`maximum - deadZone`），會因為常數摺疊與
            //    執行期算式差 1 ULP 而假性失敗——實測踩過。邊界的**精確**位置不是契約的一部分
            //    （契約只需要「停住的點 ≤ maximum」），會動搖契約的邊界是 ② 的 `maximum` 本身，
            //    而那一條傳的是原值、沒有算術，可以安全地斷言在門檻上。
            Assert.AreEqual(
                AIMovementSource.EngagementMovement.Approach,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Approach,
                    maximum - hysteresis + 0.01f, minimum, maximum, hysteresis),
                "已在前進中且明顯還沒越過 maximum − deadZone ⇒ 繼續前進");
            Assert.AreEqual(
                AIMovementSource.EngagementMovement.Strafe,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Approach,
                    maximum - hysteresis - 0.01f, minimum, maximum, hysteresis),
                "前進到 maximum − deadZone 以內就收手 ⇒ 停住的點比 maximum 更近，契約門檻仍安全");
        }

        // =====================================================================
        // TC6G —— 死亡是 facing 的否決層（2026-09-14 使用者 Play 回報：屍體不斷面向玩家）
        // =====================================================================

        /// <summary>
        /// **死了就不再送 facing request。**
        ///
        /// <para>這條刻意做在 <c>Tick</c> 層而不是純函數層</para>
        /// ——因為缺陷不在 <c>TryResolveFacing</c> 的優先序裡，而在
        /// 「<c>CharacterFacingSource</c> 根本不認識死亡」。純函數測試會漏掉它。
        ///
        /// <para>⚠️ 為什麼 <c>DeathArbiterSource</c> 擋不住</para>
        /// 它封鎖 <c>BlockInput</c>，也就是**輸入產生的意圖**；但 facing 的第二順位讀的是
        /// <c>CombatContext</c>，由 <c>AIMovementSource</c> 在順序 2.5 直接寫黑板，**不經過輸入**。
        ///
        /// 測試先驗證「同一份資料在活著時確實會送出 request」，否則死亡分支的斷言
        /// 可能因為別的原因（例如方向退化）而假性通過。
        /// </summary>
        [Test]
        public void TC6G_DeadCharacter_SendsNoFacingRequest()
        {
            const int Sentinel = -999;

            var character = new GameObject("Facing-Death-Gate");
            try
            {
                character.transform.position = Vector3.zero;
                character.transform.rotation = Quaternion.identity;

                var motionDriver = character.AddComponent<MotionDriver>();
                var facingSource = character.AddComponent<CharacterFacingSource>();
                SetPrivateField(facingSource, "usePersistentCombatFacing", true);
                facingSource.Initialize(motionDriver, null);

                var data = new PlayerRuntimeData
                {
                    CombatContext = new CombatContextData
                    {
                        InCombat = true,
                        HasTarget = true,
                        // 刻意放在角色側後方：活著時一定要轉，死了就一定不能轉。
                        TargetPosition = new Vector3(5f, 0f, -5f),
                    },
                };

                // ── 前提：活著時這份資料確實會送出 request ─────────────────────────
                SetPrivateField(motionDriver, "_facingRequestFrame", Sentinel);
                data.Survivability.IsDead = false;
                facingSource.Tick(data);
                Assert.AreNotEqual(Sentinel, GetPrivateField<int>(motionDriver, "_facingRequestFrame"),
                    "測試前提：活著的角色面對側後方的 combat target 必須送出 facing request");

                // ── 否決層：死亡後同一份資料不得再送出任何 request ───────────────
                SetPrivateField(motionDriver, "_facingRequestFrame", Sentinel);
                data.Survivability.IsDead = true;
                facingSource.Tick(data);
                Assert.AreEqual(Sentinel, GetPrivateField<int>(motionDriver, "_facingRequestFrame"),
                    "死亡後不得送出 facing request——否則屍體會持續轉向攻擊者");
            }
            finally
            {
                Destroy(character);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位（欄位名稱可能已變更）");
            return (T)field.GetValue(target);
        }

        /// <summary>
        /// 🆕（2026-09-15，`docs/15` §16-7 結案）**屍體不得被選為目標。**
        ///
        /// `docs/15` §4.2 當年寫「沒有 Health 契約 ⇒ 死亡只能以被摧毀／停用表示」，
        /// 並登記 §16-7 等契約出現再接。契約已於 ADR-009 建立（<c>Survivability.IsDead</c>），
        /// 但 2026-09-15 查證：<c>DeathState</c>／<c>CharacterHealth</c> **都不會**
        /// 停用 collider 或 <c>ActionRequestTarget</c> ⇒ 屍體通過舊版全部檢查。
        /// 現象（2026-09-15 錄影）：玩家朝屍體轉、戰鬥語境不脫離。
        /// </summary>
        [Test]
        public void TC_D1_DeadEnemy_IsNotSelectedAsTarget()
        {
            GameObject player = CreatePlayer(out PlayerCombatContextSource source);
            GameObject enemy = CreateEnemy(new Vector3(0f, 0f, 2f));
            CharacterHealth health = enemy.AddComponent<CharacterHealth>();
            var data = new PlayerRuntimeData();
            Physics.SyncTransforms();

            Assert.IsTrue(health.ApplyDamage(health.MaxHealth), "前提：這一下必須真的致死");
            Assert.IsTrue(health.IsDead);

            source.Tick(data, 0f);

            Assert.IsFalse(data.CombatContext.HasTarget,
                "已死亡的敵人不得成為 combat target——屍體仍 active 且 collider 仍在，"
                + "只靠 isActiveAndEnabled 檢查不出來");
            Assert.IsFalse(data.CombatContext.HasSoftTarget,
                "soft target 走同一份合法性判準，不得只修 combat target 那一半");
            Destroy(player, enemy);
        }

        /// <summary>
        /// 目標是**黏性**的（合法就一直保留，避免逐幀被更近的敵人奪取）。
        /// ⇒ 死亡必須讓**已保留**的目標失效，否則玩家會被黏在一具屍體上。
        /// </summary>
        [Test]
        public void TC_D2_RetainedTarget_IsReleasedOnDeath()
        {
            GameObject player = CreatePlayer(out PlayerCombatContextSource source);
            GameObject enemy = CreateEnemy(new Vector3(0f, 0f, 2f));
            CharacterHealth health = enemy.AddComponent<CharacterHealth>();
            var data = new PlayerRuntimeData();
            Physics.SyncTransforms();

            source.Tick(data, 0f);
            Assert.IsTrue(data.CombatContext.HasTarget, "前提：活著時必須先取得目標");

            health.ApplyDamage(health.MaxHealth);
            source.Tick(data, 0.1f);

            Assert.IsFalse(data.CombatContext.HasTarget,
                "目標死亡的下一次 Tick 就必須放掉——黏性保留不得延伸到屍體");
            Destroy(player, enemy);
        }

        /// <summary>
        /// ⚠️ 反向守門：**「沒有生命值」不等於「死了」。**
        /// 訓練樁、可互動物件沒有 <c>CharacterHealth</c>，不得因為查不到而被當成屍體排除。
        /// </summary>
        [Test]
        public void TC_D3_TargetWithoutHealth_RemainsLegal()
        {
            GameObject player = CreatePlayer(out PlayerCombatContextSource source);
            GameObject dummy = CreateEnemy(new Vector3(0f, 0f, 2f)); // 刻意不加 CharacterHealth
            var data = new PlayerRuntimeData();
            Physics.SyncTransforms();

            source.Tick(data, 0f);

            Assert.IsTrue(data.CombatContext.HasTarget,
                "缺少 CharacterHealth 必須視為合法目標；把缺席當成死亡會靜默地讓非角色目標全部失效");
            Destroy(player, dummy);
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
