using Orleans.Concurrency;
using Orleans.Durable.Tasks;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 验证当 Grain 允许任务的执行段与等待请求交错执行时，等待自身 Grain 上的任务能够完成：
/// 包括可交错执行的等待方、可重入 Grain 和持久化代码三种情况。
/// </summary>
public abstract class DurableTaskSelfWaitTests(string journalFormatKey) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task InterleavingWaitsForOwnTasksComplete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<ISelfWaitingGrain>(Guid.NewGuid());

        Assert.Equal(6, await grain.InterleavingScheduleAndAwaitOwnTaskAsync(2).WaitAsync(Timeout, cancellationToken));

        var waiting = await grain.WaitForCancellationAsync().WithId("wait").ScheduleAsync(cancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, await grain.InterleavingCancelAndAwaitThroughManagerAsync("wait").WaitAsync(Timeout, cancellationToken));
        Assert.Equal(DurableTaskStatus.Canceled, (await waiting.GetResponseAsync(cancellationToken)).Status);
    }

    [Fact]
    public async Task WaitsForOwnTasksOnAReentrantGrainComplete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IReentrantSelfWaitingGrain>(Guid.NewGuid());

        Assert.Equal(6, await grain.ScheduleAndAwaitOwnTaskAsync(2).WaitAsync(Timeout, cancellationToken));

        var waiting = await grain.WaitForCancellationAsync().WithId("wait").ScheduleAsync(cancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, await grain.CancelAndAwaitThroughManagerAsync("wait").WaitAsync(Timeout, cancellationToken));
        Assert.Equal(DurableTaskStatus.Canceled, (await waiting.GetResponseAsync(cancellationToken)).Status);
    }

    [Fact]
    public async Task DurableBodyAwaitsADurableCallToItsOwnGrain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<ISelfWaitingGrain>(Guid.NewGuid());

        var outer = await grain.AwaitOwnCallAsync(2).WithId("outer").ScheduleAsync(cancellationToken);
        Assert.Equal(7, await outer.WaitAsync(Timeout, cancellationToken));
    }

    [Fact]
    public async Task InterleavingWaiterAlongsideAnotherBlockingRequestCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid();
        var grain = _fixture.Client.GetGrain<ISelfWaitingGrain>(key);
        var blocking = grain.BlockAsync();
        await SelfWaitingGrain.WaitUntilBlockedAsync(key).WaitAsync(Timeout, cancellationToken);

        // 阻塞任务执行段的是另一个请求，而非可交错执行的等待方，因此等待方等待该请求完成。
        var waiter = grain.InterleavingScheduleAndAwaitOwnTaskAsync(2);
        await SelfWaitingGrain.WaitUntilWaitStartedAsync(key).WaitAsync(Timeout, cancellationToken);
        Assert.False(waiter.IsCompleted);

        SelfWaitingGrain.Release(key);
        await blocking.WaitAsync(Timeout, cancellationToken);
        Assert.Equal(6, await waiter.WaitAsync(Timeout, cancellationToken));
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行自身任务等待相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskSelfWaitTests() : DurableTaskSelfWaitTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行自身任务等待相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskSelfWaitTests() : DurableTaskSelfWaitTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface ISelfWaitingGrain : IGrainWithGuidKey
{
    DurableTask<int> TripleAsync(int value);

    DurableTask<int> AwaitOwnCallAsync(int value);

    DurableTask<int> WaitForCancellationAsync();

    [AlwaysInterleave]
    Task<int> InterleavingScheduleAndAwaitOwnTaskAsync(int value);

    [AlwaysInterleave]
    Task<DurableTaskStatus> InterleavingCancelAndAwaitThroughManagerAsync(string rootId);

    Task BlockAsync();
}

public interface IReentrantSelfWaitingGrain : IGrainWithGuidKey
{
    DurableTask<int> TripleAsync(int value);

    DurableTask<int> WaitForCancellationAsync();

