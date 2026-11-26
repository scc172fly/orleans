namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 地图 Orleans 流和队列识别符号, 以显示 ADO.NET 队列识别标志。
/// </summary>
internal class AdoNetStreamQueueMapper(IConsistentRingStreamQueueMapper mapper)
{
    /// <summary>
    /// 将寻找 Orleans 流行的源流识别符号 ADO.NET 队列识别符号。
    /// </summary>
    private readonly ConcurrentDictionary<StreamId, string> _byStreamLookup = new();

    /// <summary>
    /// 将 Orleans 队列标识符的查找切换到 ADO.NET 队列标识符。
    /// </summary>
    private readonly ConcurrentDictionary<QueueId, string> _byQueueLookup = new();

    /// <summary>
    /// 将布景工厂的 Orleans 流行的源流识别符号 ADO.NET 队列识别符号。
    /// </summary>
    private readonly Func<StreamId, string> _fromStreamFactory = (StreamId streamId) => mapper.GetQueueForStream(streamId).ToString();

    /// <summary>
    /// 将 Orleans 队列识别符的映射厂保存到 ADO.NET 队列识别符。
    /// </summary>
    private readonly Func<QueueId, string> _fromQueueFactory = (QueueId queueId) => queueId.ToString();

    /// <summary>
    /// 得到 ADO.NET QueueId 指定 Orleans StreamId.
    /// </summary>
    public string GetAdoNetQueueId(StreamId streamId) => _byStreamLookup.GetOrAdd(streamId, _fromStreamFactory);

    /// <summary>
    /// 得到 ADO.NET QueueId 指定 Orleans QueueId.
    /// </summary>
    public string GetAdoNetQueueId(QueueId queueId) => _byQueueLookup.GetOrAdd(queueId, _fromQueueFactory);

    /// <summary>
    /// 在当前空间中获取全部 ADO.NET QueueIds 。
    /// </summary>
    public IEnumerable<string> GetAllAdoNetQueueIds()
    {
        foreach (var queueId in mapper.GetAllQueues())
        {
            yield return _byQueueLookup.GetOrAdd(queueId, _fromQueueFactory);
        }
    }
}
