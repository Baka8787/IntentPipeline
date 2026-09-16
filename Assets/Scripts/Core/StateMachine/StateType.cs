namespace Project.Core.StateMachine
{
    public enum StateType
    {
        None,
        Idle,
        Move,
        Jump,
        Roll,
        Action,
        Traversal,

        /// <summary>
        /// 🆕（ADR-009 D3）死亡。**吸收態**：進得去、出不來。
        ///
        /// ⚠️ 數值 7，**不遞補既有數值**——`StateMachineConfigSO` 的 `CanBeInterruptedBy`／
        /// `ValidTransitions` 在資產裡是以 **int** 序列化的（`0400000003000000` 這種），
        /// 改動既有數值會讓所有 config 資產默默指向別的狀態。
        /// </summary>
        Death,

        /// <summary>
        /// 🆕（`docs/26` Model B，2026-09-14）受擊硬直。**被動反應，不是主動行動。**
        ///
        /// <para><b>為什麼它不是一個 Action</b></para>
        /// 它由外部事件（傷害）觸發，不取得方向承諾、可以自我重入、沒有冷卻、沒有 release window、
        /// 沒有連段。`ActionState` 曾為這些差異各留一處反轉，那正是把它抽出來的理由。
        ///
        /// ⚠️ 數值 8，加在**最後**，不遞補——config 資產以 int 序列化 `CanBeInterruptedBy`。
        /// </summary>
        Hurt
    }
}
