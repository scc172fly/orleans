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
    /// 由于缺少异步生命周期支持，不得不采用的实现细节。
    /// 理想情况下，应将此问题移至上游处理，以免需要此实现。
    /// </summary>
    private readonly SemaphoreSlim _semaphore = new(1);

    /// <summary>
    /// 确保查询仅加载一次，同时允许加载失败后进行恢复。
    /// </summary>
    private ValueTask<RelationalOrleansQueries> GetQueriesAsync()
    {
        // 尝试快速路径。
        return _queries is not null ? new(_queries) : new(CoreAsync());

        // 慢速路径。
        async Task<RelationalOrleansQueries> CoreAsync()
        {
            await _semaphore.WaitAsync(_streamOptions.InitializationTimeout, _lifetime.ApplicationStopping);
            try
            {
                // 再次尝试快速路径。
                if (_queries is not null)
                {
                    return _queries;
                }

                // 慢速路径：只有调用成功时才设置成员变量。
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
    /// 由 Silo 及客户端配置器用作设置流的入口点。
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
    /// <see cref="AdoNetQueueAdapterFactory"/> 实例的工厂。
    /// </summary>
    private static readonly ObjectFactory<AdoNetQueueAdapterFactory> QueueAdapterFactoryFactory = ActivatorUtilities.CreateFactory<AdoNetQueueAdapterFactory>([typeof(string), typeof(AdoNetStreamOptions), typeof(ClusterOptions), typeof(SimpleQueueCacheOptions), typeof(HashRingStreamQueueMapperOptions)]);

    /// <summary>
    /// <see cref="AdoNetQueueAdapter"/> 实例的工厂。
    /// </summary>
    private static readonly ObjectFactory<AdoNetQueueAdapter> AdapterFactory = ActivatorUtilities.CreateFactory<AdoNetQueueAdapter>([typeof(string), typeof(AdoNetStreamOptions), typeof(ClusterOptions), typeof(SimpleQueueCacheOptions), typeof(AdoNetStreamQueueMapper), typeof(RelationalOrleansQueries), typeof(AdoNetStreamPositions)]);

    /// <summary>
    /// <see cref="AdoNetStreamFailureHandler"/> 实例的工厂。
    /// </summary>
    private static readonly ObjectFactory<AdoNetStreamFailureHandler> HandlerFactory = ActivatorUtilities.CreateFactory<AdoNetStreamFailureHandler>([typeof(bool), typeof(AdoNetStreamOptions), typeof(ClusterOptions), typeof(AdoNetStreamQueueMapper), typeof(RelationalOrleansQueries), typeof(AdoNetStreamPositions)]);
}
