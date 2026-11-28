using Microsoft.Extensions.Time.Testing;
using Orleans.Durable.Tasks;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>对其他 Grain 的调用进行竞速和超时处理：取消未胜出的输入，并在等待过程中恢复。</summary>
public sealed partial class DurableTaskRuntimeInvariantTests
{
    private const string RaceLostReason = "Another task won the race.";
    private const string PaymentTimeoutReason = "The operation timed out after 00:05:00.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TimeoutCancelsARemoteCallWithItsReasonAndWaitsForTheCallsOutcome(bool callCompletesAnyway) => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, storage, manager, transport) = CreateRuntime(timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        var rootId = TaskId.CreateRoot("order");
        var callId = rootId.Child("pay").Child("0");
        var canceling = ObserveCancellation(transport, callId);
        runtime.ScheduleFromInbox(rootId, CreateRequest(1, PayWithTimeout));
        await manager.WriteStateAsync(cancellationToken);
        await transport.ResumeSent.WaitAsync(CancellationTestTimeout, cancellationToken);
        var resume = Assert.Single(transport.Resumes);
        Assert.Equal((rootId.Child("pay").Child("1"), DateTimeOffset.UnixEpoch.AddMinutes(5)), (resume.TaskId, resume.DueTime));

        await HandleAsync(CreateHandler(runtime), ResumeContext(runtime, resume.TaskId, resume.Generation, resume.DueTime));
        await manager.WriteStateAsync(cancellationToken);
        await canceling.WaitAsync(CancellationTestTimeout, cancellationToken);

        // 被调用方收到超时原因，工作流等待调用的结果。
        Assert.Equal(PaymentTimeoutReason, Assert.Single(transport.CancellationReasons));
        Assert.Equal(PaymentTimeoutReason, storage.Get(callId).CancellationReason);
        Assert.Null(storage.Get(rootId).Result);
        runtime.AcceptResponseFromInbox(callId, callCompletesAnyway ? DurableTaskResponse.Completed : DurableTaskResponse.Canceled, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);

