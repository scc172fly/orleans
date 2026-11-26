using Microsoft.Extensions.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 允许在测试组中启动每个 silo 时执行配置主机构建器。
    /// </summary>
    public interface IHostConfigurator
    {
        /// <summary>
        /// 配置主机构建器。
        /// </summary>
        /// <param name="hostBuilder">主机建造者。</param>
        void Configure(IHostBuilder hostBuilder);
    }
}
