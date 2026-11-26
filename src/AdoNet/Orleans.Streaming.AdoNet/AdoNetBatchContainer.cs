namespace Orleans.Streaming.AdoNet;

/// <summary>
/// ADONET 提供者的 <see cref="IBatchContainer"/> 实施。
/// </summary>
/// <remarks>
/// 1. 本类仅支持二进制序列化,因为性能和数据大小是数据库储存的优先事项。
/// 2. 虽然 <see cref="SequenceToken"/> 在这里得到支持,但目前尚未使用,因为 ADO.NET 提供者无法倒转。
/// 3. <see cref="SequenceToken"/> 持有接收器指定的队列缓存位置,而不是 <see cref="MessageId"/>。
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
    /// 持有信件确认的收据。
    /// </summary>
    [Id(4)]
    public int Dequeued { get; internal set; }

    #endregion Serialized State

    [NonSerialized]
    private long _messageId;

    /// <summary>
    /// 确定存储的电文,供接收方确认 silo。
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
    /// 创建新 <see cref="AdoNetBatchContainer"/> 指定发自 <see cref="AdoNetStreamMessage"/> 的队列缓存位置。
    /// </summary>
    public static AdoNetBatchContainer FromMessage(Serializer<AdoNetBatchContainer> serializer, AdoNetStreamMessage message, long position)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(message);

        // 存储的流信息总是包含一个分批处理的分批货箱。
        var container = serializer.Deserialize(message.Payload)!;
        container.SequenceToken = new(position);
        container._messageId = message.MessageId;
        container.Dequeued = message.Dequeued;

        return container;
    }

    /// <summary>
    /// 将指定的 <see cref="AdoNetBatchContainer"/> 转换为信件有效载荷。
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
