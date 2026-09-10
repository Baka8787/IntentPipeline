using System;
using UnityEngine;
using Project.Presentation.Motion;
using Project.Core.Actions;

namespace Project.Core.StateMachine.Actions
{
    /// <summary>
    /// 「這一擊朝哪」的取得方式（`docs/11` §8.3，2026-09-08 使用者裁決；ADR-007 D3 §11 有對應修訂）。
    ///
    /// 一句話：**普通招式永遠可預測地朝鏡頭方向；只有明確標成 soft-target 的技能才會自動修正到敵人。**
    ///
    /// ⚠️ 這是**帶預設值的 enum，不是布林開關**——差別是失敗模式：
    /// 布林忘了勾 ⇒ 得到「這招不轉向」這種不可預測且無線索的行為；
    /// 本 enum 忘了填 ⇒ 得到 <see cref="CameraForward"/> ＝ 與所有普通招式**完全一致**的預設。
    /// ⇒ 例外是**顯性**的，不是遺漏造成的。這正是 ADR-007 D3 把原禁令收窄後仍然成立的理由。
    ///
    /// ⛔ **第一版就這三種，不得擴充**：不加第四種 policy，也不加 per-Action 的角度上限／吸敵強度旋鈕
    /// （那會退回「一致性交給填表的人」的老問題）。
    /// </summary>
    public enum ActionTargetingPolicy
    {
        /// <summary>方向 ＝ camera forward，**永不**自動修正到敵人。一般攻擊、指向技。**預設值**。</summary>
        CameraForward = 0,

        /// <summary>
        /// **僅當**鏡頭前方角錐內有合法敵人時修正到該敵人；否則退回 <see cref="CameraForward"/>。
        /// 只給明確標記的吸敵技能。
        /// </summary>
        CameraConeSoftTarget = 1,

        /// <summary>
        /// 自身中心技：不需要 target／facing，**不產生方向承諾**。
        /// ⚠️ 目前沒有合適素材 ⇒ 只保留概念，⛔ 不為它硬做技能。
        /// </summary>
        SelfCentered = 2
    }

    [Serializable]
    public struct ActionPhaseEntry
    {
        public ActionPhase Phase;
        public string AnimationKey;
        public MotionBakeData Bake;
        public float FallbackDuration;
        public bool Interruptible;
        public bool WaitForTrigger;
        public bool EmitsRelease;
        [Range(0f, 1f)] public float ReleaseNormalizedTime;
    }

    [CreateAssetMenu(fileName = "ActionDefinition", menuName = "Project/Core/Action/ActionDefinition")]
    public sealed class ActionDefinitionSO : StateParamsSO
    {
        [Tooltip("這份 Definition 的身分（ADR-005 D1）。輸入映射、冷卻、external request、中斷規則全以它為鍵。\n" +
                 "同一角色的多份 Definition 不得共用同一個 Slot；None 視為未設定。")]
        public ActionSlot Slot = ActionSlot.Slot1;

        [Tooltip("這一擊朝哪（docs/11 §8.3）。\n" +
                 "CameraForward（預設）＝永遠朝鏡頭，不自動修正 ⇒ 普通攻擊、指向技留這個。\n" +
                 "CameraConeSoftTarget ＝只有鏡頭前方角錐內有敵人時才修正到敵人，否則退回朝鏡頭。\n" +
                 "SelfCentered ＝不取得方向承諾（目前無素材，只保留概念）。\n" +
                 "⚠️ 沒填就是 CameraForward，與其他普通招式一致；要吸敵必須顯性標記。")]
        public ActionTargetingPolicy Targeting = ActionTargetingPolicy.CameraForward;

        [Tooltip("Phase 集合；Start 必須存在，其餘可省略。順序不重要，以 Phase 欄位查找。")]
        public ActionPhaseEntry[] Phases;

        [Tooltip("連段：第 2 段起的動畫段。留空 ⇒ 這不是連段技。\n" +
                 "第 1 段永遠是 Phases 的 Start；本陣列**依索引順序**推進，元素的 Phase 欄位不被讀取。\n" +
                 "整條連段共用一次 Cooldown（在最後一段結束時才提交），不是每段各算一次。")]
        public ActionPhaseEntry[] ChainSegments;

        [Tooltip("接受「再按一次」的時間窗起點，以當前段的 normalized time 表示。\n" +
                 "0 ＝ 整段都能接；0.25 ＝ 播完四分之一後才接受；1 ＝ 實質關閉連段。\n" +
                 "窗的終點固定是該段結束——該段播完仍沒接到就照原路徑收尾（Loop／End／結束）。")]
        [Range(0f, 1f)] public float ChainInputOpenNormalized = 0.25f;

        [Min(0f)] public float Cooldown = 0.5f;

        [Tooltip("冷卻的隨機加量（秒）。實際冷卻 ＝ Cooldown ＋ [0, CooldownVariance] 之間的隨機值。\n" +
                 "0 ＝ 固定節奏（玩家技能通常要這個：可預測才能規劃）。\n" +
                 "> 0 ＝ 每次出手的間隔不同（敵人通常要這個：固定間隔會像節拍器）。\n" +
                 "⚠️ 只會讓冷卻**變長**，不會短於 Cooldown ⇒ authored 的下限永遠成立。")]
        [Min(0f)] public float CooldownVariance;
        public bool RequiresGrounded = true;
        [Min(0f)] public float CancelMoveIntentThreshold = 0.1f;
    }
}
