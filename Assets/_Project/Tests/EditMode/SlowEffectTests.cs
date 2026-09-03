using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Effects;
using Project.Core.Movement;
using Project.Presentation.Actions;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// docs/11 §7 的最小 Slow 垂直切片：單一 slot、刷新而不疊層、到期自動恢復，
    /// 並直接驗證 AIMovementSource 寫出的 normalized speed 語意是「剩 30%」。
    /// </summary>
    public sealed class SlowEffectTests
    {
        private const float Tolerance = 1e-4f;
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private AIMovementSource CreateMovementSource(out TemporaryGameplayEffectState effectState)
        {
            var gameObject = new GameObject("SlowEffect-Test");
            _created.Add(gameObject);
            effectState = gameObject.AddComponent<TemporaryGameplayEffectState>();
            return gameObject.AddComponent<AIMovementSource>();
        }

        [Test]
        public void NoEffect_MovementSpeedMultiplierIsOne()
        {
            AIMovementSource source = CreateMovementSource(out TemporaryGameplayEffectState effectState);

            Assert.AreEqual(1f, effectState.GetMovementSpeedMultiplierAt(10f), Tolerance);
            Assert.AreEqual(1f, source.ResolveDesiredSpeedNormalized(10f), Tolerance,
                "未中效果時必須與既有 AIMovementSource 行為完全相同");
        }

        [Test]
        public void ActiveSlow_DesiredSpeedNormalizedKeepsThirtyPercent()
        {
            AIMovementSource source = CreateMovementSource(out TemporaryGameplayEffectState effectState);
            effectState.ApplySlowAt(0.3f, duration: 5f, currentTime: 10f);

            Assert.AreEqual(0.3f, source.ResolveDesiredSpeedNormalized(10f), Tolerance,
                "Slow 是速度剩 30%，不是降低 30% 後剩 70%");
        }

        [Test]
        public void ProjectileHit_DeliversSlowStateWithoutMovementDependency()
        {
            CreateMovementSource(out TemporaryGameplayEffectState effectState);
            var projectileObject = new GameObject("SlowProjectile-Test");
            _created.Add(projectileObject);
            ThrownProjectile projectile = projectileObject.AddComponent<ThrownProjectile>();
            SetAppliesSlow(projectile, true);

            Assert.IsTrue(projectile.TryRequestHit(null, effectState));
            Assert.AreEqual(Effect.Slow, effectState.GetTagAt(Time.time));
            Assert.AreEqual(0.3f, effectState.GetMovementSpeedMultiplier(), Tolerance,
                "projectile 應投遞狀態與倍率，不得直接操作 AIMovementSource");
        }

        /// <summary>
        /// `ThrownProjectile` 是 Throw／Quick Spell／Ice Spell **共用**的元件，只換 prefab。
        /// `docs/11` §4 明列**只有 Ice Spell 投遞 Slow** ⇒ 減速必須由資產開關決定，不得寫死在程式。
        /// 預設關閉才能保證既有 Throw prefab 不改一個欄位、行為與加 Slow 之前完全相同。
        /// </summary>
        [Test]
        public void ProjectileWithoutAppliesSlow_DoesNotSlowTarget()
        {
            CreateMovementSource(out TemporaryGameplayEffectState effectState);
            var projectileObject = new GameObject("PlainProjectile-Test");
            _created.Add(projectileObject);
            ThrownProjectile projectile = projectileObject.AddComponent<ThrownProjectile>();

            Assert.IsTrue(projectile.TryRequestHit(null, effectState));
            Assert.AreEqual(Effect.None, effectState.GetTagAt(Time.time),
                "未開啟 appliesSlow 的投射物（Throw／Quick Spell）命中後不得施加 Slow");
            Assert.AreEqual(1f, effectState.GetMovementSpeedMultiplier(), Tolerance);
        }

        private static void SetAppliesSlow(ThrownProjectile projectile, bool value)
        {
            FieldInfo field = typeof(ThrownProjectile)
                .GetField("appliesSlow", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "appliesSlow 欄位不存在——Slow 是否又被寫死回程式裡？");
            field.SetValue(projectile, value);
        }

        [Test]
        public void RepeatedSlow_RefreshesExpiryWithoutStacking()
        {
            AIMovementSource source = CreateMovementSource(out TemporaryGameplayEffectState effectState);
            effectState.ApplySlowAt(0.3f, duration: 5f, currentTime: 10f);
            effectState.ApplySlowAt(0.3f, duration: 5f, currentTime: 12f);

            Assert.AreEqual(17f, effectState.ExpiresAt, Tolerance, "第二次命中應以本次時間刷新 expiresAt");
            Assert.AreEqual(0.3f, source.ResolveDesiredSpeedNormalized(12f), Tolerance,
                "單一 slot 只能維持 0.3 倍，不得把兩次命中乘成 0.09 倍");
        }

        [Test]
        public void ExpiredSlow_AutomaticallyRestoresSpeedAndClearsTag()
        {
            AIMovementSource source = CreateMovementSource(out TemporaryGameplayEffectState effectState);
            effectState.ApplySlowAt(0.3f, duration: 5f, currentTime: 10f);

            Assert.AreEqual(1f, source.ResolveDesiredSpeedNormalized(15f), Tolerance,
                "到達 expiresAt 時應自動恢復，不需要 projectile 或移動系統呼叫 remove");
            Assert.AreEqual(Effect.None, effectState.GetTagAt(15f), "到期 slot 的 tag 也必須清空");
        }
    }
}
