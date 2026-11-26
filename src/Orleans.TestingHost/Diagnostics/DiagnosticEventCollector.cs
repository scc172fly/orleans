using System.Collections.Concurrent;
using System.Diagnostics;

namespace Orleans.TestingHost.Diagnostics;

/// <summary>
/// 代表一个记录下来的诊断事件,带有其名称和有效载荷。
/// </summary>
/// <param name="Name">事件名称。</param>
/// <param name="Payload">事件有效载荷物体。</param>
/// <param name="Timestamp">事件被捕捉的时间。</param>
public readonly record struct DiagnosticEvent(
    string Name,
    object? Payload,
    DateTimeOffset Timestamp);

/// <summary>
/// 用于收集和等待诊断事件的测试工具。
/// 订阅 <see cref="DiagnosticListener.AllListeners"/> 并记录事件
/// 由符合指定模式的听众提供。
/// </summary>
/// <remarks>
/// 使用此类在测试中等待特定 Orleans 事件, 不使用
/// 依赖 Task.Delay 或 Thread.Sleep。
/// </remarks>
public sealed class DiagnosticEventCollector : IDisposable, IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private readonly ConcurrentQueue<DiagnosticEvent> _events = new();
    private readonly ConcurrentDictionary<string, List<EventWaiter>> _waiters = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly HashSet<string> _listenerPrefixes;
    private readonly IDisposable? _allListenersSubscription;
    private bool _disposed;

    /// <summary>
    /// 创建一个新的诊断事件收藏器, 订阅有名字的听众
    /// 开始于指定的前缀中的任何一处。
    /// </summary>
    /// <param name="listenerPrefixes">
    /// 订阅(e.g, "Orleans.")以记录所有 Orleans 事件)的听众姓名前缀。
    /// 如果为空,则订阅所有听众。
    /// </param>
    public DiagnosticEventCollector(IEnumerable<string>? listenerPrefixes = null)
    {
        _listenerPrefixes = listenerPrefixes?.ToHashSet() ?? [];
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    /// <summary>
    /// 创建一个新的诊断事件收藏器, 订阅有名字的听众
    /// 开始于指定的前缀中的任何一处。
    /// </summary>
    /// <param name="listenerPrefixes">
    /// 订阅(e.g, "Orleans.")以记录所有 Orleans 事件)的听众姓名前缀。
    /// 如果为空,则订阅所有听众。
    /// </param>
    public DiagnosticEventCollector(params string[] listenerPrefixes)
        : this((IEnumerable<string>)listenerPrefixes)
    {
    }

    /// <summary>
    /// 掌握了所有被俘获的诊断事件
    /// </summary>
    public IReadOnlyList<DiagnosticEvent> Events => [.. _events];

    /// <summary>
    /// 获取指定名称的事件 。
    /// </summary>
    public IEnumerable<DiagnosticEvent> GetEvents(string eventName)
        => _events.Where(e => e.Name == eventName);

    /// <summary>
    /// 获取指定名称的事件的统计。
    /// </summary>
    public int GetEventCount(string eventName)
        => _events.Count(e => e.Name == eventName);

    /// <summary>
    /// 清除所有被俘事件
    /// </summary>
    public void Clear()
    {
        _events.Clear();
    }

    /// <summary>
    /// 等待包含指定名称的事件被捕捉 。
    /// 如果已经捕获了使用此名称的事件, 立即返回 。
    /// </summary>
    /// <param name="eventName">等待的事件名称。</param>
    /// <param name="timeout">等待的最长时间。</param>
    /// <param name="cancellationToken">取消的符号。</param>
    /// <returns>被捕获事件。</returns>
    /// <exception cref="TimeoutException">如果事件未在超时内被记录, 丢弃。</exception>
    public async Task<DiagnosticEvent> WaitForEventAsync(
        string eventName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (TryGetEvent(eventName, static _ => true, out var existing))
        {
            return existing;
        }

        var waiter = CreateWaiter(eventName);

        try
        {
            if (TryGetEvent(eventName, static _ => true, out existing))
            {
                return existing;
            }

            return await waiter.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RemoveWaiter(eventName, waiter);
        }
    }

    /// <summary>
    /// 等待一个符合前提的指定名称的事件。
    /// </summary>
    /// <param name="eventName">等待的事件名称。</param>
    /// <param name="predicate">与事件有效载荷匹配的上游。</param>
    /// <param name="timeout">等待的最长时间。</param>
    /// <param name="cancellationToken">取消的符号。</param>
    /// <returns>被捕获事件。</returns>
    public async Task<DiagnosticEvent> WaitForEventAsync(
        string eventName,
        Func<DiagnosticEvent, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (TryGetEvent(eventName, predicate, out var existing))
        {
            return existing;
        }

        var waiter = CreateWaiter(eventName, predicate);
        try
        {
            if (TryGetEvent(eventName, predicate, out existing))
            {
                return existing;
            }

            return await waiter.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RemoveWaiter(eventName, waiter);
        }
    }

    /// <summary>
    /// 创建任务,在记录指定事件时完成。
    /// 与 WaitForEventAsync不同,此选项不检查现有事件。
    /// </summary>
    /// <param name="eventName">等待的事件名称。</param>
    /// <returns>任务完成源,将在事件被捕捉后完成。</returns>
    public TaskCompletionSource<DiagnosticEvent> CreateEventAwaiter(string eventName)
    {
        return CreateWaiter(eventName).Completion;
    }

    /// <summary>
    /// 等待至少捕捉到指定名称的事件数量 。
    /// </summary>
    /// <param name="eventName">要计算的事件名称。</param>
    /// <param name="expectedCount">预期活动的最低数量。</param>
    /// <param name="timeout">等待的最长时间。</param>
    /// <param name="cancellationToken">取消的符号。</param>
    /// <returns>所有记录的事件都以指定名称。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
    public async Task<IReadOnlyList<DiagnosticEvent>> WaitForEventCountAsync(
        string eventName,
        int expectedCount,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedCount, 1);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var currentCount = GetEventCount(eventName);
            if (currentCount >= expectedCount)
            {
                return GetEvents(eventName).ToList();
            }

            var waiter = CreateWaiter(eventName);

            try
            {
                currentCount = GetEventCount(eventName);
                if (currentCount >= expectedCount)
                {
                    return GetEvents(eventName).ToList();
                }

                await waiter.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting for {expectedCount} '{eventName}' events after {timeout}. Got {GetEventCount(eventName)} events.");
            }
            finally
            {
                RemoveWaiter(eventName, waiter);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"Timed out waiting for {expectedCount} '{eventName}' events after {timeout}. Got {GetEventCount(eventName)} events.");
    }

    private EventWaiter CreateWaiter(string eventName, Func<DiagnosticEvent, bool>? predicate = null)
    {
        var waiter = new EventWaiter(predicate);
        var waiters = _waiters.GetOrAdd(eventName, static _ => []);
        lock (waiters)
        {
            waiters.Add(waiter);
        }

        return waiter;
    }

    private void RemoveWaiter(string eventName, EventWaiter waiter)
    {
        if (!_waiters.TryGetValue(eventName, out var waiters))
        {
            return;
        }

        lock (waiters)
        {
            waiters.Remove(waiter);
        }
    }

    private bool TryGetEvent(string eventName, Func<DiagnosticEvent, bool> predicate, out DiagnosticEvent result)
    {
        result = _events.FirstOrDefault(e => e.Name == eventName && predicate(e));
        return result.Name is not null;
    }

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
    {
        // 如果未指定前缀, 订阅所有听众, 或者匹配的
        if (_listenerPrefixes.Count == 0 || _listenerPrefixes.Any(p => listener.Name.StartsWith(p, StringComparison.Ordinal)))
        {
            var subscription = listener.Subscribe(this);
            _subscriptions.Add(subscription);
        }
    }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> kvp)
    {
        var evt = new DiagnosticEvent(kvp.Key, kvp.Value, DateTimeOffset.UtcNow);
        _events.Enqueue(evt);

        // 信号 任何服务员
        if (_waiters.TryGetValue(kvp.Key, out var waiters))
        {
            List<EventWaiter> waitersSnapshot;
            lock (waiters)
            {
                waitersSnapshot = [.. waiters];
            }

            foreach (var waiter in waitersSnapshot)
            {
                if (waiter.TrySetResult(evt))
                {
                    RemoveWaiter(kvp.Key, waiter);
                }
            }
        }
    }

    void IObserver<DiagnosticListener>.OnError(Exception error) { }
    void IObserver<DiagnosticListener>.OnCompleted() { }
    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }
    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }

    private sealed class EventWaiter(Func<DiagnosticEvent, bool>? predicate)
    {
        private readonly Func<DiagnosticEvent, bool>? _predicate = predicate;

        public TaskCompletionSource<DiagnosticEvent> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DiagnosticEvent> Task => Completion.Task;

        public bool TrySetResult(DiagnosticEvent evt)
        {
            return _predicate is null || _predicate(evt) ? Completion.TrySetResult(evt) : false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _allListenersSubscription?.Dispose();
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
    }
}
