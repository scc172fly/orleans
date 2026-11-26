using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>运行任务状态的尝试取消覆盖, 以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskAttemptCancellationTests() : DurableTaskAttemptCancellationTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行任务状态中以 Orleans 二进制日记格式存储的任务状态的尝试取消覆盖。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskAttemptCancellationTests() : DurableTaskAttemptCancellationTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>
/// 测试一个取消的持久任务尝试的失败:其持久的操作会抛出,清理状态的变化
/// 承诺,任务取消记录它的补偿。
/// </summary>
public abstract class DurableTaskAttemptCancellationTests : IAsyncLifetime
{
    private readonly DurableTaskRecoveryFixture _fixture;

    protected DurableTaskAttemptCancellationTests(string journalFormatKey)
    {
        var probe = new AttemptCancellationProbe();
        Probe = probe;
        _fixture = new(journalFormatKey, silo => silo.Services.AddSingleton(probe));
    }

    private AttemptCancellationProbe Probe { get; }

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task DeactivationDiscardsTheParkedAttemptWithoutRunningItsCleanup()
    {
        const string rootId = "deactivated";
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IAttemptCancellationTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();
        var scheduled = await grain.WaitForCancellationAsync().WithId(rootId).ScheduleAsync(cancellationToken);
        await Probe.WaitAsync(grainId, AttemptCancellationProbe.Waiting, cancellationToken);
        Assert.True(_fixture.Cluster.TryGetGrainContext(grainId, out var activation));

        // 尝试停在信号上 所以停止工作丢弃它: 它没有恢复,所以清理,这也不是
        // 死刑执行过程的一部分,从不运行
        await grain.RequestDeactivationAsync();
        await activation.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(0, Probe.Get(grainId, AttemptCancellationProbe.Catches));
        Assert.False(Probe.GetOutcomes(grainId).ContainsKey(AttemptCancellationProbe.Caught));

        // 下一次激活后重播放任务, 重新指定它的地位, 一旦收到信号就完成
        // 不再运行效果 。
        Assert.Equal(new AttemptCancellationSnapshot(Status: "running", Unrelated: 0), await grain.GetSnapshotAsync());
        await grain.SignalDurableTaskAsync(rootId, AttemptCancellationTestGrain.Approved, "approved", cancellationToken);
        Assert.Equal("approved", (await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).GetResult<string>());
        Assert.Equal(new AttemptCancellationSnapshot(Status: "running", Unrelated: 0), await grain.GetSnapshotAsync());
        Assert.Equal(1, Probe.Get(grainId, AttemptCancellationProbe.EffectRuns));
        Assert.Equal(0, Probe.Get(grainId, AttemptCancellationProbe.Catches));
    }

    [Fact]
    public async Task CancellationReachesAStepWhoseSegmentHoldsTheGrain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IAttemptCancellationTestGrain>(Guid.NewGuid());
        var scheduled = await grain.BlockedStepAsync().WithId("blocked").ScheduleAsync(cancellationToken);
        await Probe.WaitAsync(grain.GetGrainId(), AttemptCancellationProbe.Waiting, cancellationToken);

