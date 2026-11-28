using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Orleans.TestingHost.Logging;

/// <summary>
/// 所有 <see cref="InMemoryLogger"/> 实例共享的内存日志缓冲区。
/// 适用于需要捕获并检查日志的模拟测试。
/// </summary>
/// <remarks>
/// 创建新的内存日志缓冲区。
/// </remarks>
public sealed class InMemoryLogBuffer
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>
    /// 获取当前缓冲的全部日志条目。
    /// </summary>
    public IReadOnlyList<LogEntry> AllEntries => [.. _entries];

    /// <summary>
    /// 获取按最低日志级别筛选后的日志条目。
    /// </summary>
    public IEnumerable<LogEntry> GetEntries(LogLevel minimumLevel) => _entries.Where(e => e.LogLevel >= minimumLevel);

    /// <summary>
    /// 如果存在达到或超过指定日志级别的任何条目，则返回 true。
    /// </summary>
    public bool HasEntriesAtOrAbove(LogLevel level) => _entries.Any(e => e.LogLevel >= level);

    /// <summary>
    /// 如果存在任何警告或错误条目，则返回 true。
    /// </summary>
    public bool HasWarningsOrErrors => HasEntriesAtOrAbove(LogLevel.Warning);

    /// <summary>
    /// 将消息记录到缓冲区。
    /// </summary>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter,
        string category)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var message = formatter(state, exception);
        var entry = new LogEntry(
            DateTimeOffset.UtcNow,
            logLevel,
            category,
            eventId,
            message,
            exception)
        {
            ThreadId = Environment.CurrentManagedThreadId
        };
        _entries.Enqueue(entry);
    }

    /// <summary>
    /// 清除全部缓冲的日志条目。
    /// </summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// 将所有日志条目格式化为字符串。
    /// </summary>
    public string FormatAllEntries() => FormatEntries(_entries);

    /// <summary>
    /// 将达到或超过指定最低级别的日志条目格式化为字符串。
    /// </summary>
    public string FormatEntries(LogLevel minimumLevel) => FormatEntries(GetEntries(minimumLevel));

    /// <summary>
    /// 获取全部格式化日志条目的近似字节大小。
    /// </summary>
    public long ApproximateSizeBytes
    {
        get
        {
            // 根据条目估算大小。这是近似值，但可以避免
            // 仅为检查大小而格式化所有条目。
            long size = 0;
            foreach (var entry in _entries)
            {
                // 估算值：时间戳(30) + 级别(10) + 类别(50) + 消息 + 异常。
                size += 90 + (entry.Message?.Length ?? 0) * 2; // UTF-16 字符。
                if (entry.Exception != null)
                {
                    size += 500; // 异常文本的粗略估算值。
                }
            }

            return size;
        }
    }

    /// <summary>
    /// 格式化实际条目，同时返回格式化后的字符串及其字节大小。
    /// </summary>
    public (string Content, long SizeBytes) FormatAllEntriesWithSize()
    {
        var content = FormatAllEntries();
        var sizeBytes = Encoding.UTF8.GetByteCount(content);
        return (content, sizeBytes);
    }

    /// <summary>
    /// 格式化达到或超过指定级别的条目，同时返回格式化后的字符串及其字节大小。
    /// </summary>
    public (string Content, long SizeBytes) FormatEntriesWithSize(LogLevel minimumLevel)
    {
        var content = FormatEntries(minimumLevel);
        var sizeBytes = Encoding.UTF8.GetByteCount(content);
        return (content, sizeBytes);
    }

    /// <summary>
    /// 断言没有记录警告或错误；如果存在，则抛出异常。
    /// </summary>
    /// <exception cref="InvalidOperationException">日志中存在警告或错误时抛出。</exception>
    public void AssertNoWarningsOrErrors()
    {
        var issues = GetEntries(LogLevel.Warning).ToList();
        if (issues.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Found {issues.Count} warnings/errors:");
            foreach (var entry in issues.Take(10))
            {
                sb.AppendLine(FormatEntry(entry));
            }
            if (issues.Count > 10)
            {
                sb.AppendLine($"... and {issues.Count - 10} more.");
            }
            throw new InvalidOperationException(sb.ToString());
        }
    }

    private static string FormatEntries(IEnumerable<LogEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            sb.AppendLine(FormatEntry(entry));
        }

        return sb.ToString();
    }

    private static string FormatEntry(LogEntry entry)
    {
        var levelStr = entry.LogLevel switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FAIL",
            LogLevel.Critical => "CRIT",
            _ => "NONE",
        };

        var prefix = entry.LogLevel == LogLevel.Error ? "!!!!!!!!!! " : "";
        var exc = entry.Exception != null ? $"\n{PrintException(entry.Exception)}" : "";

        return string.Create(CultureInfo.InvariantCulture, $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} {entry.ThreadId}\t{levelStr}\t{entry.EventId}\t{entry.Category}]\t{prefix}{entry.Message}{exc}");
    }

    private static string PrintException(Exception? exception)
    {
        if (exception == null)
            return "";

        var sb = new StringBuilder();
        PrintException(sb, exception, 0);
        return sb.ToString();
    }

    private static void PrintException(StringBuilder sb, Exception exception, int level)
    {
        if (exception == null) return;

        sb.Append(CultureInfo.InvariantCulture, $"Exc level {level}: {exception.GetType()}: {exception.Message}");

        if (exception.StackTrace is { } stack)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}{stack}");
        }

        if (exception is ReflectionTypeLoadException typeLoadException)
        {
            var loaderExceptions = typeLoadException.LoaderExceptions;
            if (loaderExceptions == null || loaderExceptions.Length == 0)
            {
                sb.Append("No LoaderExceptions found");
            }
            else
            {
                foreach (var inner in loaderExceptions)
                {
                    if (inner is not null)
                    {
                        PrintException(sb, inner, level + 1);
                    }
                }
            }
        }
        else if (exception.InnerException != null)
        {
            if (exception is AggregateException { InnerExceptions: { Count: > 1 } innerExceptions })
            {
                foreach (var inner in innerExceptions)
                {
                    PrintException(sb, inner, level + 1);
                }
            }
            else
            {
                PrintException(sb, exception.InnerException, level + 1);
            }
        }
    }
}

