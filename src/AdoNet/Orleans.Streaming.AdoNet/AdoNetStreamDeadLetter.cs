namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 代表ADONET流源提供商的一纸空文
/// </summary>
internal record AdoNetStreamDeadLetter(
    string ServiceId,
    string ProviderId,
    string QueueId,
    long MessageId,
    int Dequeued,
    DateTime VisibleOn,
    DateTime ExpiresOn,
    DateTime CreatedOn,
    DateTime ModifiedOn,
    DateTime DeadOn,
    DateTime RemoveOn,
    byte[] Payload)
{
    public AdoNetStreamDeadLetter() : this("", "", "", 0, 0, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, [])
    {
    }
}
