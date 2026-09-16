using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Project.Presentation.Actions;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// **視覺 core 必須與 collider 同步**——投射物唯一的視覺／判定不變量（使用者裁決 2026-09-16）。
    ///
    /// <para><b>🐞 這條在防什麼</b></para>
    /// 玩家**瞄的是 core、遊戲判的是 collider**。兩者分家就會出現「明明打到了卻沒傷害」。
    /// 2026-09-16 實測：core 曾以 10 m/s 的相對速度甩開 collider（0.22 s 差 2.22 m、0.67 s 差 6.68 m），
    /// 那是「火球穿過敵人卻沒受傷」的**主要**成因。
    ///
    /// <para><b>🔄 現行做法：VFX 與投射物是兩個獨立物件</b></para>
    /// `Human_Spell_Fireball` 的原生模型是**「發射器不動、core 粒子自走」**——
    /// 三個尾巴是掛在 core 粒子上的 [Birth] 子發射器，且用 `rateOverDistance` 依**移動距離**出粒。
    /// 曾嘗試把 core 的 `startSpeed` 歸零來強制對齊，結果**同時抽掉了尾巴的空間展開與出粒依據**
    /// ⇒ 拖尾幾乎消失（使用者回報）。
    ///
    /// ⇒ 改為讓 VFX 回到原生模型：`Initialize` 時 **unparent、留在發射點不動**，core 照原設定自走 15 m/s；
    /// gameplay 投射物也以 **15 m/s** 平行飛。**不變量因此變強**——
    /// 兩套互不相干的模擬必須靠「同源、同速、同向」持續對齊，而視覺零妥協。
    ///
    /// <para><b>⛔ 為什麼不斷言某個具體參數</b></para>
    /// `startSpeed == 0`／`== 15` 都是**實作細節**：換素材或改用 velocityOverLifetime／
    /// inheritVelocity／forceOverLifetime／SubEmitter 推進，都會讓那種斷言失效卻仍破壞不變量。
    /// ⇒ 這裡斷言的是**可觀察結果**：core 粒子與 collider 的實際距離。
    ///
    /// <para><b>core 的身分用結構判準</b></para>
    /// 帶 SubEmitter 的那一個就是 core——其餘四個是它生出來的尾巴。
    /// 名字不保證語意；粒子大小差距（0.55 vs 0.49）太接近，不足以當判準。
    ///
    /// 📌 **trail／sparks 不在此約束內**：它們明確是尾巴，不是玩家認知裡的「火球本體」。
    /// </summary>
    public class ProjectileVisualAlignmentPlayModeTests
    {
        private const string FireballPrefabPath = "Assets/Prefabs/Projectile_Fireball.prefab";

        /// <summary>
        /// 容許值。實測殘差 0.02 m（發射器形狀散佈）；自走回歸只要 0.1 秒就會超過 1 m
        /// ⇒ 這個門檻既不會 flaky，也一定抓得到回歸。
        /// </summary>
        private const float MaximumCoreOffset = 1f;

        private readonly List<Object> _created = new List<Object>();
        private static readonly ParticleSystem.Particle[] Buffer = new ParticleSystem.Particle[2048];

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator PV1_ProjectileVisualCore_StaysWithTheCollider()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(FireballPrefabPath);
            Assert.IsNotNull(source, $"找不到 {FireballPrefabPath}");

            GameObject go = Object.Instantiate(source);
            _created.Add(go);
            go.transform.position = new Vector3(0f, 1f, -8f);
            go.transform.forward = Vector3.forward;

            var sphere = go.GetComponentInChildren<SphereCollider>();
            Assert.IsNotNull(sphere, "投射物必須有 SphereCollider，否則不會命中任何東西");

            // **結構判準**：帶 SubEmitter 的那一個是 core，其餘是它生出來的尾巴。
            // ⚠️ 必須在 Initialize **之前**取得——Initialize 會把視覺 unparent 出去
            //    （2026-09-16 裁決 B：VFX 回到「發射器不動、core 自走」的原生模型）。
            ParticleSystem core = null;
            var candidates = new StringBuilder();
            foreach (ParticleSystem system in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                candidates.Append(system.name).Append('(')
                    .Append(system.subEmitters.subEmittersCount).Append(") ");
                if (system.subEmitters.subEmittersCount > 0) core = system;
            }

            // 速度必須與 core 的 startSpeed 一致——兩套獨立模擬靠「同源同速」保持對齊。
            go.GetComponent<ThrownProjectile>().Initialize(15f, 5f, null);
            if (core != null) _created.Add(core.transform.root.gameObject);

            Assert.IsNotNull(core,
                "找不到帶 SubEmitter 的 core emitter——VFX 結構已改變，"
                + $"本測項的身分判準需要重新檢視。候選：{candidates}");

            // 飛一段時間，讓「自走」型的回歸有機會把差距拉開。
            for (int i = 0; i < 40 && go != null; i++) yield return null;
            Assert.IsNotNull(go, "自由飛行中不該消失");

            int count = core.GetParticles(Buffer);
            Assert.Greater(count, 0, $"{core.name} 沒有存活粒子——core 看不見就談不上對齊");

            Vector3 colliderPosition = sphere.transform.position;
            float worst = 0f;
            for (int i = 0; i < count; i++)
            {
                // Local space 的 particle.position 相對發射器，必須自己轉世界。
                Vector3 world = core.main.simulationSpace == ParticleSystemSimulationSpace.World
                    ? Buffer[i].position
                    : core.transform.TransformPoint(Buffer[i].position);
                worst = Mathf.Max(worst, Vector3.Distance(world, colliderPosition));
            }

            Assert.LessOrEqual(worst, MaximumCoreOffset,
                $"視覺 core（{core.name}）離 collider {worst:F2} m，超過容許的 {MaximumCoreOffset} m。\n"
                + "玩家瞄的是 core、遊戲判的是 collider——兩者分家就會出現「打到了卻沒傷害」。\n"
                + "常見成因：core emitter 自帶位移（startSpeed／velocityOverLifetime／inheritVelocity／"
                + "forceOverLifetime），與 ThrownProjectile 搬 GameObject 的位移相加。\n"
                + "⇒ 位移只能由一方承擔：GameObject 搬，core 粒子不自走。");
        }
    }
}
