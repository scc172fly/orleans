using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 记录类型:重复播放,要求根据记录的识别符号进行另一种操作,但有差异,从而拖延
/// 而不是观察或改变记录的行动。
/// </summary>
public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Fact]
    public Task DelayWhereAStepWasRecordedStallsAsDiverged() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        var waitId = taskId.Child("wait");
        storage.AddInvoked(TaskId.Parse("root"));
        var step = storage.GetOrCreate(waitId, DurableTaskRecordKind.LocalStep);
        storage.SetResponse(waitId, step, DurableTaskResponse.FromResult(1));
        using var stalls = new StallRecorder(taskId);

        // 工作流程发生了变化:重播要求推迟执行第一个执行过程记录了一步结果。
        _ = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(async () => await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("wait")),
            TestContext.Current.CancellationToken);

        var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
        Assert.Contains("It records a local child task, and this attempt requested a delay.", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, storage.Get(waitId).Result!.GetResult<int>());
        Assert.False(storage.Get(taskId).Result is { IsCompleted: true });
        Assert.Empty(transport.Resumes);
    });

    [Fact]
    public Task StepWhereADelayWasRecordedStallsAsDiverged() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        var waitId = taskId.Child("wait");
        storage.AddInvoked(TaskId.Parse("root"));
        await runtime.ScheduleDelayAsync(waitId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);
        var dueTime = storage.Get(waitId).DueTime;
        using var stalls = new StallRecorder(taskId);

        // 工作流程发生了变化:重播要求迈出第一步,即第一次执行记录了延迟时间。
        _ = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(async () => await DurableTask.Run(static _ => 1).WithId("wait")),
            TestContext.Current.CancellationToken);

        var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
        Assert.Contains($"It records a delay due at {dueTime:O}, and this attempt requested a local child task.", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Equal(DurableTaskRecordKind.Delay, storage.Get(waitId).Kind);
        Assert.Equal(dueTime, storage.Get(waitId).DueTime);
        Assert.Null(storage.Get(waitId).Result);
        Assert.Single(transport.Resumes);
    });

    [Fact]
    public Task DelayAtAPendingCallRecordStallsWithoutChangingIt() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        var callId = taskId.Child("wait");
        var target = GrainId.Create("target", "one");
        storage.AddInvoked(TaskId.Parse("root"));
        storage.SetRemoteRequest(callId, storage.GetOrCreate(callId, DurableTaskRecordKind.RemoteCall), target, "method", "0123456789ABCDEF0123");

        // 儿童身份识别资料下的处决时间表发现电话记录,但没有任何改变的延迟。
        var direct = await Assert.ThrowsAsync<DurableTaskDivergenceException>(
            () => runtime.ScheduleDelayAsync(callId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken).AsTask());
        Assert.Contains($"It records a call to method 'method' on grain '{target}' (fingerprint 0123456789AB), and this attempt requested a delay due at", direct.Message, StringComparison.Ordinal);

        // 重播等待延迟 在电话的识别信号卡下
        using var stalls = new StallRecorder(taskId);
        _ = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(async () => await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("wait")),
            TestContext.Current.CancellationToken);

        var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
        var call = storage.Get(callId);
        Assert.Equal((DurableTaskRecordKind.RemoteCall, target), (call.Kind, call.RemoteTarget));
        Assert.Null(call.DueTime);
        Assert.Equal(0, call.ResumeGeneration);
        Assert.Null(call.Result);
        Assert.Empty(transport.Resumes);
    });

    [Fact]
    public Task PendingStepWithACancellationRequestIsNotAnEarlyCancellationRecord() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        var stepId = rootId.Child("step");
        storage.AddInvoked(rootId);
        var step = storage.GetOrCreate(stepId, DurableTaskRecordKind.LocalStep);
        storage.RequestCancellation(stepId, step);

        // 该步骤是执行该步骤的一个操作,一个重播达到,没有收到作为援引任务的任何信号。
        Assert.Equal([stepId], runtime.GetRecordedOperations(rootId));
        Assert.IsType<DurableTaskNotFoundException>(runtime.StageSignalFromInbox(stepId, "approved", DurableTaskResponse.FromResult("ada")));
        Assert.False(storage.Contains(stepId.Child("$signal:approved")));

        // 在援引之前就已作此记录,而且其援引信号先于该请求发出。
        var earlyId = TaskId.CreateRoot("early");
        runtime.SignalCancellationFromInbox(earlyId, GrainId.Create("caller", "one"), reason: null);
        Assert.Equal(DurableTaskRecordKind.EarlyCancellation, storage.Get(earlyId).Kind);
        Assert.Null(runtime.StageSignalFromInbox(earlyId, "approved", DurableTaskResponse.FromResult("ada")));
        Assert.True(storage.Contains(earlyId.Child("$signal:approved")));
        Assert.Null(runtime.GetRecordedOperations(earlyId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
    });
}
