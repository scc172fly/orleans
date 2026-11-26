using Orleans.Internal;

namespace Orleans.Streaming.AdoNet;

internal interface IStreamMessageQueries
{
    Task<IList<AdoNetStreamMessage>> GetStreamMessagesAsync(
        string serviceId,
        string providerId,
        string queueId,
        int maxCount,
        int maxAttempts,
        int visibilityTimeout,
        int removalTimeout,
        int evictionInterval,
        int evictionBatchSize);

    Task<IList<AdoNetStreamConfirmationAck>> ConfirmStreamMessagesAsync(
        string serviceId,
        string providerId,
        string queueId,
        IList<AdoNetStreamConfirmation> messages);

    Task<IList<AdoNetStreamConfirmationAck>> ReleaseStreamMessagesAsync(
        string serviceId,
        string providerId,
        string queueId,
        IList<AdoNetStreamConfirmation> messages);
}

/// <summary>
/// 接收来自 ADO.NET 提供者单列的批量信件。
/// </summary>
internal partial class AdoNetQueueAdapterReceiver(string providerId, string queueId, AdoNetStreamOptions streamOptions, ClusterOptions clusterOptions, SimpleQueueCacheOptions cacheOptions, IStreamMessageQueries queries, AdoNetStreamPositions positions, Serializer<AdoNetBatchContainer> serializer, ILogger<AdoNetQueueAdapterReceiver> logger) : IQueueAdapterReceiver
{
    private readonly ILogger<AdoNetQueueAdapterReceiver> _logger = logger;
    private readonly AdmissionGate _admissionGate = new();
    private readonly object _lock = new();
    private readonly Dictionary<long, PendingMessage> _pendingMessages = [];

    public AdoNetQueueAdapterReceiver(
        string providerId,
        string queueId,
        AdoNetStreamOptions streamOptions,
        ClusterOptions clusterOptions,
        SimpleQueueCacheOptions cacheOptions,
        RelationalOrleansQueries queries,
        AdoNetStreamPositions positions,
        Serializer<AdoNetBatchContainer> serializer,
        ILogger<AdoNetQueueAdapterReceiver> logger)
        : this(providerId, queueId, streamOptions, clusterOptions, cacheOptions, new RelationalStreamMessageQueries(queries), positions, serializer, logger)
    {
    }

    /// <summary>
    /// 立即完成初始化。
    /// </summary>
    public Task Initialize(TimeSpan timeout) => Task.CompletedTask;

    /// <summary>
    /// 关闭接收、排水管接受操作和发布待发信息。
    /// </summary>
    public async Task Shutdown(TimeSpan timeout)
    {
        try
        {
            await _admissionGate.CloseAsync().WaitAsync(timeout);
        }
        catch (Exception ex)
        {
            LogShutdownFault(ex, clusterOptions.ServiceId, providerId, queueId);
            return;
        }

        List<AdoNetStreamConfirmation> pending;
        lock (_lock)
        {
            RemoveExpiredPendingMessages();
            if (_pendingMessages.Count == 0)
            {
                return;
            }

            pending = new(_pendingMessages.Count);
            foreach (var (messageId, message) in _pendingMessages)
            {
                positions.Remove(message.Position);
                pending.Add(new(messageId, message.Dequeued));
            }
        }

        try
        {
            var released = await queries.ReleaseStreamMessagesAsync(clusterOptions.ServiceId, providerId, queueId, pending).WaitAsync(timeout);
            lock (_lock)
            {
                foreach (var message in released)
                {
                    _pendingMessages.Remove(message.MessageId);
                }
            }
        }
        catch (Exception ex)
        {
            LogReleaseFailed(ex, clusterOptions.ServiceId, providerId, queueId, pending);
        }
    }

