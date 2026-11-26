using Chirper.Grains.Models;

namespace Chirper.Grains;

/// <summary>
/// Orleans 观察员接口 IChirperSubscriber。
/// </summary>
public interface IChirperSubscriber : IGrainWithStringKey
{
    /// <summary>
    /// 收到新的通知。
    /// </summary>
    /// <param name="chirp">尖锐的邮件条目。</param>
    Task NewChirpAsync(ChirperMessage chirp);
}
