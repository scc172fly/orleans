using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Streams;
using Orleans.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration.Overrides;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 创建以 Azure Table Storage 为后端的 Event Hub 分区检查点管理器。
    /// </summary>
    public class EventHubCheckpointerFactory : IStreamQueueCheckpointerFactory
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly string providerName;
        private readonly AzureTableStreamCheckpointerOptions options;
        private readonly ClusterOptions clusterOptions;

        /// <summary>
        /// 初始化 <see cref="EventHubCheckpointerFactory"/> 类的新实例。
        /// </summary>
        /// <param name="providerName">流提供程序名称。</param>
        /// <param name="options">Azure Table Storage 检查点管理器选项。</param>
        /// <param name="clusterOptions">集群选项。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public EventHubCheckpointerFactory(string providerName, AzureTableStreamCheckpointerOptions options, IOptions<ClusterOptions> clusterOptions, ILoggerFactory loggerFactory)
        {
            this.options = options;
            this.clusterOptions = clusterOptions.Value;
            this.loggerFactory = loggerFactory;
            this.providerName = providerName;
        }

        /// <inheritdoc />
        public Task<IStreamQueueCheckpointer<string>> Create(string partition)
        {
            return EventHubCheckpointer.Create(options, providerName, partition, this.clusterOptions.ServiceId.ToString(), loggerFactory);
        }

        /// <summary>
        /// 创建 Event Hub 分区检查点管理器工厂。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="providerName">流提供程序名称。</param>
        /// <returns>检查点管理器工厂。</returns>
        public static IStreamQueueCheckpointerFactory CreateFactory(IServiceProvider services, string providerName)
        {
            var options = services.GetOptionsByName<AzureTableStreamCheckpointerOptions>(providerName);
            IOptions<ClusterOptions> clusterOptions = services.GetProviderClusterOptions(providerName);
            return ActivatorUtilities.CreateInstance<EventHubCheckpointerFactory>(services, providerName, options, clusterOptions);
        }
    }

    /// <summary>
    /// 此类在 Azure Table Storage 中存储 Event Hub 分区检查点信息。
    /// </summary>
    public class EventHubCheckpointer : IStreamQueueCheckpointer<string>
    {
        private readonly IStreamQueueCheckpointer<string> _inner;

        /// <inheritdoc />
        public bool CheckpointExists => _inner.CheckpointExists;

        /// <summary>
        /// 创建并初始化 Event Hub 分区检查点管理器。
        /// </summary>
        /// <param name="options">Azure Table Storage 检查点管理器选项。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="partition">Event Hub 分区标识符。</param>
        /// <param name="serviceId">Orleans 服务标识符。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        /// <returns>完成时返回已初始化检查点管理器的任务。</returns>
        public static async Task<IStreamQueueCheckpointer<string>> Create(AzureTableStreamCheckpointerOptions options, string streamProviderName, string partition, string serviceId, ILoggerFactory loggerFactory)
        {
            var inner = await AzureTableStreamQueueCheckpointer.Create(
                options,
                streamProviderName,
                partition,
                serviceId,
                loggerFactory,
                StreamCheckpointComparers.Numeric,
                StreamQueueCheckpointEntity.EventHubPartitionKeyPrefix);
            return new EventHubCheckpointer(inner);
        }

        private EventHubCheckpointer(IStreamQueueCheckpointer<string> inner)
        {
            _inner = inner;
        }

        /// <summary>
        /// 加载检查点。
        /// </summary>
        /// <returns>完成时返回检查点偏移量的任务。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        public Task<string> Load() => Load(CancellationToken.None);

        /// <summary>
        /// 加载检查点。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>检查点。</returns>
        public async Task<string> Load(CancellationToken cancellationToken)
        {
            var checkpoint = await _inner.Load(cancellationToken);
            return CheckpointExists ? checkpoint : EventHubConstants.StartOfStream;
        }

        /// <summary>
        /// 更新检查点。此操作采用尽力而为的方式，并不总是更新检查点。
        /// 始终在内存中跟踪最新偏移量，以便 <see cref="FlushAsync(CancellationToken)"/> 在关闭时将其持久化。
        /// </summary>
        /// <param name="offset">检查点偏移量。</param>
        /// <param name="utcNow">当前 UTC 时间。</param>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        public void Update(string offset, DateTime utcNow)
            => Update(offset, utcNow, CancellationToken.None);

        /// <summary>
        /// 更新检查点。
        /// </summary>
        /// <param name="offset">检查点偏移量。</param>
        /// <param name="utcNow">当前 UTC 时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public void Update(string offset, DateTime utcNow, CancellationToken cancellationToken)
            => _inner.Update(offset, utcNow, cancellationToken);

        /// <summary>
        /// 将所有待处理的检查点刷新到持久化存储。
        /// 等待正在进行的保存操作；若最新偏移量已超过上次保存值，则将其持久化。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示刷新操作的任务。</returns>
        public Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    }
}
