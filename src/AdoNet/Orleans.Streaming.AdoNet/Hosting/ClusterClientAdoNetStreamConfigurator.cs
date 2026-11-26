using Orleans.Streaming.AdoNet;

namespace Orleans.Hosting;

/// <summary>
/// 配置客户端上的 ADO.NET 流提供商 。
/// </summary>
public class ClusterClientAdoNetStreamConfigurator : ClusterClientPersistentStreamConfigurator
{
    /// <summary>
    /// 初始化 <see cref="ClusterClientAdoNetStreamConfigurator"/> 类的新实例。
    /// </summary>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="clientBuilder">客户端构建器。</param>
    public ClusterClientAdoNetStreamConfigurator(string name, IClientBuilder clientBuilder) : base(name, clientBuilder, AdoNetQueueAdapterFactory.Create)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(clientBuilder);

        clientBuilder.ConfigureServices(services => services
            .ConfigureNamedOptionForLogging<AdoNetStreamOptions>(name)
            .ConfigureNamedOptionForLogging<SimpleQueueCacheOptions>(name)
            .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name)
            .AddTransient<IConfigurationValidator>(sp => new AdoNetStreamOptionsValidator(sp.GetOptionsByName<AdoNetStreamOptions>(name), name)));

        // 在典型i/O-Obed共享数据库中,每个供应商多排一个队没有多大好处
        // 但是如果用户想要微调自己的系统通量, 多队列会得到完全支持
        ConfigurePartitioning(1);
    }

    /// <summary>
    /// 配置 ADO.NET 流提供方选项。
    /// </summary>
    /// <param name="configureOptions">代表用于配置指定供应商选项。</param>
    /// <returns>此配置。</returns>
    public ClusterClientAdoNetStreamConfigurator ConfigureAdoNet(Action<OptionsBuilder<AdoNetStreamOptions>> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        this.Configure(configureOptions);

        return this;
    }

    /// <summary>
    /// 配置接收器缓存大小 。
    /// </summary>
    /// <param name="cacheSize">缓存大小。</param>
    /// <returns>此配置。</returns>
    public ClusterClientAdoNetStreamConfigurator ConfigureCache(int cacheSize = SimpleQueueCacheOptions.DEFAULT_CACHE_SIZE)
    {
        this.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(options => options.CacheSize = cacheSize));

        return this;
    }

    /// <summary>
    /// 配置用于分区流的 Orleans 队列数量。
    /// </summary>
    /// <param name="partitions">流队列数。</param>
    /// <returns>此配置。</returns>
    public ClusterClientAdoNetStreamConfigurator ConfigurePartitioning(int partitions = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
    {
        this.Configure<HashRingStreamQueueMapperOptions>(ob => ob.Configure(options => options.TotalQueueCount = partitions));

        return this;
    }
}
