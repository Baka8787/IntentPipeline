using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Combat;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.Survivability;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-10-12，`docs/54` §3.3.2／§3.4，WP-1b＋WP-2 方案③）敵人的 combat context producer。
    ///
    /// <para><b>測試分兩層</b></para>
    /// ①純函數 <see cref="AICombatContextSource.ResolveEngagement"/>：Q4 的進入／保持／寬限規則（注入時間）。
    /// ②**真元件、依 Runner 順序**（2.4 <see cref="AICombatContextSource.Tick"/> → 2.5
    /// <see cref="AIMovementSource.ProduceIntent"/>）：同帧一致、目標死亡、自己死亡、單一 target 權威。
    /// 第二層刻意不 mock 任何一方——要驗的就是兩顆元件**經黑板**接起來之後的行為。
    ///
    /// ⚠️ EditMode 沒有 NavMesh：<c>_agent</c> 打成 null ⇒ 交戰中的移動走 Recover 的「朝目標」分支，
    /// 仍是一個**非零**意圖，足以分辨「有沒有在追」。（同 <c>CombatContextTests.TC9</c> 的手法。）
    /// </summary>
    public sealed class AICombatContextSourceTests
    {
        private const float Enter = 8f;
        private const float Leave = 12f;
        private const float ProvokedLeave = 20f;
        private const float Grace = 5f;

        // =====================================================================
        // ① 純函數：Q4 規則
        // =====================================================================

        private static AICombatContextSource.EngagementMemory Step(
            AICombatContextSource.EngagementMemory previous, float distance, bool tookDamage, float time)
        {
            return AICombatContextSource.ResolveEngagement(
                previous, distance, tookDamage, time, Enter, Leave, ProvokedLeave, Grace);
        }

        /// <summary>
        /// AC-B.6：任何距離受到有效傷害 ⇒ 當帧進入；保持半徑變成 L；超出 L 開始寬限；
        /// 寬限內回到 L 內或再受擊 ⇒ 取消；期滿 ⇒ 離開，且 episode 記憶清空。
        /// </summary>
        [Test]
        public void AB6_ProvokedEngagement_UsesProvokedRadiusAndGrace()
        {
            var m = default(AICombatContextSource.EngagementMemory);

            m = Step(m, 15f, tookDamage: true, time: 0f);
            Assert.IsTrue(m.InCombat, "15 m 受擊 ⇒ 當帧進入（不需要先走進 8 m）");
            Assert.IsTrue(m.DamagedThisEpisode);

            m = Step(m, 19.9f, false, 1f);
            Assert.IsTrue(m.InCombat, "受過傷 ⇒ 保持半徑是 L（20 m），不是 12 m");
            Assert.IsFalse(m.GraceActive);

            m = Step(m, 20.5f, false, 2f);
            Assert.IsTrue(m.InCombat, "剛超出 L ⇒ 進入寬限，不是立即離開");
            Assert.IsTrue(m.GraceActive);
            Assert.AreEqual(2f, m.GraceStartTime);

            m = Step(m, 25f, false, 6.99f);
            Assert.IsTrue(m.InCombat, "寬限 4.99 s < 5 s ⇒ 仍在交戰");

            m = Step(m, 19f, false, 6.995f);
            Assert.IsTrue(m.InCombat);
            Assert.IsFalse(m.GraceActive, "寬限內回到 L 內 ⇒ 取消寬限");

            m = Step(m, 25f, false, 10f);
            Assert.IsTrue(m.GraceActive, "再次超出 ⇒ 重新開始寬限");
            Assert.AreEqual(10f, m.GraceStartTime);

            m = Step(m, 25f, tookDamage: true, time: 12f);
            Assert.IsTrue(m.InCombat);
            Assert.AreEqual(12f, m.GraceStartTime,
                "寬限內再受擊 ⇒ 取消寬限；仍在 L 外 ⇒ 同帧從這一刻重新起算");

            m = Step(m, 25f, false, 16.99f);
            Assert.IsTrue(m.InCombat, "從 12 s 起算的寬限尚未期滿");

            m = Step(m, 25f, false, 17f);
            Assert.IsFalse(m.InCombat, "寬限期滿（≥ 5 s）⇒ 離開");
            Assert.IsFalse(m.DamagedThisEpisode, "離開時清空 episode 記憶");
            Assert.IsFalse(m.GraceActive);

            m = Step(m, 15f, false, 18f);
            Assert.IsFalse(m.InCombat, "新的 episode 從頭算：未受擊時仍需走進 8 m 才進入");
        }

        /// <summary>AC-B.6 邊界：在 L 外受擊（25 m）⇒ 進入，但同帧就開始寬限；追不回 L 內就在 G 後放手。</summary>
        [Test]
        public void AB6_ProvokedBeyondProvokedRadius_EntersWithGraceAlreadyRunning()
        {
            var m = Step(default, 25f, tookDamage: true, time: 3f);
            Assert.IsTrue(m.InCombat);
            Assert.IsTrue(m.GraceActive, "受擊時已在 L 外 ⇒ 同帧開始寬限");
            Assert.AreEqual(3f, m.GraceStartTime);

            var chasedBack = Step(m, 18f, false, 7f);
            Assert.IsTrue(chasedBack.InCombat, "寬限內追回 L 內 ⇒ 繼續交戰");
            Assert.IsFalse(chasedBack.GraceActive);

            var gaveUp = Step(m, 25f, false, 8f);
            Assert.IsFalse(gaveUp.InCombat, "5 s 內沒追回 ⇒ 離開");
        }

        /// <summary>
        /// AC-B.7：政策不看觸發原因。7 m 受擊後退到 13 m、與 15 m 受擊，兩者的脫戰距離都是 L。
        /// （第二版的缺陷：同一個受擊事件依距離有不同語意。）
        /// </summary>
        [Test]
        public void AB7_DisengageRadius_DoesNotDependOnWhereTheHitHappened()
        {
            var nearHit = Step(Step(default, 7f, false, 0f), 7f, tookDamage: true, time: 0.5f);
            var farHit = Step(default, 15f, tookDamage: true, time: 0.5f);

            foreach (float distance in new[] { 13f, 19.99f, 20f, 20.01f })
            {
                var near = Step(nearHit, distance, false, 1f);
                var far = Step(farHit, distance, false, 1f);
                Assert.AreEqual(far.InCombat, near.InCombat, $"{distance} m：兩種受擊位置的交戰結果必須相同");
                Assert.AreEqual(far.GraceActive, near.GraceActive, $"{distance} m：寬限是否啟動必須相同");
            }

            Assert.IsTrue(Step(nearHit, 13f, false, 1f).InCombat, "7 m 受擊後退到 13 m ⇒ 仍在交戰（R＝L）");
            Assert.IsTrue(Step(nearHit, 20.01f, false, 1f).GraceActive, "超出 L ⇒ 寬限");
        }

        /// <summary>
        /// 退化設定：L 小於 leave 時以 leave 為下限——受傷不得讓敵人比未受傷時**更早**放手。
        /// </summary>
        [Test]
        public void AB6_ProvokedRadiusSmallerThanLeave_IsClampedToLeave()
        {
            var damaged = AICombatContextSource.ResolveEngagement(
                default, 5f, true, 0f, Enter, Leave, provokedLeaveRadius: 3f, provokedGraceSeconds: Grace);
            var next = AICombatContextSource.ResolveEngagement(
                damaged, 11.9f, false, 1f, Enter, Leave, provokedLeaveRadius: 3f, provokedGraceSeconds: Grace);
            Assert.IsTrue(next.InCombat);
            Assert.IsFalse(next.GraceActive);
        }

        // =====================================================================
        // ② 真元件、依 Runner 順序 2.4 → 2.5
        // =====================================================================

        private sealed class Rig
        {
            public GameObject Enemy;
            public GameObject Target;
            public AIMovementSource Movement;
            public AICombatContextSource Combat;
            public PlayerRuntimeData Data = new PlayerRuntimeData();

            /// <summary>一帧中屬於本案的兩步，順序與 <c>CharacterPipelineRunner.Update</c> 相同。</summary>
            public void Frame(float time)
            {
                InputData input = default;
                Combat.Tick(Data, time);
                Movement.ProduceIntent(ref input, Data);
            }

            public void PlaceTargetAt(float distance)
            {
                Target.transform.position = Enemy.transform.position + Vector3.forward * distance;
            }
        }

        private Rig _rig;

        private Rig CreateRig(float targetDistance, bool targetHasHealth)
        {
            var rig = new Rig
            {
                Enemy = new GameObject("AICombat-Enemy"),
                Target = new GameObject("AICombat-Target"),
            };
            _rig = rig;

            rig.Enemy.AddComponent<NavMeshAgent>();
            rig.Movement = rig.Enemy.AddComponent<AIMovementSource>();
            rig.Combat = rig.Enemy.AddComponent<AICombatContextSource>();
            rig.Movement.SetTargetForTests(rig.Target.transform);
            SetPrivateField(rig.Movement, "_agent", null);
            SetPrivateField(rig.Combat, "aggroEnterRadius", Enter);
            SetPrivateField(rig.Combat, "aggroLeaveRadius", Leave);
            SetPrivateField(rig.Combat, "provokedLeaveRadius", ProvokedLeave);
            SetPrivateField(rig.Combat, "provokedGraceSeconds", Grace);

            if (targetHasHealth) rig.Target.AddComponent<CharacterHealth>().InitializeForTest(100f);
            rig.PlaceTargetAt(targetDistance);
            return rig;
        }

        [TearDown]
        public void TearDown()
        {
            if (_rig == null) return;
            if (_rig.Enemy != null) Object.DestroyImmediate(_rig.Enemy);
            if (_rig.Target != null) Object.DestroyImmediate(_rig.Target);
            _rig = null;
        }

        /// <summary>
        /// AC-B.5：從未受擊的交戰在超出 12 m 的**那一帧**，CombatContext 與 MovementIntent 同時歸零。
        /// 舊順序（2.5 → 2.6）下，movement 在這一帧讀到的是上一帧的 InCombat ⇒ 會多追一帧
        /// （違反 `docs/21` §1.1「!InCombat 時整體清空 MovementIntent」）。
        /// </summary>
        [Test]
        public void AB5_NeverDamaged_LeavingTwelveMetres_StopsChaseOnTheSameFrame()
        {
            Rig rig = CreateRig(targetDistance: 5f, targetHasHealth: true);

            rig.Frame(0f);
            Assert.IsTrue(rig.Data.CombatContext.InCombat, "前提：5 m ⇒ 交戰");
            Assert.Greater(rig.Data.MovementIntent.DesiredSpeedNormalized, 0f, "前提：交戰中有追擊意圖");

            rig.PlaceTargetAt(11.9f);
            rig.Frame(0.1f);
            Assert.IsTrue(rig.Data.CombatContext.InCombat, "12 m 內仍在交戰（黏性帶）");

            rig.PlaceTargetAt(12.1f);
            rig.Frame(0.2f);
            Assert.IsFalse(rig.Data.CombatContext.InCombat, "從未受擊 ⇒ 超出 12 m 立即離開");
            Assert.AreEqual(default(CombatContextData), rig.Data.CombatContext);
            Assert.AreEqual(default(MovementIntentData), rig.Data.MovementIntent,
                "同一帧移動意圖必須已經歸零——不得朝已脫戰的目標多走一帧");
        }

        /// <summary>
        /// AC-B.8（核心）：目標死亡的**那一帧**，CombatContext＝default 且 MovementIntent＝default；
        /// 受過傷的 episode 也不給寬限。Revive 之後依距離重新交戰。
        /// </summary>
        [Test]
        public void AB8_TargetDeath_ClearsCombatAndMovementOnTheSameFrame_ThenRevives()
        {
            Rig rig = CreateRig(targetDistance: 1.5f, targetHasHealth: true);
            CharacterHealth targetHealth = rig.Target.GetComponent<CharacterHealth>();

            // 用受擊把 episode 變成 Provoked：驗證「目標死亡不給寬限」不是因為剛好沒受過傷。
            rig.Data.Survivability.JustTookDamage = true;
            rig.Frame(0f);
            rig.Data.Survivability.JustTookDamage = false;
            Assert.IsTrue(rig.Data.CombatContext.InCombat);
            Assert.Greater(rig.Data.MovementIntent.DesiredSpeedNormalized, 0f, "前提：活著的目標 ⇒ 在追");

            Assert.IsTrue(targetHealth.ApplyDamage(1000f));
            Assert.IsTrue(targetHealth.IsDead, "前提：目標已死");

            rig.Frame(0.1f);
            Assert.AreEqual(default(CombatContextData), rig.Data.CombatContext,
                "目標死亡當帧 ⇒ CombatContext 整體歸零（不給寬限）");
            Assert.AreEqual(default(MovementIntentData), rig.Data.MovementIntent,
                "目標死亡當帧 ⇒ 移動意圖歸零：不得朝屍體多走一帧");

            rig.Frame(10f);
            Assert.IsFalse(rig.Data.CombatContext.InCombat, "屍體一直不是目標");

            Assert.IsTrue(targetHealth.Revive());
            rig.Frame(10.1f);
            Assert.IsTrue(rig.Data.CombatContext.InCombat, "Revive 後在 8 m 內 ⇒ 依距離重新交戰");
            Assert.IsFalse(GetMemory(rig.Combat).DamagedThisEpisode,
                "新的 episode 不得繼承死亡前的 Provoked 記憶");
        }

        /// <summary>沒有 CharacterHealth 的目標（訓練樁）視為活著：缺席 ≠ 死亡（Q3b）。</summary>
        [Test]
        public void AB8_UntrackedTarget_IsTreatedAsAlive()
        {
            Rig rig = CreateRig(targetDistance: 3f, targetHasHealth: false);
            rig.Frame(0f);
            Assert.IsTrue(rig.Data.CombatContext.InCombat);
            Assert.AreEqual(rig.Target.transform.position, rig.Data.CombatContext.TargetPosition);
        }

        /// <summary>
        /// AC-B.9：自己死亡 ⇒ **不寫** CombatContext（沿用 `docs/19` §3.1 的既有裁決），
        /// 也不產生移動意圖。
        /// </summary>
        [Test]
        public void AB9_SelfDead_DoesNotWriteCombatContextNorMove()
        {
            Rig rig = CreateRig(targetDistance: 3f, targetHasHealth: true);
            var sentinel = new CombatContextData
            {
                InCombat = true,
                HasTarget = true,
                TargetPosition = new Vector3(9f, 9f, 9f),
            };
            rig.Data.CombatContext = sentinel;
            rig.Data.Survivability.IsDead = true;

            rig.Frame(0f);
            Assert.AreEqual(sentinel, rig.Data.CombatContext, "自己死亡 ⇒ 不寫 CombatContext，由消費者自己收手");
            Assert.AreEqual(default(MovementIntentData), rig.Data.MovementIntent, "屍體不產生移動意圖");
        }

        /// <summary>
        /// 整合：**自己的** CharacterHealth 受到未致死傷害（順序 0.5 發布 JustTookDamage）⇒
        /// 15 m 外也當帧進入交戰，而且移動同帧開始追。
        /// </summary>
        [Test]
        public void AB6_OwnNonLethalDamage_ProvokesAtAnyDistance_AndMovementChasesSameFrame()
        {
            Rig rig = CreateRig(targetDistance: 15f, targetHasHealth: true);
            CharacterHealth ownHealth = rig.Enemy.AddComponent<CharacterHealth>();
            ownHealth.InitializeForTest(100f);

            ownHealth.PublishTo(rig.Data);
            rig.Frame(0f);
            Assert.IsFalse(rig.Data.CombatContext.InCombat, "前提：15 m、未受擊 ⇒ 不交戰");

            Assert.IsTrue(ownHealth.ApplyDamage(10f));
            ownHealth.PublishTo(rig.Data);
            Assert.IsTrue(rig.Data.Survivability.JustTookDamage, "前提：未致死傷害 ⇒ JustTookDamage");
            rig.Frame(0.1f);
            Assert.IsTrue(rig.Data.CombatContext.InCombat, "受擊 ⇒ 任何距離當帧進入（Provoked）");
            Assert.Greater(rig.Data.MovementIntent.DesiredSpeedNormalized, 0f, "同帧開始追");

            rig.PlaceTargetAt(19f);
            ownHealth.PublishTo(rig.Data);
            rig.Frame(0.2f);
            Assert.IsTrue(rig.Data.CombatContext.InCombat, "受過傷 ⇒ 19 m 仍在交戰（未受擊時 12 m 就會放手）");
        }

        /// <summary>
        /// 驗收④：<c>AIInputSource</c>、<c>AICombatContextSource</c>、<c>AIMovementSource</c>
        /// **用同一個 target**——改 <c>AIMovementSource</c> 的那一份，三者同時跟著變；沒有第二份可以分岔。
        /// </summary>
        [Test]
        public void SingleTargetAuthority_AllThreeAIComponentsFollowTheSameReference()
        {
            Rig rig = CreateRig(targetDistance: 1.5f, targetHasHealth: true);
            AIInputSource attack = rig.Enemy.AddComponent<AIInputSource>();
            SetPrivateField(attack, "attackRange", 1.6f);

            rig.Frame(0f);
            Assert.AreEqual(rig.Target.transform, rig.Movement.Target);
            Assert.AreEqual(rig.Target.transform.position, rig.Data.CombatContext.TargetPosition);
            Assert.IsTrue(attack.WantsToAttack(), "攻擊來源經 AIMovementSource.Target 看到同一個目標");

            var other = new GameObject("AICombat-OtherTarget");
            try
            {
                other.transform.position = rig.Enemy.transform.position + Vector3.right * 1.2f;
                rig.Movement.SetTargetForTests(other.transform);

                rig.Frame(0.1f);
                Assert.AreEqual(other.transform.position, rig.Data.CombatContext.TargetPosition,
                    "combat producer 跟著同一份 target 切換");
                Assert.IsTrue(attack.WantsToAttack());

                rig.Movement.SetTargetForTests(null);
                rig.Frame(0.2f);
                Assert.AreEqual(default(CombatContextData), rig.Data.CombatContext, "沒有 target ⇒ 不交戰");
                Assert.AreEqual(default(MovementIntentData), rig.Data.MovementIntent);
                Assert.IsFalse(attack.WantsToAttack(), "沒有 target ⇒ 不出手");
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        // =====================================================================
        // AC-B.1：全專案只有一個序列化的 AI target
        // =====================================================================

        [Test]
        public void AB1_OnlyAIMovementSourceSerializesATarget()
        {
            Assert.IsNull(typeof(AIInputSource).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance),
                "AIInputSource 不得再有自己的 target 欄位（單一權威：AIMovementSource.target）");

            foreach (System.Type type in new[] { typeof(AIInputSource), typeof(AICombatContextSource) })
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
                {
                    bool serialized = field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
                    if (!serialized) continue;
                    Assert.IsFalse(typeof(Object).IsAssignableFrom(field.FieldType),
                        $"{type.Name}.{field.Name} 是序列化的物件引用——AI 的目標只能有一份（AIMovementSource.target）");
                }
            }

            FieldInfo movementTarget = typeof(AIMovementSource).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(movementTarget);
            Assert.IsNotNull(movementTarget.GetCustomAttribute<SerializeField>(), "唯一權威必須是序列化欄位（場景 override 掛在它上面）");
        }

        private static AICombatContextSource.EngagementMemory GetMemory(AICombatContextSource source)
        {
            FieldInfo field = typeof(AICombatContextSource).GetField("_memory", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            return (AICombatContextSource.EngagementMemory)field.GetValue(source);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }
    }
}
