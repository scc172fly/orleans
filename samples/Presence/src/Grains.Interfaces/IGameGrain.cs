using Presence.Grains.Models;

namespace Presence.Grains;

/// <summary>
/// 某个具体游戏实例的接口，该实例有自己的状态和玩家列表。
/// </summary>
public interface IGameGrain : IGrainWithGuidKey
{
    Task UpdateGameStatusAsync(GameStatus status);
    Task ObserveGameUpdatesAsync(IGameObserver observer);
    Task UnobserveGameUpdatesAsync(IGameObserver observer);
}
