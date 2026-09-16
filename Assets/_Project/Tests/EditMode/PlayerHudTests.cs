using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Project.App;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Pipeline;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// HUD 的讀取與換算（2026-09-15）。
    ///
    /// <para><b>驗「數字對不對」，不驗「好不好看」</b></para>
    /// 版面、顏色、動畫是人眼的事。這裡只回答：**給定角色狀態，填充比例算得對嗎**，
    /// 以及**接線缺一半的時候會不會炸**。
    ///
    /// <para><b>⭐ 本檔順帶釘住一條架構界線</b></para>
    /// 血量走黑板、冷卻走 Runner 的唯讀查詢——**兩條不同的路**。
    /// 冷卻之所以不走黑板，是因為 `docs/17` §3.3 紅線 2 禁止「為了顯示而新增黑板欄位」。
    /// 若哪天有人把冷卻塞進 `PlayerRuntimeData`，這裡的測試仍會綠——
    /// 守那條線的是 `ArchitectureRegressionTests.WriterRules` 與 review，不是本檔。
    /// </summary>
    public sealed class PlayerHudTests
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
        public void HealthFill_MatchesCurrentOverMax()
        {
            PlayerHud hud = CreateRig(out PlayerRuntimeData data, out Image healthFill, out _);

            data.Survivability.MaxHealth = 100f;
            data.Survivability.CurrentHealth = 73f;
            hud.TickForTests();

            Assert.AreEqual(0.73f, healthFill.fillAmount, 0.0001f);
        }

        /// <summary>
        /// 🔴 **`MaxHealth == 0` 不得除以零。**
        ///
        /// 缺 `CharacterHealth` 的角色整區恆為 `default` ⇒ `MaxHealth` 就是 0。
        /// 除下去會得到 NaN，而 Unity **不會為 NaN 的 `fillAmount` 報任何錯**——
        /// 畫面上只是「血條怪怪的」，沒有人會聯想到少掛了一顆元件。
        /// </summary>
        [Test]
        public void HealthFill_WithoutCharacterHealth_IsZeroNotNaN()
        {
            PlayerHud hud = CreateRig(out PlayerRuntimeData data, out Image healthFill, out _);

            data.Survivability = default;   // 完全沒有 CharacterHealth 的角色
            hud.TickForTests();

            Assert.IsFalse(float.IsNaN(healthFill.fillAmount), "⛔ 不得把 NaN 餵給 fillAmount");
            Assert.AreEqual(0f, healthFill.fillAmount, 0.0001f);
        }

        /// <summary>超出範圍的血量（溢補／負值）必須夾回 0–1，不能畫出超長或反向的長條。</summary>
        [Test]
        public void HealthFill_IsClampedToZeroOne()
        {
            PlayerHud hud = CreateRig(out PlayerRuntimeData data, out Image healthFill, out _);

            data.Survivability.MaxHealth = 100f;
            data.Survivability.CurrentHealth = 250f;
            hud.TickForTests();
            Assert.AreEqual(1f, healthFill.fillAmount, 0.0001f);

            data.Survivability.CurrentHealth = -30f;
            hud.TickForTests();
            Assert.AreEqual(0f, healthFill.fillAmount, 0.0001f);
        }

        /// <summary>
        /// 冷卻遮罩吃的是 Runner 的唯讀查詢。
        /// ⚠️ 這裡用一顆沒有 `Awake` 過的 Runner ⇒ 沒有狀態機 ⇒ 查詢安靜回 0（可用）。
        /// 那正是**接線做好但還沒開始玩**時該有的畫面：技能全亮，不是全暗。
        /// </summary>
        [Test]
        public void CooldownOverlay_WithoutStateMachine_ReadsAsReady()
        {
            PlayerHud hud = CreateRig(out _, out _, out Image overlay);

            overlay.fillAmount = 1f;   // 先弄髒，證明它真的有被寫
            hud.TickForTests();

            Assert.AreEqual(0f, overlay.fillAmount, 0.0001f,
                "沒有狀態機時冷卻查詢回 0 ⇒ 遮罩完全露出（技能可用）");
        }

        /// <summary>接線缺一半也不能炸——少一條 binding 只該少畫一格。</summary>
        [Test]
        public void MissingWiring_DoesNotThrow()
        {
            var host = new GameObject("Hud-Unwired");
            _created.Add(host);
            var hud = host.AddComponent<PlayerHud>();
            hud.ResolveRunner();

            Assert.IsNull(hud.ResolvedRunner, "測試前提：找不到任何 Runner");
            Assert.DoesNotThrow(() => hud.TickForTests());
        }

        /// <summary>
        /// Runner 還沒 `Awake`（黑板尚未建立）時必須安靜跳過。
        /// **腳本執行順序不保證**，而 HUD 炸掉會把整條 LateUpdate 鏈路一起帶走。
        /// </summary>
        [Test]
        public void RunnerWithoutRuntimeData_DoesNotThrow()
        {
            var character = new GameObject("Hud-No-Blackboard");
            _created.Add(character);
            CharacterPipelineRunner runner = character.AddComponent<CharacterPipelineRunner>();

            var hudObject = new GameObject("PlayerHud");
            _created.Add(hudObject);
            hudObject.transform.SetParent(character.transform, false);
            var hud = hudObject.AddComponent<PlayerHud>();
            hud.ResolveRunner();

            Assert.IsNotNull(hud.ResolvedRunner, "測試前提：往父物件找得到 Runner");
            Assert.IsNull(runner.RuntimeData, "測試前提：Runner 尚未 Awake ⇒ 黑板還不存在");
            Assert.DoesNotThrow(() => hud.TickForTests());
        }

        /// <summary>HUD 掛在角色底下時不必接線——`GetComponentInParent` 就找得到。</summary>
        [Test]
        public void ResolveRunner_FindsRunnerInParent()
        {
            PlayerHud hud = CreateRig(out _, out _, out _);
            Assert.IsNotNull(hud.ResolvedRunner);
        }

        // =====================================================================
        // 治具
        // =====================================================================

        private PlayerHud CreateRig(
            out PlayerRuntimeData data, out Image healthFill, out Image cooldownOverlay)
        {
            var character = new GameObject("Hud-Character");
            _created.Add(character);
            CharacterPipelineRunner runner = character.AddComponent<CharacterPipelineRunner>();

            // EditMode 不呼叫 Awake ⇒ 手動建立 Runner 在 Awake 會建的黑板。
            data = new PlayerRuntimeData();
            SetPrivateField(runner, "_runtimeData", data);

            var hudObject = new GameObject("PlayerHud", typeof(RectTransform));
            _created.Add(hudObject);
            hudObject.transform.SetParent(character.transform, false);

            var fillObject = new GameObject("Fill", typeof(RectTransform));
            fillObject.transform.SetParent(hudObject.transform, false);
            healthFill = fillObject.AddComponent<Image>();
            healthFill.type = Image.Type.Filled;

            var overlayObject = new GameObject("CooldownOverlay", typeof(RectTransform));
            overlayObject.transform.SetParent(hudObject.transform, false);
            cooldownOverlay = overlayObject.AddComponent<Image>();
            cooldownOverlay.type = Image.Type.Filled;

            var hud = hudObject.AddComponent<PlayerHud>();
            SetPrivateField(hud, "healthFill", healthFill);
            SetPrivateField(hud, "cooldownBindings", new[]
            {
                new PlayerHud.CooldownBinding { Slot = ActionSlot.Slot1, CooldownOverlay = cooldownOverlay },
            });
            hud.ResolveRunner();
            return hud;
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
