using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 测试集群的扩展方法。
    /// </summary>
    public static class TestClusterExtensions
    {
        /// <summary>
        /// 从指定宿主构建器获取配置。
        /// </summary>        
        /// <param name="builder">
        /// 构建器。
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
        /// 获取配置值。
        /// </summary>
        /// <param name="hostBuilder">宿主构建器。</param>
        /// <param name="key">键。</param>
        /// <returns>配置值。</returns>
        public static string? GetConfigurationValue(this IHostBuilder hostBuilder, string key)
        {
            return hostBuilder.GetConfiguration()[key];
        }

        /// <summary>
        /// 获取测试集群选项。
        /// </summary>
        /// <param name="hostBuilder">宿主构建器。</param>
        /// <returns>测试集群选项。</returns>
        public static TestClusterOptions GetTestClusterOptions(this IHostBuilder hostBuilder)
        {
            return hostBuilder.GetConfiguration().GetTestClusterOptions();
        }

        /// <summary>
        /// 获取测试集群选项。
        /// </summary>
        /// <param name="config">配置。</param>
        /// <returns>测试集群选项。</returns>
        public static TestClusterOptions GetTestClusterOptions(this IConfiguration config)
        {
            var result = new TestClusterOptions();
            config.Bind(result);
            return result;
        }
    }
}
