using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.AzureQueue
{
    /// <summary>基于 Azure Queue 的流提供程序工厂类。</summary>
    public class AzureQueueAdapterFactory : IQueueAdapterFactory
    {
        private readonly string providerName;
        private readonly AzureQueueOptions options;
        private readonly IQueueDataAdapter<string, IBatchContainer> dataAdapter;
        private readonly ILoggerFactory loggerFactory;
        private readonly HashRingBasedPartitionedStreamQueueMapper streamQueueMapper;
        private readonly SimpleQueueAdapterCache adapterCache;

        /// <summary>
        /// 应用程序级故障处理程序的重写。
        /// </summary>
        protected Func<QueueId, Task<IStreamFailureHandler>> StreamFailureHandlerFactory { private get; set; } = null!;

        /// <summary>
        /// 初始化 <see cref="AzureQueueAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="options">Azure Queue 流提供程序选项。</param>
        /// <param name="cacheOptions">队列缓存选项。</param>
        /// <param name="dataAdapter">用于在流批次与 Azure Queue 消息之间转换的适配器。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public AzureQueueAdapterFactory(
            string name,
            AzureQueueOptions options,
            SimpleQueueCacheOptions cacheOptions,
            IQueueDataAdapter<string, IBatchContainer> dataAdapter,
            ILoggerFactory loggerFactory)
        {
            this.providerName = name;
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.dataAdapter = dataAdapter ?? throw new ArgumentNullException(nameof(dataAdapter));
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.streamQueueMapper = new(options.QueueNames, providerName);
            this.adapterCache = new SimpleQueueAdapterCache(cacheOptions, this.providerName, this.loggerFactory);
        }

        /// <summary>初始化工厂。</summary>
        public virtual void Init()
        {
            this.StreamFailureHandlerFactory = this.StreamFailureHandlerFactory ??
                    ((qid) => Task.FromResult<IStreamFailureHandler>(new NoOpStreamDeliveryFailureHandler()));
        }

        /// <summary>创建基于 Azure Queue 的适配器。</summary>
        public virtual Task<IQueueAdapter> CreateAdapter()
        {
            var adapter = new AzureQueueAdapter(
                this.dataAdapter,
                this.streamQueueMapper,
                this.loggerFactory,
                this.options,
                this.providerName);
            return Task.FromResult<IQueueAdapter>(adapter);
        }

        /// <summary>创建适配器缓存。</summary>
        public virtual IQueueAdapterCache GetQueueAdapterCache()
        {
            return adapterCache;
        }

        /// <summary>创建工厂使用的流队列映射器。</summary>
        public IStreamQueueMapper GetStreamQueueMapper()
        {
            return streamQueueMapper;
        }

        /// <summary>
        /// 为指定队列创建投递失败处理程序。
        /// </summary>
        /// <param name="queueId"></param>
        /// <returns></returns>
        public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
        {
            return StreamFailureHandlerFactory(queueId);
        }

        /// <summary>
        /// 使用命名选项和服务创建并初始化适配器工厂。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <returns>已初始化的适配器工厂。</returns>
        public static AzureQueueAdapterFactory Create(IServiceProvider services, string name)
        {
            var azureQueueOptions = services.GetOptionsByName<AzureQueueOptions>(name);
            var cacheOptions = services.GetOptionsByName<SimpleQueueCacheOptions>(name);
            var dataAdapter = services.GetKeyedService<IQueueDataAdapter<string, IBatchContainer>>(name)
                ?? services.GetService<IQueueDataAdapter<string, IBatchContainer>>();
            var factory = ActivatorUtilities.CreateInstance<AzureQueueAdapterFactory>(services, name, azureQueueOptions, cacheOptions, dataAdapter!);
            factory.Init();
            return factory;
        }
    }
}
