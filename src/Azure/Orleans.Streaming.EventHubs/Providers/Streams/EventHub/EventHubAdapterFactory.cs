using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Statistics;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 队列适配器工厂，使 PersistentStreamProvider 能够使用 Event Hub 作为后端持久化事件队列。
    /// </summary>
    public class EventHubAdapterFactory : IQueueAdapterFactory, IQueueAdapter, IQueueAdapterCache
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly IEnvironmentStatisticsProvider environmentStatisticsProvider;
        private readonly OrleansInstruments orleansInstruments;

        /// <summary>
        /// 数据适配器。
        /// </summary>
        protected readonly IEventHubDataAdapter dataAdapter;

        /// <summary>
        /// Orleans 日志记录。
        /// </summary>
        protected ILogger logger = null!;

        /// <summary>
        /// 框架服务提供程序。
        /// </summary>
        protected readonly IServiceProvider serviceProvider;

        /// <summary>
        /// 流提供程序设置。
        /// </summary>
        private readonly EventHubOptions ehOptions;
        private readonly EventHubStreamCachePressureOptions cacheOptions;
        private readonly EventHubReceiverOptions receiverOptions;
        private readonly StreamStatisticOptions statisticOptions;
        private readonly StreamCacheEvictionOptions cacheEvictionOptions;
        private HashRingBasedPartitionedStreamQueueMapper streamQueueMapper = null!;
        private string[] partitionIds = null!;
        private ConcurrentDictionary<QueueId, EventHubAdapterReceiver> receivers = null!;
        private EventHubProducerClient client = null!;

        /// <summary>
        /// 适配器名称，主要用于日志记录。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 判断此流适配器是否支持回溯，即支持从过去的某个位置开始订阅。
        /// </summary>
        /// <returns>此流适配器支持回溯时为 true，否则为 false。</returns>
        public bool IsRewindable => true;

        /// <summary>
        /// 此队列适配器的方向：Read、Write 或 ReadWrite。
        /// </summary>
        /// <returns>此适配器提供数据的方向。</returns>
        public StreamProviderDirection Direction { get; protected set; } = StreamProviderDirection.ReadWrite;

        /// <summary>
        /// 为 Event Hub 分区创建消息缓存。
        /// </summary>
        protected Func<string, IStreamQueueCheckpointer<string>, ILoggerFactory, IEventHubQueueCache> CacheFactory { get; set; } = null!;

        /// <summary>
        /// 创建分区检查点管理器。
        /// </summary>
        private IStreamQueueCheckpointerFactory checkpointerFactory = null!;

        /// <summary>
        /// 为分区创建故障处理程序。
        /// </summary>
        protected Func<string, Task<IStreamFailureHandler>> StreamFailureHandlerFactory { get; set; } = null!;

        /// <summary>
        /// 创建队列映射器，将 Event Hub 分区映射到队列。
        /// </summary>
        protected Func<string[], HashRingBasedPartitionedStreamQueueMapper> QueueMapperFactory { get; set; } = null!;

        /// <summary>
        /// 创建接收器监视器以报告性能指标。
        /// 工厂函数应返回 IEventHubReceiverMonitor。
        /// </summary>
        protected Func<EventHubReceiverMonitorDimensions, ILoggerFactory, IQueueAdapterReceiverMonitor> ReceiverMonitorFactory { get; set; } = null!;

        // 用于测试，在 EventHubGeneratorStreamProvider 中使用。
        /// <summary>
        /// 用于创建 IEventHubReceiver 的工厂。
        /// </summary>
        protected Func<EventHubPartitionSettings, string, ILogger, IEventHubReceiver> EventHubReceiverFactory = null!;
        internal ConcurrentDictionary<QueueId, EventHubAdapterReceiver> EventHubReceivers => receivers;
        internal HashRingBasedPartitionedStreamQueueMapper EventHubQueueMapper => streamQueueMapper;

        /// <summary>
        /// 初始化 <see cref="EventHubAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="ehOptions">Event Hub 连接选项。</param>
        /// <param name="receiverOptions">Event Hub 接收器选项。</param>
        /// <param name="cacheOptions">Event Hub 缓存压力选项。</param>
        /// <param name="cacheEvictionOptions">流缓存淘汰选项。</param>
        /// <param name="statisticOptions">流统计信息选项。</param>
        /// <param name="dataAdapter">用于在 Event Hubs 数据和 Orleans 流数据之间转换的适配器。</param>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        /// <param name="environmentStatisticsProvider">环境统计信息提供程序。</param>
        public EventHubAdapterFactory(
            string name,
            EventHubOptions ehOptions,
            EventHubReceiverOptions receiverOptions,
            EventHubStreamCachePressureOptions cacheOptions,
            StreamCacheEvictionOptions cacheEvictionOptions,
            StreamStatisticOptions statisticOptions,
            IEventHubDataAdapter dataAdapter,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory,
            IEnvironmentStatisticsProvider environmentStatisticsProvider)
        {
            this.Name = name;
            this.cacheEvictionOptions = cacheEvictionOptions ?? throw new ArgumentNullException(nameof(cacheEvictionOptions));
            this.statisticOptions = statisticOptions ?? throw new ArgumentNullException(nameof(statisticOptions));
            this.ehOptions = ehOptions ?? throw new ArgumentNullException(nameof(ehOptions));
            this.cacheOptions = cacheOptions ?? throw new ArgumentNullException(nameof(cacheOptions));
            this.dataAdapter = dataAdapter ?? throw new ArgumentNullException(nameof(dataAdapter));
            this.receiverOptions = receiverOptions ?? throw new ArgumentNullException(nameof(receiverOptions));
            this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.environmentStatisticsProvider = environmentStatisticsProvider;
            this.orleansInstruments = serviceProvider.GetRequiredService<OrleansInstruments>();
        }

        /// <summary>
        /// 初始化适配器工厂及其 Event Hub 客户端、缓存、队列映射和监控组件。
        /// </summary>
        public virtual void Init()
        {
            this.receivers = new ConcurrentDictionary<QueueId, EventHubAdapterReceiver>();

            InitEventHubClient();

            if (this.CacheFactory == null)
            {
                this.CacheFactory = CreateCacheFactory(this.cacheOptions).CreateCache;
            }

            if (this.StreamFailureHandlerFactory == null)
            {
                // TODO：添加队列专用的默认故障处理程序，并提供合理的错误报告。
                this.StreamFailureHandlerFactory = partition => Task.FromResult<IStreamFailureHandler>(new NoOpStreamDeliveryFailureHandler());
            }

            if (this.QueueMapperFactory == null)
            {
                this.QueueMapperFactory = partitions => new(partitions, this.Name);
            }

            if (this.ReceiverMonitorFactory == null)
            {
                this.ReceiverMonitorFactory = (dimensions, logger) => new DefaultEventHubReceiverMonitor(dimensions, this.orleansInstruments);
            }

            this.logger = this.loggerFactory.CreateLogger($"{this.GetType().FullName}.{this.ehOptions.EventHubName}");
        }

        // 仅 Silo 端需要检查点管理器，因此将其初始化逻辑推迟到实际使用时执行。
        [MemberNotNull(nameof(checkpointerFactory))]
        private void InitCheckpointerFactory()
        {
            this.checkpointerFactory = this.serviceProvider.GetRequiredKeyedService<IStreamQueueCheckpointerFactory>(this.Name);
        }
        /// <summary>
        /// 创建队列适配器，并初始化其分区映射。
        /// </summary>
        /// <returns>队列适配器。</returns>
        public async Task<IQueueAdapter> CreateAdapter()
        {
            if (this.streamQueueMapper == null)
            {
                this.partitionIds = await GetPartitionIdsAsync();
                this.streamQueueMapper = this.QueueMapperFactory(this.partitionIds);
            }
            return this;
        }

        /// <summary>
        /// 获取此实例实现的队列缓存工厂。
        /// </summary>
        /// <returns>队列缓存工厂。</returns>
        public IQueueAdapterCache GetQueueAdapterCache()
        {
            return this;
        }

        /// <summary>
        /// 获取 Orleans 流队列与 Event Hub 分区之间的映射器。
        /// </summary>
        /// <returns>流队列映射器。</returns>
        public IStreamQueueMapper GetStreamQueueMapper()
        {
            // TODO：必须先调用 CreateAdapter。需要确定如何安全地强制此约束。
            return this.streamQueueMapper;
        }

        /// <summary>
        /// 获取队列的投递失败处理程序。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>完成时返回投递失败处理程序的任务。</returns>
        public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
        {
            return this.StreamFailureHandlerFactory(this.streamQueueMapper.QueueToPartition(queueId));
        }

        /// <summary>
        /// 将一组事件作为与所提供 streamId 关联的单个批次写入队列。
        /// </summary>
        /// <typeparam name="T">事件类型。</typeparam>
        /// <param name="streamId">目标流。</param>
        /// <param name="events">要入队的事件。</param>
        /// <param name="token">流序列令牌；对于 Event Hubs 流，它必须为 <see langword="null"/>。</param>
        /// <param name="requestContext">随事件传播的请求上下文。</param>
        /// <returns>表示入队操作的任务。</returns>
        public virtual Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token,
            Dictionary<string, object>? requestContext)
        {
            EventData eventData = this.dataAdapter.ToQueueMessage(streamId, events, token, requestContext);
            string partitionKey = this.dataAdapter.GetPartitionKey(streamId);
            return this.client.SendAsync(new[] { eventData }, new SendEventOptions { PartitionKey = partitionKey });
        }

        /// <summary>
        /// 为指定队列创建队列接收器。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>队列接收器。</returns>
        public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
        {
            return GetOrCreateReceiver(queueId);
        }

        /// <summary>
        /// 为指定队列创建缓存。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>队列缓存。</returns>
        public IQueueCache CreateQueueCache(QueueId queueId)
        {
            return GetOrCreateReceiver(queueId);
        }

        private EventHubAdapterReceiver GetOrCreateReceiver(QueueId queueId)
        {
            return this.receivers.GetOrAdd(queueId, (q, instance) => instance.MakeReceiver(q), this);
        }

        /// <summary>
        /// 初始化 Event Hub 生产者客户端。
        /// </summary>
        protected virtual void InitEventHubClient()
        {
            var connectionOptions = ehOptions.ConnectionOptions;
            var connection = ehOptions.CreateConnection(connectionOptions);
            this.client = new EventHubProducerClient(connection, new EventHubProducerClientOptions { ConnectionOptions = connectionOptions });
        }

        /// <summary>
        /// 创建 IEventHubQueueCacheFactory。默认创建 EventHubQueueCacheFactory。
        /// 用户可以重写此函数以返回自己的 IEventHubQueueCacheFactory 实现，
        /// 并根据需要进行其他 IEventHubQueueCacheFactory 自定义配置。
        /// </summary>
        /// <param name="eventHubCacheOptions">缓存压力选项。</param>
        /// <returns>Event Hub 队列缓存工厂。</returns>
        protected virtual IEventHubQueueCacheFactory CreateCacheFactory(EventHubStreamCachePressureOptions eventHubCacheOptions)
        {
            var eventHubPath = this.ehOptions.EventHubName;
            var sharedDimensions = new EventHubMonitorAggregationDimensions(eventHubPath);
            return new EventHubQueueCacheFactory(eventHubCacheOptions, cacheEvictionOptions, statisticOptions, this.dataAdapter, sharedDimensions, this.orleansInstruments);
        }

        private EventHubAdapterReceiver MakeReceiver(QueueId queueId)
        {
            var config = new EventHubPartitionSettings
            {
                Hub = ehOptions,
                Partition = this.streamQueueMapper.QueueToPartition(queueId),
                ReceiverOptions = this.receiverOptions
            };

            var receiverMonitorDimensions = new EventHubReceiverMonitorDimensions
            {
                EventHubPartition = config.Partition,
                EventHubPath = config.Hub.EventHubName,
            };
            if (this.checkpointerFactory == null)
                InitCheckpointerFactory();
            return new EventHubAdapterReceiver(
                config,
                this.CacheFactory,
                (partition, cancellationToken) => this.checkpointerFactory.Create(partition, cancellationToken),
                this.loggerFactory,
                this.ReceiverMonitorFactory(receiverMonitorDimensions, this.loggerFactory),
                this.serviceProvider.GetRequiredService<IOptions<LoadSheddingOptions>>().Value,
                this.environmentStatisticsProvider,
                this.EventHubReceiverFactory);
        }

        /// <summary>
        /// 从 Event Hubs 获取分区标识符。
        /// </summary>
        /// <returns>完成时返回分区标识符的任务。</returns>
        protected virtual async Task<string[]> GetPartitionIdsAsync()
        {
            return await client.GetPartitionIdsAsync();
        }

        /// <summary>
        /// 创建并初始化 Event Hubs 适配器工厂。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <returns>已初始化的适配器工厂。</returns>
        public static EventHubAdapterFactory Create(IServiceProvider services, string name)
        {
            var ehOptions = services.GetOptionsByName<EventHubOptions>(name);
            var receiverOptions = services.GetOptionsByName<EventHubReceiverOptions>(name);
            var cacheOptions = services.GetOptionsByName<EventHubStreamCachePressureOptions>(name);
            var statisticOptions = services.GetOptionsByName<StreamStatisticOptions>(name);
            var evictionOptions = services.GetOptionsByName<StreamCacheEvictionOptions>(name);
            IEventHubDataAdapter dataAdapter = services.GetKeyedService<IEventHubDataAdapter>(name)
                ?? services.GetService<IEventHubDataAdapter>()
                ?? ActivatorUtilities.CreateInstance<EventHubDataAdapter>(services);
            var factory = ActivatorUtilities.CreateInstance<EventHubAdapterFactory>(services, name, ehOptions, receiverOptions, cacheOptions, evictionOptions, statisticOptions, dataAdapter);
            factory.Init();
            return factory;
        }
    }
}
