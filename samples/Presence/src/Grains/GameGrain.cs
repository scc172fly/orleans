using Microsoft.Extensions.Logging;
using Presence.Grains.Models;

namespace Presence.Grains;

/// <summary>
/// 表示正在进行的游戏，并在内存中保存游戏状态。
/// 通知玩家 Grain 其对应玩家加入或离开游戏。
/// 向已订阅的观察者更新游戏进度。
/// </summary>
public class GameGrain : Grain, IGameGrain
{
    private readonly ILogger<GameGrain> _logger;
    private readonly HashSet<IGameObserver> _observers = new();
    private readonly HashSet<Guid> _players = new();
    private GameStatus _status = GameStatus.Empty;

    public GameGrain(ILogger<GameGrain> logger)
    {
        _logger = logger;
    }

    private Guid GrainKey => this.GetPrimaryKey();

    /// <summary>
    /// Presence Grain 调用此方法，使用最新状态更新游戏。
    /// </summary>
    public async Task UpdateGameStatusAsync(GameStatus status)
    {
        _status = status;

        // 检查自上次更新以来新加入的玩家。
        foreach (var player in _status.PlayerKeys)
        {
            if (!_players.Contains(player))
            {
                try
                {
                    // 此处串行调用玩家 Grain。与扇出调用相比，这样效率较低，但表达起来更简单。
                    await GrainFactory.GetGrain<IPlayerGrain>(player).JoinGameAsync(this.AsReference<IGameGrain>());
                    _players.Add(player);
                }
                catch (Exception error)
                {
                    // 通知玩家 Grain 加入游戏时，忽略产生的异常。
                    // 由于尚未将该玩家加入列表，下次更新时会重试此操作。
                    _logger.LogWarning(error, "Failed to tell player {PlayerKey} to join game {GameKey}", player, GrainKey);
                }
            }
        }

        // 检查自上次更新以来离开的玩家。
        var promises = new List<(Guid PlayerKey, Task Task)>();
        foreach (var player in _players)
        {
            if (!_status.PlayerKeys.Contains(player))
            {
                // 此处使用扇出调用，并行发出多个调用，随后统一等待这些异步操作。
                // 虽然需要编写更多代码，但调用多个玩家 Grain 时延迟更低。
                promises.Add((player, GrainFactory.GetGrain<IPlayerGrain>(player).LeaveGameAsync(this.AsReference<IGameGrain>())));
            }
        }

        // 统一等待异步操作完成。
        foreach (var (playerKey, task) in promises)
        {
            try
            {
                await task;
                _players.Remove(playerKey);
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "Failed to tell player {PlayerKey} to leave the game {GameKey}", playerKey, GrainKey);
            }
        }

        // 将最新游戏分数通知观察者。
        List<IGameObserver> failed = null!;
        foreach (var observer in _observers)
        {
            try
            {
                observer.UpdateGameScore(_status.Score);
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "Failed to notify observer {ObserverKey} of score for game {GameKey}. Removing observer.", observer.GetPrimaryKey(), GrainKey);

                // 将通知失败的观察者加入失败列表。
                // 但仅在需要时创建失败列表，避免每次调用都产生内存分配。
                failed ??= new();
                failed.Add(observer);
            }
        }

        // 移除失效的观察者。
        if (failed is not null)
        {
            foreach (var observer in failed)
            {
                _observers.Remove(observer);
            }
        }

        return;
    }

    public Task ObserveGameUpdatesAsync(IGameObserver observer)
    {
        _observers.Add(observer);
        return Task.CompletedTask;
    }

    public Task UnobserveGameUpdatesAsync(IGameObserver observer)
    {
        _observers.Remove(observer);
        return Task.CompletedTask;
    }
}
