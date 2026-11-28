namespace Presence.Grains;

/// <summary>
/// 单个玩家的接口，玩家在任意时刻可能正在参加游戏，也可能没有参加游戏。
/// </summary>
public interface IPlayerGrain : IGrainWithGuidKey
{
    Task<IGameGrain?> GetCurrentGameAsync();
    Task JoinGameAsync(IGameGrain game);
    Task LeaveGameAsync(IGameGrain game);
}
