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
/// 所有 <see cref="InMemoryLogger"/> 例数共享的模拟日志缓冲 。
/// 可用于模拟测试,因为需要采集和检查日志。
/// </summary>
/// <remarks>
/// 创建一个新的记忆中日志缓冲。
/// </remarks>
public sealed class InMemoryLogBuffer
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>
    /// 获取当前缓冲的所有日志条目 。
    /// </summary>
    public IReadOnlyList<LogEntry> AllEntries => [.. _entries];

    /// <summary>
    /// 获取通过最低日志水平过滤的日志条目 。
    /// </summary>
    public IEnumerable<LogEntry> GetEntries(LogLevel minimumLevel) => _entries.Where(e => e.LogLevel >= minimumLevel);

    /// <summary>
    /// 如果在指定的日志级别上或以上存在任何条目,则返回真实 。
    /// </summary>
    public bool HasEntriesAtOrAbove(LogLevel level) => _entries.Any(e => e.LogLevel >= level);

    /// <summary>
    /// 如果存在任何警告或错误条目, 返回 True 。
    /// </summary>
    public bool HasWarningsOrErrors => HasEntriesAtOrAbove(LogLevel.Warning);

    /// <summary>
    /// 记录缓冲线的一条消息。
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
    /// 清除所有缓冲日志条目。
    /// </summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// 将所有日志条目格式化为字符串。
    /// </summary>
    public string FormatAllEntries() => FormatEntries(_entries);

    /// <summary>
    /// 将指定最低级别上或以上的日志条目格式化为字符串。
    /// </summary>
    public string FormatEntries(LogLevel minimumLevel) => FormatEntries(GetEntries(minimumLevel));

    /// <summary>
    /// 在所有格式化日志条目中以字节获取大约大小。
    /// </summary>
    public long ApproximateSizeBytes
    {
        get
        {
            // 基于条目的估算大小 - 这是近似, 但避免
            // 格式化所有条目以检查大小
            long size = 0;
            foreach (var entry in _entries)
            {
                // 估计:时间戳(30)+级别(10)+类别(50)+信息+例外
                size += 90 + (entry.Message?.Length ?? 0) * 2; // UTF-16 字符
                if (entry.Exception != null)
                {
                    size += 500; // 例外案文粗略估计
                }
            }

            return size;
        }
    }

    /// <summary>
    /// 格式化实际条目并返回格式化字符串及其字节大小。
    /// </summary>
    public (string Content, long SizeBytes) FormatAllEntriesWithSize()
    {
        var content = FormatAllEntries();
        var sizeBytes = Encoding.UTF8.GetByteCount(content);
        return (content, sizeBytes);
    }

    /// <summary>
    /// 将条目格式化在指定级别或以上,并返回格式化的字符串及其字节大小。
    /// </summary>
    public (string Content, long SizeBytes) FormatEntriesWithSize(LogLevel minimumLevel)
    {
        var content = FormatEntries(minimumLevel);
        var sizeBytes = Encoding.UTF8.GetByteCount(content);
        return (content, sizeBytes);
    }

    /// <summary>
    /// 声称没有记录任何警告或错误,如果有例外的话,则予以排除。
    /// </summary>
    /// <exception cref="InvalidOperationException">当日志中存在警告或错误时丢弃。</exception>
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
/// 缓冲记录信息在模拟中进行记录以便日后检索的 Aloger 提供商。
/// 可用于试验,因为试验结果必须附带附日志。
/// </summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly bool _ownsBuffer;
    private bool _disposed;

    /// <summary>
    /// 创建新的有其自身缓冲的模拟登录器供应商。
    /// </summary>
    public InMemoryLoggerProvider()
    {
        Buffer = new InMemoryLogBuffer();
        _ownsBuffer = true;
    }

    /// <summary>
    /// 创建一个新的同名记录器供应商,该服务器可以写入共享缓冲。
    /// 多个提供者可以共享来自多个主机的日志集存的相同缓冲 。
    /// </summary>
    /// <param name="sharedBuffer">用于写入日志的共享缓冲。</param>
    public InMemoryLoggerProvider(InMemoryLogBuffer sharedBuffer)
    {
        ArgumentNullException.ThrowIfNull(sharedBuffer);
        Buffer = sharedBuffer;
        _ownsBuffer = false;
    }

    /// <summary>
    /// 获取包含所有登录条目的日志缓冲。
    /// </summary>
    public InMemoryLogBuffer Buffer { get; }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, Buffer);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 只有拥有缓冲带才能清除
        if (_ownsBuffer)
        {
            Buffer.Clear();
        }
    }
}

/// <summary>
/// 正在通过 InMemoryLogBuffer向一个模拟缓冲器写字的 Alogger 。
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
/// 代表与其相关元数据单项日志条目。
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
