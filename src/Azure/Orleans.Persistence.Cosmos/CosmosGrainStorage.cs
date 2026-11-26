using System.Net;
using System.Threading;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;
using Orleans.Serialization.Serializers;

namespace Orleans.Persistence.Cosmos;

/// <summary>
/// 仓库 Orleans grain 状态内 Azure 宇宙DB号
/// </summary>
public sealed partial class CosmosGrainStorage : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
{
    private const string ANY_ETAG = "*";
    private const string GRAINTYPE_PARTITION_KEY_PATH = "/GrainType";
    private readonly ILogger _logger;
    private readonly CosmosGrainStorageOptions _options;
    private readonly string _name;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _serviceId;
    private string _partitionKeyPath;
    private readonly IDocumentIdProvider _documentIdProvider;
    private readonly IActivatorProvider _activatorProvider;
    private readonly ICosmosOperationExecutor _executor;
    private CosmosClient _client = default!;
    private Container _container = default!;

    /// <summary>
    /// 初始化 <see cref="CosmosGrainStorage"/> 类的新实例。
    /// </summary>
    /// <param name="name">存储提供者的名称。</param>
    /// <param name="options">储存提供者的选项。</param>
    /// <param name="loggerFactory">伐木厂</param>
    /// <param name="serviceProvider">服务供应商。</param>
    /// <param name="clusterOptions">组合组合选项。</param>
    /// <param name="documentIdProvider">用于创建宇宙 DB 文件标识符的提供者。</param>
    /// <param name="activatorProvider">用于创建 grain 状态实例的提供者。</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> 或 <paramref name="clusterOptions"/> 是 <see langword="null"/>。
    /// </exception>
    public CosmosGrainStorage(
        string name,
        CosmosGrainStorageOptions options,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IOptions<ClusterOptions> clusterOptions,
        IDocumentIdProvider documentIdProvider,
        IActivatorProvider activatorProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clusterOptions);

