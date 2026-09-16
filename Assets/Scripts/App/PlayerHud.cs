using System;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Pipeline;
using UnityEngine;
using UnityEngine.UI;

namespace Project.App
{
    /// <summary>
    /// 玩家 HUD：血條 ＋ 每個 slot 的冷卻進度。**只讀、只畫，零決策。**
    ///
    /// <para><b>⭐ 兩個資料來源，兩條不同的路，理由不同</b></para>
    /// <list type="bullet">
    /// <item><b>血量</b>走**黑板**（<c>RuntimeData.Survivability</c>）——它本來就是多個 runtime 系統
    /// 共享的角色真相（ADR-009 D1），HUD 只是又一個讀者，不需要任何新東西。</item>
    /// <item><b>冷卻</b>走 Runner 的**唯讀查詢**（<c>GetActionCooldownNormalized</c>）——
    /// ⛔ `docs/17` §3.3 **紅線 2 明文禁止「為了顯示而新增黑板欄位」**。
    /// 冷卻住在 `ActionState` 內部（ADR-004 D2），把它搬進黑板只為了畫圖，
    /// 就會讓「能不能出手」多一個看起來也很權威的來源。</item>
    /// </list>
    /// ⇒ **「顯示需要它」不是把資料放進黑板的理由。**
    ///
    /// <para><b>⚖️ 為什麼這顆元件住在 <c>App/</c> 而不是 <c>Presentation/</c></b></para>
    /// 這不是品味問題，是**機器守著的**：`ArchitectureRegressionTests.LayerRules` 禁止
    /// `Presentation/` 出現 `Project.Core.Pipeline`（表現層只讀黑板，不得認識管線）。
    /// HUD 需要 Runner 才問得到冷卻 ⇒ 它結構上就不屬於 `Presentation/`。
    /// 放在 `App/` 也與既有的 `GamePauseController`／`CursorModeController`／`RespawnController`
    /// 一致：**全域／系統層的東西，不是角色的一部分。**
    ///
    /// ⛔ 它也**不是** `IPresentationController`：那支介面只收 `PlayerRuntimeData`，
    /// 而冷卻不在黑板上——硬要塞就會逼出紅線 2 禁止的那個欄位。
    ///
    /// <para><b>零 GC：v1 刻意沒有任何文字</b></para>
    /// 數字標籤（「73 / 100」「2.4s」）每幀都要配置字串，是 HUD 最典型的 GC 來源，
    /// 而本專案的穩態 PlayerLoop 是 **0 B**（changelog v0.24 實測）。
    /// ⇒ v1 只有**填充比例**：`Image.fillAmount` 是 float 賦值，零配置。
    /// 要加數字時必須先準備無配置的 int→char 寫法，那是另一件事。
    ///
    /// <para><b>⛔ 不做的</b></para>
    /// 沒有技能圖示（使用者 2026-09-15 裁決 v1 不做）、沒有傷害數字、沒有 buff 列、
    /// 沒有敵人血條、沒有 UI 動畫。這顆元件只回答「現在幾滴血、技能好了沒」。
    /// </summary>
    public sealed class PlayerHud : MonoBehaviour
    {
        /// <summary>一個 slot 的冷卻顯示接線。`Slot` ＋ 一張會被填充的 `Image`，沒有第三個概念。</summary>
        [Serializable]
        public struct CooldownBinding
        {
            [Tooltip("要顯示哪一格的冷卻。")]
            public ActionSlot Slot;

            [Tooltip("冷卻遮罩。fillAmount 1 ＝ 剛進冷卻（整塊蓋住）、0 ＝ 已可用（完全露出）。\n" +
                     "Image 的 Type 要設 Filled；Radial 360 是最常見的技能冷卻形狀。")]
            public Image CooldownOverlay;
        }

        [Header("Source")]
        [Tooltip("要顯示哪一隻角色。留空 ⇒ 往父物件找（HUD 掛在角色底下時不必接線）。")]
        [SerializeField] private CharacterPipelineRunner runner;

        [Header("Health")]
        [Tooltip("血條填充。fillAmount ＝ CurrentHealth / MaxHealth。Image 的 Type 要設 Filled。")]
        [SerializeField] private Image healthFill;

        [Header("Cooldown")]
        [Tooltip("每個 slot 一列。沒列出來的 slot 就不顯示——⛔ 不要為了湊滿而填空 Image。")]
        [SerializeField] private CooldownBinding[] cooldownBindings = Array.Empty<CooldownBinding>();

        private void Awake() => ResolveRunner();

        /// <summary>
        /// 解析要顯示的角色。**與 <see cref="Awake"/> 分開，是為了 EditMode 測試**——
        /// EditMode 不在 Play mode，<c>AddComponent</c> 不會呼叫 <c>Awake</c>。
        /// 比照 <c>WeaponVisibilitySink.ResolveSocket</c>／<c>RespawnController.ResolveDependencies</c>。
        /// </summary>
        internal void ResolveRunner()
        {
            if (runner == null) runner = GetComponentInParent<CharacterPipelineRunner>(true);
        }

        /// <summary>
        /// **LateUpdate**：血量在順序 0.5（Update）發布、冷卻在順序 4 之後才可能改變
        /// ⇒ 在 LateUpdate 讀到的是**本幀已經定案**的值，不會畫出比實際落後一幀的畫面。
        /// 這與表現層管線選在順序 6.5（LateUpdate）是同一個理由。
        /// </summary>
        private void LateUpdate()
        {
            if (runner == null) return;

            // ⚠️ Runner 的黑板在它自己的 `Awake` 才建立，而**腳本執行順序不保證**。
            //    正常情況下 LateUpdate 必定晚於所有 Awake，但 Runner 被停用時 `RuntimeData` 仍是 null。
            //    安靜跳過一幀，⛔ 不要讓 HUD 把整個 LateUpdate 鏈路炸掉。
            PlayerRuntimeData data = runner.RuntimeData;
            if (data == null) return;

            UpdateHealth(data);
            UpdateCooldowns();
        }

        private void UpdateHealth(PlayerRuntimeData data)
        {
            if (healthFill == null) return;

            // 除零守衛與夾持都在 HealthBarFill 裡（見該處說明：NaN 的 fillAmount 不會報錯，
            // 只會畫出奇怪的東西）——⛔ 不要在這裡重寫一份。
            healthFill.fillAmount = HealthBarFill.Resolve(data.Survivability);
        }

        private void UpdateCooldowns()
        {
            // 索引迴圈：陣列雖然是具體型別、foreach 本可零配置，但本 repo 的熱路徑鐵律
            // 統一用索引迴圈，避免日後改成介面型集合時靜默回歸（changelog v0.24 的裝箱事件）。
            for (int i = 0; i < cooldownBindings.Length; i++)
            {
                CooldownBinding binding = cooldownBindings[i];
                if (binding.CooldownOverlay == null) continue;

                binding.CooldownOverlay.fillAmount =
                    runner.GetActionCooldownNormalized(binding.Slot);
            }
        }

        /// <summary>供 EditMode 驗證：解析到的角色（可能為 null ＝ 接線缺失）。</summary>
        internal CharacterPipelineRunner ResolvedRunner => runner;

        /// <summary>供 EditMode 驗證：直接驅動一次更新，不需要真的跑 LateUpdate。</summary>
        internal void TickForTests() => LateUpdate();
    }
}
