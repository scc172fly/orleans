namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 尝试将消息移入死信的 <see cref="IStreamFailureHandler"/> 实现。
/// </summary>
internal partial class AdoNetStreamFailureHandler(bool faultOnFailure, AdoNetStreamOptions streamOptions, ClusterOptions clusterOptions, AdoNetStreamQueueMapper mapper, RelationalOrleansQueries queries, AdoNetStreamPositions positions, ILogger<AdoNetStreamFailureHandler> logger) : IStreamFailureHandler
{
    private readonly ILogger<AdoNetStreamFailureHandler> _logger = logger;

    /// <summary>
    /// 获取一个值，指示发生错误时订阅是否应进入故障状态。
    /// </summary>
    public bool ShouldFaultSubsriptionOnError { get; } = faultOnFailure;

    /// <summary>
    /// 在投递失败时尝试将消息移入死信。
    /// </summary>
    public Task OnDeliveryFailure(GuidId subscriptionId, string streamProviderName, StreamId streamIdentity, StreamSequenceToken? sequenceToken) => OnFailureAsync(streamProviderName, streamIdentity, sequenceToken);

    /// <summary>
    /// 在投递失败时尝试将消息移入死信。
    /// </summary>
    public Task OnSubscriptionFailure(GuidId subscriptionId, string streamProviderName, StreamId streamIdentity, StreamSequenceToken? sequenceToken) => OnFailureAsync(streamProviderName, streamIdentity, sequenceToken);

    /// <summary>
    /// 在投递失败时尝试将消息移入死信。
    /// </summary>
    /// <remarks>
    /// 序列令牌保存由此提供程序的接收器分配的队列缓存位置。
    /// 无法再解析的位置对应接收器已经释放、重新投递或已过期的消息，因此跳过这些位置。
    /// </remarks>
    private async Task OnFailureAsync(string streamProviderName, StreamId streamIdentity, StreamSequenceToken? sequenceToken)
    {
        ArgumentNullException.ThrowIfNull(streamProviderName);
        ArgumentNullException.ThrowIfNull(sequenceToken);

        var queueId = mapper.GetAdoNetQueueId(streamIdentity);

        if (!positions.TryGetMessageId(sequenceToken.SequenceNumber, out var messageId))
        {
            LogSkippedUnknownPosition(clusterOptions.ServiceId, streamProviderName, queueId, sequenceToken.SequenceNumber);
            return;
        }

        try
        {
            await queries.FailStreamMessageAsync(clusterOptions.ServiceId, streamProviderName, queueId, messageId, streamOptions.MaxAttempts, streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling());

            LogMovedMessage(clusterOptions.ServiceId, streamProviderName, queueId, messageId);
        }
        catch (Exception ex)
        {
            LogFailedToMoveMessage(ex, clusterOptions.ServiceId, streamProviderName, queueId, messageId);
            throw;
        }
    }

    #region Logging

    [LoggerMessage(1, LogLevel.Warning, "Moved failed delivery to dead letters: ({ServiceId}, {ProviderId}, {QueueId}, {MessageId})")]
    private partial void LogMovedMessage(string serviceId, string providerId, string queueId, long messageId);

    [LoggerMessage(2, LogLevel.Error, "Failed to move failed delivery to dead letters: ({ServiceId}, {ProviderId}, {QueueId}, {MessageId}")]
    private partial void LogFailedToMoveMessage(Exception ex, string serviceId, string providerId, string queueId, long messageId);

    [LoggerMessage(3, LogLevel.Debug, "Skipped failed delivery for a message no longer held by this silo: ({ServiceId}, {ProviderId}, {QueueId}, {Position})")]
    private partial void LogSkippedUnknownPosition(string serviceId, string providerId, string queueId, long position);

    #endregion Logging
}
