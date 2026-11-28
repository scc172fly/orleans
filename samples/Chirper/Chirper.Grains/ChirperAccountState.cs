using Chirper.Grains.Models;

namespace Chirper.Grains;

[GenerateSerializer]
public record class ChirperAccountState
{
    /// <summary>
    /// 此用户正在关注的发布者列表。
    /// </summary>
    [Id(0)]
    public Dictionary<string, IChirperPublisher> Subscriptions { get; init; } = new();

    /// <summary>
    /// 正在关注此用户的订阅者列表。
    /// </summary>
    [Id(1)]
    public Dictionary<string, IChirperSubscriber> Followers { get; init; } = new();

    /// <summary>
    /// 此用户最近收到的 Chirp 消息。
    /// </summary>
    [Id(2)]
    public Queue<ChirperMessage> RecentReceivedMessages { get; init; } = new();

    /// <summary>
    /// 此用户最近发布的 Chirp 消息。
    /// </summary>
    [Id(3)]
    public Queue<ChirperMessage> MyPublishedMessages { get; init; } = new();
}
