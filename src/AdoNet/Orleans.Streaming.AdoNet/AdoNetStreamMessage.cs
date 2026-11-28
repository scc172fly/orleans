namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 表示 ADO.NET 流提供程序中已存储消息的模型。
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
