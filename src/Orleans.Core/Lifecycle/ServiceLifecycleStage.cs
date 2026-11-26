
namespace Orleans
{
    /// <summary>
    /// Orleans 客户端或 silo的生命周期阶段。
    /// </summary>
    public static class ServiceLifecycleStage
    {
        /// <summary>
        /// 服役使用寿命周期第一有效阶段
        /// </summary>
        public const int First = int.MinValue;

        /// <summary>
        /// 初始启动运行时间
        /// </summary>
        public const int RuntimeInitialize = 2000;

        /// <summary>
        /// 启动运行服务
        /// </summary>
        public const int RuntimeServices = 4000;

        /// <summary>
        /// 初始化运行时存储
        /// </summary>
        public const int RuntimeStorageServices = 6000;

        /// <summary>
        /// 启动运行服务
        /// </summary>
        public const int RuntimeGrainServices = 8000;

        /// <summary>
        /// 运行后服务已经开始。
        /// </summary>
        public const int AfterRuntimeGrainServices = 8100;

        /// <summary>
        /// 启动应用程序图层服务
        /// </summary>
        public const int ApplicationServices = 10000;

        /// <summary>
        /// 之后将积极提供服务。
        /// 仅应由成员甲骨文使用 
        /// 网关,其他部件都不应该运行
        /// 现阶段
        /// </summary>
        public const int BecomeActive = Active - 1;

        /// <summary>
        /// 取消活动 grain 启动期间的激活 silo 关闭。
        /// </summary>
        public const int GrainDeactivation = BecomeActive - 1;

        /// <summary>
        /// 停下来 grain 关闭触发后目录 grain 移民已经完成。
        /// </summary>
        public const int GrainDirectoryShutdown = GrainDeactivation - 1;

        /// <summary>
        /// 在活跃之前,先验证与活跃的集群成员的连接。
        /// </summary>
        public const int ValidateInitialConnectivity = Active - 100;

        /// <summary>
        /// 服务在进行中。
        /// </summary>
        public const int Active = 20000;

        /// <summary>
        /// 服役寿命周期的最后有效阶段
        /// </summary>
        public const int Last = int.MaxValue;
    }
}
