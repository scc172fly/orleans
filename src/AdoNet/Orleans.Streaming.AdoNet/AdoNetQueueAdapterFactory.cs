using System.Threading;
using Microsoft.Extensions.Hosting;

namespace Orleans.Streaming.AdoNet;

internal class AdoNetQueueAdapterFactory : IQueueAdapterFactory
{
    public AdoNetQueueAdapterFactory(string name, AdoNetStreamOptions streamOptions, ClusterOptions clusterOptions, SimpleQueueCacheOptions cacheOptions, HashRingStreamQueueMapperOptions hashOptions, ILoggerFactory loggerFactory, IHostApplicationLifetime lifetime, IServiceProvider serviceProvider)
    {
        _name = name;
        _streamOptions = streamOptions;
        _clusterOptions = clusterOptions;
        _cacheOptions = cacheOptions;
        _lifetime = lifetime;
        _serviceProvider = serviceProvider;

        _streamQueueMapper = new HashRingBasedStreamQueueMapper(hashOptions, name);
        _cache = new SimpleQueueAdapterCache(cacheOptions, name, loggerFactory);
        _adoNetQueueMapper = new AdoNetStreamQueueMapper(_streamQueueMapper);
    }

    private readonly string _name;
    private readonly AdoNetStreamOptions _streamOptions;
    private readonly ClusterOptions _clusterOptions;
    private readonly SimpleQueueCacheOptions _cacheOptions;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceProvider _serviceProvider;

    private readonly HashRingBasedStreamQueueMapper _streamQueueMapper;
    private readonly SimpleQueueAdapterCache _cache;
    private readonly AdoNetStreamQueueMapper _adoNetQueueMapper;
    private readonly AdoNetStreamPositions _positions = new();

    private RelationalOrleansQueries? _queries;

    /// <summary>
    /// 不幸地执行细节,以说明缺乏同步寿命。
    /// 理想的情况是,这种关注将上移到上游,这样就不需要了。
    /// </summary>
    private readonly SemaphoreSlim _semaphore = new(1);

    /// <summary>
    /// 确保只装入一次查询, 并允许在负载失败时恢复 。
    /// </summary>
    private ValueTask<RelationalOrleansQueries> GetQueriesAsync()
    {
        // 尝试快速路径
        return _queries is not null ? new(_queries) : new(CoreAsync());

        // 慢速路径
        async Task<RelationalOrleansQueries> CoreAsync()
        {
            await _semaphore.WaitAsync(_streamOptions.InitializationTimeout, _lifetime.ApplicationStopping);
            try
            {
                // 重新尝试快速路径
                if (_queries is not null)
                {
                    return _queries;
                }

                // 慢慢路径路路路 - 只有在调用成功时才会设定成员变量
                return _queries = await RelationalOrleansQueries
                    .CreateInstance(_streamOptions.Invariant, _streamOptions.ConnectionString, _streamOptions.DataSource)
                    .WaitAsync(_streamOptions.InitializationTimeout);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    public async Task<IQueueAdapter> CreateAdapter()
    {
        var queries = await GetQueriesAsync();

        return AdapterFactory(_serviceProvider, [_name, _streamOptions, _clusterOptions, _cacheOptions, _adoNetQueueMapper, queries, _positions]);
    }

    public async Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
    {
        var queries = await GetQueriesAsync();

        return HandlerFactory(_serviceProvider, [false, _streamOptions, _clusterOptions, _adoNetQueueMapper, queries, _positions]);
    }

    public IQueueAdapterCache GetQueueAdapterCache() => _cache;

    public IStreamQueueMapper GetStreamQueueMapper() => _streamQueueMapper;

    /// <summary>
    /// silo 和客户端配置器用作设置流的切入点。
    /// </summary>
    public static IQueueAdapterFactory Create(IServiceProvider serviceProvider, string name)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(name);

        var streamOptions = serviceProvider.GetOptionsByName<AdoNetStreamOptions>(name);
        var clusterOptions = serviceProvider.GetProviderClusterOptions(name).Value;
        var cacheOptions = serviceProvider.GetOptionsByName<SimpleQueueCacheOptions>(name);
        var hashOptions = serviceProvider.GetOptionsByName<HashRingStreamQueueMapperOptions>(name);

        return QueueAdapterFactoryFactory(serviceProvider, [name, streamOptions, clusterOptions, cacheOptions, hashOptions]);
    }

    /// <summary>
    /// 工厂 <see cref="AdoNetQueueAdapterFactory"/> 实例。
    /// </summary>
    private static readonly ObjectFactory<AdoNetQueueAdapterFactory> QueueAdapterFactoryFactory = ActivatorUtilities.CreateFactory<AdoNetQueueAdapterFactory>([typeof(string), typeof(AdoNetStreamOptions), typeof(ClusterOptions), typeof(SimpleQueueCacheOptions), typeof(HashRingStreamQueueMapperOptions)]);

    /// <summary>
    /// 工厂 <see cref="AdoNetQueueAdapter"/> 实例。
    /// </summary>
    private static readonly ObjectFactory<AdoNetQueueAdapter> AdapterFactory = ActivatorUtilities.CreateFactory<AdoNetQueueAdapter>([typeof(string), typeof(AdoNetStreamOptions), typeof(ClusterOptions), typeof(SimpleQueueCacheOptions), typeof(AdoNetStreamQueueMapper), typeof(RelationalOrleansQueries), typeof(AdoNetStreamPositions)]);

    /// <summary>
    /// 工厂 <see cref="AdoNetStreamFailureHandler"/> 实例。
    /// </summary>
    private static readonly ObjectFactory<AdoNetStreamFailureHandler> HandlerFactory = ActivatorUtilities.CreateFactory<AdoNetStreamFailureHandler>([typeof(bool), typeof(AdoNetStreamOptions), typeof(ClusterOptions), typeof(AdoNetStreamQueueMapper), typeof(RelationalOrleansQueries), typeof(AdoNetStreamPositions)]);
}
