using Chirper.Grains.Models;

namespace Chirper.Grains;

[GenerateSerializer]
public record class ChirperAccountState
{
    /// <summary>
    /// 用户所跟随的出版商列表 。
    /// </summary>
    [Id(0)]
    public Dictionary<string, IChirperPublisher> Subscriptions { get; init; } = new();

    /// <summary>
    /// 跟随此用户的订阅者列表 。
    /// </summary>
    [Id(1)]
    public Dictionary<string, IChirperSubscriber> Followers { get; init; } = new();

    /// <summary>
    /// 此用户最近收到尖锐消息 。
    /// </summary>
    [Id(2)]
    public Queue<ChirperMessage> RecentReceivedMessages { get; init; } = new();

    /// <summary>
    /// 由此用户最近发布消息,
    /// </summary>
    [Id(3)]
    public Queue<ChirperMessage> MyPublishedMessages { get; init; } = new();
}
