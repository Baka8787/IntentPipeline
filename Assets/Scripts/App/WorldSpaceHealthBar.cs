using Project.Core.Blackboard;
using Project.Core.Pipeline;
using UnityEngine;
using UnityEngine.UI;

namespace Project.App
{
    /// <summary>
    /// 角色頭上的世界空間血條（首個使用者：敵人）。**只讀、只畫，零決策。**
    ///
    /// <para><b>⭐ 可見性直接讀既有的 <c>CombatContext.InCombat</c>（使用者 2026-09-15 裁決）</b></para>
    /// ⛔ **沒有新增任何狀態、旗標或黑板欄位。** 敵人自己在管線順序 2.5 就在寫 `CombatContext`，
    /// 而且它已經有黏性進出半徑（8 m 進／12 m 出）⇒ 血條在邊界附近**不會逐幀閃爍**，
    /// 那是免費附贈的，不是這裡另外做的防抖。
    ///
    /// <para><b>🔴 死亡也要隱藏，而且理由不明顯</b></para>
    /// `AIMovementSource` **完全不認識死亡**（2026-09-14 那輪「屍體持續面向玩家」的同一個根因）
    /// ⇒ 死掉的敵人**仍然是 `InCombat`**。只看 `InCombat` 的話，屍體頭上會一直掛著一條空血條。
    /// ⇒ 一併讀 `Survivability.IsDead`。
    /// 📌 這是同一個教訓的第二次出現：**「死了」不會自動傳播到所有系統，每個讀者都要自己認識它。**
    ///
    /// <para><b>Billboard</b></para>
    /// 直接把相機的世界 rotation 抄過來（screen-aligned），不是 `LookAt`——
    /// `LookAt` 會讓畫面邊緣的血條各自傾斜一點點，看起來像歪掉。
    /// 相機解析沿用本專案既有慣例：**序列化欄位優先、`Camera.main` 退路**
    /// （同 `AimResolver` 與 `CharacterPipelineRunner`）。
    ///
    /// <para><b>⛔ 不是什麼</b></para>
    /// 沒有傷害數字、沒有名字、沒有狀態圖示、沒有淡入淡出、不會因為遮擋而隱藏。
    /// 它只回答一件事：**這隻敵人還剩多少血。**
    /// </summary>
    public sealed class WorldSpaceHealthBar : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("要顯示哪一隻角色。留空 ⇒ 往父物件找（血條掛在角色底下時不必接線）。")]
        [SerializeField] private CharacterPipelineRunner runner;

        [Header("View")]
        [Tooltip("要顯隱的整塊視覺。留空 ⇒ 用本物件自己。")]
        [SerializeField] private GameObject visualRoot;

        [Tooltip("血條填充。fillAmount ＝ CurrentHealth / MaxHealth。Image 的 Type 要設 Filled。")]
        [SerializeField] private Image healthFill;

        [Header("Billboard")]
        [Tooltip("要面向的相機。留空 ⇒ 退回 Camera.main（與 AimResolver 同慣例）。")]
        [SerializeField] private Camera facingCamera;

        private Camera _resolvedCamera;

        // 目前是否顯示。⚠️ 用來避免每幀呼叫 SetActive——
        // 那不只是浪費，重複 SetActive 會觸發 Canvas 重建。
        private bool _visible = true;

        private void Awake()
        {
            ResolveDependencies();
            // 一進場先收起來：還沒交戰就不該看到血條，而 prefab 上的預設是顯示的（方便編輯時看版面）。
            ApplyVisibility(false);
        }

        /// <summary>
        /// 解析相依物件。**與 <see cref="Awake"/> 分開，是為了 EditMode 測試**——
        /// EditMode 不在 Play mode，<c>AddComponent</c> 不會呼叫 <c>Awake</c>。
        /// 比照 <c>PlayerHud.ResolveRunner</c>／<c>RespawnController.ResolveDependencies</c>。
        /// </summary>
        internal void ResolveDependencies()
        {
            if (runner == null) runner = GetComponentInParent<CharacterPipelineRunner>(true);
            if (visualRoot == null) visualRoot = gameObject;
        }

        /// <summary>
        /// **LateUpdate**：血量在順序 0.5 發布、`CombatContext` 在順序 2.5 寫入
        /// ⇒ 這裡讀到的是本幀已定案的值。Billboard 也必須在相機移動之後才對齊。
        /// </summary>
        private void LateUpdate()
        {
            if (runner == null) return;

            PlayerRuntimeData data = runner.RuntimeData;
            if (data == null) return;   // Runner 尚未 Awake（腳本執行順序不保證）

            // 死亡要優先於交戰：屍體仍然是 InCombat（見型別註解）。
            bool shouldShow = data.CombatContext.InCombat && !data.Survivability.IsDead;
            ApplyVisibility(shouldShow);
            if (!shouldShow) return;   // 看不見就不必更新填充與朝向

            if (healthFill != null) healthFill.fillAmount = HealthBarFill.Resolve(data.Survivability);

            FaceCamera();
        }

        private void ApplyVisibility(bool visible)
        {
            if (visualRoot == null || _visible == visible) return;

            _visible = visible;
            visualRoot.SetActive(visible);
        }

        private void FaceCamera()
        {
            // 快取：Camera.main 每次都要找 tag。相機被換掉／銷毀時自然會是 null，下一幀重解。
            if (_resolvedCamera == null) _resolvedCamera = facingCamera != null ? facingCamera : Camera.main;
            if (_resolvedCamera == null) return;

            // 抄 rotation 而不是 LookAt：screen-aligned，畫面邊緣的血條才不會各自歪一點。
            transform.rotation = _resolvedCamera.transform.rotation;
        }

        /// <summary>供 EditMode 驗證：解析到的角色（可能為 null ＝ 接線缺失）。</summary>
        internal CharacterPipelineRunner ResolvedRunner => runner;

        /// <summary>供 EditMode 驗證：目前是否顯示。</summary>
        internal bool IsVisible => _visible;

        /// <summary>供 EditMode 驗證：直接驅動一次更新，不需要真的跑 LateUpdate。</summary>
        internal void TickForTests() => LateUpdate();
    }
}
