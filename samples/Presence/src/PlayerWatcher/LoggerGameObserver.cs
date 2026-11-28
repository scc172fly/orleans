using Microsoft.Extensions.Logging;
using Presence.Grains;

namespace Presence.PlayerWatcher;

/// <summary>
/// 实现 <see cref="IGameObserver"/>，将通知输出到指定日志记录器。
/// </summary>
public class LoggerGameObserver : IGameObserver
{
    private readonly ILogger<LoggerGameObserver> _logger;

    public LoggerGameObserver(ILogger<LoggerGameObserver> logger)
    {
        _logger = logger;
    }

    public void UpdateGameScore(string score) =>
        _logger.LogInformation("New game score: {GameScore}", score);
}
