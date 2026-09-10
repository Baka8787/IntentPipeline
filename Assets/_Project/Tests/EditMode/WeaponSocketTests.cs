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
    }
}
