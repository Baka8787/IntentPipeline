using Project.Core.Blackboard;
using UnityEngine;

namespace Project.App
{
    /// <summary>
    /// 把 <see cref="SurvivabilityData"/> 換算成 0–1 的填充比例。
    ///
    /// <para><b>⚖️ 為什麼這三行值得一個共用函式</b></para>
    /// 本專案的紀律是「第二個使用者出現前不建 abstraction」——而第二個使用者現在到了
    /// （`PlayerHud` 的螢幕血條 ＋ `WorldSpaceHealthBar` 的敵人頭上血條）。
    /// 但真正的理由不是「少寫三行」，是**那個除零守衛**：
    ///
    /// 缺 <c>CharacterHealth</c> 的角色整區恆為 <c>default</c> ⇒ <c>MaxHealth</c> 就是 0。
    /// 除下去得到 NaN，而 **Unity 不會為 NaN 的 <c>fillAmount</c> 報任何錯**——
    /// 畫面上只是「血條怪怪的」，沒有人會聯想到少掛了一顆元件。
    /// ⇒ 這個守衛**只想寫對一次**，不想在每個新血條上重新記得它。
    ///
    /// ⚠️ 純函式、無 Unity 物件依賴 ⇒ EditMode 可完整驗證，不需要場景。
    /// </summary>
    public static class HealthBarFill
    {
        /// <summary>
        /// 0 ＝ 空、1 ＝ 滿。<c>MaxHealth &lt;= 0</c>（沒有 <c>CharacterHealth</c> 的角色）回 0，
        /// **不回 NaN**；超出範圍的血量夾回 0–1。
        /// </summary>
        public static float Resolve(in SurvivabilityData survivability)
        {
            return survivability.MaxHealth > 0f
                ? Mathf.Clamp01(survivability.CurrentHealth / survivability.MaxHealth)
                : 0f;
        }
    }
}
