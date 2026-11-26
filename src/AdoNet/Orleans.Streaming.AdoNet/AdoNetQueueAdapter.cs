namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 用于 ADO.NET 提供者的流队队列存储适配器。
/// </summary>
internal partial class AdoNetQueueAdapter(string name, AdoNetStreamOptions streamOptions, ClusterOptions clusterOptions, SimpleQueueCacheOptions cacheOptions, AdoNetStreamQueueMapper mapper, RelationalOrleansQueries queries, AdoNetStreamPositions positions, Serializer<AdoNetBatchContainer> serializer, ILogger<AdoNetQueueAdapter> logger, IServiceProvider serviceProvider) : IQueueAdapter
{
    private readonly ILogger<AdoNetQueueAdapter> _logger = logger;

    /// <summary>
    /// 数据库中的 ProviderId 地图。
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// ADO.NET 提供者尚未可倒转 。
    /// </summary>
    public bool IsRewindable => false;

    /// <summary>
    /// 提供商 ADO.NET 双管齐下。
    /// </summary>
    public StreamProviderDirection Direction => StreamProviderDirection.ReadWrite;

    public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
    {
        // 队列 ID 地图
        var adoNetQueueId = mapper.GetAdoNetQueueId(queueId);

        // 创建接收器
        return ReceiverFactory(serviceProvider, [Name, adoNetQueueId, streamOptions, clusterOptions, cacheOptions, queries, positions]);
    }

    public async Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext)
    {
        // ADO.NET 提供者无法倒转,所以我们不支持用户提供的质点
        if (token is not null)
        {
            throw new ArgumentException($"{nameof(AdoNetQueueAdapter)} does not support a user supplied {nameof(StreamSequenceToken)}.");
        }

        // 映射 Orleans 流 ID 到相应的队列id
        var queueId = mapper.GetAdoNetQueueId(streamId);

        // 创建事件产生的有效载荷
        var payload = AdoNetBatchContainer.ToMessagePayload(serializer, streamId, events.Cast<object>().ToList(), requestContext);

        // 我们现在可以把消息排成字母
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
    /// 接收厂
    /// </summary>
    private static readonly ObjectFactory<AdoNetQueueAdapterReceiver> ReceiverFactory = ActivatorUtilities.CreateFactory<AdoNetQueueAdapterReceiver>([typeof(string), typeof(string), typeof(AdoNetStreamOptions), typeof(ClusterOptions), typeof(SimpleQueueCacheOptions), typeof(RelationalOrleansQueries), typeof(AdoNetStreamPositions)]);

    #region Logging

    [LoggerMessage(1, LogLevel.Error, "Failed to queue stream message with ({ServiceId}, {ProviderId}, {QueueId})")]
    private partial void LogFailedToQueueStreamMessage(Exception ex, string serviceId, string providerId, string queueId);

    #endregion Logging
}
