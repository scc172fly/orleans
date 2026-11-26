namespace Orleans.Runtime
{
    /// <summary>
    /// 生命周期 grains 的各个阶段。
    /// 待办事项:以后再增加,见 ActivationInitializationStage
    /// 完整的 grain 生命周期,包括登记、国家设置和
    ///   流流清理最终应全部由
    ///   grain 生命周期。
    /// </summary>
    public static class GrainLifecycleStage
    {
        /// <summary>
        /// grain生命周期中的第一个有效阶段。
        /// </summary>
        public const int First = int.MinValue;

        /// <summary>
        /// 激活前设置 grain 状态 。
        /// </summary>
        public const int SetupState = 1000;

        /// <summary>
        /// 激活 grain。
        /// </summary>
        public const int Activate = 2000;

        /// <summary>
        /// grain生命周期中的最后有效阶段。
        /// </summary>
        public const int Last = int.MaxValue;
    }
}
