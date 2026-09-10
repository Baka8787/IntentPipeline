using UnityEngine;

namespace Project.Presentation.Equipment
{
    /// <summary>
    /// 把一個武器視覺掛到指定骨骼上。**只做視覺附著，不做任何 gameplay**——
    /// 命中判定仍然是 `MeleeHitboxSink`（由 `ActionState` 的 lifecycle 驅動），
    /// 這顆元件不知道攻擊、傷害或 Action 的存在。
    ///
    /// <para><b>⚖️ 為什麼是執行期掛載，而不是直接把劍拖進 prefab 階層</b></para>
    /// 兩種做法在遊戲裡看起來一樣，但維護成本差很多：
    /// <list type="bullet">
    /// <item>拖進階層 ⇒ 每個角色 prefab 各自複製一份武器子樹；換武器要逐一改。</item>
    /// <item>執行期掛載 ⇒ 武器是**一個資產引用**，換武器＝換一個欄位。</item>
    /// </list>
    /// 這也讓 AI 不需要為了「拿劍」而改動任何既有接線。
    ///
    /// <para><b>⛔ 這不是裝備系統</b></para>
    /// 沒有背包、沒有切換、沒有 `EquippedWeapon` 黑板欄位、沒有 `EquipmentDriver`。
    /// `PlayerRuntimeData` 註解裡提到的那套在**第二把武器出現前不建**
    /// （CLAUDE.md：第二個使用者出現前不得建立 production abstraction）。
    /// 本檔要解的問題只有一個：**空手揮劍在畫面上讀不出來**。
    /// </summary>
    public sealed class WeaponSocket : MonoBehaviour
    {
        [Tooltip("要掛上去的武器（模型資產或 prefab 皆可）。留空 ⇒ 什麼都不做。")]
        [SerializeField] private GameObject weaponPrefab;

        [Tooltip("掛載點。直接指定最精確；留空則改用下面的骨骼名稱查找。")]
        [SerializeField] private Transform socket;

        [Tooltip("`socket` 留空時，依名稱在階層裡找骨骼。\n" +
                 "⚠️ 這是刻意的：Y Bot 的 prefab 只有 2 個 GameObject（骨架來自模型實例），" +
                 "prefab 階段根本沒有手骨 Transform 可以拖。名稱查找讓兩個角色共用同一套接線。")]
        [SerializeField] private string socketBoneName = "mixamorig:RightHand";

        [Header("Local Offset（進 Play 後選中生成出來的武器即可讀出對的數值）")]
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;

        private GameObject _instance;

        private void Awake() => Rebuild();

        /// <summary>
        /// 重新生成並套用位移。**進 Play 之後可以在 Inspector 右鍵呼叫**——
        /// 掛載點的位移／旋轉只能靠眼睛調，每次都要重進 Play 會非常慢。
        /// </summary>
        [ContextMenu("Rebuild Attachment")]
        public void Rebuild()
        {
            if (_instance != null)
            {
                if (Application.isPlaying) Destroy(_instance);
                else DestroyImmediate(_instance);
                _instance = null;
            }

            if (weaponPrefab == null) return;

            Transform parent = ResolveParent(socket, socketBoneName, transform);
#if UNITY_EDITOR
            if (parent == transform && socket == null && !string.IsNullOrEmpty(socketBoneName))
            {
                Debug.LogWarning(
                    $"[{gameObject.name}] WeaponSocket 找不到骨骼 '{socketBoneName}'，武器改掛在角色原點。" +
                    "請確認骨骼名稱，或直接指定 Socket。", this);
            }
#endif
            _instance = Instantiate(weaponPrefab, parent);

            Transform instanceTransform = _instance.transform;
            instanceTransform.localPosition = localPosition;
            instanceTransform.localEulerAngles = localEulerAngles;
            instanceTransform.localScale = localScale;
        }

        /// <summary>供 EditMode 驗證：目前是否真的掛上了東西。</summary>
        internal bool HasAttachment => _instance != null;

        /// <summary>
        /// 掛載點解析：**直接指定 → 依名稱查找 → 退回自己**。
        ///
        /// 退回自己而不是「什麼都不做」是刻意的——忘了接線時，武器會出現在角色原點
        /// （很醜但**看得見**），而不是靜默消失讓人以為是模型壞了。
        /// 可見的錯誤比不可見的錯誤便宜太多。
        /// </summary>
        internal static Transform ResolveParent(Transform socket, string boneName, Transform fallback)
        {
            if (socket != null) return socket;

            Transform found = FindBone(fallback != null ? fallback.root : null, boneName);
            return found != null ? found : fallback;
        }

        /// <summary>
        /// 依名稱深度優先找骨骼。**只在 <see cref="Rebuild"/> 呼叫一次**，不在熱路徑，
        /// 因此遞迴搜尋的成本可以接受（零 GC 的約束針對的是每幀執行的程式碼）。
        /// </summary>
        internal static Transform FindBone(Transform root, string boneName)
        {
            if (root == null || string.IsNullOrEmpty(boneName)) return null;
            if (root.name == boneName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindBone(root.GetChild(i), boneName);
                if (found != null) return found;
            }

            return null;
        }
    }
}
