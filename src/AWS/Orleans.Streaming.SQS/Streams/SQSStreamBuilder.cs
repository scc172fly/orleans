using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Streaming.SQS.Streams;
using OrleansAWSUtils.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 在 Orleans Silo 上配置由 Amazon SQS 支持的持久化流提供程序。
    /// </summary>
    public class SiloSqsStreamConfigurator : SiloPersistentStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloSqsStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureServicesDelegate">用于配置 Silo 服务的委托。</param>
        public SiloSqsStreamConfigurator(string name, Action<Action<IServiceCollection>> configureServicesDelegate)
            : base(name, configureServicesDelegate, SQSAdapterFactory.Create)
        {
            this.ConfigureDelegate(services =>
            {
                services.ConfigureNamedOptionForLogging<SqsOptions>(name)
                    .ConfigureNamedOptionForLogging<SimpleQueueCacheOptions>(name)
                    .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name);
            });
        }

        /// <summary>
        /// 配置流提供程序的 SQS 选项。
        /// </summary>
        /// <param name="configureOptions">用于配置具名 <see cref="SqsOptions"/> 的委托。</param>
        /// <returns>流提供程序配置器。</returns>
        public SiloSqsStreamConfigurator ConfigureSqs(Action<OptionsBuilder<SqsOptions>> configureOptions)
        {
            this.Configure(configureOptions);
            return this;
        }

        /// <summary>
        /// 配置每个接收器缓存保留的流批次数量。
        /// </summary>
        /// <param name="cacheSize">每个接收器缓存保留的流批次数量。</param>
        /// <returns>流提供程序配置器。</returns>
        public SiloSqsStreamConfigurator ConfigureCache(int cacheSize = SimpleQueueCacheOptions.DEFAULT_CACHE_SIZE)
        {
            this.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(options => options.CacheSize = cacheSize));
            return this;
        }

        /// <summary>
        /// 配置用于划分流的 SQS 队列数量。
        /// </summary>
        /// <param name="numOfparitions">SQS 队列数量。</param>
        /// <returns>流提供程序配置器。</returns>
        public SiloSqsStreamConfigurator ConfigurePartitioning(int numOfparitions = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
        {
            this.Configure<HashRingStreamQueueMapperOptions>(ob => ob.Configure(options => options.TotalQueueCount = numOfparitions));
            return this;
        }

        /// <summary>
        /// 配置用于在 Orleans 流批次和 SQS 消息之间转换的数据适配器。
        /// </summary>
        /// <param name="factory">
        /// 使用服务提供程序和流提供程序名称调用的工厂，用于创建数据适配器。
        /// </param>
        /// <returns>流提供程序配置器。</returns>
        public SiloSqsStreamConfigurator UseDataAdapter(Func<IServiceProvider, string, ISQSDataAdapter> factory)
        {
            this.ConfigureComponent(factory);
            return this;
        }
    }

    /// <summary>
    /// 在 Orleans 客户端上配置由 Amazon SQS 支持的持久化流提供程序。
    /// </summary>
    public class ClusterClientSqsStreamConfigurator : ClusterClientPersistentStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientSqsStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="builder">客户端构建器。</param>
        public ClusterClientSqsStreamConfigurator(string name, IClientBuilder builder)
            : base(name, builder, SQSAdapterFactory.Create)
        {
            builder
                .ConfigureServices(services =>
                {
                    services.ConfigureNamedOptionForLogging<SqsOptions>(name)
                    .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name);
                });
        }

        /// <summary>
        /// 配置流提供程序的 SQS 选项。
        /// </summary>
        /// <param name="configureOptions">用于配置具名 <see cref="SqsOptions"/> 的委托。</param>
        /// <returns>流提供程序配置器。</returns>
        public ClusterClientSqsStreamConfigurator ConfigureSqs(Action<OptionsBuilder<SqsOptions>> configureOptions)
        {
            this.Configure(configureOptions);
            return this;

        }

        /// <summary>
        /// 配置用于划分流的 SQS 队列数量。
        /// </summary>
        /// <param name="numOfparitions">SQS 队列数量。</param>
        /// <returns>流提供程序配置器。</returns>
        public ClusterClientSqsStreamConfigurator ConfigurePartitioning(int numOfparitions = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
        {
            this.Configure<HashRingStreamQueueMapperOptions>(ob => ob.Configure(options => options.TotalQueueCount = numOfparitions));
            return this;
        }

        /// <summary>
        /// 配置用于在 Orleans 流批次和 SQS 消息之间转换的数据适配器。
        /// </summary>
        /// <param name="factory">
        /// 使用服务提供程序和流提供程序名称调用的工厂，用于创建数据适配器。
        /// </param>
        /// <returns>流提供程序配置器。</returns>
        public ClusterClientSqsStreamConfigurator UseDataAdapter(Func<IServiceProvider, string, ISQSDataAdapter> factory)
        {
            this.ConfigureComponent(factory);
            return this;
        }
    }
}
