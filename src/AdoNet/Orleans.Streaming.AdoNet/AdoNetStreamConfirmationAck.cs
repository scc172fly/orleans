namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 代表成功确认信息的模式。
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
