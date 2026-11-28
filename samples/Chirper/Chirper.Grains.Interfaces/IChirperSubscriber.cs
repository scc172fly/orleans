using Chirper.Grains.Models;

namespace Chirper.Grains;

/// <summary>
/// Orleans 观察者接口 IChirperSubscriber。
/// </summary>
public interface IChirperSubscriber : IGrainWithStringKey
{
    /// <summary>
    /// 通知已收到新的 Chirp 消息。
    /// </summary>
    /// <param name="chirp">Chirp 消息条目。</param>
    Task NewChirpAsync(ChirperMessage chirp);
}
