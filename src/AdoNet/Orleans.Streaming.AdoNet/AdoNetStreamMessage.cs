namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 代表ADONET流源提供商存储信息的模式。
/// </summary>
internal record AdoNetStreamMessage(
    string ServiceId,
    string ProviderId,
    string QueueId,
    long MessageId,
    int Dequeued,
    DateTime VisibleOn,
    DateTime ExpiresOn,
    DateTime CreatedOn,
    DateTime ModifiedOn,
    byte[] Payload)
{
    public AdoNetStreamMessage() : this("", "", "", 0, 0, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, [])
    {
    }
}
