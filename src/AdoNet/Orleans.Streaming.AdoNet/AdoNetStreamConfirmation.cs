namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 代表一个可以确认的信息的模型。
/// </summary>
internal record AdoNetStreamConfirmation(
    long MessageId,
    int Dequeued)
{
    public AdoNetStreamConfirmation() : this(0, 0)
    {
    }
}
