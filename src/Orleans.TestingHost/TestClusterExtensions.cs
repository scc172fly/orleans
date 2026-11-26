using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 试验组群的扩展方法。
    /// </summary>
    public static class TestClusterExtensions
    {
        /// <summary>
        /// 从指定的主机构建器获取配置 。
        /// </summary>        
        /// <param name="builder">
        /// 建筑工
        /// </param>
        public static IConfiguration GetConfiguration(this IHostBuilder builder)
        {
            if (builder.Properties.TryGetValue("Configuration", out var configObject) && configObject is IConfiguration config)
            {
                return config;
            }

            throw new InvalidOperationException(
                $"Expected configuration object in \"Configuration\" property of type {nameof(IConfiguration)} on {nameof(ISiloBuilder)}.");
        }

        /// <summary>
        /// 获得配置值。
        /// </summary>
        /// <param name="hostBuilder">主机建造者。</param>
        /// <param name="key">密钥。</param>
        /// <returns>配置值。</returns>
        public static string? GetConfigurationValue(this IHostBuilder hostBuilder, string key)
        {
            return hostBuilder.GetConfiguration()[key];
        }

        /// <summary>
        /// 获得测试组选项。
        /// </summary>
        /// <param name="hostBuilder">主机建造者。</param>
        /// <returns>测试组群选项。</returns>
        public static TestClusterOptions GetTestClusterOptions(this IHostBuilder hostBuilder)
        {
            return hostBuilder.GetConfiguration().GetTestClusterOptions();
        }

        /// <summary>
        /// 获得测试组选项。
        /// </summary>
        /// <param name="config">配置。</param>
        /// <returns>测试组群选项。</returns>
        public static TestClusterOptions GetTestClusterOptions(this IConfiguration config)
        {
            var result = new TestClusterOptions();
            config.Bind(result);
            return result;
        }
    }
}
