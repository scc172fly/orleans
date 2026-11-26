using Microsoft.Extensions.Logging;

namespace Orleans.Durable.Tasks;

/// <summary>提供记录,报告每次持久任务事件一次。</summary>
public static class DurableTaskLoggerExtensions
{
    /// <summary>
    /// 返回一个向 <paramref name="logger"/> 写入的对数日志,但调用代码重弹耐久任务时除外
    /// 记录的业务。
    /// </summary>
    /// <param name="logger">要写入的日志器。</param>
    /// <returns>日志, 它在 <see cref="DurableTaskContext.IsReplaying"/> 是 <see langword="true"/>时跳过事件。</returns>
    /// <remarks>
    /// 日志器读取每次事件被记录时运行的任务, 所以一个 grain 缓存一个任务并从耐久任务中使用它
    /// 无论是机构还是普通的方法,除了持久的任务之外,它写每件事件。虽然任务重放,但它报告
    /// 无法启用, 所以源生成的记录方法会跳过格式化信息。 范围总是从
    /// <paramref name="logger"/>.
    /// </remarks>
    public static ILogger ReplaySafe(this ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger as ReplaySafeLogger ?? new ReplaySafeLogger(logger);
    }

    private sealed class ReplaySafeLogger(ILogger logger) : ILogger
    {
        private static bool IsReplaying => DurableTaskContext.Ambient is { IsReplaying: true };

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => logger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => !IsReplaying && logger.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsReplaying)
            {
                logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
