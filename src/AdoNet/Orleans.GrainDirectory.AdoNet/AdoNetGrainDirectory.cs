namespace Orleans.GrainDirectory.AdoNet;

internal sealed partial class AdoNetGrainDirectory(string name, AdoNetGrainDirectoryOptions options, ILogger<AdoNetGrainDirectory> logger, IOptions<ClusterOptions> clusterOptions, IHostApplicationLifetime lifetime) : IGrainDirectory
{
    private readonly ILogger _logger = logger;
    private readonly string _clusterId = clusterOptions.Value.ClusterId;
    private RelationalOrleansQueries? _queries;

    /// <summary>
    /// 检查一个 grain 激活。
    /// </summary>
    /// <param name="grainId"> grain 标识符。</param>
    /// <returns>下 grain 找到地址后, 找不到地址或地址无效 。</returns>
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
    /// 注册新的 grain 激活。
    /// </summary>
    /// <param name="address"> grain 地址。</param>
    /// <returns>新的或当前的 grain 地址。</returns>
    public async Task<GrainAddress?> Register(GrainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(address.SiloAddress);

        try
        {
            var queries = await GetQueriesAsync();

            // 如果以线索安全方式找到,预计此呼叫将登记新条目或返回现有条目
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
    /// 解除已有的 grain 激活的登记。
    /// </summary>
    /// <param name="address"> grain 地址。</param>
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
    /// 全部不登记者 grain 指定集中的激活 silos.
    /// </summary>
    /// <param name="siloAddresses">集 silos.</param>
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
            await _semaphore.WaitAsync(lifetime.ApplicationStopping);
            try
            {
                // 重新尝试快速路径
                if (_queries is not null)
                {
                    return _queries;
                }

                // 慢慢路径路路路 - 只有在调用成功时才会设定成员变量
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