    /// <inheritdoc />
    public async Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount)
    {
        using var admission = _admissionGate.TryEnter();
        if (!admission.Entered)
        {
            return [];
        }

        // 酌情计入上限
        maxCount = Math.Min(maxCount, cacheOptions.CacheSize);

        try
        {
            var messages = await queries.GetStreamMessagesAsync(
                clusterOptions.ServiceId,
                providerId,
                queueId,
                maxCount,
                streamOptions.MaxAttempts,
                streamOptions.VisibilityTimeout.TotalSecondsCeiling(),
                streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling(),
                streamOptions.EvictionInterval.TotalSecondsCeiling(),
                streamOptions.EvictionBatchSize);

            // 队列缓存需要序列顺序符号, 且会增加符号的读数, 所以表示符号是指定的位置而不是信件标识符
            var dequeued = new List<(AdoNetStreamMessage Message, long Position)>(messages.Count);
            lock (_lock)
            {
                RemoveExpiredPendingMessages();
                foreach (var message in messages.OrderBy(static x => x.MessageId))
                {
                    if (_pendingMessages.TryGetValue(message.MessageId, out var previous))
                    {
                        positions.Remove(previous.Position);
                    }

                    var position = positions.Add(message.MessageId);
                    _pendingMessages[message.MessageId] = new(message.Dequeued, message.ExpiresOn, position);
                    dequeued.Add((message, position));
                }
            }

            // 将信息转换成标准批量容器
            return dequeued.Select(x => AdoNetBatchContainer.FromMessage(serializer, x.Message, x.Position)).Cast<IBatchContainer>().ToList();
        }
        catch (Exception ex)
        {
            LogDequeueFailed(ex, clusterOptions.ServiceId, providerId, queueId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task MessagesDeliveredAsync(IList<IBatchContainer> messages)
    {
        // 如果没有要发送的信件, 跳过工作
        if (messages.Count == 0)
        {
            return;
        }

        using var admission = _admissionGate.TryEnter();
        if (!admission.Entered)
        {
            return;
        }

        // 释放留下暗藏处的邮件位置,并用最新收到的信件确认每封信一次,
        // 因为再交付可以隐藏一些相同的电文尝试, 并且只有最新的收据匹配存储
        var receipts = new Dictionary<long, int>(messages.Count);
        foreach (AdoNetBatchContainer message in messages)
        {
            positions.Remove(message.SequenceToken.SequenceNumber);
            receipts[message.MessageId] = receipts.TryGetValue(message.MessageId, out var receipt)
                ? Math.Max(receipt, message.Dequeued)
                : message.Dequeued;
        }

        var items = receipts.Select(static item => new AdoNetStreamConfirmation(item.Key, item.Value)).ToList();

        try
        {
            var confirmed = await queries.ConfirmStreamMessagesAsync(clusterOptions.ServiceId, providerId, queueId, items);
            lock (_lock)
            {
                foreach (var message in confirmed)
                {
                    if (receipts.TryGetValue(message.MessageId, out var receipt)
                        && _pendingMessages.TryGetValue(message.MessageId, out var pending)
                        && receipt == pending.Dequeued)
                    {
                        _pendingMessages.Remove(message.MessageId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogConfirmationFailed(ex, clusterOptions.ServiceId, providerId, queueId, items);
            throw;
        }
    }

    private void RemoveExpiredPendingMessages()
    {
        var now = DateTime.UtcNow;
        var expired = _pendingMessages
            .Where(message => message.Value.ExpiresOn <= now)
            .Select(static message => message.Key)
            .ToList();
        foreach (var messageId in expired)
        {
            if (_pendingMessages.Remove(messageId, out var message))
            {
                positions.Remove(message.Position);
            }
        }
    }

    private readonly record struct PendingMessage(int Dequeued, DateTime ExpiresOn, long Position);

    private sealed class RelationalStreamMessageQueries(RelationalOrleansQueries queries) : IStreamMessageQueries
    {
        public Task<IList<AdoNetStreamMessage>> GetStreamMessagesAsync(
            string serviceId,
            string providerId,
            string queueId,
            int maxCount,
            int maxAttempts,
            int visibilityTimeout,
            int removalTimeout,
            int evictionInterval,
            int evictionBatchSize) =>
            queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        public Task<IList<AdoNetStreamConfirmationAck>> ConfirmStreamMessagesAsync(
            string serviceId,
            string providerId,
            string queueId,
            IList<AdoNetStreamConfirmation> messages) =>
            queries.ConfirmStreamMessagesAsync(serviceId, providerId, queueId, messages);

        public Task<IList<AdoNetStreamConfirmationAck>> ReleaseStreamMessagesAsync(
            string serviceId,
            string providerId,
            string queueId,
            IList<AdoNetStreamConfirmation> messages) =>
            queries.ReleaseStreamMessagesAsync(serviceId, providerId, queueId, messages);
    }

    #region Logging

    [LoggerMessage(1, LogLevel.Error, "Failed to get messages from ({ServiceId}, {ProviderId}, {QueueId})")]
    private partial void LogDequeueFailed(Exception exception, string serviceId, string providerId, string queueId);

    [LoggerMessage(2, LogLevel.Error, "Failed to confirm messages for ({ServiceId}, {ProviderId}, {QueueId}, {@Items})")]
    private partial void LogConfirmationFailed(Exception exception, string serviceId, string providerId, string queueId, List<AdoNetStreamConfirmation> items);

    [LoggerMessage(3, LogLevel.Warning, "Handled fault while shutting down receiver for ({ServiceId}, {ProviderId}, {QueueId})")]
    private partial void LogShutdownFault(Exception exception, string serviceId, string providerId, string queueId);

    [LoggerMessage(4, LogLevel.Warning, "Failed to release messages while shutting down receiver for ({ServiceId}, {ProviderId}, {QueueId}, {@Items})")]
    private partial void LogReleaseFailed(Exception exception, string serviceId, string providerId, string queueId, List<AdoNetStreamConfirmation> items);

    #endregion Logging
}
