namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 表示存储返回的消息入队确认。
/// 用于公开生成的标识符以便测试。
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