    Task<int> ScheduleAndAwaitOwnTaskAsync(int value);

    Task<DurableTaskStatus> CancelAndAwaitThroughManagerAsync(string rootId);
}

[GrainType("durable-task-self-waiting-test")]
public sealed class SelfWaitingGrain(IDurableTaskManager tasks) : DurableGrain, ISelfWaitingGrain
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (TaskCompletionSource Blocked, TaskCompletionSource Release)> Gates = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskCompletionSource> WaitsStarted = new();

    public static Task WaitUntilBlockedAsync(Guid key) => GetGate(key).Blocked.Task;

    /// <summary>Grain 中可交错执行的等待方调度自身任务并开始等待后即完成。</summary>
    public static Task WaitUntilWaitStartedAsync(Guid key) =>
        WaitsStarted.GetOrAdd(key, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public static void Release(Guid key) => GetGate(key).Release.TrySetResult();

    private static (TaskCompletionSource Blocked, TaskCompletionSource Release) GetGate(Guid key) =>
        Gates.GetOrAdd(key, _ => (new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)));

    public DurableTask<int> TripleAsync(int value) => DurableTask.FromResult(value * 3);

    // 对 Grain 自身的持久化调用是分离根任务，主体在持久化等待点等待其结果。
    public async DurableTask<int> AwaitOwnCallAsync(int value) =>
        await this.AsReference<ISelfWaitingGrain>().TripleAsync(value).Detached("own-call") + 1;

    public DurableTask<int> WaitForCancellationAsync() => SelfWaiting.WaitForCancellationAsync();

    public async Task<int> InterleavingScheduleAndAwaitOwnTaskAsync(int value)
    {
        var scheduled = await this.AsReference<ISelfWaitingGrain>().TripleAsync(value).ScheduleAsync();
        WaitsStarted.GetOrAdd(this.GetPrimaryKey(), _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        return await scheduled;
    }

    public Task<DurableTaskStatus> InterleavingCancelAndAwaitThroughManagerAsync(string rootId) => SelfWaiting.CancelAndAwaitAsync(tasks, rootId);

    public async Task BlockAsync()
    {
        var gate = GetGate(this.GetPrimaryKey());
        gate.Blocked.TrySetResult();
        await gate.Release.Task;
    }
}

[Reentrant]
[GrainType("durable-task-reentrant-self-waiting-test")]
public sealed class ReentrantSelfWaitingGrain(IDurableTaskManager tasks) : DurableGrain, IReentrantSelfWaitingGrain
{
    public DurableTask<int> TripleAsync(int value) => DurableTask.FromResult(value * 3);

    public DurableTask<int> WaitForCancellationAsync() => SelfWaiting.WaitForCancellationAsync();

    public Task<int> ScheduleAndAwaitOwnTaskAsync(int value) => SelfWaiting.ScheduleAndAwaitAsync(this.AsReference<IReentrantSelfWaitingGrain>().TripleAsync(value));

    public Task<DurableTaskStatus> CancelAndAwaitThroughManagerAsync(string rootId) => SelfWaiting.CancelAndAwaitAsync(tasks, rootId);
}

internal static class SelfWaiting
{
    private static readonly DurableSignal<int> Never = new("never");

    // 任务挂起在永远不会到达的持久化信号处，因此只有取消任务才会结束等待。
    public static async DurableTask<int> WaitForCancellationAsync()
    {
        await Never.WaitAsync();
        return 0;
    }

    public static async Task<int> ScheduleAndAwaitAsync(DurableTask<int> task)
    {
        var scheduled = await task.ScheduleAsync();
        return await scheduled;
    }

    public static async Task<DurableTaskStatus> CancelAndAwaitAsync(IDurableTaskManager tasks, string rootId)
    {
        var taskId = TaskId.CreateRoot(rootId);
        await tasks.CancelAsync(taskId);
        return (await tasks.GetTask<int>(taskId).GetResponseAsync()).Status;
    }
}
