using Microsoft.Extensions.Logging;
using Presence.Grains.Models;

namespace Presence.Grains;

/// <summary>
/// 代表游戏正在进行中, 并保持游戏状态的记忆 。
/// 通知玩家加入和退出游戏的玩家 grains 。
/// 最新被订阅的观察员关于游戏进展的最新情况。
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
    /// 预设 grain 调用此方法来更新游戏的最新状态 。
    /// </summary>
    public async Task UpdateGameStatusAsync(GameStatus status)
    {
        _status = status;

        // 检查上次更新后加入的新玩家
        foreach (var player in _status.PlayerKeys)
        {
            if (!_players.Contains(player))
            {
                try
                {
                    // 在这里我们连号调用玩家 grains , 效率比扇子外出低, 但表达简单。
                    await GrainFactory.GetGrain<IPlayerGrain>(player).JoinGameAsync(this.AsReference<IGameGrain>());
                    _players.Add(player);
                }
                catch (Exception error)
                {
                    // 通知玩家加入游戏时忽略例外 grains 。
                    // 既然我们没有将玩家添加到列表中, 下次更新时还会再试一次 。
                    _logger.LogWarning(error, "Failed to tell player {PlayerKey} to join game {GameKey}", player, GrainKey);
                }
            }
        }

        // 检查上次更新后退出游戏的玩家
        var promises = new List<(Guid PlayerKey, Task Task)>();
        foreach (var player in _players)
        {
            if (!_status.PlayerKeys.Contains(player))
            {
                // 我们在这里做一个大动作,同时发出多个电话。我们稍后加入承诺。
                // 更多要写入的代码, 但是在调用多个玩家时, 我们得到较低的延迟度 grains。
                promises.Add((player, GrainFactory.GetGrain<IPlayerGrain>(player).LeaveGameAsync(this.AsReference<IGameGrain>())));
            }
        }

        // 共同承诺
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

        // 通知观察者最新游戏分数
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

                // 将观察员添加到失败列表中
                // 但是,将清单编制推迟至必要的时间,以避免每次通话都分配款项时,但未能完成
                failed ??= new();
                failed.Add(observer);
            }
        }

        // 删除死观察者
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
