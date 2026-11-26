using Microsoft.Extensions.Configuration;
using Orleans.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 允许在测试组中启动每个 silo 时, 执行配置客户端构建器。
    /// </summary>
    public interface IClientBuilderConfigurator
    {
        /// <summary>
        /// 配置客户端构建器 。
        /// </summary>
        /// <param name="configuration">配置。</param>
        /// <param name="clientBuilder">客户端构建器。</param>
        void Configure(IConfiguration configuration, IClientBuilder clientBuilder);
    }
}
