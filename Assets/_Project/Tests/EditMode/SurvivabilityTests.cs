using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Core.Survivability;
using Project.Presentation.Actions;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// `CharacterHealth` 的決策層（ADR-009 D1／D2 ＋ `docs/26` §I）。
    /// **本檔只驗「傷害進來之後 commit 了什麼」**；「commit 之後 FSM 怎麼反應」在
    /// <c>HurtAndDeathStateTests</c>。兩者刻意分開——那正是本次 migration 想建立的邊界。
    ///
    /// ⚠️ **EditMode 不呼叫 <c>Awake</c>**（本 repo 踩過：commit 58b79ca）。
    /// `CharacterHealth` 因此走惰性初始化，`H1` 同時守著這件事：
    /// 若有人把初始化搬回 `Awake`，第一下傷害就會把滿血角色打死。
    /// </summary>
    public sealed class SurvivabilityTests
    {
        private const float Tolerance = 1e-4f;
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // =====================================================================
        // 扣血與「後果由誰決定」
        // =====================================================================

        [Test]
        public void H1_NonLethalDamage_DeductsHealthAndPublishesHurt()
        {
            CharacterHealth health = CreateHealth(maxHealth: 100f);
            var data = new PlayerRuntimeData();

            Assert.IsTrue(health.ApplyDamage(30f));
            health.PublishTo(data);

            Assert.AreEqual(70f, data.Survivability.CurrentHealth, Tolerance, "未致死傷害必須確實扣血");
            Assert.IsFalse(data.Survivability.IsDead);
            Assert.IsTrue(data.Survivability.JustTookDamage,
                "未致死 ⇒ 發布一次受擊事件，交給 HurtState 決定要不要播");
        }

        /// <summary>
        /// ⭐ **本組最重要的一條**（ADR-009 D2）。致死時若同時抬起受擊旗標，
        /// Hurt 與 Death 會在同一幀爭同一顆 FSM——那正是「攻擊方自己決定後果」的具體代價。
        /// </summary>
        [Test]
        public void H2_LethalDamage_CommitsDeathAndDoesNotPublishHurt()
        {
            CharacterHealth health = CreateHealth(maxHealth: 25f);
            var data = new PlayerRuntimeData();

            Assert.IsTrue(health.ApplyDamage(25f));
            health.PublishTo(data);

            Assert.AreEqual(0f, data.Survivability.CurrentHealth, Tolerance, "生命值不得扣成負數");
            Assert.IsTrue(data.Survivability.IsDead);
            Assert.IsFalse(data.Survivability.JustTookDamage,
                "致死不得同時發布受擊——否則 Hurt 與 Death 同幀競爭");
        }

        [Test]
        public void H3_DamageAfterDeath_IsIgnored()
        {
            CharacterHealth health = CreateHealth(maxHealth: 10f);
            var data = new PlayerRuntimeData();
            health.ApplyDamage(10f);
            health.PublishTo(data);

            Assert.IsFalse(health.ApplyDamage(999f), "屍體不再結算傷害");
            health.PublishTo(data);
            Assert.IsFalse(data.Survivability.JustTookDamage, "屍體不再踉蹌");
            Assert.AreEqual(0f, data.Survivability.CurrentHealth, Tolerance);
            Assert.IsTrue(data.Survivability.IsDead, "死亡是不可逆的");
        }

        [Test]
        public void H4_NonPositiveOrNonFiniteDamage_IsRejected()
        {
            CharacterHealth health = CreateHealth(maxHealth: 100f);
            var data = new PlayerRuntimeData();

            Assert.IsFalse(health.ApplyDamage(0f));
            Assert.IsFalse(health.ApplyDamage(-5f));
            Assert.IsFalse(health.ApplyDamage(float.NaN));
            Assert.IsFalse(health.ApplyDamage(float.PositiveInfinity));

            health.PublishTo(data);
            Assert.AreEqual(100f, data.Survivability.CurrentHealth, Tolerance);
            Assert.IsFalse(data.Survivability.JustTookDamage, "無效傷害不得觸發受擊");
        }

        // =====================================================================
        // ⭐ 單幀發布語意（docs/26 §I：不排隊、不補播）
        // =====================================================================

        /// <summary>
        /// 🔴 **這條守的是「被 Roll／Traversal 擋掉的受擊要被丟棄，不補播」。**
        ///
        /// 若哪天有人把 `JustTookDamage` 改成「消費前不清除」或換成單調計數器，
        /// `HurtState.CanEnter` 會在翻滾結束後才變真 ⇒ **無敵幀之後補播一次踉蹌**。
        /// 那是錯的行為，而且在畫面上只會像「偶爾晚一拍抽動一下」，極難歸因。
        /// </summary>
        [Test]
        public void H5_JustTookDamage_IsOneFramePublication_AndDoesNotQueue()
        {
            CharacterHealth health = CreateHealth(maxHealth: 100f);
            var data = new PlayerRuntimeData();

            health.ApplyDamage(10f);
            health.PublishTo(data);
            Assert.IsTrue(data.Survivability.JustTookDamage, "發布當幀為真");

            // 下一幀沒有新傷害 ⇒ 整區覆寫自然回 false，不需要 ResetTransientState 參與。
            health.PublishTo(data);
            Assert.IsFalse(data.Survivability.JustTookDamage,
                "單幀事件：整區每幀覆寫即是它的復位機制（同 PresentationEventData）");
            Assert.AreEqual(90f, data.Survivability.CurrentHealth, Tolerance,
                "扣血是持續狀態，不會跟著旗標一起消失");
        }

        /// <summary>同一幀內多次命中只抬起一次 ⇒ 一次硬直。與舊 mailbox 的單格語意一致。</summary>
        [Test]
        public void H6_MultipleHitsInOneFrame_PublishOneHurtEvent()
        {
            CharacterHealth health = CreateHealth(maxHealth: 100f);
            var data = new PlayerRuntimeData();

            health.ApplyDamage(5f);
            health.ApplyDamage(5f);
            health.ApplyDamage(5f);
            health.PublishTo(data);

            Assert.IsTrue(data.Survivability.JustTookDamage);
            Assert.AreEqual(85f, data.Survivability.CurrentHealth, Tolerance,
                "三次傷害都要扣——被丟棄的只有『表現』，不是傷害");

            health.PublishTo(data);
            Assert.IsFalse(data.Survivability.JustTookDamage, "不排隊、不補播");
        }

        [Test]
        public void H7_PublishTo_WritesTheWholeSurvivabilityRegion()
        {
            CharacterHealth health = CreateHealth(maxHealth: 80f);
            var data = new PlayerRuntimeData();

            health.PublishTo(data);
            Assert.AreEqual(80f, data.Survivability.CurrentHealth, Tolerance);
            Assert.AreEqual(80f, data.Survivability.MaxHealth, Tolerance);
            Assert.IsFalse(data.Survivability.IsDead);
            Assert.IsFalse(data.Survivability.JustTookDamage);
        }

        // =====================================================================
        // 攻擊方 sink → CharacterHealth（ADR-009 D2 的投遞端）
        // =====================================================================

        /// <summary>
        /// 取代已移除的 `ActionStateTests.T14`：投射物只送傷害，**不再自己決定要播什麼**。
        /// </summary>
        [Test]
        public void H10_ProjectileHit_DeliversDamageOnly_AndPublishesHurt()
        {
            CharacterHealth health = CreateHealth(maxHealth: 100f);
            var projectileObject = new GameObject("Projectile-Test");
            _created.Add(projectileObject);
            ThrownProjectile projectile = projectileObject.AddComponent<ThrownProjectile>();

            Assert.IsTrue(projectile.TryRequestHit(health));
            Assert.IsFalse(projectile.TryRequestHit(health), "同一 projectile 不得重複提交 hit");

            var data = new PlayerRuntimeData();
            health.PublishTo(data);
            Assert.IsTrue(data.Survivability.JustTookDamage,
                "投射物命中 ⇒ 傷害 ⇒ CharacterHealth 發布受擊；投射物本身不認識受擊");
            Assert.Less(data.Survivability.CurrentHealth, 100f);
        }

        // =====================================================================
        // helpers
        // =====================================================================

        // =====================================================================
        // H11–H13 — Revive（2026-09-14，respawn）
        // =====================================================================

        /// <summary>
        /// **H11 — <c>Revive()</c> 是唯一能把 <c>IsDead</c> commit 回 false 的 API，且會回滿血。**
        ///
        /// ⚠️ 它**只負責 survivability**（使用者 2026-09-14 的 authority／orchestration 分離裁決）：
        /// 傳送、速度歸零、collider 還原都是 <c>RespawnController</c> 的事，不在這裡。
        /// </summary>
        [Test]
        public void H11_Revive_ClearsDeathAndRestoresFullHealth()
        {
            CharacterHealth health = CreateHealth(100f);
            health.ApplyDamage(150f);
            Assert.IsTrue(health.IsDead, "測試前提：先真的死掉");

            Assert.IsTrue(health.Revive(), "真的復活了 ⇒ 回 true，呼叫端可據此避免重複播效果");

            Assert.IsFalse(health.IsDead);
            Assert.AreEqual(100f, health.CurrentHealth, 0.0001f, "重生回滿血");
        }

        /// <summary>
        /// **H12 — 對活著的角色呼叫 <c>Revive()</c> 不做任何事。**
        ///
        /// 守的是「重生鍵按了會不會回血」：那等於一個免費的補血鍵，而且是**靜默**的
        /// ——沒有人會預期一顆重生鍵能在活著的時候回滿血。
        /// </summary>
        [Test]
        public void H12_ReviveWhileAlive_ChangesNothing()
        {
            CharacterHealth health = CreateHealth(100f);
            health.ApplyDamage(30f);
            Assert.AreEqual(70f, health.CurrentHealth, 0.0001f);

            Assert.IsFalse(health.Revive(), "本來就活著 ⇒ 回 false");
            Assert.AreEqual(70f, health.CurrentHealth, 0.0001f,
                "⛔ 重生鍵不得變成活著時的免費補血鍵");
        }

        /// <summary>
        /// **H13 — 重生不是受擊：<c>Revive()</c> 必須清掉待發布的受擊事件。**
        ///
        /// 殘留的旗標會讓角色**一復活就踉蹌一下**——看起來像被空氣打了一拳，
        /// 而且沒有任何錯誤訊息。
        ///
        /// <para><b>⚠️ 為什麼要用反射把 <c>_pendingHurt</c> 設起來</b></para>
        /// **今天的傷害路徑做不出這個組合**：致死那一支會同時把 <c>_pendingHurt</c> 清掉
        /// （ADR-009 D2——否則 Hurt 與 Death 會在同一幀爭同一顆 FSM）。
        /// 所以若照正常流程走，這條測試會**恆綠而且什麼都沒驗到**。
        /// ⇒ 直接把那個狀態擺出來，驗的才是 `Revive()` 自己的清除行為——
        /// 它是給**未來**的傷害路徑（DoT、環境傷害、處決）準備的防線，不是當下的修補。
        /// </summary>
        [Test]
        public void H13_ReviveClearsPendingHurt()
        {
            CharacterHealth health = CreateHealth(100f);
            health.ApplyDamage(200f);
            Assert.IsTrue(health.IsDead, "測試前提：先真的死掉");

            SetPrivateInstanceField(health, "_pendingHurt", true);
            Assert.IsTrue(health.HasPendingHurt, "測試前提：擺出「死了而且還有待發布的受擊」這個組合");

            health.Revive();
            Assert.IsFalse(health.HasPendingHurt, "復活必須清掉待發布的受擊事件");

            var data = new PlayerRuntimeData();
            health.PublishTo(data);
            Assert.IsFalse(data.Survivability.JustTookDamage,
                "復活後的第一次發布不得帶著死前殘留的受擊事件");
            Assert.IsFalse(data.Survivability.IsDead);
        }

        private CharacterHealth CreateHealth(float maxHealth)
        {
            var go = new GameObject("Survivability-Test");
            _created.Add(go);
            CharacterHealth health = go.AddComponent<CharacterHealth>();
            SetPrivateInstanceField(health, "maxHealth", maxHealth);
            health.InitializeForTest(maxHealth);
            return health;
        }

        private static void SetPrivateInstanceField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(
                name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {instance.GetType().Name}.{name}");
            field.SetValue(instance, value);
        }
    }
}
