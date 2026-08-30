using UnityEngine;

namespace Project.Core.Actions
{
    /// <summary>
    /// 外部 gameplay event 對角色提出 Action request 的單格 mailbox。
    /// 重複提交合併為一筆；FSM 每次 Tick 評估後即清除，不排隊、不重試。
    /// </summary>
    public sealed class ActionRequestTarget : MonoBehaviour
    {
        private bool _hasPendingRequest;

        internal bool HasPendingRequest => _hasPendingRequest;

        public void RequestAction()
        {
            _hasPendingRequest = true;
        }

        internal void ClearAfterEvaluation()
        {
            _hasPendingRequest = false;
        }
    }
}
