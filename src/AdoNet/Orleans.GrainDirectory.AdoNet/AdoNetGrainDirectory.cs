namespace Orleans.GrainDirectory.AdoNet;

internal sealed partial class AdoNetGrainDirectory(string name, AdoNetGrainDirectoryOptions options, ILogger<AdoNetGrainDirectory> logger, IOptions<ClusterOptions> clusterOptions, IHostApplicationLifetime lifetime) : IGrainDirectory
{
    private readonly ILogger _logger = logger;
    private readonly string _clusterId = clusterOptions.Value.ClusterId;
    private RelationalOrleansQueries? _queries;

    /// <summary>
    /// 查找 Grain 激活实例。
    /// </summary>
    /// <param name="grainId">Grain 标识符。</param>
    /// <returns>如果找到则返回 Grain 地址，否则返回 null。</returns>
    public async Task<GrainAddress?> Lookup(GrainId grainId)
    {
        try
        {
            var queries = await GetQueriesAsync();

            var entry = await queries
                .LookupGrainActivationAsync(_clusterId, name, grainId.ToString())
                .WaitAsync(lifetime.ApplicationStopping);

            return entry?.ToGrainAddress();
        }
        catch (Exception ex)
        {
            LogFailedToLookup(ex, _clusterId, grainId);
            throw;
        }
    }

    /// <summary>
    /// 注册新的 Grain 激活实例。
    /// </summary>
    /// <param name="address">Grain 地址。</param>
    /// <returns>新的或当前的 Grain 地址。</returns>
    public async Task<GrainAddress?> Register(GrainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(address.SiloAddress);

        try
        {
            var queries = await GetQueriesAsync();

            // 此调用应以线程安全的方式注册新条目，或者在找到现有条目时返回该条目。
            var entry = await queries
                .RegisterGrainActivationAsync(_clusterId, name, address.GrainId.ToString(), address.SiloAddress.ToParsableString(), address.ActivationId.ToParsableString())
                .WaitAsync(lifetime.ApplicationStopping);

            LogRegistered(_clusterId, address.GrainId, address.SiloAddress, address.ActivationId);

            return entry.ToGrainAddress();
        }
        catch (Exception ex)
        {
            LogFailedToRegister(ex, _clusterId, address.GrainId, address.SiloAddress, address.ActivationId);
            throw;
        }
    }

    /// <summary>
    /// 注销现有的 Grain 激活实例。
    /// </summary>
    /// <param name="address">Grain 地址。</param>
    public async Task Unregister(GrainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(address.SiloAddress);

        try
        {
            var queries = await GetQueriesAsync();

            var count = await queries
                .UnregisterGrainActivationAsync(_clusterId, name, address.GrainId.ToString(), address.ActivationId.ToParsableString())
                .WaitAsync(lifetime.ApplicationStopping);

            if (count > 0)
            {
                LogUnregistered(_clusterId, address.GrainId, address.SiloAddress, address.ActivationId);
            }
        }
        catch (Exception ex)
        {
            LogFailedToUnregister(ex, _clusterId, address.GrainId, address.SiloAddress, address.ActivationId);
            throw;
        }
    }

    /// <summary>
    /// 注销指定 Silo 集合中的所有 Grain 激活实例。
    /// </summary>
    /// <param name="siloAddresses">Silo 集合。</param>
    public async Task UnregisterSilos(List<SiloAddress> siloAddresses)
    {
        ArgumentNullException.ThrowIfNull(siloAddresses);

        if (siloAddresses.Count == 0)
        {
            return;
        }

        try
        {
            var queries = await GetQueriesAsync();

            var count = await queries
                .UnregisterGrainActivationsAsync(_clusterId, name, GetSilosAddressesAsString(siloAddresses))
                .WaitAsync(lifetime.ApplicationStopping);

            if (count > 0)
            {
                LogUnregisteredSilos(count, _clusterId, siloAddresses);
            }
        }
        catch (Exception ex)
        {
            LogFailedToUnregisterSilos(ex, _clusterId, siloAddresses);
            throw;
        }

        static string GetSilosAddressesAsString(IEnumerable<SiloAddress> siloAddresses) => string.Join('|', siloAddresses.Select(x => x.ToParsableString()));
    }

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
            await _semaphore.WaitAsync(lifetime.ApplicationStopping);
            try
            {
                // 再次尝试快速路径。
                if (_queries is not null)
                {
                    return _queries;
                }

                // 慢速路径：只有调用成功时才设置成员变量。
                return _queries = await RelationalOrleansQueries
                    .CreateInstance(options.Invariant, options.ConnectionString, options.DataSource)
                    .WaitAsync(lifetime.ApplicationStopping);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    #region Logging

    [LoggerMessage(1, LogLevel.Error, "Failed to lookup({ClusterId}, {GrainId})")]
    private partial void LogFailedToLookup(Exception ex, string clusterId, GrainId grainId);

    [LoggerMessage(2, LogLevel.Debug, "Registered ({ClusterId}, {GrainId}, {SiloAddress}, {ActivationId})")]
    private partial void LogRegistered(string clusterId, GrainId grainId, SiloAddress siloAddress, ActivationId activationId);

    [LoggerMessage(3, LogLevel.Error, "Failed to register ({ClusterId}, {GrainId}, {SiloAddress}, {ActivationId})")]
    private partial void LogFailedToRegister(Exception ex, string clusterId, GrainId grainId, SiloAddress siloAddress, ActivationId activationId);

    [LoggerMessage(4, LogLevel.Debug, "Unregistered ({ClusterId}, {GrainId}, {SiloAddress}, {ActivationId})")]
    private partial void LogUnregistered(string clusterId, GrainId grainId, SiloAddress siloAddress, ActivationId activationId);

    [LoggerMessage(5, LogLevel.Error, "Failed to unregister ({ClusterId}, {GrainId}, {SiloAddress}, {ActivationId})")]
    private partial void LogFailedToUnregister(Exception ex, string clusterId, GrainId grainId, SiloAddress siloAddress, ActivationId activationId);

    [LoggerMessage(6, LogLevel.Debug, "Unregistered {Count} activations from silos {SiloAddresses} in cluster {ClusterId}")]
    private partial void LogUnregisteredSilos(int count, string clusterId, IEnumerable<SiloAddress> siloAddresses);

    [LoggerMessage(7, LogLevel.Error, "Failed to unregister silos {SiloAddresses} in cluster {ClusterId}")]
    private partial void LogFailedToUnregisterSilos(Exception ex, string clusterId, IEnumerable<SiloAddress> siloAddresses);

    #endregion Logging
}
