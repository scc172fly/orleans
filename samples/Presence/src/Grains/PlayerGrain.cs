using Microsoft.Extensions.Logging;

namespace Presence.Grains;

/// <summary>
/// 代表任何时间点可能或不会出现在游戏中的单个玩家。
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
    /// 玩家当前正在游戏中。 可能为空 。
    /// </summary>
    public Task<IGameGrain?> GetCurrentGameAsync() => Task.FromResult(_currentGame);

    /// <summary>
    /// 游戏 grain 调用此方法通知玩家已加入游戏 。
    /// </summary>
    public Task JoinGameAsync(IGameGrain game)
    {
        _currentGame = game;
        _logger.LogInformation("Player {PlayerKey} joined game {GameKey}", GrainKey, game.GetPrimaryKey());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 游戏 grain 调用此方法通知玩家已离开游戏 。
    /// </summary>
    public Task LeaveGameAsync(IGameGrain game)
    {
        _currentGame = null;
        _logger.LogInformation("Player {PlayerKey} left game {GameKey}", GrainKey, game.GetPrimaryKey());
        return Task.CompletedTask;
    }
}
