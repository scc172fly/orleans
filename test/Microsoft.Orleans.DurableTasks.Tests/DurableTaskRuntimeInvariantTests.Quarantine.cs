using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 任务/任务失败:储存的请求不再仅对检疫工作解码,而取消
/// 任何任务都不得托付,除非打电话者取消儿童,而超过儿童的祈祷。
/// </summary>
public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Fact]
    public Task RecoveredRequestWhichNoLongerDecodesStallsOnlyItsTask() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var stateMachine = (IStateMachine)runtime;
        var undecodableId = TaskId.CreateRoot("undecodable");
        var otherId = TaskId.CreateRoot("other");
        var undecodableRequest = CreateRequest(1, () => DurableTask.FromResult(7));
        var otherRequest = CreateRequest(2, () => DurableTask.FromResult(8));
        stateMachine.Reset(default);
        var undecodable = AddRecoveredRequest(undecodableId, undecodableRequest);
        undecodable.RequestDecodeFailure = new InvalidOperationException("The request's type was removed.");
        AddRecoveredRequest(otherId, otherRequest);
        stateMachine.OnRecoveryCompleted();
        using var stalls = new StallRecorder(undecodableId);

        // 激活解除代码不提出任何请求,因此它恢复了,而另一项任务将持续完成。
        await runtime.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(otherId).Result is { IsCompleted: true });
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(8, storage.Get(otherId).Result!.GetResult<int>());

        // 要求不解码摊位的任务 保留存储的要求 民意调查报告为未完成
        var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
        Assert.Contains($"The request which durable task '{undecodableId}' stores no longer decodes", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("The request's type was removed.", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Null(undecodable.Result);
        Assert.True(undecodable.HasRequest);
        Assert.Equal(0, undecodableRequest.CreateTaskCallCount);
        Assert.Equal(
            DurableTaskStatus.Pending,
            (await runtime.SubscribeOrPollAsync(undecodableId, new SubscribeOrPollOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(TimeSpan.FromMinutes(1), runtime.GetWakeupDelay());
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 稍后启动,其代码解码请求的代码将执行任务。
        undecodable.RequestDecodeFailure = null;
        var (replacement, _, _, _) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => undecodable.Result is { IsCompleted: true });
        Assert.Equal(7, undecodable.Result!.GetResult<int>());
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        TestTaskState AddRecoveredRequest(TaskId taskId, RuntimeTestDurableTaskRequest request)
        {
            var state = storage.GetOrCreate(taskId, DurableTaskRecordKind.Invocation);
            storage.SetRequest(taskId, state, request);
            storage.SetRequestFingerprint(taskId, state, IDurableTaskRequest.GetFingerprint(request, CreateSerializer()), IDurableTaskRequest.GetMethod(request));
            storage.SetIncarnation(taskId, state, Guid.NewGuid());
            return state;
        }
    });

    [Fact]
    public Task DirectCancellationOfAnUnknownTaskThrowsNotFoundAndStoresNothing() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var missing = TaskId.CreateRoot("missing");

        var fromClient = await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            () => ((IDurableTaskServer)runtime).CancelAsync(missing, "client", TestContext.Current.CancellationToken).AsTask());
        var fromManager = await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            () => ((IDurableTaskManager)runtime).CancelAsync(missing, "grain", TestContext.Current.CancellationToken).AsTask());

        Assert.Equal((missing, missing), (fromClient.TaskId, fromManager.TaskId));
        Assert.Empty(storage.Tasks);
        Assert.Equal(0, manager.WriteCount);
    });

    [Fact]
    public Task InboxCancellationOfACallersChildWhichHasNotArrivedIsRecorded() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var caller = GrainId.Create("caller", "one");
        var childId = TaskId.Parse("starter/call");
        var storedId = CallerScope.GetTaskId(caller, childId);

        // 打电话者取消孩子的电话 可能会超过孩子的召唤 所以收件箱记录下来
        Assert.True(CreateHandler(runtime).TryAccept(CancellationMessage(caller, runtime.GrainId, childId, "caller"), out var result));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.IsType<DeliveryResult.Accepted>(result.Value);
        var record = storage.Get(storedId);
        Assert.Equal(DurableTaskRecordKind.EarlyCancellation, record.Kind);
        Assert.Equal(caller, record.CallerId);
        Assert.Equal("caller", record.CancellationReason);
        Assert.False(record.HasRequest);
    });
}
