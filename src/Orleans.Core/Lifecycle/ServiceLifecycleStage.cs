
namespace Orleans
{
    /// <summary>
    /// Orleans 客户端或 Silo 的生命周期阶段。
    /// </summary>
    public static class ServiceLifecycleStage
    {
        /// <summary>
        /// 服务生命周期中第一个有效阶段。
        /// </summary>
        public const int First = int.MinValue;

        /// <summary>
        /// 初始化运行时。
        /// </summary>
        public const int RuntimeInitialize = 2000;

        /// <summary>
        /// 启动运行时服务。
        /// </summary>
        public const int RuntimeServices = 4000;

        /// <summary>
        /// 初始化运行时存储。
        /// </summary>
        public const int RuntimeStorageServices = 6000;

        /// <summary>
        /// 启动运行时服务。
        /// </summary>
        public const int RuntimeGrainServices = 8000;

        /// <summary>
        /// 运行时服务启动之后。
        /// </summary>
        public const int AfterRuntimeGrainServices = 8100;

        /// <summary>
        /// 启动应用层服务。
        /// </summary>
        public const int ApplicationServices = 10000;

        /// <summary>
        /// 此步骤之后服务将处于活动状态。
        /// 此阶段应仅由成员资格预言器（membership oracle）
        /// 和网关使用，其他组件均不应在
        /// 此阶段运行。
        /// </summary>
        public const int BecomeActive = Active - 1;

        /// <summary>
        /// 在 Silo 关闭期间停用 Grain 激活实例。
        /// </summary>
        public const int GrainDeactivation = BecomeActive - 1;

        /// <summary>
        /// 在关闭触发的 Grain 迁移完成后停止 Grain 目录。
        /// </summary>
        public const int GrainDirectoryShutdown = GrainDeactivation - 1;

        /// <summary>
        /// 在进入活动状态之前验证与活动集群成员之间的连通性。
        /// </summary>
        public const int ValidateInitialConnectivity = Active - 100;

        /// <summary>
        /// 服务处于活动状态。
        /// </summary>
        public const int Active = 20000;

        /// <summary>
        /// 服务生命周期中最后一个有效阶段。
        /// </summary>
        public const int Last = int.MaxValue;
    }
}
