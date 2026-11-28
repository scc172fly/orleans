using Microsoft.Extensions.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 允许实现在启动测试集群的每个 Silo 时配置宿主构建器。
    /// </summary>
    public interface IHostConfigurator
    {
        /// <summary>
        /// 配置宿主构建器。
        /// </summary>
        /// <param name="hostBuilder">宿主构建器。</param>
        void Configure(IHostBuilder hostBuilder);
    }
}
