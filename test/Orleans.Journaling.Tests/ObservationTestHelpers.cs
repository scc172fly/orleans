using System.Buffers;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 观察日记状态的测试基础设施。
/// </summary>
internal static class ObservationTestHelpers
{
    /// <summary>
    /// 在独家调度器上运行 <paramref name="body"/> , 反映激活单行调度器的镜像 。
    /// </summary>
    /// <remarks>
    /// 经理的工作循环和每个监视器的继续运行 都继承了调度器 所以状态通过一个逻辑进入
    /// 继续线只在试验体等待时运行。
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
    /// 阅读下一个日志事件,而不使用它的时代。测试中肯定该时代的值读 <see cref="IAsyncEnumerator{T}.Current"/>。
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
/// 将耐久数据下放给不稳定的存储。
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
    /// 将下一个写作保持到 <see cref="Release"/> 被调用。 <see cref="WriteStarted"/> 当写入到达存储点时完成 。
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
