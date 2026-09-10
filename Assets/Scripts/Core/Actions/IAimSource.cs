using UnityEngine;

namespace Project.Core.Actions
{
    /// <summary>
    /// Action 在段落邊界取得方向承諾所需的最小查詢 seam。
    /// Core 只消費世界點與既有起點，不認識相機、射線或任何 Presentation 實作細節。
    /// </summary>
    public interface IAimSource
    {
        /// <summary>
        /// 方向承諾的起點＝角色世界位置。
        /// ⚠️ 這個位置由 aim source 提供，只是忠實承接目前 <c>AimResolver</c> 與角色 Root
        /// 同物件的既有耦合；不表示「瞄準來源理應知道角色在哪」。拆開兩項責任屬另一個切片。
        /// </summary>
        Vector3 CommitmentOrigin { get; }

        /// <summary>嘗試取得當幀的世界瞄準點；失敗時呼叫端不得建立方向承諾。</summary>
        bool TryGetAimPoint(out Vector3 worldPoint);
    }
}