/// <summary>
/// 将日志消息缓冲在内存中以便稍后检索的日志提供程序。
/// 适用于需要有条件地将日志附加到测试结果的测试。
/// </summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly bool _ownsBuffer;
    private bool _disposed;

    /// <summary>
    /// 创建拥有自己缓冲区的新内存日志提供程序。
    /// </summary>
    public InMemoryLoggerProvider()
    {
        Buffer = new InMemoryLogBuffer();
        _ownsBuffer = true;
    }

    /// <summary>
    /// 创建写入共享缓冲区的新内存日志提供程序。
    /// 多个提供程序可以共享同一缓冲区，以聚合多个宿主的日志。
    /// </summary>
    /// <param name="sharedBuffer">日志写入的共享缓冲区。</param>
    public InMemoryLoggerProvider(InMemoryLogBuffer sharedBuffer)
    {
        ArgumentNullException.ThrowIfNull(sharedBuffer);
        Buffer = sharedBuffer;
        _ownsBuffer = false;
    }

    /// <summary>
    /// 获取包含全部已记录条目的日志缓冲区。
    /// </summary>
    public InMemoryLogBuffer Buffer { get; }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, Buffer);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 仅在拥有缓冲区时清空它。
        if (_ownsBuffer)
        {
            Buffer.Clear();
        }
    }
}

/// <summary>
/// 通过 InMemoryLogBuffer 写入内存缓冲区的日志记录器。
/// </summary>
public sealed class InMemoryLogger(string categoryName, InMemoryLogBuffer buffer) : ILogger
{
    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => NullScope.Instance;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        buffer.Log(logLevel, eventId, state, exception, formatter, categoryName);
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// 表示单个日志条目及其关联元数据。
/// </summary>
public readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogLevel LogLevel,
    string Category,
    EventId EventId,
    string Message,
    Exception? Exception)
{
    internal int ThreadId { get; init; }
}
