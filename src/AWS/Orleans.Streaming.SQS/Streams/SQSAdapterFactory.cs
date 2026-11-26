using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Configuration.Overrides;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;
using Orleans.Streaming.SQS.Streams;
using Orleans.Streams;

namespace OrleansAWSUtils.Streams
{
    /// <summary>亚马逊SQS流供应商的工厂类。</summary>
    public class SQSAdapterFactory : IQueueAdapterFactory
    {
        private readonly string providerName;
        private readonly SqsOptions sqsOptions;
        private readonly ClusterOptions clusterOptions;
        private readonly ISQSDataAdapter dataAdapter;
        private readonly ILoggerFactory loggerFactory;
        private readonly HashRingBasedStreamQueueMapper streamQueueMapper;
        private readonly IQueueAdapterCache adapterCache;

        /// <summary>
        /// 应用级别故障处理器控制
        /// </summary>
        protected Func<QueueId, Task<IStreamFailureHandler>> StreamFailureHandlerFactory { private get; set; } = null!;

        /// <summary>
        /// 初始化 <see cref="SQSAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="sqsOptions">SQS 选项。</param>
        /// <param name="queueMapperOptions">串流到排队映射选项。</param>
        /// <param name="cacheOptions">接收器缓存选项。</param>
        /// <param name="clusterOptions">组合组合选项。</param>
        /// <param name="dataAdapter">用于在 Orleans 流体批量和 SQS 消息 。</param>
        /// <param name="loggerFactory">伐木厂</param>
        public SQSAdapterFactory(
            string name,
            SqsOptions sqsOptions,
            HashRingStreamQueueMapperOptions queueMapperOptions,
            SimpleQueueCacheOptions cacheOptions,
            IOptions<ClusterOptions> clusterOptions,
            ISQSDataAdapter dataAdapter,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(sqsOptions);
            ArgumentNullException.ThrowIfNull(queueMapperOptions);
            ArgumentNullException.ThrowIfNull(cacheOptions);
            ArgumentNullException.ThrowIfNull(clusterOptions);
            ArgumentNullException.ThrowIfNull(dataAdapter);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            this.providerName = name;
            this.sqsOptions = sqsOptions;
            this.clusterOptions = clusterOptions.Value;
            this.dataAdapter = dataAdapter;
            this.loggerFactory = loggerFactory;
            streamQueueMapper = new HashRingBasedStreamQueueMapper(queueMapperOptions, this.providerName);

            adapterCache = new SimpleQueueAdapterCache(cacheOptions, this.providerName, this.loggerFactory);
        }

        /// <summary>
        /// 使用默认的 SQS 数据适配器初始化 <see cref="SQSAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="sqsOptions">SQS 选项。</param>
        /// <param name="queueMapperOptions">串流到排队映射选项。</param>
        /// <param name="cacheOptions">接收器缓存选项。</param>
        /// <param name="clusterOptions">组合组合选项。</param>
        /// <param name="serializer">默认 SQS 数据适配器使用的序列器。</param>
        /// <param name="loggerFactory">伐木厂</param>
        public SQSAdapterFactory(
            string name,
            SqsOptions sqsOptions,
            HashRingStreamQueueMapperOptions queueMapperOptions,
            SimpleQueueCacheOptions cacheOptions,
            IOptions<ClusterOptions> clusterOptions,
            Serializer serializer,
            ILoggerFactory loggerFactory)
            : this(
                name,
                sqsOptions,
                queueMapperOptions,
                cacheOptions,
                clusterOptions,
                new SQSDataAdapter(serializer),
                loggerFactory)
        {
        }

        /// <summary> 工厂开工了</summary>
        public virtual void Init()
        {
            if (StreamFailureHandlerFactory == null)
            {
                StreamFailureHandlerFactory =
                    qid => Task.FromResult<IStreamFailureHandler>(new NoOpStreamDeliveryFailureHandler());
            }
        }

        /// <summary>创建 亚马逊 SQS 队列适配器。</summary>
        /// <returns>包含队列适配器的任务。</returns>
        public virtual Task<IQueueAdapter> CreateAdapter()
        {
            var adapter = new SQSAdapter(this.dataAdapter, this.streamQueueMapper, this.loggerFactory, this.sqsOptions, this.clusterOptions.ServiceId, this.providerName);
            return Task.FromResult<IQueueAdapter>(adapter);
        }

        /// <summary>创建适配器缓存。</summary>
        /// <returns>适配器缓存。</returns>
        public virtual IQueueAdapterCache GetQueueAdapterCache()
        {
            return adapterCache;
        }

        /// <summary>创建工厂流队列映像仪。</summary>
        /// <returns>流队列映像仪。</returns>
        public IStreamQueueMapper GetStreamQueueMapper()
        {
            return streamQueueMapper;
        }

        /// <summary>
        /// 为指定的队列创建发送失败处理器 。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>含有交付失败处理器的任务。</returns>
        public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
        {
            return StreamFailureHandlerFactory(queueId);
        }

        /// <summary>
        /// 使用为指定的流提供商注册的服务创建并启动一个 SQS 适配工厂。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="name">流体提供者的名称。</param>
        /// <returns>初始化适配器厂。</returns>
        public static SQSAdapterFactory Create(IServiceProvider services, string name)
        {
            var sqsOptions = services.GetOptionsByName<SqsOptions>(name);
            var cacheOptions = services.GetOptionsByName<SimpleQueueCacheOptions>(name);
            var queueMapperOptions = services.GetOptionsByName<HashRingStreamQueueMapperOptions>(name);
            IOptions<ClusterOptions> clusterOptions = services.GetProviderClusterOptions(name);
            var dataAdapter = services.GetKeyedService<ISQSDataAdapter>(name)
                               ?? services.GetService<ISQSDataAdapter>()
                               ?? ActivatorUtilities.CreateInstance<SQSDataAdapter>(services);
            var factory = ActivatorUtilities.CreateInstance<SQSAdapterFactory>(services, name, sqsOptions, queueMapperOptions, cacheOptions, clusterOptions, dataAdapter);
            factory.Init();
            return factory;
        }
    }
}
