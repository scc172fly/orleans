namespace Orleans.Streaming.AdoNet;

/// <summary>
/// ADO.NET 提供程序的 <see cref="IBatchContainer"/> 实现。
/// </summary>
/// <remarks>
/// 1. 此类仅支持二进制序列化，因为性能及数据大小是数据库存储的首要考虑因素。
/// 2. 虽然此处支持 <see cref="SequenceToken"/>，但尚未使用它，因为 ADO.NET 提供程序不支持回溯。
/// 3. <see cref="SequenceToken"/> 保存接收器分配的队列缓存位置，而不是 <see cref="MessageId"/>。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Streaming.AdoNet.AdoNetBatchContainer")]
internal class AdoNetBatchContainer : IBatchContainer
{
    public AdoNetBatchContainer(StreamId streamId, List<object> events, Dictionary<string, object>? requestContext)
    {
        ArgumentNullException.ThrowIfNull(events);

        StreamId = streamId;
        Events = events;
        RequestContext = requestContext;
    }

    #region Serialized State

    [Id(0)]
    public StreamId StreamId { get; }

    [Id(1)]
    public List<object> Events { get; }

    [Id(2)]
    public Dictionary<string, object>? RequestContext { get; }

    [Id(3)]
    public EventSequenceTokenV2 SequenceToken { get; internal set; } = null!;

    /// <summary>
    /// 保存用于确认消息的回执。
    /// </summary>
    [Id(4)]
    public int Dequeued { get; internal set; }

    #endregion Serialized State

    [NonSerialized]
    private long _messageId;

    /// <summary>
    /// 标识已存储的消息，供接收消息的 Silo 进行确认。
    /// </summary>
    public long MessageId => _messageId;

    #region Interface

    StreamSequenceToken IBatchContainer.SequenceToken => SequenceToken;

    public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
    {
        return SequenceToken is null
            ? throw new InvalidOperationException($"Cannot get events from a half-baked {nameof(AdoNetBatchContainer)}")
            : Events
                .OfType<T>()
                .Select((e, i) => Tuple.Create<T, StreamSequenceToken>(e, SequenceToken.CreateSequenceTokenForEvent(i)));
    }

    public bool ImportRequestContext()
    {
        if (RequestContext is not null)
        {
            RequestContextExtensions.Import(RequestContext);
            return true;
        }

        return false;
    }

    #endregion Interface

    #region Conversion

    /// <summary>
    /// 创建新的 <see cref="AdoNetBatchContainer"/>，使用指定的 <see cref="AdoNetStreamMessage"/> 以及指定的队列缓存位置。
    /// </summary>
    public static AdoNetBatchContainer FromMessage(Serializer<AdoNetBatchContainer> serializer, AdoNetStreamMessage message, long position)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(message);

        // 已存储的流消息始终包含序列化的批次容器。
        var container = serializer.Deserialize(message.Payload)!;
        container.SequenceToken = new(position);
        container._messageId = message.MessageId;
        container.Dequeued = message.Dequeued;

        return container;
    }

    /// <summary>
    /// 将指定的 <see cref="AdoNetBatchContainer"/> 转换为消息有效载荷。
    /// </summary>
    public static byte[] ToMessagePayload(Serializer<AdoNetBatchContainer> serializer, StreamId streamId, List<object> events, Dictionary<string, object>? requestContext)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(events);

        var container = new AdoNetBatchContainer(streamId, events, requestContext);
        var payload = serializer.SerializeToArray(container);

        return payload;
    }

    #endregion Conversion

    public override string ToString() => $"[{nameof(AdoNetBatchContainer)}:Stream={StreamId},#Items={Events.Count}]";
}
