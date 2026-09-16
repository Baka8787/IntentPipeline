using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Project.Presentation.Equipment;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 武器掛載點的解析規則。**只驗解析,不驗視覺**——「劍看起來對不對」是 Play 的事。
    /// </summary>
    public sealed class WeaponSocketTests
    {
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

        private Transform CreateRig()
        {
            var root = new GameObject("Character");
            _created.Add(root);

            var hips = new GameObject("mixamorig:Hips");
            hips.transform.SetParent(root.transform);

            var spine = new GameObject("mixamorig:Spine");
            spine.transform.SetParent(hips.transform);

            var hand = new GameObject("mixamorig:RightHand");
            hand.transform.SetParent(spine.transform);

            return root.transform;
        }

        /// <summary>骨骼藏在階層深處也要找得到——`Transform.Find` 只找一層，所以必須遞迴。</summary>
        [Test]
        public void FindBone_LocatesDeeplyNestedBone()
        {
            Transform root = CreateRig();

            Transform hand = WeaponSocket.FindBone(root, "mixamorig:RightHand");

            Assert.IsNotNull(hand, "深層骨骼必須找得到");
            Assert.AreEqual("mixamorig:RightHand", hand.name);
        }

        [Test]
        public void FindBone_ReturnsNull_ForUnknownName()
        {
            Transform root = CreateRig();

            Assert.IsNull(WeaponSocket.FindBone(root, "mixamorig:NoSuchBone"));
            Assert.IsNull(WeaponSocket.FindBone(root, string.Empty));
            Assert.IsNull(WeaponSocket.FindBone(null, "mixamorig:RightHand"));
        }

        /// <summary>直接指定的 socket 優先於名稱查找——精確接線不該被自動搜尋蓋掉。</summary>
        [Test]
        public void ResolveParent_PrefersExplicitSocket()
        {
            Transform root = CreateRig();
            var explicitSocket = new GameObject("ExplicitSocket");
            _created.Add(explicitSocket);

            Transform resolved = WeaponSocket.ResolveParent(
                explicitSocket.transform, "mixamorig:RightHand", root);

            Assert.AreSame(explicitSocket.transform, resolved);
        }

        [Test]
        public void ResolveParent_FallsBackToBoneLookup()
        {
            Transform root = CreateRig();

            Transform resolved = WeaponSocket.ResolveParent(null, "mixamorig:RightHand", root);

            Assert.AreEqual("mixamorig:RightHand", resolved.name);
        }

        /// <summary>
        /// 🔴 **接線錯誤必須是看得見的**：骨骼找不到時退回角色本身，
        /// 武器會出現在角色原點——很醜，但你會**立刻知道接線錯了**。
        /// 靜默回傳 null、什麼都不生成，只會讓人以為模型壞了而去查錯的地方。
        /// </summary>
        [Test]
        public void ResolveParent_FallsBackToSelf_WhenBoneMissing()
        {
            Transform root = CreateRig();

            Transform resolved = WeaponSocket.ResolveParent(null, "mixamorig:NoSuchBone", root);

            Assert.AreSame(root, resolved, "找不到骨骼時要退回角色本身，讓錯誤看得見");
        }

        // =====================================================================
        // 武器顯隱（2026-09-14）—— 「攻擊時才顯現」
        // =====================================================================

        /// <summary>
        /// 沒掛 <see cref="WeaponVisibilitySink"/> 的角色**行為逐位元不變**：武器一直看得見。
        /// 這條守的是「加功能不得順手改掉既有角色」——Y Bot 沒有武器、其他角色也可能就是要一直握著。
        /// </summary>
        [Test]
        public void WeaponSocket_WithoutVisibilitySink_StaysVisible()
        {
            WeaponSocket socket = CreateSocketWithWeapon();

            Assert.IsTrue(socket.HasAttachment, "測試前提：武器必須真的生成出來");
            Assert.IsTrue(socket.IsAttachmentVisible, "沒有人管可見性時，武器維持可見（導入旗標前的行為）");
        }

        /// <summary>
        /// 🔴 **`Rebuild` 不得靜默地把武器變回可見。**
        ///
        /// `Rebuild` 會銷毀並重建實例——`Awake` 一次，之後每次在 Inspector 右鍵微調掛載位移也會再一次。
        /// 若可見狀態存在呼叫端而不是 <see cref="WeaponSocket"/> 自己，隱藏中的武器會在調整位移的那一刻
        /// 突然冒出來，而且**只在調位移時發作** —— 那是最難聯想到原因的一類 bug。
        /// </summary>
        [Test]
        public void SetVisible_SurvivesRebuild()
        {
            WeaponSocket socket = CreateSocketWithWeapon();

            socket.SetVisible(false);
            Assert.IsFalse(socket.IsAttachmentVisible, "測試前提：先確實隱藏");

            socket.Rebuild();

            Assert.IsTrue(socket.HasAttachment, "Rebuild 之後仍要有掛載物");
            Assert.IsFalse(socket.IsAttachmentVisible,
                "Rebuild 必須把**目前想要的**可見狀態套回新實例，不得悄悄變回可見");
        }

        /// <summary>
        /// Sink 的時點語意：<c>Begin</c> 顯現、<c>Release</c> 不動、<c>Cleanup</c> 收起。
        ///
        /// <c>Release</c> 刻意什麼都不做——那是「命中／發射」那一瞬間，
        /// 武器在整個動作期間都該留著，不是在 release 才出現、也不是在 release 就消失。
        /// </summary>
        [Test]
        public void VisibilitySink_ShowsOnBegin_KeepsOnRelease_HidesOnCleanup()
        {
            WeaponSocket socket = CreateSocketWithWeapon();
            WeaponVisibilitySink sink = AttachVisibilitySink(socket, visibleWhenIdle: false);

            Assert.IsFalse(socket.IsAttachmentVisible, "非出手期間預設不可見");

            sink.Begin();
            Assert.IsTrue(socket.IsAttachmentVisible, "出手開始 ⇒ 武器顯現");

            sink.Release(default);
            Assert.IsTrue(socket.IsAttachmentVisible,
                "release 是命中／發射的瞬間，與武器該不該看得見無關 ⇒ 必須維持顯現");

            sink.Cleanup();
            Assert.IsFalse(socket.IsAttachmentVisible, "動作結束 ⇒ 收起");
        }

        /// <summary>
        /// `Cleanup` 在自然播完與被中斷**兩條路徑**都會被呼叫，所以只要它負責收起，
        /// 就不會出現「被打斷後劍卡在手上」。這裡直接驗「Begin 之後單獨呼叫 Cleanup」也會收乾淨。
        /// </summary>
        [Test]
        public void VisibilitySink_CleanupWithoutRelease_StillHides()
        {
            WeaponSocket socket = CreateSocketWithWeapon();
            WeaponVisibilitySink sink = AttachVisibilitySink(socket, visibleWhenIdle: false);

            sink.Begin();
            sink.Cleanup();   // 被中斷：沒有經過 Release

            Assert.IsFalse(socket.IsAttachmentVisible, "被中斷的出手也必須把武器收回去");
        }

        /// <summary>
        /// `visibleWhenIdle = true` ⇒ 退回「武器一直掛著」，但接線不必拆。
        /// 這是為了讓「只在攻擊時顯現」與「一直顯現」可以直接比對手感。
        /// </summary>
        [Test]
        public void VisibilitySink_VisibleWhenIdle_KeepsWeaponAfterCleanup()
        {
            WeaponSocket socket = CreateSocketWithWeapon();
            WeaponVisibilitySink sink = AttachVisibilitySink(socket, visibleWhenIdle: true);

            Assert.IsTrue(socket.IsAttachmentVisible);
            sink.Begin();
            sink.Cleanup();

            Assert.IsTrue(socket.IsAttachmentVisible, "visibleWhenIdle ⇒ 動作結束後武器仍留著");
        }

        /// <summary>掛點缺失時必須安靜地什麼都不做，不得丟例外——接線缺失不該讓整個 Action 炸掉。</summary>
        [Test]
        public void VisibilitySink_WithoutSocket_DoesNothing()
        {
            var host = new GameObject("No-Socket-Host");
            _created.Add(host);
            var sink = host.AddComponent<WeaponVisibilitySink>();
            sink.ResolveSocket();

            Assert.IsNull(sink.ResolvedSocket, "測試前提：這顆角色身上沒有 WeaponSocket");
            Assert.DoesNotThrow(() =>
            {
                sink.Begin();
                sink.Release(default);
                sink.Cleanup();
            });
        }

        // =====================================================================
        // 治具
        // =====================================================================

        /// <summary>
        /// 建一個真的生成出掛載物的 <see cref="WeaponSocket"/>。
        /// ⚠️ EditMode 不呼叫 <c>Awake</c>，所以必須手動 <c>Rebuild()</c>；
        /// production 的時機與結果不變。
        /// </summary>
        private WeaponSocket CreateSocketWithWeapon()
        {
            Transform root = CreateRig();
            var weapon = new GameObject("Sword-Stand-In");
            _created.Add(weapon);

            WeaponSocket socket = root.gameObject.AddComponent<WeaponSocket>();
            SetPrivateField(socket, "weaponPrefab", weapon);
            socket.Rebuild();
            return socket;
        }

        /// <summary>
        /// 掛上 sink 並跑組裝期解析。
        /// ⚠️ 同樣因為 EditMode 不呼叫 <c>Awake</c>，這裡手動重現 <c>Awake</c> 的兩步：
        /// 解析掛點 ＋ 套用 idle 可見性。
        /// </summary>
        private static WeaponVisibilitySink AttachVisibilitySink(WeaponSocket socket, bool visibleWhenIdle)
        {
            var sink = socket.gameObject.AddComponent<WeaponVisibilitySink>();
            SetPrivateField(sink, "visibleWhenIdle", visibleWhenIdle);
            sink.ResolveSocket();
            socket.SetVisible(visibleWhenIdle);
            return sink;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }
    }
}
