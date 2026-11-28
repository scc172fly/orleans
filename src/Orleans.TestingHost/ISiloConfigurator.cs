using Orleans.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 允许实现在启动测试集群的每个 Silo 时配置 Silo 构建器。
    /// </summary>
    public interface ISiloConfigurator
    {
        /// <summary>
        /// 配置 Silo 构建器。
        /// </summary>
        /// <param name="siloBuilder">Silo 构建器。</param>
        void Configure(ISiloBuilder siloBuilder);
    }
}
