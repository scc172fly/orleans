using System.Buffers;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 用于观察日志化状态的测试基础设施。
/// </summary>
internal static class ObservationTestHelpers
{
    /// <summary>
    /// 在独占调度器上运行 <paramref name="body"/>，模拟激活实例的单线程调度器。
    /// </summary>
    /// <remarks>
    /// 管理器的工作循环及每个观察流的延续操作均继承该调度器，因此状态由一个逻辑
    /// 线程访问，且仅当测试主体执行 await 时，延续操作才会运行。
    /// </remarks>
    public static Task RunOnActivationSchedulerAsync(Func<Task> body)
    {
        var scheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
        return Task.Factory.StartNew(
            () =>
            {
                SynchronizationContext.SetSynchronizationContext(null);
                return body();
            },
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler).Unwrap().WaitAsync(TimeSpan.FromSeconds(30));
    }

    public static async Task<T> NextAsync<T>(this IAsyncEnumerator<T> enumerator)
    {
        Assert.True(await enumerator.MoveNextAsync(), "The stream ended before producing the expected item.");
        return enumerator.Current;
    }

    /// <summary>
    /// 读取下一条日志事件但不返回其 epoch。断言 epoch 的测试读取 <see cref="IAsyncEnumerator{T}.Current"/>。
    /// </summary>
    public static async Task<LogEvent<T>> NextAsync<T>(this IAsyncEnumerator<LogEvent<T>> enumerator)
    {
        Assert.True(await enumerator.MoveNextAsync(), "The stream ended before producing the expected item.");
        return enumerator.Current with { Epoch = 0 };
    }

    public static JournaledStateManager AsJournaledStateManager(this IJournaledStateManager manager) =>
        Assert.IsType<JournaledStateManager>(manager, exactMatch: false);
}

/// <summary>
/// 能够挂起正在进行的写入并使其失败的日志存储，将持久化数据委托给易失性存储。
/// </summary>
internal sealed class GatedJournalStorage(VolatileJournalStorage inner) : IJournalStorage
{
    private TaskCompletionSource? _gate;

    public GatedJournalStorage() : this(new VolatileJournalStorage())
    {
    }

    public VolatileJournalStorage Inner { get; } = inner;

    public TaskCompletionSource WriteStarted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Exception? Failure { get; set; }

    public bool IsCompactionRequested { get; set; }

    public int ReplaceCount { get; private set; }

    /// <summary>
    /// 挂起下一次写入，直到调用 <see cref="Release"/>。当写入到达存储层时，<see cref="WriteStarted"/> 完成。
    /// </summary>
    public void HoldNextWrite()
    {
        _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release()
    {
        var gate = _gate ?? throw new InvalidOperationException("No write is held.");
        _gate = null;
        gate.SetResult();
    }

    public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken) => Inner.ReadAsync(consumer, cancellationToken);

    public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        var bytes = value.ToArray();
        await WaitAsync();
        await Inner.AppendAsync(new ReadOnlySequence<byte>(bytes), cancellationToken);
    }

    public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        var bytes = value.ToArray();
        await WaitAsync();
        ReplaceCount++;
        IsCompactionRequested = false;
        await Inner.ReplaceAsync(new ReadOnlySequence<byte>(bytes), cancellationToken);
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken) => Inner.DeleteAsync(cancellationToken);

    private async Task WaitAsync()
    {
        if (_gate is { } gate)
        {
            WriteStarted.TrySetResult();
            await gate.Task;
        }

        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}
