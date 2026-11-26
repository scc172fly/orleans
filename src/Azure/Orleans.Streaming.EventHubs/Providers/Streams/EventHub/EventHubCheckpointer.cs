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
    /// 创建由 Azure 表存储支持的事件枢纽分区检查站。
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
        /// <param name="providerName">流体提供者名称。</param>
        /// <param name="options">下 Azure 表格存储检查站选项。</param>
        /// <param name="clusterOptions">组合组合选项。</param>
        /// <param name="loggerFactory">伐木厂</param>
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
        /// 创建事件枢纽分区检查站工厂。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="providerName">流体提供者名称。</param>
        /// <returns>检查站工厂</returns>
        public static IStreamQueueCheckpointerFactory CreateFactory(IServiceProvider services, string providerName)
        {
            var options = services.GetOptionsByName<AzureTableStreamCheckpointerOptions>(providerName);
            IOptions<ClusterOptions> clusterOptions = services.GetProviderClusterOptions(providerName);
            return ActivatorUtilities.CreateInstance<EventHubCheckpointerFactory>(services, providerName, options, clusterOptions);
        }
    }

    /// <summary>
    /// 这个班级商店 EventHub 分隔区间分隔区检查点信息 Azure 桌子储存。
    /// </summary>
    public class EventHubCheckpointer : IStreamQueueCheckpointer<string>
    {
        private readonly IStreamQueueCheckpointer<string> _inner;

        /// <inheritdoc />
        public bool CheckpointExists => _inner.CheckpointExists;

        /// <summary>
        /// 创建并初始化事件枢纽分区检查器。
        /// </summary>
        /// <param name="options">下 Azure 表格存储检查站选项。</param>
        /// <param name="streamProviderName">流体提供者名称。</param>
        /// <param name="partition">事件枢纽分区标识符。</param>
        /// <param name="serviceId">下 Orleans 服务标识符。</param>
        /// <param name="loggerFactory">伐木厂</param>
        /// <returns>一项任务,即解决初始化检查站的任务。</returns>
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
        /// 装满了检查站
        /// </summary>
        /// <returns>任务由检查站承担</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        public Task<string> Load() => Load(CancellationToken.None);

        /// <summary>
        /// 装满了一个检查站
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>检查站。</returns>
        public async Task<string> Load(CancellationToken cancellationToken)
        {
            var checkpoint = await _inner.Load(cancellationToken);
            return CheckpointExists ? checkpoint : EventHubConstants.StartOfStream;
        }

        /// <summary>
        /// 更新检查站,这是最好的努力,并不总是更新检查站。
        /// 最新的抵消总是在记忆中跟踪,以便 <see cref="FlushAsync(CancellationToken)"/> 能够持续关闭。
        /// </summary>
        /// <param name="offset">检查站</param>
        /// <param name="utcNow">当前UTC 时间。</param>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        public void Update(string offset, DateTime utcNow)
            => Update(offset, utcNow, CancellationToken.None);

        /// <summary>
        /// 更新检查站
        /// </summary>
        /// <param name="offset">检查站</param>
        /// <param name="utcNow">当前UTC 时间。</param>
        /// <param name="cancellationToken">取消牌。</param>
        public void Update(string offset, DateTime utcNow, CancellationToken cancellationToken)
            => _inner.Update(offset, utcNow, cancellationToken);

        /// <summary>
        /// 任何待设的持久性储存检查站都可穿透。
        /// 等待任何在建的储蓄,如果超过上次节省的价值,则维持最新的抵消。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>代表冲洗操作的任务。</returns>
        public Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    }
}
