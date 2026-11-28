using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Configuration.Overrides;

namespace Orleans.Streams
{
    /// <summary>
    /// 创建基于 Grain 的流队列检查点管理器。
    /// </summary>
    public class GrainStreamQueueCheckpointerFactory : IStreamQueueCheckpointerFactory
    {
        private readonly string _providerName;
        private readonly IClusterClient _clusterClient;
        private readonly ClusterOptions _clusterOptions;
        private readonly GrainStreamQueueCheckpointerOptions _checkpointerOptions;

        /// <summary>
        /// 使用默认检查点管理器选项初始化新实例。
        /// </summary>
        public GrainStreamQueueCheckpointerFactory(string providerName, IOptions<ClusterOptions> clusterOptions, IClusterClient clusterClient)
            : this(providerName, clusterOptions, clusterClient, new GrainStreamQueueCheckpointerOptions())
        {
        }

        /// <summary>
        /// 初始化新实例。
        /// </summary>
        public GrainStreamQueueCheckpointerFactory(
            string providerName,
            IOptions<ClusterOptions> clusterOptions,
            IClusterClient clusterClient,
            GrainStreamQueueCheckpointerOptions checkpointerOptions)
        {
            _providerName = providerName;
            _clusterClient = clusterClient;
            _clusterOptions = clusterOptions.Value;
            _checkpointerOptions = checkpointerOptions;
        }

        /// <summary>
        /// 从服务提供程序创建工厂。
        /// </summary>
        public static IStreamQueueCheckpointerFactory CreateFactory(IServiceProvider services, string providerName)
        {
            var options = services.GetOptionsByName<GrainStreamQueueCheckpointerOptions>(providerName);
            var clusterOptions = services.GetProviderClusterOptions(providerName);
            return ActivatorUtilities.CreateInstance<GrainStreamQueueCheckpointerFactory>(
                services,
                providerName,
                clusterOptions,
                options);
        }

        /// <inheritdoc />
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        public Task<IStreamQueueCheckpointer<string>> Create(string partition)
            => Create(partition, CancellationToken.None);

        /// <inheritdoc />
        public Task<IStreamQueueCheckpointer<string>> Create(string partition, CancellationToken cancellationToken)
        {
            return GrainStreamQueueCheckpointer.Create(
                _providerName,
                partition,
                _clusterOptions.ServiceId.ToString(),
                _clusterClient,
                _checkpointerOptions,
                cancellationToken);
        }
    }
}
