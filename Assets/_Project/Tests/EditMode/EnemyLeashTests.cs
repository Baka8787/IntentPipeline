using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Combat;
using Project.Core.Movement;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.3，P1.2）Leash：Roam／Soft／Hard 三層、再交戰冷卻。
    /// 純函數一組＋真 <see cref="AICombatContextSource"/>（帶 <see cref="EnemyTerritory"/>）一組。
    /// </summary>
    public sealed class EnemyLeashTests
    {
        // ── 純函數 ─────────────────────────────────────────────────────

        [Test]
        public void Classify_UsesDistanceFromHome()
        {
            Assert.AreEqual(LeashZone.Inside, EnemyLeash.Classify(4f, 4f, 6f));
            Assert.AreEqual(LeashZone.BeyondSoft, EnemyLeash.Classify(4.01f, 4f, 6f));
            Assert.AreEqual(LeashZone.BeyondSoft, EnemyLeash.Classify(6f, 4f, 6f));
            Assert.AreEqual(LeashZone.BeyondHard, EnemyLeash.Classify(6.01f, 4f, 6f));
        }

        [Test]
        public void Disengage_HardAlways_SoftOnlyWhenNeverDamaged()
        {
            Assert.IsFalse(EnemyLeash.ShouldDisengage(LeashZone.Inside, false));
            Assert.IsTrue(EnemyLeash.ShouldDisengage(LeashZone.BeyondSoft, false), "沒被打過的追擊在 Soft 外放手");
            Assert.IsFalse(EnemyLeash.ShouldDisengage(LeashZone.BeyondSoft, true), "被打過可以追過 Soft");
            Assert.IsTrue(EnemyLeash.ShouldDisengage(LeashZone.BeyondHard, true), "Hard 外：被打過也一定放手");
            Assert.IsTrue(EnemyLeash.ShouldDisengage(LeashZone.BeyondHard, false));
        }

        [Test]
        public void Entry_CooldownAndHardBlock_SoftBandAllowsOnlyDamage()
        {
            Assert.AreEqual(LeashEntry.Normal, EnemyLeash.ResolveEntry(LeashZone.Inside, false));
            Assert.AreEqual(LeashEntry.DamageOnly, EnemyLeash.ResolveEntry(LeashZone.BeyondSoft, false));
            Assert.AreEqual(LeashEntry.Blocked, EnemyLeash.ResolveEntry(LeashZone.BeyondHard, false));
            Assert.AreEqual(LeashEntry.Blocked, EnemyLeash.ResolveEntry(LeashZone.Inside, true), "冷卻中一律不得進入");
            Assert.AreEqual(LeashEntry.Blocked, EnemyLeash.ResolveEntry(LeashZone.BeyondSoft, true));
        }

        // ── 真元件：AICombatContextSource＋EnemyTerritory ─────────────────

        private GameObject _enemy;
        private GameObject _target;
        private AICombatContextSource _combat;
        private EnemyTerritory _territory;
        private PlayerRuntimeData _data;

        [SetUp]
        public void SetUp()
        {
            _enemy = new GameObject("Leash-Enemy");
            _target = new GameObject("Leash-Target");
            _enemy.AddComponent<NavMeshAgent>();
            AIMovementSource movement = _enemy.AddComponent<AIMovementSource>();
            movement.SetTargetForTests(_target.transform);
            _combat = _enemy.AddComponent<AICombatContextSource>();
            _territory = _enemy.AddComponent<EnemyTerritory>();
            _territory.ConfigureForTests(Vector3.zero, roam: 2f, soft: 4f, hard: 6f, cooldown: 2f);
            _data = new PlayerRuntimeData();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_enemy);
            Object.DestroyImmediate(_target);
        }

        private void Place(float enemyFromHome, float targetAhead)
        {
            _enemy.transform.position = new Vector3(0f, 0f, enemyFromHome);
            _target.transform.position = new Vector3(0f, 0f, enemyFromHome + targetAhead);
        }

        private bool Tick(float time, bool damage = false)
        {
            _data.Survivability.JustTookDamage = damage;
            _combat.Tick(_data, time);
            return _data.CombatContext.InCombat;
        }

        [Test]
        public void SoftLeash_UndamagedChase_DisengagesBeyondSoft_ThenCooldownBlocksEvenDamage()
        {
            Place(0f, 5f);
            Assert.IsTrue(Tick(0f), "前提：在家、目標 5 m ⇒ 交戰");

            Place(3.9f, 3f);
            Assert.IsTrue(Tick(0.5f), "Soft（4 m）內仍追");

            Place(4.2f, 3f);
            Assert.IsFalse(Tick(1f), "沒被打過 ⇒ 過了 Soft 就放手（目標仍在 8 m aggro 內也一樣）");

            Place(3f, 3f);
            Assert.IsFalse(Tick(1.5f, damage: true), "冷卻 2 s 內：受擊也不得再交戰（N3 防振盪）");
            Assert.IsFalse(Tick(2.9f), "冷卻內：距離 aggro 也不得再交戰");

            Assert.IsTrue(Tick(3.01f), "冷卻結束、回到 Soft 內 ⇒ 照既有規則（≤ 8 m）再交戰");
        }

        [Test]
        public void SoftToHardBand_OnlyDamageCanEngage()
        {
            Place(5f, 3f);
            Assert.IsFalse(Tick(0f), "Soft～Hard 之間：距離 aggro 不算");
            Assert.IsTrue(Tick(0.1f, damage: true), "Soft～Hard 之間：受擊可以進入");
            Assert.IsTrue(Tick(0.2f), "受過傷 ⇒ 在 Soft 外也能持續交戰");
        }

        [Test]
        public void HardLeash_ContinuousDamage_CannotExtendTheChase()
        {
            Place(0f, 3f);
            Assert.IsTrue(Tick(0f, damage: true), "前提：受擊交戰");

            float t = 0f;
            for (float d = 0.5f; d <= 6f; d += 0.5f)
            {
                t += 0.1f;
                Place(d, 3f);
                Assert.IsTrue(Tick(t, damage: true), $"受過傷 ⇒ dHome {d} m（≤ Hard）仍在交戰");
            }

            Place(6.2f, 3f);
            Assert.IsFalse(Tick(t += 0.1f, damage: true), "Hard 外：本帧仍在受擊也一定脫戰（Hard 不可被延長）");

            for (int i = 0; i < 10; i++)
                Assert.IsFalse(Tick(t += 0.1f, damage: true), "Hard 外持續受擊：不得再交戰");

            Place(5f, 3f);
            Assert.IsFalse(Tick(t + 0.1f, damage: true), "冷卻內：回到 Hard 內也不得再交戰");
            Assert.IsTrue(Tick(t + 2.5f, damage: true), "冷卻結束、Soft～Hard 帶內受擊 ⇒ 可以再交戰");
        }

        [Test]
        public void NoTerritory_LeashNeverApplies()
        {
            Object.DestroyImmediate(_territory);
            Place(50f, 3f);
            Assert.IsTrue(Tick(0f), "沒有 EnemyTerritory ⇒ 離家多遠都照舊交戰（導入前行為）");
        }
    }
}
