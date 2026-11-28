namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 表示 ADO.NET 流提供程序中死信的模型。
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