        Assert.Equal(callCompletesAnyway ? "paid" : PaymentTimeoutReason, (await WaitForResponseAsync(runtime, rootId)).GetResult<string>());
    });

    [Fact]
    public Task RecoveryMidWaitKeepsTheTimeoutsRecordedDueTime() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var (runtime, storage, manager, transport) = CreateRuntime(timeProvider: clock);
        var rootId = TaskId.CreateRoot("order");
        var callId = rootId.Child("pay").Child("0");
        var delayId = rootId.Child("pay").Child("1");
        runtime.ScheduleFromInbox(rootId, CreateRequest(1, PayWithTimeout));
        await manager.WriteStateAsync(cancellationToken);
        await transport.ResumeSent.WaitAsync(CancellationTestTimeout, cancellationToken);
        var resume = Assert.Single(transport.Resumes);

        // 激活实例在工作流等待期间停止，下一个激活实例在三分钟后重放工作流。
        await runtime.StopAsync(cancellationToken).WaitAsync(CancellationTestTimeout, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(3));
        var (replacement, _, _, replacementTransport) = CreateRuntime(storage, manager, timeProvider: clock);
        var canceling = ObserveCancellation(replacementTransport, callId);
        await replacement.ResumePendingTasksAsync(cancellationToken);
        await HandleAsync(CreateHandler(replacement), ResumeContext(replacement, resume.TaskId, resume.Generation, resume.DueTime));
        await manager.WriteStateAsync(cancellationToken);
        await canceling.WaitAsync(CancellationTestTimeout, cancellationToken);
        replacement.AcceptResponseFromInbox(callId, DurableTaskResponse.Canceled, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);

        Assert.Equal(PaymentTimeoutReason, (await WaitForResponseAsync(replacement, rootId)).GetResult<string>());
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(5), resume.DueTime);
        Assert.Empty(replacementTransport.Resumes);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(delayId).Result!.Status);
        Assert.Equal(PaymentTimeoutReason, Assert.Single(replacementTransport.CancellationReasons));
    });

    [Fact]
    public Task RaceCancelsTheLosingRemoteCallsAndWaitsForTheirOutcomes() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, storage, manager, transport) = CreateRuntime();
        var rootId = TaskId.CreateRoot("shipping");
        var quoteIds = Enumerable.Range(0, 3).Select(index => rootId.Child("quote").Child($"{index}")).ToArray();
        var invoked = ObserveInvocations(transport, quoteIds.Length);
        var canceling = ObserveCancellations(transport, 2);
        runtime.ScheduleFromInbox(rootId, CreateRequest(1, () => DurableTask.Run(async _ =>
            await DurableTask.Race(quoteIds.Select(static (_, index) => (DurableTask)new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(index + 2))))
                .WithId("quote"))));
        await manager.WriteStateAsync(cancellationToken);
        await invoked.WaitAsync(CancellationTestTimeout, cancellationToken);

        runtime.AcceptResponseFromInbox(quoteIds[1], DurableTaskResponse.Completed, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);
        await canceling.WaitAsync(CancellationTestTimeout, cancellationToken);

        Assert.Equal([quoteIds[0], quoteIds[2]], transport.Cancellations.Select(static cancellation => GetNodeId(cancellation.TaskId)).OrderBy(static id => id.ToString(), StringComparer.Ordinal));
        Assert.Equal([RaceLostReason, RaceLostReason], transport.CancellationReasons);

        // 竞速操作等待其他所有调用进入终态：一个以取消结束，另一个仍然正常完成。
        runtime.AcceptResponseFromInbox(quoteIds[0], DurableTaskResponse.Canceled, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);
        Assert.Null(storage.Get(rootId).Result);
        runtime.AcceptResponseFromInbox(quoteIds[2], DurableTaskResponse.Completed, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);

        Assert.Equal(1, (await WaitForResponseAsync(runtime, rootId)).GetResult<int>());
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(quoteIds[0]).Result!.Status);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(quoteIds[2]).Result!.Status);
    });

    [Fact]
    public Task CancellationOfARaceReachesEveryRemoteCall() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, _, manager, transport) = CreateRuntime();
        var rootId = TaskId.CreateRoot("shipping");
        var quoteIds = Enumerable.Range(0, 2).Select(index => rootId.Child("quote").Child($"{index}")).ToArray();
        var invoked = ObserveInvocations(transport, quoteIds.Length);
        var canceling = ObserveCancellations(transport, quoteIds.Length);
        runtime.ScheduleFromInbox(rootId, CreateRequest(1, () => DurableTask.Run(async _ =>
            await DurableTask.Race(quoteIds.Select(static (_, index) => (DurableTask)new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(index + 2))))
                .WithId("quote"))));
        await manager.WriteStateAsync(cancellationToken);
        await invoked.WaitAsync(CancellationTestTimeout, cancellationToken);

        await ((IDurableTaskManager)runtime).CancelAsync(rootId, "buyer", cancellationToken);
        await canceling.WaitAsync(CancellationTestTimeout, cancellationToken);
        Assert.Equal(["buyer", "buyer"], transport.CancellationReasons);
        foreach (var quoteId in quoteIds)
        {
            runtime.AcceptResponseFromInbox(quoteId, DurableTaskResponse.Canceled, RemoteTarget);
        }

        await manager.WriteStateAsync(cancellationToken);

        Assert.Equal(DurableTaskStatus.Canceled, (await WaitForResponseAsync(runtime, rootId)).Status);
    });

    private static GrainId RemoteTarget => CreateRemoteRequest(0).Context!.TargetId;

    private static DurableTask<string> PayWithTimeout() => DurableTask.Run(async _ =>
    {
        try
        {
            await new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(2)).WaitAsync(TimeSpan.FromMinutes(5)).WithId("pay");
            return "paid";
        }
        catch (TimeoutException exception) when (exception.InnerException is OperationCanceledException)
        {
            return exception.Message;
        }
    });

    private static Task ObserveCancellation(RecordingDurableTaskMessageTransport transport, TaskId taskId)
    {
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.BeforeSendCancellation = (_, _, canceled) =>
        {
            if (GetNodeId(canceled) == taskId)
            {
                sent.TrySetResult();
            }
        };
        return sent.Task;
    }

    private static Task ObserveCancellations(RecordingDurableTaskMessageTransport transport, int count)
    {
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = count;
        transport.BeforeSendCancellation = (_, _, _) =>
        {
            if (Interlocked.Decrement(ref remaining) == 0)
            {
                sent.TrySetResult();
            }
        };
        return sent.Task;
    }

    /// <summary>返回传输标识符 <paramref name="wireId"/> 所指定的此 Grain 执行树节点。</summary>
    private static TaskId GetNodeId(TaskId wireId)
    {
        Assert.True(global::Orleans.Durable.Tasks.Runtime.CallerScope.TryParseWireId(wireId, out _, out var nodeId));
        return nodeId;
    }

    private static Task ObserveInvocations(RecordingDurableTaskMessageTransport transport, int count)
    {
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = count;
        transport.BeforeSendInvocation = (_, _, _, _) =>
        {
            if (Interlocked.Decrement(ref remaining) == 0)
            {
                sent.TrySetResult();
            }
        };
        return sent.Task;
    }
}
