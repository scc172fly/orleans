namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 将 Orleans 流及队列标识符映射到 ADO.NET 队列标识符。
/// </summary>
internal class AdoNetStreamQueueMapper(IConsistentRingStreamQueueMapper mapper)
{
    /// <summary>
    /// 缓存 Orleans 流标识符到 ADO.NET 队列标识符的查找结果。
    /// </summary>
    private readonly ConcurrentDictionary<StreamId, string> _byStreamLookup = new();

    /// <summary>
    /// 缓存 Orleans 队列标识符到 ADO.NET 队列标识符的查找结果。
    /// </summary>
    private readonly ConcurrentDictionary<QueueId, string> _byQueueLookup = new();

    /// <summary>
    /// 缓存 Orleans 流标识符到 ADO.NET 队列标识符的映射工厂。
    /// </summary>
    private readonly Func<StreamId, string> _fromStreamFactory = (StreamId streamId) => mapper.GetQueueForStream(streamId).ToString();

    /// <summary>
    /// 缓存 Orleans 队列标识符到 ADO.NET 队列标识符的映射工厂。
    /// </summary>
    private readonly Func<QueueId, string> _fromQueueFactory = (QueueId queueId) => queueId.ToString();

    /// <summary>
    /// 获取指定 Orleans StreamId 对应的 ADO.NET QueueId。
    /// </summary>
    public string GetAdoNetQueueId(StreamId streamId) => _byStreamLookup.GetOrAdd(streamId, _fromStreamFactory);

    /// <summary>
    /// 获取指定 Orleans QueueId 对应的 ADO.NET QueueId。
    /// </summary>
    public string GetAdoNetQueueId(QueueId queueId) => _byQueueLookup.GetOrAdd(queueId, _fromQueueFactory);

    /// <summary>
    /// 获取当前空间中的所有 ADO.NET QueueId。
    /// </summary>
    public IEnumerable<string> GetAllAdoNetQueueIds()
    {
        foreach (var queueId in mapper.GetAllQueues())
        {
            yield return _byQueueLookup.GetOrAdd(queueId, _fromQueueFactory);
        }
    }
}
