using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Project.App;
using Project.Core.Blackboard;
using Project.Core.Pipeline;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 敵人頭上血條的**可見性政策**與填充換算（2026-09-15 使用者裁決）。
    ///
    /// <para><b>可見性直接讀既有的 <c>CombatContext.InCombat</c></b></para>
    /// ⛔ 零新增狀態、零新增黑板欄位。敵人自己在順序 2.5 就在寫它，而且已經有黏性進出半徑
    /// ⇒ 邊界附近不會逐幀閃爍是**免費附贈**的，不是這裡另做的防抖。
    ///
    /// ⚠️ Billboard 與版面不在這裡驗——那是人眼的事。
    /// </summary>
    public sealed class WorldSpaceHealthBarTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [Test]
        public void OutOfCombat_IsHidden()
        {
            WorldSpaceHealthBar bar = CreateRig(out PlayerRuntimeData data, out _, out GameObject visual);

            data.CombatContext.InCombat = false;
            bar.TickForTests();

            Assert.IsFalse(bar.IsVisible);
            Assert.IsFalse(visual.activeSelf, "沒交戰就不該佔畫面");
        }

        [Test]
        public void InCombat_IsVisibleAndTracksHealth()
        {
            WorldSpaceHealthBar bar = CreateRig(
                out PlayerRuntimeData data, out Image fill, out GameObject visual);

            data.CombatContext.InCombat = true;
            data.Survivability.MaxHealth = 100f;
            data.Survivability.CurrentHealth = 40f;
            bar.TickForTests();

            Assert.IsTrue(bar.IsVisible);
            Assert.IsTrue(visual.activeSelf);
            Assert.AreEqual(0.4f, fill.fillAmount, 0.0001f);
        }

        /// <summary>
        /// 🔴 **死亡必須隱藏，而且理由不明顯。**
        ///
        /// `AIMovementSource` **完全不認識死亡**（2026-09-14「屍體持續面向玩家」的同一個根因）
        /// ⇒ 死掉的敵人**仍然是 `InCombat`**。只看 `InCombat` 的話，屍體頭上會一直掛著一條空血條。
        /// 📌 這是同一個教訓第二次出現：**「死了」不會自動傳播到所有系統，每個讀者都要自己認識它。**
        /// </summary>
        [Test]
        public void DeadWhileStillInCombat_IsHidden()
        {
            WorldSpaceHealthBar bar = CreateRig(out PlayerRuntimeData data, out _, out GameObject visual);

            // 刻意擺出真實會發生的組合：死了，但 CombatContext 還說在交戰中。
            data.CombatContext.InCombat = true;
            data.Survivability.IsDead = true;
            bar.TickForTests();

            Assert.IsFalse(bar.IsVisible, "屍體頭上不得掛著一條空血條");
            Assert.IsFalse(visual.activeSelf);
        }

        /// <summary>交戰狀態來回切換時，顯隱要跟著切回來——不是只會關一次。</summary>
        [Test]
        public void LeavingAndReenteringCombat_TogglesVisibilityBothWays()
        {
            WorldSpaceHealthBar bar = CreateRig(out PlayerRuntimeData data, out _, out GameObject visual);

            data.CombatContext.InCombat = true;
            bar.TickForTests();
            Assert.IsTrue(visual.activeSelf);

            data.CombatContext.InCombat = false;
            bar.TickForTests();
            Assert.IsFalse(visual.activeSelf);

            data.CombatContext.InCombat = true;
            bar.TickForTests();
            Assert.IsTrue(visual.activeSelf, "重新交戰必須再顯示出來");
        }

        /// <summary>接線缺失與尚未 `Awake` 的 Runner 都不得炸掉 LateUpdate 鏈路。</summary>
        [Test]
        public void MissingWiring_DoesNotThrow()
        {
            var host = new GameObject("Bar-Unwired");
            _created.Add(host);
            var bar = host.AddComponent<WorldSpaceHealthBar>();
            bar.ResolveDependencies();

            Assert.IsNull(bar.ResolvedRunner);
            Assert.DoesNotThrow(() => bar.TickForTests());
        }

        // =====================================================================
        // 治具
        // =====================================================================

        private WorldSpaceHealthBar CreateRig(
            out PlayerRuntimeData data, out Image healthFill, out GameObject visualRoot)
        {
            var character = new GameObject("Bar-Character");
            _created.Add(character);
            CharacterPipelineRunner runner = character.AddComponent<CharacterPipelineRunner>();

            // EditMode 不呼叫 Awake ⇒ 手動建立 Runner 在 Awake 會建的黑板。
            data = new PlayerRuntimeData();
            SetPrivateField(runner, "_runtimeData", data);

            var barObject = new GameObject("HealthBar", typeof(RectTransform));
            barObject.transform.SetParent(character.transform, false);

            visualRoot = new GameObject("Visual", typeof(RectTransform));
            visualRoot.transform.SetParent(barObject.transform, false);

            var fillObject = new GameObject("Fill", typeof(RectTransform));
            fillObject.transform.SetParent(visualRoot.transform, false);
            healthFill = fillObject.AddComponent<Image>();
            healthFill.type = Image.Type.Filled;

            var bar = barObject.AddComponent<WorldSpaceHealthBar>();
            SetPrivateField(bar, "visualRoot", visualRoot);
            SetPrivateField(bar, "healthFill", healthFill);
            bar.ResolveDependencies();
            return bar;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName}（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }
    }
}
