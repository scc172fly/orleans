using Chirper.Grains.Models;

namespace Chirper.Grains;

/// <summary>
/// 用于观察 <see cref="IChirperAccount"/> 实例的观察者接口。
/// </summary>
public interface IChirperViewer : IGrainObserver
{
    /// <summary>
    /// 通知某个账户已发布消息。
    /// 该账户可能是正在观察的账户，也可能是该账户所关注的账户。
    /// </summary>
    void NewChirp(ChirperMessage message);

    /// <summary>
    /// 通知该账户已添加一项订阅。
    /// </summary>
    void SubscriptionAdded(string username);

    /// <summary>
    /// 通知该账户已移除一项订阅。
    /// </summary>
    void SubscriptionRemoved(string username);

    void NewFollower(string username);
}
