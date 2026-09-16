using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Effects;
using Project.Core.Movement;
using Project.Core.Survivability;
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
        public void MeleeDistanceBand_TooCloseRetreats_InsideBandStrafes_TooFarApproaches()
        {
            const float minimum = 1.25f;
            const float maximum = 2f;
            const float hysteresis = 0.15f;

            Assert.AreEqual(AIMovementSource.EngagementMovement.Retreat,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Hold, 1f, minimum, maximum, hysteresis));
            Assert.AreEqual(AIMovementSource.EngagementMovement.Strafe,
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

            Assert.AreEqual(AIMovementSource.EngagementMovement.Strafe,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Retreat, 1.41f, minimum, maximum, hysteresis),
                "跨過內側 dead zone 後應進入 Strafe，不得讓 Retreat 黏住");
            Assert.AreEqual(AIMovementSource.EngagementMovement.Strafe,
                AIMovementSource.ResolveEngagementMovement(
                    AIMovementSource.EngagementMovement.Approach, 1.84f, minimum, maximum, hysteresis),
                "跨過外側 dead zone 後應進入 Strafe，不得讓 Approach 黏住");
        }

        [Test]
        public void StrafeDirection_IsPerpendicularToTargetDirection()
        {
            var toTarget = new Vector3(3f, 4f, -2f);

            Vector3 tangent = AIMovementSource.ResolveStrafeDirection(toTarget, 1);
            toTarget.y = 0f;

            Assert.AreEqual(0f, Vector3.Dot(toTarget.normalized, tangent), Tolerance);
        }

        [Test]
        public void StrafeDirection_WhenSignFlips_ReturnsOppositeDirection()
        {
            var toTarget = new Vector3(1f, 0f, 2f);

            Vector3 clockwise = AIMovementSource.ResolveStrafeDirection(toTarget, 1);
            Vector3 counterClockwise = AIMovementSource.ResolveStrafeDirection(toTarget, -1);

            Assert.Less((counterClockwise + clockwise).sqrMagnitude, Tolerance * Tolerance);
        }

        [Test]
        public void StrafeDirection_IsHorizontalAndNormalized()
        {
            Vector3 tangent = AIMovementSource.ResolveStrafeDirection(new Vector3(-4f, 8f, 3f), 1);

            Assert.AreEqual(0f, tangent.y, Tolerance);
            Assert.AreEqual(1f, tangent.magnitude, Tolerance);
        }

        [Test]
        public void StrafeDirection_WhenTargetDirectionIsZero_ReturnsFiniteZero()
        {
            Vector3 tangent = AIMovementSource.ResolveStrafeDirection(Vector3.zero, 1);

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

        /// <summary>
        /// 🆕（2026-09-15）**屍體不擋子彈。**
        ///
        /// 舊版 <c>TryRequestHit</c> 對已死目標照樣 <c>_completed = true</c> ＋ <c>Destroy</c>，
        /// 即使 <c>ApplyDamage</c> 回 false ⇒ 投射物被屍體吃掉、站在屍體後面的活敵人打不到。
        /// ⚠️ 這個缺陷**不會**表現成錯誤訊息或例外——只會表現成「這一發好像沒打到」。
        /// </summary>
        [Test]
        public void ProjectileHit_PassesThroughDeadTarget_WithoutConsumingItself()
        {
            var corpseObject = new GameObject("DeadTarget-Test");
            _created.Add(corpseObject);
            CharacterHealth corpse = corpseObject.AddComponent<CharacterHealth>();
            corpse.ApplyDamage(corpse.MaxHealth);
            Assert.IsTrue(corpse.IsDead, "前提：目標必須真的已死");

            var projectileObject = new GameObject("PassThroughProjectile-Test");
            _created.Add(projectileObject);
            ThrownProjectile projectile = projectileObject.AddComponent<ThrownProjectile>();

            Assert.IsFalse(projectile.TryRequestHit(corpse),
                "命中屍體不得算成一次命中");

            // 決定性的一半：投射物**沒有被消耗**，後面的活目標仍打得到。
            var liveObject = new GameObject("LiveTarget-Test");
            _created.Add(liveObject);
            CharacterHealth live = liveObject.AddComponent<CharacterHealth>();

            Assert.IsTrue(projectile.TryRequestHit(live),
                "穿過屍體之後必須仍能結算下一個合法目標——否則屍體等於一面永久的盾");
            Assert.Less(live.CurrentHealth, live.MaxHealth);
        }

        /// <summary>
        /// 反向守門：穿過屍體時**不得**順手施加 Slow。
        /// 減速一具屍體沒有意義，而且那會讓「命中」語意分岔成「傷害沒中但效果中了」兩半。
        /// </summary>
        [Test]
        public void ProjectileHit_DoesNotApplySlowToDeadTarget()
        {
            CreateMovementSource(out TemporaryGameplayEffectState effectState);
            CharacterHealth corpse = effectState.gameObject.AddComponent<CharacterHealth>();
            corpse.ApplyDamage(corpse.MaxHealth);

            var projectileObject = new GameObject("IceOnCorpse-Test");
            _created.Add(projectileObject);
            ThrownProjectile projectile = projectileObject.AddComponent<ThrownProjectile>();
            SetAppliesSlow(projectile, true);

            Assert.IsFalse(projectile.TryRequestHit(corpse, effectState));
            Assert.AreEqual(Effect.None, effectState.GetTagAt(Time.time),
                "屍體不得被減速——命中判定要嘛整個成立，要嘛整個不成立");
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
        public void GroundEffect_DeliversSameDamageAndSlowSeamsAsProjectile()
        {
            AIMovementSource source = CreateMovementSource(out TemporaryGameplayEffectState effectState);
            // 🔄（ADR-009 D2 ＋ docs/26）三個 sink 送的都是**傷害**；受擊由 CharacterHealth 發布。
            // 這條測試的價值反而更大了：它證明「換 execution shape 不必換投遞機制」。
            CharacterHealth health = source.gameObject.AddComponent<CharacterHealth>();
            Collider targetCollider = source.gameObject.AddComponent<SphereCollider>();

            GroundEffectSink sink = CreateGroundEffectSink();
            sink.ApplyToRoot(targetCollider);

            var data = new PlayerRuntimeData();
            health.PublishTo(data);
            Assert.IsTrue(data.Survivability.JustTookDamage,
                "地面爆發應與投射物、近戰走同一條傷害 seam（CharacterHealth.ApplyDamage）");
            Assert.Less(data.Survivability.CurrentHealth, data.Survivability.MaxHealth);
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
        /// <summary>
        /// 🔴 **冰刺的判定形狀必須對齊視覺形狀**（2026-09-15 使用者 Play 回報：「冰刺中了但沒生效」）。
        ///
        /// <para><b>舊形狀錯在哪</b></para>
        /// `Human_Spell_Ice` 的六排冰刺沿 local +Z 排在 0.58／1.15／1.87／2.66／3.42／4.42 m，
        /// 左右只有 ±0.17 m——**它是一條線**。舊判定卻是以身前 0.4 m 為心、半徑 3 m 的**球**：
        /// 前緣只到 3.4 m ⇒ 最後兩排冰刺（3.82／4.82 m）**完全在判定外**；
        /// 同時背後 3 m 內卻會被打到，與「前方一列冰刺」的語意完全相反。
        /// ⛔ 把半徑加大到 4.42 是**錯的修法**——那會讓背後也變成 4.42 m。
        /// </summary>
        [Test]
        public void IceHitVolume_CoversTheFullSpikeRow_AndDoesNotExtendBehindTheCaster()
        {
            GroundEffectSink sink = CreateGroundEffectSink();
            Vector3 center = Vector3.zero;

            sink.ResolveHitCapsule(center, Vector3.forward, out Vector3 start, out Vector3 end);

            const float FirstSpikeZ = 0.58f;
            const float LastSpikeZ = 4.42f;

            Assert.That(start.z, Is.EqualTo(FirstSpikeZ).Within(0.001f),
                "膠囊起點必須對齊第一排冰刺");
            Assert.That(end.z, Is.EqualTo(LastSpikeZ).Within(0.001f),
                "膠囊終點必須對齊最後一排冰刺——這正是舊球形判定構不到的那兩排");

            Assert.Greater(start.y, 0f,
                "端點必須自地面抬高一個 radius，讓膠囊向上撐開涵蓋站立角色，而不是只掃到腳踝");
            Assert.AreEqual(start.y, end.y, 0.001f, "兩端等高：判定體是水平躺著的膠囊");

            Assert.GreaterOrEqual(start.z, 0f,
                "判定不得延伸到落點後方——背後不該有冰刺，也不該有傷害");
        }

        /// <summary>判定體必須跟著施法方向轉，而不是永遠指著世界 +Z。</summary>
        [Test]
        public void IceHitVolume_FollowsCastDirection()
        {
            GroundEffectSink sink = CreateGroundEffectSink();

            sink.ResolveHitCapsule(Vector3.zero, Vector3.right, out Vector3 start, out Vector3 end);

            Assert.That(end.x, Is.GreaterThan(start.x), "朝 +X 施法時膠囊必須沿 +X 延伸");
            Assert.That(Mathf.Abs(end.z), Is.LessThan(0.001f), "不得殘留世界 +Z 的成分");
        }

        /// <summary>
        /// 退化守門：施法方向為零向量時不得產生 NaN。
        /// ⛔ 也**不得就地發明一個朝向**——方向權威屬於 ActionState 交付的承諾（A29）。
        /// 正確的退化是塌成一顆球（零長度膠囊），而不是自己讀 transform.root.forward。
        /// </summary>
        [Test]
        public void IceHitVolume_DegenerateDirection_CollapsesToSphereWithoutNaN()
        {
            GroundEffectSink sink = CreateGroundEffectSink();

            sink.ResolveHitCapsule(Vector3.zero, Vector3.zero, out Vector3 start, out Vector3 end);

            Assert.IsFalse(float.IsNaN(start.x) || float.IsNaN(end.x),
                "退化方向不得產生 NaN——NaN 會讓 OverlapCapsule 靜默查不到任何東西");
            Assert.AreEqual(0f, Vector3.Distance(start, end), 0.001f,
                "退化方向必須塌成零長度膠囊（＝球），不得自行解算出一個朝向");
        }
    }
}
