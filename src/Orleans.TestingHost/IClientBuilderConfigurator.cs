using Microsoft.Extensions.Configuration;
using Orleans.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 允许实现在启动测试集群的每个 Silo 时配置客户端构建器。
    /// </summary>
    public interface IClientBuilderConfigurator
    {
        /// <summary>
        /// 配置客户端构建器。
        /// </summary>
        /// <param name="configuration">配置。</param>
        /// <param name="clientBuilder">客户端构建器。</param>
        void Configure(IConfiguration configuration, IClientBuilder clientBuilder);
    }
}
