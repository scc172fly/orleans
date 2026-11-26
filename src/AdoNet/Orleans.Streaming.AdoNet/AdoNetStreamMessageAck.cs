namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 表示存储中确认有一条电文被排入队列。
/// 用于表面任何生成的测试识别符。
/// </summary>
internal record AdoNetStreamMessageAck(
    string ServiceId,
    string ProviderId,
    string QueueId,
    long MessageId)
{
    public AdoNetStreamMessageAck() : this("", "", "", 0)
    {
    }
}
