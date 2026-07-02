namespace Project.Core.Arbitration
{
    /// <summary>
    /// v0.3 新增：仲裁旗標結構體。由 ArbiterPipeline 每幀寫入，各表現層唯讀。
    /// </summary>
    public struct ArbiterData
    {
        public bool BlockInput;       // 輸入封鎖
        public bool BlockIK;          // IK 封鎖
        public bool BlockAudio;       // 音頻封鎖
        public bool BlockExpression;  // 表情封鎖
    }
}
