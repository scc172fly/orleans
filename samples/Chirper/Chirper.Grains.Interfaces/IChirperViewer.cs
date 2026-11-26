using Chirper.Grains.Models;

namespace Chirper.Grains;

/// <summary>
/// <see cref="IChirperAccount"/> 实例观察员的接口。
/// </summary>
public interface IChirperViewer : IGrainObserver
{
    /// <summary>
    /// 通知某一账户已公布消息。
    /// 这是记述或记述。
    /// </summary>
    void NewChirp(ChirperMessage message);

    /// <summary>
    /// 通知账户已添加订阅费。
    /// </summary>
    void SubscriptionAdded(string username);

    /// <summary>
    /// 通知账户已删除订阅。
    /// </summary>
    void SubscriptionRemoved(string username);

    void NewFollower(string username);
}
