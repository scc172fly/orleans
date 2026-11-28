using Microsoft.Extensions.Logging;

namespace Orleans.Durable.Tasks;

/// <summary>提供使持久化任务的每个事件只报告一次的日志记录功能。</summary>
public static class DurableTaskLoggerExtensions
{
    /// <summary>
    /// 返回向 <paramref name="logger"/> 写入的日志记录器，但调用代码正在重放持久化任务的
    /// 已记录操作期间除外。
    /// </summary>
    /// <param name="logger">接收日志的记录器。</param>
    /// <returns>当 <see cref="DurableTaskContext.IsReplaying"/> 为 <see langword="true"/> 时跳过事件的日志记录器。</returns>
    /// <remarks>
    /// 日志记录器在记录每个事件时读取正在运行的任务，因此 Grain 可以缓存一个记录器，并同时在持久化任务
    /// 主体与普通方法中使用；在持久化任务之外，它会记录所有事件。任务重放期间，它报告
    /// 日志记录未启用，因此源生成的日志方法不会格式化消息。日志作用域始终在下述日志记录器上启动：
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
