namespace Project.Presentation.Motion
{
    /// <summary>
    /// 🆕（ADR-002）一次跳躍發射的資料契約：起跳初速 + 該次採用的重力大小。
    /// 由 <c>JumpState</c> 依烘焙資料逆推（v = √(2gh)）並套用倍率後，傳給
    /// <see cref="MotionDriver.ApplyJumpLaunch"/>。命名採 <c>...Data</c> 尾綴，與
    /// <c>MotionBakeData</c> / <c>JumpStateParams</c> / <c>PlayerRuntimeData</c> 一致——
    /// 它是一份「資料（Data）」，不是事件或命令。
    /// <para>readonly struct：傳值零 heap 配置，符合 Zero-GC 規範。</para>
    /// </summary>
    public readonly struct JumpLaunchData
    {
        /// <summary>起跳初速（向上為正，m/s）。</summary>
        public readonly float InitialVerticalVelocity;

        /// <summary>本次發射採用的重力大小（正值，m/s²；由 MotionDriver 轉為向下積分）。</summary>
        public readonly float Gravity;

        public JumpLaunchData(float initialVerticalVelocity, float gravity)
        {
            InitialVerticalVelocity = initialVerticalVelocity;
            Gravity = gravity;
        }
    }
}
