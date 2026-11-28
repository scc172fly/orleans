namespace Orleans.Runtime
{
    /// <summary>
    /// Grain 生命周期的各个阶段。
    /// TODO：稍后添加更多阶段，参见 ActivationInitializationStage。
    /// 完整的 Grain 生命周期，包括注册、状态初始化和
    /// 流清理，最终都应由
    /// Grain 生命周期机制触发。
    /// </summary>
    public static class GrainLifecycleStage
    {
        /// <summary>
        /// Grain 生命周期中的第一个有效阶段。
        /// </summary>
        public const int First = int.MinValue;

        /// <summary>
        /// 在激活前初始化 Grain 状态。
        /// </summary>
        public const int SetupState = 1000;

        /// <summary>
        /// 激活 Grain。
        /// </summary>
        public const int Activate = 2000;

        /// <summary>
        /// Grain 生命周期中的最后一个有效阶段。
        /// </summary>
        public const int Last = int.MaxValue;
    }
}
