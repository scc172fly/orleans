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
    /// 将允许 PersistentStreamProvider 将 EventHub 用作后端持久性事件队列的调制器工厂排队。
    /// </summary>
    public class EventHubAdapterFactory : IQueueAdapterFactory, IQueueAdapter, IQueueAdapterCache
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly IEnvironmentStatisticsProvider environmentStatisticsProvider;
        private readonly OrleansInstruments orleansInstruments;

        /// <summary>
        /// 数据适配器
        /// </summary>
        protected readonly IEventHubDataAdapter dataAdapter;

        /// <summary>
        /// Orleans 记录
        /// </summary>
        protected ILogger logger = null!;

        /// <summary>
        /// 框架服务供应商
        /// </summary>
        protected readonly IServiceProvider serviceProvider;

        /// <summary>
        /// 串流提供商设置
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
        /// 适配器的名称。 主要用于记录
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 确定此是否是一个可回放流调整器 - 支持从上一个时间点签名 。
        /// </summary>
        /// <returns>如果这是可倒放流适配器, 则正确。</returns>
        public bool IsRewindable => true;

        /// <summary>
        /// 此队列适配器的方向: 读、 写或 ReadWrite。
        /// </summary>
        /// <returns>该适配器提供数据的方向。</returns>
        public StreamProviderDirection Direction { get; protected set; } = StreamProviderDirection.ReadWrite;

        /// <summary>
        /// 为平线分区创建信件缓存 。
        /// </summary>
        protected Func<string, IStreamQueueCheckpointer<string>, ILoggerFactory, IEventHubQueueCache> CacheFactory { get; set; } = null!;

        /// <summary>
        /// 创建分区检查器。
        /// </summary>
        private IStreamQueueCheckpointerFactory checkpointerFactory = null!;

        /// <summary>
        /// 为分区创建失败处理程序 。
        /// </summary>
        protected Func<string, Task<IStreamFailureHandler>> StreamFailureHandlerFactory { get; set; } = null!;

        /// <summary>
        /// 创建队列映像仪,将 EventHub 分区映射到队列
        /// </summary>
        protected Func<string[], HashRingBasedPartitionedStreamQueueMapper> QueueMapperFactory { get; set; } = null!;

        /// <summary>
        /// 创建接收器监测器,报告业绩衡量标准。
        /// 工厂函数应返回 IEventHubReceiverMonitor。
        /// </summary>
        protected Func<EventHubReceiverMonitorDimensions, ILoggerFactory, IQueueAdapterReceiverMonitor> ReceiverMonitorFactory { get; set; } = null!;

        //EventHubGeneratorStreamProvider中所用用于试验目的
        /// <summary>
        /// 创建 IEventHubReceiver的工厂
        /// </summary>
        protected Func<EventHubPartitionSettings, string, ILogger, IEventHubReceiver> EventHubReceiverFactory = null!;
        internal ConcurrentDictionary<QueueId, EventHubAdapterReceiver> EventHubReceivers => receivers;
        internal HashRingBasedPartitionedStreamQueueMapper EventHubQueueMapper => streamQueueMapper;

        /// <summary>
        /// 初始化 <see cref="EventHubAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="ehOptions">活动枢纽连接选项。</param>
        /// <param name="receiverOptions">事件枢纽接收器选项。</param>
        /// <param name="cacheOptions">事件枢纽缓存压力选项。</param>
        /// <param name="cacheEvictionOptions">溪流缓冲驱逐选项。</param>
        /// <param name="statisticOptions">流统计选项。</param>
        /// <param name="dataAdapter">用于在事件枢纽数据与 Orleans 数据。</param>
        /// <param name="serviceProvider">服务供应商。</param>
        /// <param name="loggerFactory">伐木厂</param>
        /// <param name="environmentStatisticsProvider">环境统计提供者。</param>
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
        /// 初始化了适配器厂及其事件枢纽客户端、缓存、队列绘图和监测组件。
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
                //TODO: 添加一个有合理错误报告功能的队列特定默认故障处理程序 。
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

        //只应在 silo 一侧需要检查站,所以在使用时要移动其内在逻辑
        [MemberNotNull(nameof(checkpointerFactory))]
        private void InitCheckpointerFactory()
        {
            this.checkpointerFactory = this.serviceProvider.GetRequiredKeyedService<IStreamQueueCheckpointerFactory>(this.Name);
        }
        /// <summary>
        /// 创建队列适配器并初始化分区映射。
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
        /// 获得由此实例实施的队列缓存工厂。
        /// </summary>
        /// <returns>排队缓冲厂</returns>
        public IQueueAdapterCache GetQueueAdapterCache()
        {
            return this;
        }

        /// <summary>
        /// 获取 Orleans 串列队和事件枢纽分区之间的映射器。
        /// </summary>
        /// <returns>流队列映像仪。</returns>
        public IStreamQueueMapper GetStreamQueueMapper()
        {
            //托尔: CreateAdapter 一定要先叫来, 想办法安全执行
            return this.streamQueueMapper;
        }

        /// <summary>
        /// 获得一个排队的送货失败处理器。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>一项任务,即解决交付失败处理人的任务。</returns>
        public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
        {
            return this.StreamFailureHandlerFactory(this.streamQueueMapper.QueueToPartition(queueId));
        }

        /// <summary>
        /// 将一组事件作为与所提供 streamId相关的单批内容写入队列。
        /// </summary>
        /// <typeparam name="T">事件类型。</typeparam>
        /// <param name="streamId">目的地流。</param>
        /// <param name="events">用于排队的事件。</param>
        /// <param name="token">流序序列符号,必须是事件枢纽流的 <see langword="null"/> 。</param>
        /// <param name="requestContext">请求与事件一起宣传。</param>
        /// <returns>代表后方命令操作的任务。</returns>
        public virtual Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token,
            Dictionary<string, object>? requestContext)
        {
            EventData eventData = this.dataAdapter.ToQueueMessage(streamId, events, token, requestContext);
            string partitionKey = this.dataAdapter.GetPartitionKey(streamId);
            return this.client.SendAsync(new[] { eventData }, new SendEventOptions { PartitionKey = partitionKey });
        }

        /// <summary>
        /// 为指定的队列创建队列接收器 。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>队列接收器。</returns>
        public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
        {
            return GetOrCreateReceiver(queueId);
        }

        /// <summary>
        /// 为指定的队列创建缓存 。
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
        /// 初始化事件枢纽制作客户端。
        /// </summary>
        protected virtual void InitEventHubClient()
        {
            var connectionOptions = ehOptions.ConnectionOptions;
            var connection = ehOptions.CreateConnection(connectionOptions);
            this.client = new EventHubProducerClient(connection, new EventHubProducerClientOptions { ConnectionOptions = connectionOptions });
        }

        /// <summary>
        /// 创建 IEventHubQueueCacheFactory。它会默认地创建一个 EventHubQueueCacheFactory 。
        /// 用户可以推翻此函数返回 IEventHubQueueCacheFactory的自有执行状态,
        /// 或按 IEventHubQueueCacheFactory 的其他定制。
        /// </summary>
        /// <param name="eventHubCacheOptions">缓存压力选项。</param>
        /// <returns>事件枢纽队列缓存工厂。</returns>
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
        /// 从事件枢纽获取分区标识符 。
        /// </summary>
        /// <returns>解决分区标识符的任务。</returns>
        protected virtual async Task<string[]> GetPartitionIdsAsync()
        {
            return await client.GetPartitionIdsAsync();
        }

        /// <summary>
        /// 创建并启动事件枢纽调整工厂。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <returns>初始化适配器厂。</returns>
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
