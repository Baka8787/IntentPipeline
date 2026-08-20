using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.IK;

namespace Project.Presentation.Footstep
{
    /// <summary>
    /// 🆕（M3.x-B）落腳偵測器——第一個 <see cref="IPresentationEventSource"/> 實作。
    ///
    /// 讀 <see cref="FootIKPoseData"/>（**pre-IK 動畫原始 pose**，M3.x-A 起由其唯一 Writer
    /// <see cref="FootIKRig"/> 擁有生命週期）→ 自行維持跨帧偵測狀態 → 回傳 value struct。
    ///
    /// **職責邊界（輪 3 裁決，逐條）**：
    /// * **不寫黑板**——回傳值而非寫入，這是型別層的保證，不是紀律。
    /// * **不知道黑板上有沒有對應欄位**、不認識 <c>AudioController</c>／VFX／鏡頭、
    ///   不參與黑板事件的發布生命週期（那屬 <see cref="PresentationPipeline"/>）。
    /// * **不持有 <see cref="PlayerRuntimeData"/> 欄位引用**：黑板每帧由參數傳入。
    /// * 不 raycast、不詢問地面、不讀 post-IK 骨骼 Transform。
    ///
    /// ⚠️ **語意：這是「動畫落腳事件」，不是物理 ground contact。**
    /// 因此刻意讀 IK 套用**前**的 pose——斜坡上 IK 會把腳修到別處，但**落腳的時機不變**，
    /// 而玩家聽到的腳步聲要對齊看到的動畫落腳。改讀 post-IK 位置會讓聲音跟著地形修正漂移。
    /// </summary>
    public class FootstepDetector : MonoBehaviour, IPresentationEventSource
    {
        [SerializeField] private FootstepDetectionSettings settings = new();

        // Pose 管道：向 owner（唯一 Writer）取得引用，**不是**向其他 Controller 要
        // ——後者會構成 Controller 互相引用，違反 IPresentationController 契約（M3.x-A 明訂）。
        private FootIKPoseData _poseData;

        // 跨帧偵測狀態：本元件是它的擁有者。與「黑板事件的發布／生命週期」刻意分屬兩個擁有者。
        private FootPlantTracker _leftTracker;
        private FootPlantTracker _rightTracker;

        private void Awake()
        {
            var rig = GetComponentInChildren<FootIKRig>();
            if (rig == null)
            {
                Debug.LogError($"[{gameObject.name}] FootstepDetector 找不到 FootIKRig——" +
                    "落腳偵測需要它產出的 pose 快照，本元件將全程靜默。" +
                    "Rig 必須掛在 Model 子物件（與 Animator 同物件）。", this);
                return;
            }

            // Rig 以欄位初始式持有快照（早於所有 Awake），故此處不需要關心元件間的 Awake 順序。
            _poseData = rig.PoseData;
        }

        /// <summary>
        /// 【管線順序 6.5 末尾】推進雙腳偵測並回報本帧事件。熱路徑零配置（純數值運算，無 new／無 LINQ／無字串）。
        /// </summary>
        public PresentationEventData Evaluate(PlayerRuntimeData data)
        {
            // 本方法刻意只做「取得本帧輸入」這一件事，判定全部在 Detect —— 見 Detect 的說明。
            return Detect(_poseData, data != null && data.JustLanded, Time.deltaTime);
        }

        /// <summary>
        /// 落腳判定的完整邏輯，**輸入全部顯式傳入**。
        /// 拆出來的唯一理由是可測性：<c>Time.deltaTime</c> 在 EditMode 不可控，
        /// 若把它藏在方法內部，抑制規則與雙腳獨立性就只能靠 Play 模式肉眼驗
        /// （同 <c>FootIKController.ComputeFootWeight</c> 公開為純函數以供測試的先例）。
        /// ⚠️ 推進 tracker 會改動跨帧狀態，故本方法**不是**純函數，不可重複呼叫同一帧。
        /// </summary>
        public PresentationEventData Detect(FootIKPoseData pose, bool justLanded, float deltaTime)
        {
            PresentationEventData events = default;

            // IsWarm：快照尚未被 Rig 寫過（IK pass 未開／Animator 未評估）前不消費全零數據。
            if (pose == null || !pose.IsWarm) return events;

            // 腳底世界高度＝IK goal 世界 Y − 腳踝到腳底的 Avatar 常數。不參考地面、不 raycast。
            bool leftPlanted = _leftTracker.Advance(
                pose.LeftFootPosition.y - pose.LeftFootBottomHeight, deltaTime, settings);
            bool rightPlanted = _rightTracker.Advance(
                pose.RightFootPosition.y - pose.RightFootBottomHeight, deltaTime, settings);

            // Landing 優先（輪 3 裁決）：落地是更高階的語意，該帧不再額外報一次腳步。
            // ⚠️ 抑制刻意發生在**推進之後**——tracker 的跨帧狀態照常前進，
            //    否則落地那一步會讓下一步的行程基準與上膛狀態錯亂。
            //    「不報告」與「沒發生」是兩件事，這裡只做前者。
            if (justLanded) return events;

            events.LeftFootPlanted = leftPlanted;
            events.RightFootPlanted = rightPlanted;
            return events;
        }
    }
}
