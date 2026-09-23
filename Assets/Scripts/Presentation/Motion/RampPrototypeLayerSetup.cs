using UnityEngine;

namespace Project.Presentation.Motion
{
    /// <summary>
    /// 🔬 PROTOTYPE（docs/43 §4，單一樓梯 Ramp Collider 實驗，2026-09-23）——局部、可撤銷，
    /// 尚未升格為專案 authoring 規範。
    ///
    /// 唯一職責：在執行期把「Player」層與「TreadIKOnly」層之間的碰撞關掉，讓角色只會撞到
    /// `RampProto` 層的 ramp collider，看不見（物理上）真正的階梯幾何——後者仍留給 Foot IK
    /// 的 raycast 查詢（Layer Collision Matrix 完全不影響 <c>Physics.Raycast</c>／
    /// <c>CapsuleCast</c> 的命中結果，只影響 CharacterController／Rigidbody 的實際碰撞解算，
    /// 所以這裡只需要處理「Player 不要真的撞上階梯」這一件事）。
    ///
    /// 只掛在本次 prototype 用的單一樓梯場景，若實驗結束不採用，整個檔案與掛載可以直接刪除，
    /// 不影響任何其他系統。
    /// </summary>
    public class RampPrototypeLayerSetup : MonoBehaviour
    {
        private void Awake()
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            int treadIkOnlyLayer = LayerMask.NameToLayer("TreadIKOnly");

            if (playerLayer < 0 || treadIkOnlyLayer < 0)
            {
                Debug.LogWarning("[RampPrototypeLayerSetup] 找不到 Player 或 TreadIKOnly layer，" +
                    "prototype 的 layer 隔離未生效。", this);
                return;
            }

            Physics.IgnoreLayerCollision(playerLayer, treadIkOnlyLayer, true);
        }
    }
}