        // 步骤段的段段为非复入 grain和取消(仅分期任务状态)的间断
        // 与它,所以步骤的标志观察它。
        await scheduled.CancelAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        var response = await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal("canceled", response.GetResult<string>());
    }

    [Fact]
    public async Task AbortCancelsTheAttemptWhileOtherExecutionsOfTheGrainKeepCommitting()
    {
        const string rootId = "aborted";
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IAttemptCancellationTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();
        Probe.HoldUnwinding(grainId);
        var scheduled = await grain.WaitForCancellationAsync().WithId(rootId).ScheduleAsync(cancellationToken);
        await Probe.WaitAsync(grainId, AttemptCancellationProbe.Waiting, cancellationToken);

        await scheduled.AbortAsync("operator", cancellationToken);
        await Probe.WaitAsync(grainId, AttemptCancellationProbe.Unwinding, cancellationToken);

        // 继续运行的 grain执行中的另一件执行过程,在中止尝试后解除时实施。
        var increment = await grain.IncrementAsync().WithId("increment").ScheduleAsync(cancellationToken);
        Assert.Equal(1, await increment.WaitAsync(cancellationToken));
        Probe.Signal(grainId, AttemptCancellationProbe.Release);
        await Probe.WaitAsync(grainId, AttemptCancellationProbe.Unwound, cancellationToken);

        var aborted = await Assert.ThrowsAsync<DurableTaskAbortedException>(async () => await scheduled.WaitAsync(cancellationToken));
        Assert.Equal("operator", aborted.Message);
        AssertCanceledAttemptUnwound(grainId, DurableTaskAttemptCancellationReason.TaskAborted);
        var cleanedUp = new AttemptCancellationSnapshot(Status: "cleaned up", Unrelated: 1, AttemptCancellationTestGrain.CleanupChanges);
        Assert.Equal(cleanedUp, await grain.GetSnapshotAsync());

        // 承诺日记保持同一状态,包括中止的清理尝试。
        Assert.True(_fixture.Cluster.TryGetGrainContext(grainId, out var activation));
        await grain.RequestDeactivationAsync();
        await activation.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(cleanedUp, await grain.GetSnapshotAsync());
        Assert.Equal(1, Probe.Get(grainId, AttemptCancellationProbe.EffectRuns));
    }

    [Fact]
    public async Task TaskCancellationRecordsCompensationMadeWithTheAttemptCancellationToken()
    {
        const string rootId = "compensated";
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IAttemptCancellationTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();
        Probe.Signal(grainId, AttemptCancellationProbe.Release);
        var scheduled = await grain.CompensateOnCancellationAsync().WithId(rootId).ScheduleAsync(cancellationToken);
        await Probe.WaitAsync(grainId, AttemptCancellationProbe.Waiting, cancellationToken);

        await scheduled.CancelAsync("buyer", cancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await scheduled.WaitAsync(cancellationToken));
        Assert.Equal(new AttemptCancellationSnapshot(Status: "compensated: buyer", Unrelated: 0), await grain.GetSnapshotAsync());
        Assert.Equal((1, 1, 1), (Probe.Get(grainId, AttemptCancellationProbe.Compensations), Probe.Get(grainId, AttemptCancellationProbe.Refunds), Probe.Get(grainId, AttemptCancellationProbe.ExternalCalls)));

        // 赔偿由已承诺的日记予以保管。
        Assert.True(_fixture.Cluster.TryGetGrainContext(grainId, out var activation));
        await grain.RequestDeactivationAsync();
        await activation.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(new AttemptCancellationSnapshot(Status: "compensated: buyer", Unrelated: 0), await grain.GetSnapshotAsync());
    }

    [Fact]
    public async Task AttemptCancellationDuringCompensationLeavesItForTheReplayToRedo()
    {
        const string rootId = "interrupted-compensation";
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IAttemptCancellationTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();
        var scheduled = await grain.CompensateOnCancellationAsync().WithId(rootId).ScheduleAsync(cancellationToken);
        await Probe.WaitAsync(grainId, AttemptCancellationProbe.Waiting, cancellationToken);
        await scheduled.CancelAsync("buyer", cancellationToken);
        await Probe.WaitAsync(grainId, AttemptCancellationProbe.Compensating, cancellationToken);
        Assert.True(_fixture.Cluster.TryGetGrainContext(grainId, out var activation));

        // 补偿的外部电话观察了取消尝试和随后国家的变化
        // 成功成功。
        await grain.RequestDeactivationAsync();
        await activation.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        var interrupted = Probe.GetOutcomes(grainId);
        Assert.IsType<TaskCanceledException>(interrupted["external-call"]);
        Assert.Null(interrupted["after-external-call"]);

        // 重播发现效果记录 并重新处理其余的补偿。
        Probe.Signal(grainId, AttemptCancellationProbe.Release);
        _ = await grain.GetSnapshotAsync();
        var response = await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, response.Status);
        Assert.Equal(new AttemptCancellationSnapshot(Status: "compensated: buyer", Unrelated: 0), await grain.GetSnapshotAsync());
        Assert.Equal((1, 1, 2), (Probe.Get(grainId, AttemptCancellationProbe.Compensations), Probe.Get(grainId, AttemptCancellationProbe.Refunds), Probe.Get(grainId, AttemptCancellationProbe.ExternalCalls)));
    }

    private void AssertCanceledAttemptUnwound(GrainId grainId, DurableTaskAttemptCancellationReason reason)
    {
        var outcomes = Probe.GetOutcomes(grainId);
        var caught = Assert.IsType<DurableTaskAttemptCanceledException>(outcomes[AttemptCancellationProbe.Caught]);
        Assert.Equal(reason, caught.Reason);
        Assert.Null(outcomes[AttemptCancellationProbe.TaskCancellationRequested]);
        foreach (var operation in AttemptCancellationTestGrain.CanceledAttemptOperations)
        {
            var exception = Assert.IsType<DurableTaskAttemptCanceledException>(outcomes[operation]);
            Assert.Equal(reason, exception.Reason);
        }

        // 清理工作对日记状态的成功的改变。
        foreach (var operation in AttemptCancellationTestGrain.CleanupOperations)
        {
            Assert.Null(outcomes[operation]);
        }

        // 流程中的资源在撤离时释放出来。
        Assert.Null(outcomes[AttemptCancellationProbe.SemaphoreReleased]);
        Assert.Null(outcomes[AttemptCancellationProbe.ResourceDisposed]);
    }
}

