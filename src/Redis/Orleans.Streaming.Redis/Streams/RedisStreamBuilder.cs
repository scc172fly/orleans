using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Streaming.Redis;

namespace Orleans.Hosting;

/// <summary>
/// 配置配置 Redis 在一个溪流上 silo.
/// </summary>
public sealed class SiloRedisStreamConfigurator : SiloPersistentStreamConfigurator
{
    /// <summary>
    /// 初始化 <see cref="SiloRedisStreamConfigurator"/> 类的新实例。
    /// </summary>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configureServicesDelegate">代表用于配置服务。</param>
    public SiloRedisStreamConfigurator(string name, Action<Action<IServiceCollection>> configureServicesDelegate)
        : base(name, configureServicesDelegate, RedisStreamAdapterFactory.Create)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configureServicesDelegate);

        ConfigureDelegate(services => services.ConfigureNamedOptionForLogging<RedisStreamingOptions>(name)
                .ConfigureNamedOptionForLogging<RedisStreamReceiverOptions>(name)
                .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name)
                .ConfigureNamedOptionForLogging<SimpleQueueCacheOptions>(name));
    }

    /// <summary>
    /// 配置 Redis 流动选项。
    /// </summary>
    /// <param name="configureOptions">用于配置选项的代表。</param>
    public void ConfigureOptions(Action<RedisStreamingOptions, IServiceProvider> configureOptions) => RedisStreamingOptions.Configure(configureOptions);

    /// <summary>
    /// 获取 <see cref="RedisStreamingOptions"/>的选项构建器。
    /// </summary>
    public OptionsBuilder<RedisStreamingOptions> RedisStreamingOptions => this.GetNamedOptionsBuilder<RedisStreamingOptions>();

    /// <summary>
    /// 配置 Redis 流接收器选项。
    /// </summary>
    /// <param name="configureOptions">代表用来配置命名的接收器选项。</param>
    /// <returns>此配置。</returns>
    public SiloRedisStreamConfigurator ConfigureReceiver(Action<OptionsBuilder<RedisStreamReceiverOptions>> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        this.Configure(configureOptions);
        return this;
    }

    /// <summary>
    /// 配置用于分区流的 Orleans 队列数量。
    /// </summary>
    /// <param name="numOfPartitions">流队列数。</param>
    /// <returns>此配置。</returns>
    public SiloRedisStreamConfigurator ConfigurePartitioning(int numOfPartitions = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
    {
        this.Configure<HashRingStreamQueueMapperOptions>(builder => builder.Configure(options => options.TotalQueueCount = numOfPartitions));
        return this;
    }

    /// <summary>
    /// 配置接收器缓存大小 。
    /// </summary>
    /// <param name="cacheSize">缓存大小。</param>
    /// <returns>此配置。</returns>
    public SiloRedisStreamConfigurator ConfigureCache(int cacheSize = SimpleQueueCacheOptions.DEFAULT_CACHE_SIZE)
    {
        this.Configure<SimpleQueueCacheOptions>(builder => builder.Configure(options => options.CacheSize = cacheSize));
        return this;
    }
}

/// <summary>
/// 客户端上的配置 Redis 流。
/// </summary>
public sealed class ClusterClientRedisStreamConfigurator : ClusterClientPersistentStreamConfigurator
{
    /// <summary>
    /// 初始化 <see cref="ClusterClientRedisStreamConfigurator"/> 类的新实例。
    /// </summary>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="builder">客户端构建器。</param>
    public ClusterClientRedisStreamConfigurator(string name, IClientBuilder builder)
        : base(name, builder, RedisStreamAdapterFactory.Create)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .ConfigureServices(services => services.ConfigureNamedOptionForLogging<RedisStreamingOptions>(name)
                    .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name));
    }

    /// <summary>
    /// 配置 Redis 流动选项。
    /// </summary>
    /// <param name="configureOptions">用于配置选项的代表。</param>
    public void ConfigureOptions(Action<RedisStreamingOptions, IServiceProvider> configureOptions) => RedisStreamingOptions.Configure(configureOptions);

    /// <summary>
    /// 获取 <see cref="RedisStreamingOptions"/>的选项构建器。
    /// </summary>
    public OptionsBuilder<RedisStreamingOptions> RedisStreamingOptions => this.GetNamedOptionsBuilder<RedisStreamingOptions>();

    /// <summary>
    /// 配置用于分区流的 Orleans 队列数量。
    /// </summary>
    /// <param name="numOfPartitions">流队列数。</param>
    /// <returns>此配置。</returns>
    public ClusterClientRedisStreamConfigurator ConfigurePartitioning(int numOfPartitions = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
    {
        this.Configure<HashRingStreamQueueMapperOptions>(builder => builder.Configure(options => options.TotalQueueCount = numOfPartitions));
        return this;
    }
}

file static class RedisStreamConfiguratorExtensions
{
    public static OptionsBuilder<TOptions> GetNamedOptionsBuilder<TOptions>(this INamedServiceConfigurator configurator)
        where TOptions : class, new()
    {
        ArgumentNullException.ThrowIfNull(configurator);

        OptionsBuilder<TOptions> optionsBuilder = null!;
        configurator.ConfigureDelegate(services =>
        {
            optionsBuilder = services.AddOptions<TOptions>(configurator.Name);
            services.ConfigureNamedOptionForLogging<TOptions>(configurator.Name);
        });

        return optionsBuilder;
    }
}
