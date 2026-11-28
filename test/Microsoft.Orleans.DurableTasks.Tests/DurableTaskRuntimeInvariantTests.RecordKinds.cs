using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 记录类型：若重放在已有记录的标识符下请求不同类型的操作，则发生重放偏离，使本次
/// 执行尝试停滞，而非读取或更改已记录的操作。
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

        // 工作流已改变：重放在首次执行记录步骤结果的位置请求延迟操作。
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

        // 工作流已改变：重放在首次执行记录待完成延迟操作的位置请求执行步骤。
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

        // 执行实例在其子任务标识符下调度的延迟会找到该调用的记录，不改变任何状态。
        var direct = await Assert.ThrowsAsync<DurableTaskDivergenceException>(
            () => runtime.ScheduleDelayAsync(callId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken).AsTask());
        Assert.Contains($"It records a call to method 'method' on grain '{target}' (fingerprint 0123456789AB), and this attempt requested a delay due at", direct.Message, StringComparison.Ordinal);

        // 在调用的标识符下等待延迟操作的重放会停滞。
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

        // 步骤是其所属执行实例中的操作，重放会到达该步骤；它不是被调用任务，不接收发往被调用任务的信号。
        Assert.Equal([stepId], runtime.GetRecordedOperations(rootId));
        Assert.IsType<DurableTaskNotFoundException>(runtime.StageSignalFromInbox(stepId, "approved", DurableTaskResponse.FromResult("ada")));
        Assert.False(storage.Contains(stepId.Child("$signal:approved")));

        // 在调用之前到达的取消会以提前取消的形式记录，而调用所对应的信号也可以先于该调用到达。
        var earlyId = TaskId.CreateRoot("early");
        runtime.SignalCancellationFromInbox(earlyId, GrainId.Create("caller", "one"), reason: null);
        Assert.Equal(DurableTaskRecordKind.EarlyCancellation, storage.Get(earlyId).Kind);
        Assert.Null(runtime.StageSignalFromInbox(earlyId, "approved", DurableTaskResponse.FromResult("ada")));
        Assert.True(storage.Contains(earlyId.Child("$signal:approved")));
        Assert.Null(runtime.GetRecordedOperations(earlyId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
    });
}
