namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 表示可确认消息的模型。
/// </summary>
internal record AdoNetStreamConfirmation(
    long MessageId,
    int Dequeued)
{
    public AdoNetStreamConfirmation() : this(0, 0)
    {
    }
}
