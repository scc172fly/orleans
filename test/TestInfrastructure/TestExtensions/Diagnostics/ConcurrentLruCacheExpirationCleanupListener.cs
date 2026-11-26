using Orleans.Caching;

namespace TestExtensions;

/// <summary>
/// 订阅 <see cref="ConcurrentLruCache{K, V}"/> 过期清理事件的测试助手。
/// </summary>
public sealed class ConcurrentLruCacheExpirationCleanupListener : IDisposable
{
    private readonly object _targetCache;
    private readonly IDisposable _subscription;
    private readonly Queue<int> _events = [];
    private readonly Queue<TaskCompletionSource<int>> _waiters = [];

    /// <summary>
    /// 初始化 <see cref="ConcurrentLruCacheExpirationCleanupListener"/> 类的新实例。
    /// </summary>
    /// <param name="targetCache">要收听的缓存 。</param>
    public ConcurrentLruCacheExpirationCleanupListener(object targetCache)
    {
        _targetCache = targetCache;
        _subscription = ConcurrentLruCacheDiagnostics.ExpiredItemsRemovedEvents.Subscribe(new Observer(this));
    }

    /// <summary>
    /// 等待下一次到期清理事件。
    /// </summary>
    /// <param name="timeout">等待的最长时间。 默认为 10 秒 。</param>
    /// <returns>清理后移除的项目数量。</returns>
    public Task<int> WaitForCleanupAsync(TimeSpan? timeout = null)
    {
        lock (_events)
        {
            if (_events.TryDequeue(out var removedCount))
            {
                return Task.FromResult(removedCount);
            }

            var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            return waiter.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(10));
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _subscription.Dispose();

    private void OnNext(ConcurrentLruCacheDiagnostics.ExpiredItemsRemoved value)
    {
        if (!ReferenceEquals(value.Cache, _targetCache))
        {
            return;
        }

        TaskCompletionSource<int> waiter;
        lock (_events)
        {
            if (_waiters.TryDequeue(out var existingWaiter))
            {
                waiter = existingWaiter;
            }
            else
            {
                _events.Enqueue(value.RemovedCount);
                return;
            }
        }

        waiter.SetResult(value.RemovedCount);
    }

    private sealed class Observer(ConcurrentLruCacheExpirationCleanupListener listener) : IObserver<ConcurrentLruCacheDiagnostics.ExpiredItemsRemoved>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(ConcurrentLruCacheDiagnostics.ExpiredItemsRemoved value) => listener.OnNext(value);
    }
}
