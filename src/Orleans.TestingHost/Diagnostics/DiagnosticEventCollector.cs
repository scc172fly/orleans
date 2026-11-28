using System.Collections.Concurrent;
using System.Diagnostics;

namespace Orleans.TestingHost.Diagnostics;

/// <summary>
/// 表示捕获的诊断事件，包含其名称和有效载荷。
/// </summary>
/// <param name="Name">事件名称。</param>
/// <param name="Payload">事件有效载荷对象。</param>
/// <param name="Timestamp">捕获事件的时间。</param>
public readonly record struct DiagnosticEvent(
    string Name,
    object? Payload,
    DateTimeOffset Timestamp);

/// <summary>
/// 用于收集和等待诊断事件的测试实用工具。
/// 订阅 <see cref="DiagnosticListener.AllListeners"/>，并捕获
/// 与指定模式匹配的监听器产生的事件。
/// </summary>
/// <remarks>
/// 使用此类在测试中等待特定 Orleans 事件，而无需
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
    /// 创建新的诊断事件收集器，订阅名称
    /// 以任一指定前缀开头的监听器。
    /// </summary>
    /// <param name="listenerPrefixes">
    /// 要订阅的监听器名称前缀（例如使用 "Orleans." 捕获所有 Orleans 事件）。
    /// 如果为空，则订阅所有监听器。
    /// </param>
    public DiagnosticEventCollector(IEnumerable<string>? listenerPrefixes = null)
    {
        _listenerPrefixes = listenerPrefixes?.ToHashSet() ?? [];
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    /// <summary>
    /// 创建新的诊断事件收集器，订阅名称
    /// 以任一指定前缀开头的监听器。
    /// </summary>
    /// <param name="listenerPrefixes">
    /// 要订阅的监听器名称前缀（例如使用 "Orleans." 捕获所有 Orleans 事件）。
    /// 如果为空，则订阅所有监听器。
    /// </param>
    public DiagnosticEventCollector(params string[] listenerPrefixes)
        : this((IEnumerable<string>)listenerPrefixes)
    {
    }

    /// <summary>
    /// 获取所有已捕获的诊断事件。
    /// </summary>
    public IReadOnlyList<DiagnosticEvent> Events => [.. _events];

    /// <summary>
    /// 获取具有指定名称的事件。
    /// </summary>
    public IEnumerable<DiagnosticEvent> GetEvents(string eventName)
        => _events.Where(e => e.Name == eventName);

    /// <summary>
    /// 获取具有指定名称的事件数量。
    /// </summary>
    public int GetEventCount(string eventName)
        => _events.Count(e => e.Name == eventName);

    /// <summary>
    /// 清除所有已捕获的事件。
    /// </summary>
    public void Clear()
    {
        _events.Clear();
    }

    /// <summary>
    /// 等待捕获具有指定名称的事件。
    /// 如果已捕获该名称的事件，则立即返回。
    /// </summary>
    /// <param name="eventName">要等待的事件名称。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已捕获的事件。</returns>
    /// <exception cref="TimeoutException">在超时时限内未捕获事件时抛出。</exception>
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
    /// 等待具有指定名称且与谓词匹配的事件。
    /// </summary>
    /// <param name="eventName">要等待的事件名称。</param>
    /// <param name="predicate">用于匹配事件有效载荷的谓词。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已捕获的事件。</returns>
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
    /// 创建一个任务，在捕获指定事件时完成。
    /// 与 WaitForEventAsync 不同，此方法不检查已存在的事件。
    /// </summary>
    /// <param name="eventName">要等待的事件名称。</param>
    /// <returns>任务完成源，在捕获事件时将其标记为完成。</returns>
    public TaskCompletionSource<DiagnosticEvent> CreateEventAwaiter(string eventName)
    {
        return CreateWaiter(eventName).Completion;
    }

    /// <summary>
    /// 等待捕获具有指定名称的事件，直到至少达到指定数量。
    /// </summary>
    /// <param name="eventName">要计数的事件名称。</param>
    /// <param name="expectedCount">预期的最小事件数量。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>具有指定名称的全部已捕获事件。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
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
        // 如果未指定前缀，则订阅所有监听器；否则订阅匹配的监听器。
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

        // 通知所有等待方。
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
