using UnityEngine;

namespace Project.Core.Actions
{
    /// <summary>
    /// 外部 gameplay event 對角色提出 Action request 的單格 mailbox。
    /// 重複提交合併為一筆；FSM 每次 Tick 評估後即清除，不排隊、不重試。
    ///
    /// 🆕（ADR-005 D1）**request 帶身分**。原本無參數的版本讓「我被打到」與「我要出手」
    /// 在這條 seam 上不可區分（FU-3）——攻擊者只能說「動一下」，不能說「播受擊」。
    /// 身分沿用與輸入映射、冷卻、中斷規則同一把鍵，不另造 enum。
    /// </summary>
    public sealed class ActionRequestTarget : MonoBehaviour
    {
        private ActionSlot _pendingSlot;

        internal bool HasPendingRequest => _pendingSlot != ActionSlot.None;
        internal ActionSlot PendingSlot => _pendingSlot;

        /// <summary>
        /// 提交一筆 request。<paramref name="slot"/> 為 <see cref="ActionSlot.None"/> 時視為無效，直接忽略。
        /// ⚠️ 單格 mailbox：同一幀內的後續提交會**覆蓋**前一筆，不排隊。
        /// </summary>
        public void RequestAction(ActionSlot slot)
        {
            if (slot == ActionSlot.None) return;
            _pendingSlot = slot;
        }

        internal void ClearAfterEvaluation()
        {
            _pendingSlot = ActionSlot.None;
        }
    }
}
