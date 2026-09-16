using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Footstep
{
    /// <summary>
    /// 🆕（Stage 2）**顯式**指定一段幾何的聲學材質——Surface 解析的最高優先級。
    ///
    /// ⚠️ **這是例外機制，不是主要配置方式。** 主要方式是 `SurfaceMapSO` 的 Material 對應：
    /// 30 個物件共用一顆 Material 就只要設定一次。**若你發現自己在替大量地板逐個掛這顆元件，
    /// 代表 Material 對應沒設好，該回去補對應而不是繼續掛。**
    ///
    /// 合理用途只有這幾種：
    /// * 美術 Material 與聲學材質不同（貼石紋的木橋、鋪地毯的木地板）
    /// * 一個 Mesh 內含多種材質，靠 Renderer 第一槽取不到正確資訊
    /// * 純碰撞代理（collider 有幾何但沒有 Renderer）
    /// * 單一特例物件，為它開一筆 Material 對應反而更難維護
    ///
    /// **查找方式是 <c>GetComponentInParent</c>**：掛在父物件上即可涵蓋其下所有子 collider，
    /// 不需要逐個 collider 掛（抄自 `FMOD-Unity-Tools` 的 `SurfaceTag`，見 `docs/34` §3.2）。
    /// </summary>
    [AddComponentMenu("Project/Footstep/Surface Override")]
    [DisallowMultipleComponent]
    public sealed class SurfaceOverride : MonoBehaviour
    {
        [Tooltip("這段幾何聽起來是什麼材質。⚠️ Unknown 視同「沒有設定」，解析會繼續往下一個優先級。")]
        [SerializeField] private SurfaceId surface = SurfaceId.Stone;

        /// <summary>顯式指定的材質。<see cref="SurfaceId.Unknown"/> 代表「不表態」。</summary>
        public SurfaceId Surface => surface;
    }
}
