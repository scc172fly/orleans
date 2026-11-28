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
    /// <summary>Amazon SQS 流提供程序的工厂类。</summary>
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
        /// 应用程序级故障处理程序的替代实现。
        /// </summary>
        protected Func<QueueId, Task<IStreamFailureHandler>> StreamFailureHandlerFactory { private get; set; } = null!;

        /// <summary>
        /// 初始化 <see cref="SQSAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="sqsOptions">SQS 选项。</param>
        /// <param name="queueMapperOptions">流到队列的映射选项。</param>
        /// <param name="cacheOptions">接收器缓存选项。</param>
        /// <param name="clusterOptions">集群选项。</param>
        /// <param name="dataAdapter">用于在 Orleans 流批次和 SQS 消息之间转换的适配器。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
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
        /// 使用默认 SQS 数据适配器初始化 <see cref="SQSAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="sqsOptions">SQS 选项。</param>
        /// <param name="queueMapperOptions">流到队列的映射选项。</param>
        /// <param name="cacheOptions">接收器缓存选项。</param>
        /// <param name="clusterOptions">集群选项。</param>
        /// <param name="serializer">默认 SQS 数据适配器使用的序列化器。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
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

        /// <summary>初始化工厂。</summary>
        public virtual void Init()
        {
            if (StreamFailureHandlerFactory == null)
            {
                StreamFailureHandlerFactory =
                    qid => Task.FromResult<IStreamFailureHandler>(new NoOpStreamDeliveryFailureHandler());
            }
        }

        /// <summary>创建 Amazon SQS 队列适配器。</summary>
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

        /// <summary>创建此工厂的流队列映射器。</summary>
        /// <returns>流队列映射器。</returns>
        public IStreamQueueMapper GetStreamQueueMapper()
        {
            return streamQueueMapper;
        }

        /// <summary>
        /// 为指定队列创建投递失败处理程序。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>包含投递失败处理程序的任务。</returns>
        public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
        {
            return StreamFailureHandlerFactory(queueId);
        }

        /// <summary>
        /// 使用为具名流提供程序注册的服务创建并初始化 SQS 适配器工厂。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <returns>已初始化的适配器工厂。</returns>
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
