using UnityEngine;

namespace Project.Presentation.Motion
{
    /// <summary>
    /// **角色的膠囊姿勢配置**（`docs/27` §12／`docs/28`）——dynamic capsule offset 的唯一數值來源。
    ///
    /// <para><b>為什麼是 ScriptableObject（推翻 2026-09-14 的「刻意不是 SO」）</b></para>
    /// 舊版 <see cref="CapsuleOffsetSettings"/> 的註解寫「只被 MotionDriver 一個消費者讀，
    /// 每個角色的值本來就該不同 ⇒ 不值得抽 SO」。**那個判斷在 V1 成立，在 V2 不成立**：
    /// <list type="bullet">
    /// <item>V1 的參數是 4 個手感數字；V2 的參數是**從角色網格量出來的物理事實**
    /// （flesh radius）。它們有來源、要能追溯、換模型就得重量——那是資產，不是手感旋鈕。</item>
    /// <item>實測 X Bot 與 Y Bot **網格不同**（Beta 15,901 verts vs Alpha 22,914 verts），
    /// Chest flesh radius 0.2038 vs 0.2902。⇒ 兩個角色**必須**各有一份，不能共用也不能硬寫死。</item>
    /// </list>
    ///
    /// <para><b>⛔ 刻意不放進來的東西</b></para>
    /// <c>Enabled</c> 留在 <see cref="MotionDriver"/> 的 per-instance 設定上——那是「這一隻角色現在
    /// 要不要開」，屬於場景佈署決定，不是角色資料。把它放進共用資產會讓「關掉玩家」連帶關掉所有人。
    /// </summary>
    [CreateAssetMenu(
        fileName = "CapsulePoseProfile",
        menuName = "Project/Presentation/Motion/Capsule Pose Profile")]
    public class CapsulePoseProfileSO : ScriptableObject
    {
        /// <summary>一個 landmark（身體參考點）的 authored 條目。</summary>
        [System.Serializable]
        public struct LandmarkEntry
        {
            [Tooltip("取消勾選＝這個參考點不參與求解。用來實驗某個點的影響，不要當成常態關閉手段。")]
            public bool Enabled;

            [Tooltip("要取樣的人形骨骼。用 HumanBodyBones 而不是骨骼名稱字串，換 rig 不會因為命名慣例不同而失效。\n" +
                     "Mixamo 對應：Chest→Spine1、UpperChest→Spine2、Neck→Neck、Head→Head。")]
            public HumanBodyBones Bone;

            [Tooltip("FleshRadius（這個關節周圍代表的網格厚度，公尺）。\n" +
                     "＝ rest pose 下「以此關節為 dominant bone 的所有網格頂點」到該關節的最大距離。\n" +
                     "⚠️ 這是從角色網格量出來的物理事實，不是手感參數。換模型必須重量。\n" +
                     "量法見 docs/27 §12.1；X Bot 實測 Chest 0.2038 / UpperChest 0.1867 / Neck 0.0952 / Head 0.2109。")]
            [Min(0f)] public float FleshRadius;
        }

        [Header("Landmarks（身體參考點）")]
        [Tooltip("⛔ 不得加入手、腳、武器。揮出去的手本來就不該被膠囊包住——包了會讓角色在門框卡住（docs/27 §4.2）。")]
        [SerializeField]
        private LandmarkEntry[] landmarks = new LandmarkEntry[0];

        [Header("Safety Cap（安全上限）")]
        [Tooltip("MaxOffset（最大水平偏移安全上限，公尺）。\n\n" +
                 "⚠️ **這是上限，不是驅動量。** 不是「跑步就往前推 0.18」，而是「姿勢解出多少就用多少，" +
                 "但最多不超過 0.18」。\n" +
                 "  Walk   solver=0.00 → 0.00（完全不偏）\n" +
                 "  Run    solver=0.15 → 0.15（照解出的值）\n" +
                 "  Sprint solver=0.24 → 0.18（被上限夾住）\n\n" +
                 "為什麼不設更大：膠囊是整顆平移的，底部支撐點也跟著前移，站在平台邊緣時會出現" +
                 "「看起來踩空卻站著」。docs/27 §12.6 的取捨表：0.18 拿掉約 2/3 穿模，支撐前移還在一個腳掌的量級。")]
        [SerializeField, Min(0f)]
        private float maxOffset = 0.18f;

        [Header("Temporal Smoothing（時間平滑）")]
        [Tooltip("偏移**伸出去**時的 SmoothDamp 時間（秒）。太小會讓碰撞體瞬移。")]
        [SerializeField, Min(0f)]
        private float smoothTime = 0.12f;

        [Tooltip("偏移**收回來**時的 SmoothDamp 時間（秒）。刻意比 SmoothTime 短：" +
                 "伸出去要慢（安全），收回來要快（脫困、停步後迅速歸位）。")]
        [SerializeField, Min(0f)]
        private float releaseSmoothTime = 0.06f;

        [Tooltip("Root 轉向角速度（度／秒）超過此值時偏移權重歸零。\n\n" +
                 "⚠️ **這道 guard 在 V2 仍然成立，理由與 V1 不同**：collision-aware clamp 只沿" +
                 "「偏移方向」掃一次，它看不到「角色原地轉身時，偏移出去的膠囊繞 root 掃出的那道弧線」。\n" +
                 "180° 快速轉身在 0.18 偏移下，膠囊的世界位移速度可達 2 m/s 以上，而那個方向沒有被 cast 檢查過。")]
        [SerializeField, Min(0f)]
        private float maximumTurnRateDegreesPerSecond = 180f;

        [Header("Guards（各狀態是否允許姿勢偏移）")]
        [Tooltip("死亡期間是否仍套用水平偏移。\n\n" +
                 "第一版預設 **關閉**：死亡動畫會大幅倒地，軀幹幾乎水平，此時" +
                 "「上半身佔用位置」與「站立膠囊」已經不是同一回事，解出來的偏移沒有意義。\n" +
                 "⚠️ 這是唯一一個仍然按『狀態』而不是按『姿勢』關閉的 guard，因為姿勢本身已經離開了模型的適用範圍。")]
        [SerializeField]
        private bool applyWhileDead = false;

        public int LandmarkCount => landmarks?.Length ?? 0;

        public LandmarkEntry GetLandmark(int index) => landmarks[index];

        /// <summary>MaxOffset（最大水平偏移安全上限）。見欄位 tooltip：**上限，不是驅動量**。</summary>
        public float MaxOffset => Mathf.Max(0f, maxOffset);

        public float SmoothTime => Mathf.Max(0f, smoothTime);

        public float ReleaseSmoothTime => Mathf.Max(0f, releaseSmoothTime);

        public float MaximumTurnRateDegreesPerSecond => Mathf.Max(0f, maximumTurnRateDegreesPerSecond);

        public bool ApplyWhileDead => applyWhileDead;

        /// <summary>
        /// 資產是否足以驅動求解。不合法時 <see cref="MotionDriver"/> 會維持 authored center
        /// （安全退化），而不是用一組半成品數值去移動碰撞體。
        /// </summary>
        public bool IsUsable()
        {
            if (landmarks == null || landmarks.Length == 0) return false;
            for (int i = 0; i < landmarks.Length; i++)
            {
                if (landmarks[i].Enabled && landmarks[i].FleshRadius > 0f) return true;
            }
            return false;
        }
    }
}
