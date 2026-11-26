namespace Presence.Grains;

/// <summary>
/// 与个别玩家的接口,该玩家在任何时间点上都可能或不会出现在游戏中。
/// </summary>
public interface IPlayerGrain : IGrainWithGuidKey
{
    Task<IGameGrain?> GetCurrentGameAsync();
    Task JoinGameAsync(IGameGrain game);
    Task LeaveGameAsync(IGameGrain game);
}
