using Presence.Grains.Models;

namespace Presence.Grains;

/// <summary>
/// 与特定游戏实例的接口, 该游戏有其自身的地位和玩家列表 。
/// </summary>
public interface IGameGrain : IGrainWithGuidKey
{
    Task UpdateGameStatusAsync(GameStatus status);
    Task ObserveGameUpdatesAsync(IGameObserver observer);
    Task UnobserveGameUpdatesAsync(IGameObserver observer);
}
