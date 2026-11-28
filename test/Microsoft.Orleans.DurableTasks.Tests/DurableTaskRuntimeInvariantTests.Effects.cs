using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Fact]
    public Task EffectRecordIsATerminalTreeEntryDeletedWithItsExecutionTree() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime(TimeSpan.FromDays(1));
        var taskId = TaskId.Parse("root");
        var effectId = TaskId.Parse("root/reference");
        var reference = Guid.NewGuid();
        var request = CreateRequest(
            1,
            () => DurableTask.Run(_ => DurableTask.RunInline("reference", () => reference)));

        runtime.ScheduleFromInbox(taskId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.Completions.Count == 1);
        await WaitUntilIdleAsync(runtime);

        var effect = storage.Get(effectId);
        Assert.Equal("$effect", effect.RequestFingerprint);
        Assert.Equal(reference, effect.Result!.GetResult<Guid>());
        Assert.Null(effect.Request);
        Assert.True(effect.RemoteTarget.IsDefault);
        Assert.Equal(reference, storage.Get(taskId).Result!.GetResult<Guid>());

        // 投递任务的完成消息会删除其执行树，其中保存了效果的记录。
        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.Completions).MessageId, OutboxMessageRetirement.Delivered.Instance);

        Assert.False(storage.Contains(taskId));
        Assert.False(storage.Contains(effectId));
    });

    [Fact]
    public Task CallUnderAnEffectsIdentifierDiverges() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        ((IDurableTaskGrainStorage)storage).AddRecord(TaskId.Parse("root/local/charge"), DurableTaskRecordKind.Effect, "$effect", DurableTaskResponse.FromResult(1));
        using var stalls = new StallRecorder(taskId);

        var handle = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(async () => await CreateRemoteChild(1).WithId("charge")),
            TestContext.Current.CancellationToken);

        var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
        Assert.Contains("It records a DurableTask.RunInline effect, and this attempt requested a call to method", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.False(storage.Get(taskId).Result is { IsCompleted: true });
        Assert.Empty(transport.Invocations);
    });

    [Fact]
    public Task CallOfAnotherMethodUnderARecordedCallStallsTheAttempt() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        var callId = TaskId.Parse("root/local/charge");
        var recorded = storage.GetOrCreate(callId);
        var target = GrainId.Create("target", "one");
        ((IDurableTaskGrainStorage)storage).SetRemoteRequest(callId, recorded, target, IDurableTaskRequest.GetMethod(new RuntimeTestOtherMethodRequest()), "recorded-fingerprint");
        using var stalls = new StallRecorder(taskId);

        // 部署更改了调用的方法，因此重放使用已记录的标识符请求另一个操作。
        _ = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(async () => await CreateRemoteChild(2).WithId("charge")),
            TestContext.Current.CancellationToken);

        var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
        Assert.Contains("'root/local/charge' is already associated with a different request", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.False(storage.Get(taskId).Result is { IsCompleted: true });
        Assert.Empty(transport.Invocations);
    });
    [Fact]
    public Task DelayUnderAnEffectsIdentifierDiverges() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        ((IDurableTaskGrainStorage)storage).AddRecord(TaskId.Parse("root/local/wait"), DurableTaskRecordKind.Effect, "$effect", DurableTaskResponse.FromResult(1));
        using var stalls = new StallRecorder(taskId);

        var handle = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(async () => await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("wait")),
            TestContext.Current.CancellationToken);

        var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
        Assert.Contains("It records a DurableTask.RunInline effect", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.False(storage.Get(taskId).Result is { IsCompleted: true });
        Assert.Empty(transport.Resumes);
    });
}

/// <summary>记录任务的第一个 <see cref="DurableTaskEvents.ExecutionStalled"/> 事件。</summary>
internal sealed class StallRecorder : IDisposable
{
    private readonly TaskCompletionSource<DurableTaskEvents.ExecutionStalled> _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IDisposable _subscription;

    public StallRecorder(TaskId taskId) =>
        _subscription = DurableTaskEvents.AllEvents.Subscribe(new EventObserver(evt =>
        {
            if (evt is DurableTaskEvents.ExecutionStalled stalled && stalled.TaskId == taskId)
            {
                _stalled.TrySetResult(stalled);
            }
        }));

    public Task<DurableTaskEvents.ExecutionStalled> Stalled => _stalled.Task;

    public void Dispose() => _subscription.Dispose();
}
