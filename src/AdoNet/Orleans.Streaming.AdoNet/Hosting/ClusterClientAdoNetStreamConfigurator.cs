using Orleans.Streaming.AdoNet;

namespace Orleans.Hosting;

/// <summary>
/// 在客户端上配置 ADO.NET 流提供程序。
/// </summary>
public class ClusterClientAdoNetStreamConfigurator : ClusterClientPersistentStreamConfigurator
{
    /// <summary>
    /// 初始化 <see cref="ClusterClientAdoNetStreamConfigurator"/> 类的新实例。
    /// </summary>
    /// <param name="name">流提供程序名称。</param>
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

        // 在典型的受 I/O 限制的共享数据库中，每个提供程序使用多个队列的收益很小。
        // 不过，如果用户希望微调自身系统的吞吐量，则完全支持使用多个队列。
        ConfigurePartitioning(1);
    }

    /// <summary>
    /// 配置 ADO.NET 流提供程序选项。
    /// </summary>
    /// <param name="configureOptions">用于配置命名提供程序选项的委托。</param>
    /// <returns>此配置器。</returns>
    public ClusterClientAdoNetStreamConfigurator ConfigureAdoNet(Action<OptionsBuilder<AdoNetStreamOptions>> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        this.Configure(configureOptions);

        return this;
    }

    /// <summary>
    /// 配置接收器缓存大小。
    /// </summary>
    /// <param name="cacheSize">缓存大小。</param>
    /// <returns>此配置器。</returns>
    public ClusterClientAdoNetStreamConfigurator ConfigureCache(int cacheSize = SimpleQueueCacheOptions.DEFAULT_CACHE_SIZE)
    {
        this.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(options => options.CacheSize = cacheSize));

        return this;
    }

    /// <summary>
    /// 配置用于对流进行分区的 Orleans 队列数量。
    /// </summary>
    /// <param name="partitions">流队列的数量。</param>
    /// <returns>此配置器。</returns>
    public ClusterClientAdoNetStreamConfigurator ConfigurePartitioning(int partitions = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
    {
        this.Configure<HashRingStreamQueueMapperOptions>(ob => ob.Configure(options => options.TotalQueueCount = partitions));

        return this;
    }
}
