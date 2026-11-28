namespace Chirper.Grains.Models;

/// <summary>
/// 表示单条 Chirp 消息记录的数据对象。
/// </summary>
/// <param name="Message">此 Chirp 消息条目的内容。</param>
/// <param name="Timestamp">此 Chirp 消息条目最初被重新发布时的时间戳。</param>
/// <param name="PublisherUserName">此 Chirp 消息发布者的用户名。</param>
[GenerateSerializer]
public record class ChirperMessage(
    [property: Id(0)] string Message,
    [property: Id(1)] DateTimeOffset Timestamp,
    [property: Id(2)] string PublisherUserName)
{
    /// <summary>
    /// 此 Chirp 消息的唯一 ID。
    /// </summary>
    [Id(3)]
    public Guid MessageId { get; } = Guid.NewGuid();

    /// <summary>
    /// 返回此消息的字符串表示形式。
    /// </summary>
    public override string ToString() =>
        $"Chirp: '{Message}' from @{PublisherUserName} at {Timestamp}";
}
