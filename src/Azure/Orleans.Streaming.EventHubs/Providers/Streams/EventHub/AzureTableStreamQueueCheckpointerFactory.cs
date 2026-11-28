using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Configuration.Overrides;

namespace Orleans.Streams
{
    /// <summary>
    /// 创建 Azure Table 流队列检查点管理器。
    /// </summary>
    public class AzureTableStreamQueueCheckpointerFactory : IStreamQueueCheckpointerFactory
    {
        private readonly string _providerName;
        private readonly AzureTableStreamCheckpointerOptions _options;
        private readonly ClusterOptions _clusterOptions;
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// 初始化新实例。
        /// </summary>
        public AzureTableStreamQueueCheckpointerFactory(
            string providerName,
            AzureTableStreamCheckpointerOptions options,
            IOptions<ClusterOptions> clusterOptions,
            ILoggerFactory loggerFactory)
        {
            _providerName = providerName;
            _options = options;
            _clusterOptions = clusterOptions.Value;
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// 从服务提供程序创建工厂。
        /// </summary>
        public static IStreamQueueCheckpointerFactory CreateFactory(IServiceProvider services, string providerName)
        {
            var options = services.GetOptionsByName<AzureTableStreamCheckpointerOptions>(providerName);
            var clusterOptions = services.GetProviderClusterOptions(providerName);
            return ActivatorUtilities.CreateInstance<AzureTableStreamQueueCheckpointerFactory>(
                services,
                providerName,
                options,
                clusterOptions);
        }

        /// <inheritdoc />
        public Task<IStreamQueueCheckpointer<string>> Create(string partition)
        {
            return AzureTableStreamQueueCheckpointer.Create(
                _options,
                _providerName,
                partition,
                _clusterOptions.ServiceId.ToString(),
                _loggerFactory);
        }
    }
}