public interface IAttemptCancellationTestGrain : IGrainWithGuidKey
{
    /// <summary>等待信号,记录其代码一旦尝试取消后能做什么。</summary>
    DurableTask<string> WaitForCancellationAsync();

    /// <summary>等待一小时的延迟, 当任务取消时补偿。</summary>
    DurableTask<string> CompensateOnCancellationAsync();

    /// <summary>增加额 grain无关的柜台。</summary>
    DurableTask<int> IncrementAsync();

    /// <summary>运行一个等待它标记的一步, 而没有持久的等待, 所以它的部分会保持 grain.</summary>
    DurableTask<string> BlockedStepAsync();

    DurableTask<int> ComputeAsync(int value);

    Task<AttemptCancellationSnapshot> GetSnapshotAsync();

    /// <summary>解除 grain,与执行者进行连接 其分段运行。</summary>
    [AlwaysInterleave]
    Task RequestDeactivationAsync();
}

/// <summary> grain日记状态显示取消尝试清除的更改。</summary>
[GenerateSerializer]
public sealed record AttemptCancellationSnapshot(
    [property: Id(0)] string? Status,
    [property: Id(1)] int Unrelated,
    [property: Id(2)] int Changes = 0);

/// <summary>记录那些企图 <see cref="AttemptCancellationTestGrain"/> 观察,并同步测试与它们。</summary>
public sealed class AttemptCancellationProbe
{
    public const string Waiting = "waiting";
    public const string Unwinding = "unwinding";
    public const string Unwound = "unwound";
    public const string Release = "release";
    public const string Compensating = "compensating";
    public const string Caught = "caught";
    public const string TaskCancellationRequested = "task-cancellation-requested";
    public const string SemaphoreReleased = "semaphore-released";
    public const string ResourceDisposed = "resource-disposed";
    public const string EffectRuns = "effect-runs";
    public const string Catches = "catches";
    public const string ConcurrentWrites = "concurrent-writes";
    public const string Compensations = "compensations";
    public const string Refunds = "refunds";
    public const string ExternalCalls = "external-calls";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<(GrainId, string), TaskCompletionSource> _signals = new();
    private readonly ConcurrentDictionary<(GrainId, string), int> _counters = new();
    private readonly ConcurrentDictionary<GrainId, ConcurrentDictionary<string, Exception?>> _outcomes = new();
    private readonly ConcurrentDictionary<GrainId, bool> _heldUnwinding = new();

    public void Signal(GrainId grainId, string name) => GetSignal(grainId, name).TrySetResult();

