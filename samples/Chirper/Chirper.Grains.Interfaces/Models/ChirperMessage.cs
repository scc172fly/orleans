namespace Chirper.Grains.Models;

/// <summary>
/// 代表一个千位消息条目的数据对象
/// </summary>
/// <param name="Message">此正弦信件条目的电文内容 。</param>
/// <param name="Timestamp">最初重发此正声电文条目的时间戳。</param>
/// <param name="PublisherUserName">发送此正弦电文的出版商的用户名。</param>
[GenerateSerializer]
public record class ChirperMessage(
    [property: Id(0)] string Message,
    [property: Id(1)] DateTimeOffset Timestamp,
    [property: Id(2)] string PublisherUserName)
{
    /// <summary>
    /// 这份声频信息的独特代号
    /// </summary>
    [Id(3)]
    public Guid MessageId { get; } = Guid.NewGuid();

    /// <summary>
    /// 返回此信件的字符串表示。
    /// </summary>
    public override string ToString() =>
        $"Chirp: '{Message}' from @{PublisherUserName} at {Timestamp}";
}