        _logger = loggerFactory.CreateLogger<CosmosGrainStorage>();
        _options = options;
        _name = name;
        _serviceProvider = serviceProvider;
        _serviceId = clusterOptions.Value.ServiceId;
        _documentIdProvider = documentIdProvider;
        _activatorProvider = activatorProvider;
        _executor = options.OperationExecutor;
        _partitionKeyPath = _options.PartitionKeyPath;
    }

    /// <inheritdoc/>
    public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        => ReadStateAsync(grainType, grainId, grainState, CancellationToken.None);

    /// <inheritdoc/>
    public async Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        cancellationToken.ThrowIfCancellationRequested();

        var (id, partitionKey) = await _documentIdProvider.GetDocumentIdentifiers(grainType, grainId);
        cancellationToken.ThrowIfCancellationRequested();

        LogTraceReadingState(grainType, id, grainId, _options.ContainerName, partitionKey);

        try
        {
            var pk = new PartitionKey(partitionKey);
            var entity = await _executor.ExecuteOperation(static args =>
            {
                var (self, id, pk, cancellationToken) = args;
                return self._container.ReadItemAsync<GrainStateEntity<T>>(id, pk, cancellationToken: cancellationToken);
            },
            (this, id, pk, cancellationToken), cancellationToken).ConfigureAwait(false);

            if (entity.Resource.State != null)
            {
                grainState.State = entity.Resource.State;
                grainState.RecordExists = true;
            }
            else
            {
                grainState.State = CreateInstance<T>();
                grainState.RecordExists = false;
            }

            grainState.ETag = entity.Resource.ETag;
        }
        catch (CosmosException dce)
        {
            if (dce.StatusCode == HttpStatusCode.NotFound)
            {
                // 国家是新的,只是启动违约和返回。
                ResetGrainState(grainState);
                return;
            }

            LogErrorReadingState(dce, grainType, id);
            WrappedException.CreateAndRethrow(dce);
            throw;
        }
        catch (Exception exc) when (exc is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogErrorReadingState(exc, grainType, id);
            WrappedException.CreateAndRethrow(exc);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        => WriteStateAsync(grainType, grainId, grainState, CancellationToken.None);

    /// <inheritdoc/>
    public async Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        cancellationToken.ThrowIfCancellationRequested();

        var (id, partitionKey) = await _documentIdProvider.GetDocumentIdentifiers(grainType, grainId);
        cancellationToken.ThrowIfCancellationRequested();

        LogTraceWritingState(grainType, id, grainId, grainState.ETag, _options.ContainerName, partitionKey);

        ItemResponse<GrainStateEntity<T>>? response = null;

        try
        {
            var entity = new GrainStateEntity<T>
            {
                ETag = grainState.ETag,
                Id = id,
                GrainType = grainType,
                State = grainState.State,
                PartitionKey = partitionKey
            };

            var pk = new PartitionKey(partitionKey);
            if (string.IsNullOrWhiteSpace(grainState.ETag))
            {
                response = await _executor.ExecuteOperation(
                    static args =>
                    {
                        var (self, entity, pk, cancellationToken) = args;
                        return self._container.CreateItemAsync(entity, pk, cancellationToken: cancellationToken);
                    },
                    (this, entity, pk, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            else if (grainState.ETag == ANY_ETAG)
            {
                var requestOptions = new ItemRequestOptions { IfMatchEtag = grainState.ETag };
                response = await _executor.ExecuteOperation(
                    static args =>
                    {
                        var (self, entity, pk, requestOptions, cancellationToken) = args;
                        return self._container.UpsertItemAsync(entity, pk, requestOptions, cancellationToken);
                    },
                    (this, entity, pk, requestOptions, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var requestOptions = new ItemRequestOptions { IfMatchEtag = grainState.ETag };
                response = await _executor.ExecuteOperation(
                    static args =>
                    {
                        var (self, entity, pk, requestOptions, cancellationToken) = args;
                        return self._container.ReplaceItemAsync(entity, entity.Id, pk, requestOptions, cancellationToken);
                    },
                    (this, entity, pk, requestOptions, cancellationToken), cancellationToken).ConfigureAwait(false);
            }

            grainState.ETag = response.Resource.ETag;
            grainState.RecordExists = true;
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            throw new CosmosConditionNotSatisfiedException(grainType, grainId, _options.ContainerName, "Unknown", grainState.ETag);
        }
        catch (Exception exc) when (exc is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogErrorWritingState(exc, grainType, id);
            WrappedException.CreateAndRethrow(exc);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        => ClearStateAsync(grainType, grainId, grainState, CancellationToken.None);

    /// <inheritdoc/>
    public async Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        cancellationToken.ThrowIfCancellationRequested();

        var (id, partitionKey) = await _documentIdProvider.GetDocumentIdentifiers(grainType, grainId);
        cancellationToken.ThrowIfCancellationRequested();

        LogTraceClearingState(grainType, id, grainId, grainState.ETag, _options.DeleteStateOnClear, _options.ContainerName, partitionKey);

        var pk = new PartitionKey(partitionKey);
        var requestOptions = new ItemRequestOptions { IfMatchEtag = grainState.ETag };
        try
        {
            if (_options.DeleteStateOnClear)
            {
                if (string.IsNullOrWhiteSpace(grainState.ETag))
                {
                    try
                    {
                        var entity = await _executor.ExecuteOperation(static args =>
                        {
                            var (self, id, pk, cancellationToken) = args;
                            return self._container.ReadItemAsync<GrainStateEntity<T>>(id, pk, cancellationToken: cancellationToken);
                        },
                        (this, id, pk, cancellationToken), cancellationToken).ConfigureAwait(false);

                        // 国家存在,但目前的启动没有观察到国家创建。
                        // 状态,应投出给 grain 一个停止使用和恢复的机会。
                        throw new CosmosConditionNotSatisfiedException(grainType, grainId, _options.ContainerName, "None", entity.ETag);
                    }
                    catch (CosmosException dce) when (dce.StatusCode == HttpStatusCode.NotFound)
                    {
                        // 忽略,因为这是预期的结果。
                        // 所有其他例外将由外壳区块处理。
                    }
                }
                else
                {
                    await _executor.ExecuteOperation(static args =>
                    {
                        var (self, id, pk, requestOptions, cancellationToken) = args;
                        return self._container.DeleteItemAsync<GrainStateEntity<T>>(id, pk, requestOptions, cancellationToken);
                    },
                    (this, id, pk, requestOptions, cancellationToken), cancellationToken);
                }

                ResetGrainState(grainState);
            }
            else
            {
                var entity = new GrainStateEntity<T>
                {
                    ETag = grainState.ETag,
                    Id = id,
                    GrainType = grainType,
                    State = default,
                    PartitionKey = partitionKey
                };

                var response = await _executor.ExecuteOperation(static args =>
                {
                    var (self, grainState, entity, pk, requestOptions, cancellationToken) = args;
                    return grainState.ETag switch
                    {
                        null or { Length: 0 } => self._container.CreateItemAsync(entity, pk, cancellationToken: cancellationToken),
                        ANY_ETAG => self._container.ReplaceItemAsync(entity, entity.Id, pk, requestOptions, cancellationToken),
                        _ => self._container.ReplaceItemAsync(entity, entity.Id, pk, requestOptions, cancellationToken),
                    };
                },
                (this, grainState, entity, pk, requestOptions, cancellationToken), cancellationToken).ConfigureAwait(false);

                grainState.ETag = response.Resource.ETag;
                grainState.RecordExists = false;
                grainState.State = CreateInstance<T>();
            }
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            throw new CosmosConditionNotSatisfiedException(grainType, grainId, _options.ContainerName, "Unknown", grainState.ETag ?? "Unknown");
        }
        catch (Exception exc) when (exc is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogErrorClearingState(exc, grainType, id);
            WrappedException.CreateAndRethrow(exc);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(OptionFormattingUtilities.Name<CosmosGrainStorage>(_name), _options.InitStage, Init);
    }

    private async Task Init(CancellationToken ct)
    {
        var stopWatch = Stopwatch.StartNew();

        try
        {
            LogDebugInit(_name, _serviceId, _options.ContainerName, _options.DeleteStateOnClear);

            await InitializeCosmosClient().ConfigureAwait(false);

            if (_options.IsResourceCreationEnabled)
            {
                if (_options.CleanResourcesOnInitialization)
                {
                    await TryDeleteDatabase().ConfigureAwait(false);
                }

                await TryCreateResources().ConfigureAwait(false);
            }

            _container = _client.GetContainer(_options.DatabaseName, _options.ContainerName);

            stopWatch.Stop();
            LogDebugInitializingProvider(_name, GetType().Name, _options.InitStage, stopWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopWatch.Stop();
            LogErrorInitializationFailed(ex, _name, GetType().Name, _options.InitStage, stopWatch.ElapsedMilliseconds);
            WrappedException.CreateAndRethrow(ex);
            throw;
        }
    }

    private async Task InitializeCosmosClient()
    {
        try
        {
            _client = await _options.CreateClient(_serviceProvider).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogErrorInitializingClient(ex);
            WrappedException.CreateAndRethrow(ex);
            throw;
        }
    }

    private async Task TryCreateResources()
    {
        var dbResponse = await _client.CreateDatabaseIfNotExistsAsync(_options.DatabaseName, _options.DatabaseThroughput);
        var db = dbResponse.Database;

        var stateContainer = new ContainerProperties(_options.ContainerName, _options.PartitionKeyPath);
        stateContainer.IndexingPolicy.IndexingMode = IndexingMode.Consistent;
        stateContainer.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/*" });
        stateContainer.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = $"/{nameof(GrainStateEntity<object>.GrainType)}/?" });
        stateContainer.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = ToScalarIndexPath(_options.PartitionKeyPath) });

        if (_options.StateFieldsToIndex != null)
        {
            foreach (var idx in _options.StateFieldsToIndex)
            {
                var path = idx.StartsWith("/", StringComparison.Ordinal) ? idx[1..] : idx;
                stateContainer.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = $"/\"State\"/\"{path}\"/?" });
            }
        }

        const int maxRetries = 3;
        for (var retry = 0; retry <= maxRetries; ++retry)
        {
            var containerResponse = await db.CreateContainerIfNotExistsAsync(stateContainer, _options.ContainerThroughputProperties);

            if (containerResponse.StatusCode == HttpStatusCode.OK || containerResponse.StatusCode == HttpStatusCode.Created)
            {
                var container = containerResponse.Resource;
                _partitionKeyPath = container.PartitionKeyPath;
                if (_partitionKeyPath == GRAINTYPE_PARTITION_KEY_PATH &&
                    (_documentIdProvider is not DefaultDocumentIdProvider defaultProvider || defaultProvider.HasCustomPartitionKeyProvider))
                    throw new OrleansConfigurationException("Custom document id or partition key providers are not compatible with partition key path set to /GrainType");
            }

            if (retry == maxRetries || dbResponse.StatusCode != HttpStatusCode.Created || containerResponse.StatusCode == HttpStatusCode.Created)
            {
                break;  // 显然,有些令人振奋的逻辑返回 HttpStatusCode.OK (而不是429) ,因为收藏不是在新的 DB 中创建的。
            }
            await Task.Delay(1000);
        }
    }

    private async Task TryDeleteDatabase()
    {
        try
        {
            await _client.GetDatabase(_options.DatabaseName).DeleteAsync().ConfigureAwait(false);
        }
        catch (CosmosException dce) when (dce.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }
        catch (Exception ex)
        {
            LogErrorDeletingDatabase(ex);
            WrappedException.CreateAndRethrow(ex);
            throw;
        }
    }

    private static string ToScalarIndexPath(string path)
    {
        if (path.EndsWith("/?", StringComparison.Ordinal))
        {
            return path;
        }

        return path.EndsWith("/", StringComparison.Ordinal) ? $"{path}?" : $"{path}/?";
    }

    private void ResetGrainState<T>(IGrainState<T> grainState)
    {
        grainState.State = CreateInstance<T>();
        grainState.ETag = null;
        grainState.RecordExists = false;
    }

    private T CreateInstance<T>() => _activatorProvider.GetActivator<T>().Create();

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Reading: GrainType={GrainType} Key={Id} GrainId={GrainId} from Container={Container} with PartitionKey={PartitionKey}"
    )]
    private partial void LogTraceReadingState(string grainType, string id, GrainId grainId, string container, string partitionKey);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failure reading state for Grain Type {GrainType} with Id {Id}"
    )]
    private partial void LogErrorReadingState(Exception exception, string grainType, string id);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Writing: GrainType={GrainType} Key={Id} GrainId={GrainId} ETag={ETag} from Container={Container} with PartitionKey={PartitionKey}"
    )]
    private partial void LogTraceWritingState(string grainType, string id, GrainId grainId, string? eTag, string container, string partitionKey);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failure writing state for Grain Type {GrainType} with Id {Id}"
    )]
    private partial void LogErrorWritingState(Exception exception, string grainType, string id);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Clearing: GrainType={GrainType} Key={Id} GrainId={GrainId} ETag={ETag} DeleteStateOnClear={DeleteStateOnClear} from Container={Container} with PartitionKey {PartitionKey}"
    )]
    private partial void LogTraceClearingState(string grainType, string id, GrainId grainId, string? eTag, bool deleteStateOnClear, string container, string partitionKey);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failure clearing state for Grain Type {GrainType} with Id {Id}"
    )]
    private partial void LogErrorClearingState(Exception exception, string grainType, string id);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Initializing: Name={Name} ServiceId={ServiceId} Collection={Collection} DeleteStateOnClear={DeleteStateOnClear}"
    )]
    private partial void LogDebugInit(string name, string serviceId, string collection, bool deleteStateOnClear);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Initializing provider {ProviderName} of type {ProviderType} in stage {Stage} took {ElapsedMilliseconds} milliseconds"
    )]
    private partial void LogDebugInitializingProvider(string providerName, string providerType, int stage, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = (int)ErrorCode.Provider_ErrorFromInit,
        Level = LogLevel.Error,
        Message = "Initialization failed for provider {ProviderName} of type {ProviderType} in stage {Stage} in {ElapsedMilliseconds} milliseconds"
    )]
    private partial void LogErrorInitializationFailed(Exception exception, string providerName, string providerType, int stage, long elapsedMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error initializing Azure Cosmos DB client for grain storage provider"
    )]
    private partial void LogErrorInitializingClient(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error deleting Azure Cosmos DB database"
    )]
    private partial void LogErrorDeletingDatabase(Exception exception);
}

/// <summary>
/// 创建已配置 <see cref="CosmosGrainStorage"/> 实例。
/// </summary>
public static class CosmosStorageFactory
{
    /// <summary>
    /// 创建带有指定名称的宇宙 DB grain 存储提供者。
    /// </summary>
    /// <param name="services">服务供应商。</param>
    /// <param name="name">存储提供者的名称。</param>
    /// <returns>配置的 grain 存储提供者。</returns>
    public static CosmosGrainStorage Create(IServiceProvider services, string name)
    {
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<CosmosGrainStorageOptions>>();
        var clusterOptions = services.GetRequiredService<IOptions<ClusterOptions>>();
        var documentIdProvider = services.GetKeyedService<IDocumentIdProvider>(name);
        if (documentIdProvider is null)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var partitionKeyProvider = services.GetKeyedService<IPartitionKeyProvider>(name)
                ?? services.GetRequiredService<IPartitionKeyProvider>();
#pragma warning restore CS0618 // Type or member is obsolete
            documentIdProvider = partitionKeyProvider is DefaultPartitionKeyProvider
                ? services.GetRequiredService<IDocumentIdProvider>()
                : new DefaultDocumentIdProvider(clusterOptions, partitionKeyProvider);
        }

        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var activatorProvider = services.GetRequiredService<IActivatorProvider>();
        return new CosmosGrainStorage(
            name,
            optionsMonitor.Get(name),
            loggerFactory,
            services,
            clusterOptions,
            documentIdProvider,
            activatorProvider);
    }
}
