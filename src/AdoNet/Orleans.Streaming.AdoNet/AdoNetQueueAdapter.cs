namespace Orleans.Streaming.AdoNet;

/// <summary>
/// ADO.NET 提供程序的流队列存储适配器。
/// </summary>
internal partial class AdoNetQueueAdapter(string name, AdoNetStreamOptions streamOptions, ClusterOptions clusterOptions, SimpleQueueCacheOptions cacheOptions, AdoNetStreamQueueMapper mapper, RelationalOrleansQueries queries, AdoNetStreamPositions positions, Serializer<AdoNetBatchContainer> serializer, ILogger<AdoNetQueueAdapter> logger, IServiceProvider serviceProvider) : IQueueAdapter
{
    private readonly ILogger<AdoNetQueueAdapter> _logger = logger;

    /// <summary>
    /// 映射到数据库中的 ProviderId。
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// ADO.NET 提供程序尚不支持回溯。
    /// </summary>
    public bool IsRewindable => false;

    /// <summary>
    /// ADO.NET 提供程序支持双向操作。
    /// </summary>
    public StreamProviderDirection Direction => StreamProviderDirection.ReadWrite;

    public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
    {
        // 映射队列 ID。
        var adoNetQueueId = mapper.GetAdoNetQueueId(queueId);

        // 创建接收器。
        return ReceiverFactory(serviceProvider, [Name, adoNetQueueId, streamOptions, clusterOptions, cacheOptions, queries, positions]);
    }

    public async Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext)
    {
        // ADO.NET 提供程序不支持回溯，因此不支持用户提供的令牌。
        if (token is not null)
        {
            throw new ArgumentException($"{nameof(AdoNetQueueAdapter)} does not support a user supplied {nameof(StreamSequenceToken)}.");
        }

        // 将 Orleans 流 ID 映射到相应队列 ID。
        var queueId = mapper.GetAdoNetQueueId(streamId);

        // 根据事件创建有效载荷。
        var payload = AdoNetBatchContainer.ToMessagePayload(serializer, streamId, events.Cast<object>().ToList(), requestContext);

        // 现在可以将消息加入队列。
        try
        {
            await queries.QueueStreamMessageAsync(clusterOptions.ServiceId, Name, queueId, payload, streamOptions.ExpiryTimeout.TotalSecondsCeiling());
        }
        catch (Exception ex)
        {
            LogFailedToQueueStreamMessage(ex, clusterOptions.ServiceId, Name, queueId);
            throw;
        }
    }

    /// <summary>
    /// 接收器工厂。
    /// </summary>
    private static readonly ObjectFactory<AdoNetQueueAdapterReceiver> ReceiverFactory = ActivatorUtilities.CreateFactory<AdoNetQueueAdapterReceiver>([typeof(string), typeof(string), typeof(AdoNetStreamOptions), typeof(ClusterOptions), typeof(SimpleQueueCacheOptions), typeof(RelationalOrleansQueries), typeof(AdoNetStreamPositions)]);

    #region Logging

    [LoggerMessage(1, LogLevel.Error, "Failed to queue stream message with ({ServiceId}, {ProviderId}, {QueueId})")]
    private partial void LogFailedToQueueStreamMessage(Exception ex, string serviceId, string providerId, string queueId);

    #endregion Logging
}