    public async Task WaitAsync(GrainId grainId, string name, CancellationToken cancellationToken)
    {
        try
        {
            await GetSignal(grainId, name).Task.WaitAsync(Timeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Grain '{grainId}' didn't signal '{name}' within {Timeout}.", exception);
        }
    }

    /// <summary>使 grain取消的尝试在它卸载时等待 <see cref="Release"/> 。</summary>
    public void HoldUnwinding(GrainId grainId) => _heldUnwinding[grainId] = true;

    public bool IsUnwindingHeld(GrainId grainId) => _heldUnwinding.ContainsKey(grainId);

    public int Increment(GrainId grainId, string name) => _counters.AddOrUpdate((grainId, name), 1, static (_, count) => count + 1);

    public int Get(GrainId grainId, string name) => _counters.GetValueOrDefault((grainId, name));

    public void Record(GrainId grainId, string name, Exception? exception) =>
        _outcomes.GetOrAdd(grainId, static _ => new())[name] = exception;

    public IReadOnlyDictionary<string, Exception?> GetOutcomes(GrainId grainId) => _outcomes.GetOrAdd(grainId, static _ => new());

    private TaskCompletionSource GetSignal(GrainId grainId, string name) =>
        _signals.GetOrAdd((grainId, name), static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
}

[GrainType("durable-task-attempt-cancellation-test")]
public sealed class AttemptCancellationTestGrain(
    AttemptCancellationProbe probe,
    [FromKeyedServices("status")] IDurableValue<string> status,
    [FromKeyedServices("unrelated")] IDurableValue<int> unrelated,
    [FromKeyedServices("dictionary")] IDurableDictionary<string, int> dictionary,
    [FromKeyedServices("list")] IDurableList<string> list,
    [FromKeyedServices("queue")] IDurableQueue<string> queue,
    [FromKeyedServices("set")] IDurableSet<string> set,
    [FromKeyedServices("log")] IDurableLog<string> log,
    [FromKeyedServices("completion")] IDurableTaskCompletionSource<int> completion,
    [FromKeyedServices("state")] IPersistentState<string> state)
    : DurableGrain, IAttemptCancellationTestGrain
{
    public static readonly DurableSignal<string> Approved = new("approved");

    /// <summary>取消尝试的代码尝试的持久操作, 每一个都扔出尝试取消。</summary>
    public static readonly string[] CanceledAttemptOperations =
    [
        "once", "child", "shielded-child", "detached-call", "call", "post", "schedule", "delay", "signal-wait",
        "signal-send", "subscription", "when-all", "when-any", "race", "timeout", "try-wait",
    ];

    /// <summary>被日记的状态变化, 取消尝试清理后, 每一个都成功。</summary>
    public static readonly string[] CleanupOperations =
    [
        "value", "dictionary", "list", "queue", "set", "log", "completion", "persistent-state", "persistent-state-write",
        "grain-write",
    ];

    /// <summary>
    /// 清理后对 <see cref="AttemptCancellationSnapshot.Changes"/>增加的修改数:每个条目
    /// 收集、完成结果和持续状态记录。
    /// </summary>
    public const int CleanupChanges = 7;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async DurableTask<string> WaitForCancellationAsync()
    {
        var context = DurableTask.Current;
        var grainId = this.GetGrainId();
        DurableTask.RunInline("started", () => probe.Increment(grainId, AttemptCancellationProbe.EffectRuns));
        status.Value = "running";
        await _semaphore.WaitAsync(context.CancellationToken);
        try
        {
            await using var resource = new ProbeResource(probe, grainId);
            await DurableTask.Run(static _ => { }).WithId("commit");
            probe.Record(grainId, AttemptCancellationProbe.Waiting, null);
            probe.Signal(grainId, AttemptCancellationProbe.Waiting);
            return await Approved.WaitAsync();
        }
        catch (OperationCanceledException exception)
        {
            probe.Increment(grainId, AttemptCancellationProbe.Catches);
            probe.Record(grainId, AttemptCancellationProbe.Caught, exception);
            probe.Record(
                grainId,
                AttemptCancellationProbe.TaskCancellationRequested,
                context.IsTaskCancellationRequested ? new InvalidOperationException("Task cancellation was requested.") : null);
            await TryCanceledAttemptOperationsAsync(grainId);
            probe.Signal(grainId, AttemptCancellationProbe.Unwinding);
            await probe.WaitAsync(grainId, AttemptCancellationProbe.Release, CancellationToken.None);
            throw;
        }
        finally
        {
            _semaphore.Release();
            probe.Record(
                grainId,
                AttemptCancellationProbe.SemaphoreReleased,
                _semaphore.CurrentCount == 1 ? null : new InvalidOperationException("The semaphore is held."));
            probe.Signal(grainId, AttemptCancellationProbe.Unwound);
        }
    }

    public async DurableTask<string> CompensateOnCancellationAsync()
    {
        var context = DurableTask.Current;
        var grainId = this.GetGrainId();
        try
        {
            var wait = await DurableTask.Delay(TimeSpan.FromHours(1)).WithId("wait").ScheduleAsync(context.CancellationToken);
            probe.Signal(grainId, AttemptCancellationProbe.Waiting);
            await wait;
            return "completed";
        }
        catch (OperationCanceledException) when (context.IsTaskCancellationRequested)
        {
            DurableTask.RunInline("compensate", () => probe.Increment(grainId, AttemptCancellationProbe.Compensations));
            status.Value = "compensating";
            // 赔偿的儿童被保护, 因为儿童 被取消的任务开始结束。
            await DurableTask.Run(static _ => { }).WithId("checkpoint").Shielded();
            probe.Signal(grainId, AttemptCancellationProbe.Compensating);

            // 补偿令使用未遂的象征 因为任务的名义已经被取消了
            try
            {
                await CallExternalServiceAsync(grainId, context.AttemptCancellationToken);
            }
            catch (OperationCanceledException exception) when (context.AttemptCancellationToken.IsCancellationRequested)
            {
                probe.Record(grainId, "external-call", exception);
                probe.Record(grainId, "after-external-call", Record(() => status.Value = "half-compensated"));
                throw;
            }

            await DurableTask.Run(_ => probe.Increment(grainId, AttemptCancellationProbe.Refunds)).WithId("refund").Shielded();
            status.Value = $"compensated: {context.TaskCancellationReason}";
            throw;
        }
    }

    public DurableTask<int> IncrementAsync() => DurableTask.FromResult(++unrelated.Value);

    public DurableTask<string> BlockedStepAsync()
    {
        var grainId = this.GetGrainId();
        return DurableTask.Run(async cancellationToken =>
        {
            probe.Signal(grainId, AttemptCancellationProbe.Waiting);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "completed";
            }
            catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
            {
                return "canceled";
            }
        });
    }

    public DurableTask<int> ComputeAsync(int value) => DurableTask.FromResult(value);

    public Task<AttemptCancellationSnapshot> GetSnapshotAsync() => Task.FromResult(new AttemptCancellationSnapshot(
        status.Value,
        unrelated.Value,
        dictionary.Count + list.Count + queue.Count + set.Count + log.Count
            + (completion.State.Status is DurableTaskCompletionSourceStatus.Pending ? 0 : 1)
            + (state.RecordExists ? 1 : 0)));

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private async Task CallExternalServiceAsync(GrainId grainId, CancellationToken cancellationToken)
    {
        probe.Increment(grainId, AttemptCancellationProbe.ExternalCalls);
        await probe.WaitAsync(grainId, AttemptCancellationProbe.Release, cancellationToken);
    }

    private async Task TryCanceledAttemptOperationsAsync(GrainId grainId)
    {
        var other = GrainFactory.GetGrain<IAttemptCancellationTestGrain>(Guid.NewGuid());
        var deadline = DurableTask.Current.UtcNow.AddMinutes(1);
        Try("once", () => DurableTask.RunInline("cleanup", static () => { }));
        await TryAsync("child", async () => await DurableTask.Run(static _ => { }).WithId("child"));
        await TryAsync("shielded-child", async () => await DurableTask.Run(static _ => { }).WithId("shielded-child").Shielded());
        await TryAsync("detached-call", async () => await other.ComputeAsync(1).Detached().ScheduleAsync());
        await TryAsync("call", async () => await other.ComputeAsync(1));
        Try("post", () => DurableTask.Run(static _ => { }).WithId("posted").Post());
        await TryAsync("schedule", async () => await DurableTask.Run(static _ => { }).WithId("scheduled").ScheduleAsync());
        await TryAsync("delay", async () => await DurableTask.Delay(TimeSpan.FromMinutes(1)));
        await TryAsync("signal-wait", async () => await Approved.WaitAsync());
        await TryAsync("signal-send", async () => await other.SignalDurableTaskAsync("other", Approved, "approved"));
        await TryAsync("subscription", async () => await other.GetDurableTask<int>("other"));
        await TryAsync("when-all", async () => await DurableTask.WhenAll([DurableTask.Run(static _ => { })]));
        await TryAsync("when-any", async () => await DurableTask.WhenAny([DurableTask.Run(static _ => { })]));
        await TryAsync("race", async () => await DurableTask.Race([DurableTask.Run(static _ => { })]));
        await TryAsync("timeout", async () => await DurableTask.Run(static _ => { }).WaitAsync(TimeSpan.FromMinutes(1)));
        await TryAsync("try-wait", async () => await Approved.TryWaitAsync(deadline));
        Try("value", () => status.Value = "cleaned up");
        Try("dictionary", () => dictionary.Add("cleaned up", 1));
        Try("list", () => list.Add("cleaned up"));
        Try("queue", () => queue.Enqueue("cleaned up"));
        Try("set", () => set.Add("cleaned up"));
        Try("log", () => log.Append("cleaned up"));
        Try("completion", () => completion.TrySetResult(1));
        Try("persistent-state", () => state.State = "cleaned up");
        await TryAsync("persistent-state-write", () => state.WriteStateAsync());
        await TryAsync("grain-write", async () => await WriteStateAsync());

        async Task TryAsync(string operation, Func<Task> action) =>
            probe.Record(grainId, operation, await global::Xunit.Record.ExceptionAsync(action));

        void Try(string operation, Action action) => probe.Record(grainId, operation, Record(action));
    }

    private static Exception? Record(Action action) => global::Xunit.Record.Exception(action);

    private sealed class ProbeResource(AttemptCancellationProbe probe, GrainId grainId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            probe.Record(grainId, AttemptCancellationProbe.ResourceDisposed, null);
            return ValueTask.CompletedTask;
        }
    }
}
