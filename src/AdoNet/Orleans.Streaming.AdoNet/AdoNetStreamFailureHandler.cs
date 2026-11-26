namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 试图将信息移到死字的 <see cref="IStreamFailureHandler"/>
/// </summary>
internal partial class AdoNetStreamFailureHandler(bool faultOnFailure, AdoNetStreamOptions streamOptions, ClusterOptions clusterOptions, AdoNetStreamQueueMapper mapper, RelationalOrleansQueries queries, AdoNetStreamPositions positions, ILogger<AdoNetStreamFailureHandler> logger) : IStreamFailureHandler
{
    private readonly ILogger<AdoNetStreamFailureHandler> _logger = logger;

    /// <summary>
    /// 获得一个值,显示在错误发生时订阅是否应该失败。
    /// </summary>
    public bool ShouldFaultSubsriptionOnError { get; } = faultOnFailure;

    /// <summary>
    /// 试图在送货失败时将信息移到死信。
    /// </summary>
    public Task OnDeliveryFailure(GuidId subscriptionId, string streamProviderName, StreamId streamIdentity, StreamSequenceToken? sequenceToken) => OnFailureAsync(streamProviderName, streamIdentity, sequenceToken);

    /// <summary>
    /// 试图在送货失败时将信息移到死信。
    /// </summary>
    public Task OnSubscriptionFailure(GuidId subscriptionId, string streamProviderName, StreamId streamIdentity, StreamSequenceToken? sequenceToken) => OnFailureAsync(streamProviderName, streamIdentity, sequenceToken);

    /// <summary>
    /// 试图在送货失败时将信息移到死信。
    /// </summary>
    /// <remarks>
    /// 此序列命令持有由此提供者接收器指定的队列缓存位置 。
    /// 不再解决的位置属于接收器释放、重新交付或过期的信件,所以被跳过。
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
