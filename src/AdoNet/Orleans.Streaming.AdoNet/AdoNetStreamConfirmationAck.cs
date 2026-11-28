namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 表示已成功确认消息的模型。
/// </summary>
internal record AdoNetStreamConfirmationAck(
    string ServiceId,
    string ProviderId,
    string QueueId,
    long MessageId)
{
    public AdoNetStreamConfirmationAck() : this("", "", "", 0)
    {
    }
}
