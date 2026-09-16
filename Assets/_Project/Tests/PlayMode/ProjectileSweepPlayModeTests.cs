using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Project.Core.Survivability;
using Project.Presentation.Actions;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// **投射物的命中契約**（使用者裁決 2026-09-16，正本 `docs/11` §17）。
    ///
    /// <para><b>取代了什麼</b></para>
    /// 舊守門是 `PrefabWiringTests` 的 `speed × fixedDeltaTime ≤ radius`。
    /// 那條**不是保證**——位移在 `Update`（可變 `deltaTime`）、取樣在物理步，兩者不同步，
    /// 真實取樣間距不等於那個算式，它只能**降低**略過的機率。
    /// `ThrownProjectile` 改為 `FixedUpdate` ＋ `SphereCast` 連續掃掠之後，
    /// 安全性由**掃掠本身**保證，因此改守**行為**：高速不 tunneling、命中在接觸點截斷、
    /// owner 排除、屍體透明。
    ///
    /// ⚠️ 全部用**真實 `Projectile_Fireball.prefab`**，不用合成替身——
    /// 這幾條要守的正是「出貨的那顆投射物會不會漏」。
    /// </summary>
    public class ProjectileSweepPlayModeTests
    {
        private const string FireballPrefabPath = "Assets/Prefabs/Projectile_Fireball.prefab";

        /// <summary>
        /// 刻意遠高於出貨值（20 m/s）。60 m/s ⇒ 每物理步 1.2 m，**遠大於目標膠囊直徑 0.64 m**
        /// ⇒ 離散取樣必漏、連續掃掠必中。這個差距就是本測項的鑑別力。
        /// </summary>
        private const float TunnelingSpeed = 60f;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private GameObject CreateTarget(string name, Vector3 position, out CharacterHealth health)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.layer = 7; // Enemy，與 Y Bot 一致
            go.transform.position = position;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.32f;
            capsule.height = 1.8266189f;
            capsule.center = new Vector3(0f, 0.9134095f, 0f);
            health = go.AddComponent<CharacterHealth>();
            return go;
        }

        private ThrownProjectile CreateProjectile(Vector3 start, float speed, Transform owner)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(FireballPrefabPath);
            Assert.IsNotNull(source, $"找不到 {FireballPrefabPath}");
            GameObject go = Object.Instantiate(source);
            _created.Add(go);
            go.transform.position = start;
            go.transform.forward = Vector3.forward;
            ThrownProjectile projectile = go.GetComponent<ThrownProjectile>();

            // Initialize 會把視覺 unparent 出去（2026-09-16 裁決 B）——先抓住它才清得掉。
            var visual = go.GetComponentInChildren<ParticleSystem>(true);
            Transform visualRoot = visual != null ? visual.transform.root : null;

            projectile.Initialize(speed, 5f, owner);
            if (visualRoot != null && visualRoot.gameObject != go) _created.Add(visualRoot.gameObject);
            return projectile;
        }

        /// <summary>
        /// 🔴 **高速不得穿過目標。** 這是整個掃掠改動存在的理由。
        /// 60 m/s ⇒ 每步 1.2 m，舊的離散取樣一定會整步略過 0.64 m 寬的膠囊。
        /// </summary>
        [UnityTest]
        public IEnumerator PS1_HighSpeedProjectile_DoesNotTunnelThroughTarget()
        {
            CreateTarget("Tunnel-Target", Vector3.zero, out CharacterHealth health);
            ThrownProjectile projectile = CreateProjectile(new Vector3(0f, 0.9f, -6f), TunnelingSpeed, null);
            GameObject projectileObject = projectile.gameObject;
            Physics.SyncTransforms();

            float startHealth = health.CurrentHealth;
            for (int i = 0; i < 200 && projectileObject != null; i++) yield return new WaitForFixedUpdate();

            Assert.Less(health.CurrentHealth, startHealth,
                $"{TunnelingSpeed} m/s（每物理步 {TunnelingSpeed * Time.fixedDeltaTime:F2} m）下仍必須命中。"
                + "沒扣血代表投射物整步略過了目標——連續掃掠失效。");
        }

        /// <summary>
        /// **命中要在接觸點截斷，不得先飛到 desiredEnd 再回頭處理。**
        /// 否則視覺與 impact 位置會穿進目標裡面。
        /// </summary>
        [UnityTest]
        public IEnumerator PS2_ProjectileStopsAtContactPoint_NotInsideTheTarget()
        {
            CreateTarget("Contact-Target", Vector3.zero, out _);
            ThrownProjectile projectile = CreateProjectile(new Vector3(0f, 0.9f, -6f), TunnelingSpeed, null);
            GameObject projectileObject = projectile.gameObject;
            Physics.SyncTransforms();

            Vector3 lastSeen = projectileObject.transform.position;
            for (int i = 0; i < 200 && projectileObject != null; i++)
            {
                lastSeen = projectileObject.transform.position;
                yield return new WaitForFixedUpdate();
            }

            // 膠囊半徑 0.32 ＋ 投射物半徑 0.15 ⇒ 接觸面在 z ≈ -0.47。
            // 容許一個物理步的餘裕，但**絕不允許越過目標中心**。
            Assert.Less(lastSeen.z, 0f,
                $"投射物最後停在 z={lastSeen.z:F2}，已越過目標中心 ⇒ 位移沒有在接觸點截斷");
        }

        /// <summary>owner 與其子物件必須被排除——否則投射物一出生就打中自己。</summary>
        [UnityTest]
        public IEnumerator PS3_OwnerAndItsChildren_AreExcludedFromTheSweep()
        {
            GameObject owner = CreateTarget("Owner", new Vector3(0f, 0f, -6f), out CharacterHealth ownerHealth);
            CreateTarget("Enemy-Behind", Vector3.zero, out CharacterHealth enemyHealth);

            // 從 owner 身體內部發射。
            ThrownProjectile projectile = CreateProjectile(
                new Vector3(0f, 0.9f, -6f), TunnelingSpeed, owner.transform);
            GameObject projectileObject = projectile.gameObject;
            Physics.SyncTransforms();

            float ownerStart = ownerHealth.CurrentHealth;
            float enemyStart = enemyHealth.CurrentHealth;
            for (int i = 0; i < 200 && projectileObject != null; i++) yield return new WaitForFixedUpdate();

            Assert.AreEqual(ownerStart, ownerHealth.CurrentHealth, 0.001f,
                "投射物不得打中發射者——owner 與其子物件必須明確排除");
            Assert.Less(enemyHealth.CurrentHealth, enemyStart,
                "排除 owner 之後仍必須打中後方的敵人");
        }

        /// <summary>
        /// **屍體對掃掠是透明的。** 這守的是 2026-09-15 修掉的「屍體吃掉火球」——
        /// 改成掃掠之後必須沿用同一個語意，而不是讓屍體重新變成盾牌。
        /// </summary>
        [UnityTest]
        public IEnumerator PS4_CorpseIsTransparent_LiveTargetBehindIsStillHit()
        {
            CreateTarget("Corpse", new Vector3(0f, 0f, -2f), out CharacterHealth corpse);
            CreateTarget("Live", Vector3.zero, out CharacterHealth live);
            corpse.ApplyDamage(corpse.MaxHealth);
            Assert.IsTrue(corpse.IsDead, "前提：前排目標必須已死");

            ThrownProjectile projectile = CreateProjectile(new Vector3(0f, 0.9f, -6f), 20f, null);
            GameObject projectileObject = projectile.gameObject;
            Physics.SyncTransforms();

            float liveStart = live.CurrentHealth;
            for (int i = 0; i < 200 && projectileObject != null; i++) yield return new WaitForFixedUpdate();

            Assert.Less(live.CurrentHealth, liveStart,
                "屍體必須讓掃掠穿過去——否則站在屍體後面的活敵人打不到（屍體變成永久的盾）");
        }
    }
}
