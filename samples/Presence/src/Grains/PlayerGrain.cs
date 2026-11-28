using Microsoft.Extensions.Logging;

namespace Presence.Grains;

/// <summary>
/// 表示单个玩家，玩家在任意时刻可能正在参加游戏，也可能没有参加游戏。
/// </summary>
public class PlayerGrain : Grain, IPlayerGrain
{
    private readonly ILogger<PlayerGrain> _logger;
    private IGameGrain? _currentGame;

    public PlayerGrain(ILogger<PlayerGrain> logger)
    {
        _logger = logger;
    }

    private Guid GrainKey => this.GetPrimaryKey();

    /// <summary>
    /// 玩家当前参加的游戏，可能为 null。
    /// </summary>
    public Task<IGameGrain?> GetCurrentGameAsync() => Task.FromResult(_currentGame);

    /// <summary>
    /// 游戏 Grain 调用此方法，通知玩家已加入游戏。
    /// </summary>
    public Task JoinGameAsync(IGameGrain game)
    {
        _currentGame = game;
        _logger.LogInformation("Player {PlayerKey} joined game {GameKey}", GrainKey, game.GetPrimaryKey());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 游戏 Grain 调用此方法，通知玩家已离开游戏。
    /// </summary>
    public Task LeaveGameAsync(IGameGrain game)
    {
        _currentGame = null;
        _logger.LogInformation("Player {PlayerKey} left game {GameKey}", GrainKey, game.GetPrimaryKey());
        return Task.CompletedTask;
    }
}
