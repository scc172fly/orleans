using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Configuration;

namespace Orleans.Streaming.NATS.Hosting;

/// <summary>
/// 在 Silo 上配置 NATS JetStream 提供程序。
/// </summary>
public class SiloNatsStreamConfigurator : SiloPersistentStreamConfigurator
{
    /// <summary>
    /// 初始化 <see cref="SiloNatsStreamConfigurator"/> 类的新实例。
    /// </summary>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configureServicesDelegate">用于配置服务的委托。</param>
    public SiloNatsStreamConfigurator(string name, Action<Action<IServiceCollection>> configureServicesDelegate)
        : base(name, configureServicesDelegate, NatsAdapterFactory.Create)
    {
        this.ConfigureDelegate(services =>
        {
            services
                .ConfigureNamedOptionForLogging<NatsOptions>(name)
                .ConfigureNamedOptionForLogging<SimpleQueueCacheOptions>(name)
                .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name)
                .AddTransient<IConfigurationValidator>(sp => new NatsStreamOptionsValidator(sp.GetRequiredService<IOptionsMonitor<NatsOptions>>().Get(name), name));
        });
    }

    /// <summary>
    /// 配置 NATS JetStream 提供程序选项。
    /// </summary>
    /// <param name="configureOptions">用于配置命名提供程序选项的委托。</param>
    /// <returns>此配置器。</returns>
    public SiloNatsStreamConfigurator ConfigureNats(Action<OptionsBuilder<NatsOptions>> configureOptions)
    {
        this.Configure(configureOptions);
        return this;
    }

    /// <summary>
    /// 配置接收器缓存大小。
    /// </summary>
    /// <param name="cacheSize">缓存大小。</param>
    /// <returns>此配置器。</returns>
    public SiloNatsStreamConfigurator ConfigureCache(int cacheSize = SimpleQueueCacheOptions.DEFAULT_CACHE_SIZE)
    {
        this.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(options => options.CacheSize = cacheSize));
        return this;
    }

    /// <summary>
    /// 配置用于划分流的 Orleans 队列数量。
    /// </summary>
    /// <param name="numOfparitions">流队列数量。</param>
    /// <returns>此配置器。</returns>
    public SiloNatsStreamConfigurator ConfigurePartitioning(
        int numOfparitions = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
    {
        this.Configure<HashRingStreamQueueMapperOptions>(ob =>
            ob.Configure(options => options.TotalQueueCount = numOfparitions));
        return this;
    }
}

/// <summary>
/// 在客户端上配置 NATS JetStream 提供程序。
/// </summary>
public class ClusterClientNatsStreamConfigurator : ClusterClientPersistentStreamConfigurator
{
    /// <summary>
    /// 初始化 <see cref="ClusterClientNatsStreamConfigurator"/> 类的新实例。
    /// </summary>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="builder">客户端构建器。</param>
    public ClusterClientNatsStreamConfigurator(string name, IClientBuilder builder)
        : base(name, builder, NatsAdapterFactory.Create)
    {
        builder
            .ConfigureServices(services =>
            {
                services
                    .ConfigureNamedOptionForLogging<NatsOptions>(name)
                    .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name)
                    .AddTransient<IConfigurationValidator>(sp => new NatsStreamOptionsValidator(sp.GetRequiredService<IOptionsMonitor<NatsOptions>>().Get(name), name));
            });
    }

    /// <summary>
    /// 配置 NATS JetStream 提供程序选项。
    /// </summary>
    /// <param name="configureOptions">用于配置命名提供程序选项的委托。</param>
    /// <returns>此配置器。</returns>
    public ClusterClientNatsStreamConfigurator ConfigureNats(Action<OptionsBuilder<NatsOptions>> configureOptions)
    {
        this.Configure(configureOptions);
        return this;
    }

    /// <summary>
    /// 配置用于划分流的 Orleans 队列数量。
    /// </summary>
    /// <param name="numOfparitions">流队列数量。</param>
    /// <returns>此配置器。</returns>
    public ClusterClientNatsStreamConfigurator ConfigurePartitioning(
        int numOfparitions = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
    {
        this.Configure<HashRingStreamQueueMapperOptions>(ob =>
            ob.Configure(options => options.TotalQueueCount = numOfparitions));
        return this;
    }
}
