using Microsoft.Extensions.Logging;
using Presence.Grains;

namespace Presence.PlayerWatcher;

/// <summary>
/// 执行给给定日志的输出通知 <see cref="IGameObserver"/> 。
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
