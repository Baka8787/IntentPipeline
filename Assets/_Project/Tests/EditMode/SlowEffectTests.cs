using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Actions;
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
            var source = gameObject.AddComponent<AIMovementSource>();

            // ⚠️ EditMode 不在 Play mode ⇒ AddComponent **不會呼叫 Awake** ⇒ 元件的 sibling 快取是空的。
            //    這裡顯式補上 Awake 會做的那一步，否則 _effectState 恆為 null、倍率恆為 1，
            //    下面所有 Slow 斷言都會「假性失敗」（測的其實是沒有效果的路徑）。
            source.ResolveEffectState();
            return source;
        }

        [Test]
        public void MeleeDistanceBand_TooCloseRetreats_InsideBandHolds_TooFarApproaches()
        {
            const float minimum = 1.25f;
            const float maximum = 2f;
            const float hysteresis = 0.15f;

            Assert.AreEqual(AIMovementSource.EngagementMovement.Retreat,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Hold, 1f, minimum, maximum, hysteresis));
            Assert.AreEqual(AIMovementSource.EngagementMovement.Hold,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Hold, 1.5f, minimum, maximum, hysteresis));
            Assert.AreEqual(AIMovementSource.EngagementMovement.Approach,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Hold, 2.25f, minimum, maximum, hysteresis));
        }

        [Test]
        public void MeleeDistanceBand_HysteresisPreventsBoundaryOscillation()
        {
            const float minimum = 1.25f;
            const float maximum = 2f;
            const float hysteresis = 0.15f;

            Assert.AreEqual(AIMovementSource.EngagementMovement.Retreat,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Retreat, 1.3f, minimum, maximum, hysteresis),
                "後退進入距離帶後，必須再跨過內側 dead zone 才停住");
            Assert.AreEqual(AIMovementSource.EngagementMovement.Approach,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Approach, 1.9f, minimum, maximum, hysteresis),
                "前進進入距離帶後，必須再跨過內側 dead zone 才停住");
        }

        [Test]
        public void HoldStrafeDirection_IsPerpendicularToTargetDirection()
        {
            var toTarget = new Vector3(3f, 4f, -2f);

            Vector3 tangent = AIMovementSource.ResolveHoldStrafeDirection(toTarget, 1);
            toTarget.y = 0f;

            Assert.AreEqual(0f, Vector3.Dot(toTarget.normalized, tangent), Tolerance);
        }

        [Test]
        public void HoldStrafeDirection_WhenSignFlips_ReturnsOppositeDirection()
        {
            var toTarget = new Vector3(1f, 0f, 2f);

            Vector3 clockwise = AIMovementSource.ResolveHoldStrafeDirection(toTarget, 1);
            Vector3 counterClockwise = AIMovementSource.ResolveHoldStrafeDirection(toTarget, -1);

            Assert.Less((counterClockwise + clockwise).sqrMagnitude, Tolerance * Tolerance);
        }

        [Test]
        public void HoldStrafeDirection_IsHorizontalAndNormalized()
        {
            Vector3 tangent = AIMovementSource.ResolveHoldStrafeDirection(new Vector3(-4f, 8f, 3f), 1);

            Assert.AreEqual(0f, tangent.y, Tolerance);
            Assert.AreEqual(1f, tangent.magnitude, Tolerance);
        }

        [Test]
        public void HoldStrafeDirection_WhenTargetDirectionIsZero_ReturnsFiniteZero()
        {
            Vector3 tangent = AIMovementSource.ResolveHoldStrafeDirection(Vector3.zero, 1);

            Assert.AreEqual(Vector3.zero, tangent);
            Assert.IsFalse(float.IsNaN(tangent.x));
            Assert.IsFalse(float.IsNaN(tangent.y));
            Assert.IsFalse(float.IsNaN(tangent.z));
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

        // =====================================================================
        // 地面 AoE（docs/11 §4.4）——Ice 的 execution shape
        // =====================================================================

        /// <summary>
        /// **本組最有價值的一條**：它證明「換 execution shape 不必換投遞機制」。
        /// 地面爆發與投射物走的是**同一組**既有 seam——`ActionRequestTarget` ＋
        /// `TemporaryGameplayEffectState`——sink 只是換了決定「打到誰」的方式。
        /// </summary>
        [Test]
        public void GroundEffect_DeliversSameReactionAndSlowSeamsAsProjectile()
        {
            AIMovementSource source = CreateMovementSource(out TemporaryGameplayEffectState effectState);
            ActionRequestTarget target = source.gameObject.AddComponent<ActionRequestTarget>();
            Collider targetCollider = source.gameObject.AddComponent<SphereCollider>();

            GroundEffectSink sink = CreateGroundEffectSink();
            sink.ApplyToRoot(targetCollider);

            Assert.AreEqual(ActionSlot.Reaction, target.PendingSlot,
                "地面爆發應與投射物、近戰走同一條受擊鏈（Reaction）");
            Assert.AreEqual(Effect.Slow, effectState.GetTagAt(Time.time));
            Assert.AreEqual(0.3f, effectState.GetMovementSpeedMultiplier(), Tolerance,
                "倍率必須來自 TemporaryGameplayEffectState 的單一常數，不得由 sink 自己寫死");
        }

        /// <summary>同一個目標在一次爆發內只結算一次；多顆 collider 的敵人不得被結算多次。</summary>
        [Test]
        public void GroundEffect_SameTargetRootIsRegisteredOnlyOnce()
        {
            GroundEffectSink sink = CreateGroundEffectSink();
            var targetObject = new GameObject("AoETarget-Test");
            _created.Add(targetObject);

            Assert.IsTrue(sink.TryRegisterRoot(targetObject.transform), "第一次應成立");
            Assert.IsFalse(sink.TryRegisterRoot(targetObject.transform), "同一個 root 不得重複結算");
        }

        /// <summary>
        /// 落點是**施法者正前方固定距離**，不是瞄準點。
        ///
        /// 🔄 **2026-09-06 改寫**：舊版斷言「退回正前方 `defaultCastDistance`（4）」，
        /// 在加入上下限夾制後變成 3 而紅——**測試是對的，它照出設計已經換過兩次**。
        /// 現在的設計只剩一個 `castDistance`：方向由 Action-time facing 負責（§8.3），
        /// 距離由這個欄位負責，**沒有第二個來源可以把落點拉到敵人腳下**。
        /// </summary>
        [Test]
        public void GroundEffect_SpawnsAtFixedForwardOffsetFromCaster()
        {
            GroundEffectSink sink = CreateGroundEffectSink();
            SetPrivateFloat(sink, "castDistance", 0.4f);

            Assert.IsTrue(sink.TryResolveGroundPointForTests(out Vector3 point));

            // EditMode 沒有地面可探 ⇒ 退回水平落點，正好讓這條斷言是確定性的。
            Assert.AreEqual(0f, point.x, Tolerance);
            Assert.AreEqual(0.4f, point.z, Tolerance, "應落在角色正前方 0.4 公尺");
            Assert.IsFalse(float.IsNaN(point.x) || float.IsNaN(point.z), "不得產生 NaN");
        }

        /// <summary>落點必須跟著角色朝向轉，否則轉身施法時冰刺會留在原方向。</summary>
        [Test]
        public void GroundEffect_SpawnPointFollowsCasterFacing()
        {
            GroundEffectSink sink = CreateGroundEffectSink();
            SetPrivateFloat(sink, "castDistance", 2f);
            sink.transform.root.rotation = Quaternion.Euler(0f, 90f, 0f);

            Assert.IsTrue(sink.TryResolveGroundPointForTests(out Vector3 point));

            Assert.AreEqual(2f, point.x, Tolerance, "面向 +X 時應落在 +X");
            Assert.AreEqual(0f, point.z, Tolerance);
        }

        private GroundEffectSink CreateGroundEffectSink()
        {
            var gameObject = new GameObject("GroundEffectSink-Test");
            _created.Add(gameObject);
            return gameObject.AddComponent<GroundEffectSink>();
        }

        private static void SetPrivateFloat(Object target, string fieldName, float value)
        {
            FieldInfo field = target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName} 欄位不存在");
            field.SetValue(target, value);
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
