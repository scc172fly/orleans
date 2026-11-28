using Microsoft.Extensions.Time.Testing;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 同一根任务 ID 的多次运行：每次运行都有独立的运行世代标识，该标识包含在标识执行树节点的消息 ID 中，
/// 因此后续运行既不与先前运行共用消息，也不会看到先前运行的过期回复。
/// </summary>
public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Fact]
    public Task RootWhichRunsAgainAfterItsDeletionSendsItsOwnMessagesAndIgnoresTheEarlierRunsReplies() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, storage, manager, transport) = CreateRuntime(TimeSpan.Zero);
        var rootId = TaskId.CreateRoot("order");
        var callId = rootId.Child("call");

        // 首次运行调用另一个 Grain 并取消该调用。其结果无需保留，因此
        // 在调用产生结果后的写入会删除本次运行的执行树。
        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, CreateRequest(1, CallAndCancel), cancellationToken);
        await WaitUntilAsync(() => transport.Invocations.Count == 1 && transport.Cancellations.Count == 1);
        var firstWireId = transport.Invocations[0].TaskId;
        var firstInvocationId = Assert.Single(transport.SentInvocations);
        Assert.Equal(CallerScope.GetWireId(storage.Get(rootId).Incarnation, callId), firstWireId);
        Assert.Equal(firstWireId, transport.Cancellations[0].TaskId);
        runtime.AcceptResponseFromInbox(firstWireId, CallerScope.GetTaskId(runtime.GrainId, firstWireId), DurableTaskResponse.Canceled, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);
        await WaitUntilAsync(() => !storage.Contains(rootId) && !storage.Contains(callId));

        // 在 Messaging 的去重窗口内再次运行同一根任务 ID，并以相同 ID 记录调用。
        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, CreateRequest(1, CallAndCancel), cancellationToken);
        var handle = runtime.GetScheduledTaskHandle(rootId);
        await WaitUntilAsync(() => transport.Invocations.Count == 2 && transport.Cancellations.Count == 2);
        var secondWireId = transport.Invocations[1].TaskId;

        // 本次运行的调用和取消消息使用本次运行自己的传输标识符来标识调用，因此其消息 ID 不同于
        // 首次运行的消息 ID，目标也以另一个 ID 存储该调用。
        Assert.Equal(CallerScope.GetWireId(storage.Get(rootId).Incarnation, callId), secondWireId);
        Assert.NotEqual(firstWireId, secondWireId);
        Assert.Equal(secondWireId, transport.Cancellations[1].TaskId);
        Assert.Equal(2, transport.SentInvocations.Count);
        Assert.NotEqual(firstInvocationId, transport.SentInvocations[1]);
        Assert.NotEqual(
            DurableTaskMessageTransport.CreateMessageId(runtime.GrainId, RemoteTarget, firstWireId, DurableTaskMessageTypes.CancellationSubject),
            DurableTaskMessageTransport.CreateMessageId(runtime.GrainId, RemoteTarget, secondWireId, DurableTaskMessageTypes.CancellationSubject));
        Assert.NotEqual(CallerScope.GetTaskId(runtime.GrainId, firstWireId), CallerScope.GetTaskId(runtime.GrainId, secondWireId));

        // 首次运行的调用消息退役，且发往首次运行调用的完成消息迟到。这两者均
        // 不会传递到第二次运行的调用。
        ((IOutboxMessageSource)runtime).OnRetired(
            firstInvocationId,
            new OutboxMessageRetirement.Rejected("conflict", DurableTaskRejectionCodes.Conflict));
        runtime.AcceptResponseFromInbox(firstWireId, CallerScope.GetTaskId(runtime.GrainId, firstWireId), DurableTaskResponse.Completed, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);
        Assert.Null(storage.Get(callId).Accepted);
        Assert.Null(storage.Get(callId).Result);
        Assert.Null(storage.Get(rootId).Result);

        // 第二次运行自己的完成消息使其调用完成。
        runtime.AcceptResponseFromInbox(secondWireId, CallerScope.GetTaskId(runtime.GrainId, secondWireId), DurableTaskResponse.Canceled, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);
        Assert.Equal(nameof(DurableTaskStatus.Canceled), (await WaitForResponseAsync(handle)).GetResult<string>());

        static DurableTask<string> CallAndCancel() => DurableTask.Run(async cancellationToken =>
        {
            var call = new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(2)).WithId("call").Post();
            await call.CancelAsync("superseded", cancellationToken);
            return $"{(await call.GetResponseAsync(CancellationToken.None)).Status}";
        });
    });

    [Fact]
    public Task ResumeOfAnEarlierRunDoesNotCompleteALaterRunsDelayWithTheSameIdAndGeneration() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var (runtime, storage, _, transport) = CreateRuntime(TimeSpan.Zero, timeProvider: clock);
        var rootId = TaskId.Parse("root");
        var delayId = rootId.Child("delay");
        storage.AddInvoked(rootId);
        await runtime.ScheduleDelayAsync(delayId, TimeSpan.FromMinutes(5), cancellationToken);
        var stale = Assert.Single(transport.Resumes);

        // 本次运行结束且结果无需保留，因此随后的写入会删除其执行树。
        await runtime.AbortTaskAsync(rootId, "finished", cancellationToken);
        await WaitUntilAsync(() => !storage.Contains(rootId) && !storage.Contains(delayId));

        // 后续运行在不同的运行世代下记录相同延迟，具有相同的世代编号和到期时间。
        storage.AddInvoked(rootId);
        await runtime.ScheduleDelayAsync(delayId, TimeSpan.FromMinutes(5), cancellationToken);
        var current = transport.Resumes[1];
        Assert.Equal((stale.TaskId, stale.Generation, stale.DueTime), (current.TaskId, current.Generation, current.DueTime));
        Assert.Equal(storage.Get(rootId).Incarnation, current.Incarnation);
        Assert.NotEqual(stale.Incarnation, current.Incarnation);
        Assert.NotEqual(
            DurableTaskMessageTransport.CreateResumeMessageId(runtime.GrainId, delayId, stale.Incarnation, stale.Generation, stale.DueTime),
            DurableTaskMessageTransport.CreateResumeMessageId(runtime.GrainId, delayId, current.Incarnation, current.Generation, current.DueTime));

        // 先前运行的恢复消息不会改变任何状态；后续运行自己的恢复消息使延迟操作完成。
        var handler = CreateHandler(runtime);
        await HandleAsync(handler, ResumeContext(runtime, delayId, stale.Generation, stale.DueTime, stale.Incarnation));
        Assert.Null(storage.Get(delayId).Result);
        await HandleAsync(handler, ResumeContext(runtime, delayId, current.Generation, current.DueTime, current.Incarnation));
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(delayId).Result!.Status);
    });

    [Fact]
    public Task EarlyCancellationOfAnEarlierCallerRunDoesNotCancelTheLaterRunsInvocation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var handler = CreateHandler(runtime);
        var sender = GrainId.Create("caller", "one");
        var callId = TaskId.Parse("starter/call");
        var earlierWireId = CallerScope.GetWireId(Guid.NewGuid(), callId);
        var wireId = CallerScope.GetWireId(Guid.NewGuid(), callId);

        // 调用方执行树的先前运行取消了一个调用，但该调用消息从未到达，因此此 Grain 将
        // 取消记录在先前运行的作用域 ID 下。
        Accept(handler, CancellationMessage(sender, runtime.GrainId, earlierWireId));
        Assert.NotNull(storage.Get(CallerScope.Resolve(sender, earlierWireId)).CancellationRequestedAt);

        // 调用方的后续运行以相同的节点 ID 发起调用，此 Grain 将其存储在另一个 ID 下。
        Accept(
            handler,
            MessagingTestHelpers.CreateInvocationMessage(
                sender,
                runtime.GrainId,
                wireId,
                new DurableTaskInvocationMessage { TaskId = wireId, Request = CreateSerializableRequest(runtime.GrainId), Sender = sender },
                runtime.UtcNow));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        var storedId = CallerScope.Resolve(sender, wireId);
        await WaitUntilAsync(() => storage.Get(storedId).Result is { IsCompleted: true });
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(storedId).Result!.Status);
        Assert.Null(storage.Get(storedId).CancellationRequestedAt);
        Assert.Equal([new DurableTaskSubscriber(sender, wireId)], storage.Get(storedId).CompletionDestinations);
        Assert.Null(storage.Get(CallerScope.Resolve(sender, earlierWireId)).Result);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task ClientScheduleBeneathTheReservedCallerScopeRootIsRejected() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var scopedId = CallerScope.GetTaskId(GrainId.Create("caller", "one"), CallerScope.GetWireId(Guid.NewGuid(), TaskId.Parse("starter/call")));

        // 只有另一个 Grain 的调用才会在该 Grain 的作用域下存储任务。
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await ((IDurableTaskServer)runtime).ScheduleAsync(scopedId, CreateRequest(1), TestContext.Current.CancellationToken));

        Assert.Empty(storage.Tasks);
        Assert.Equal(0, manager.WriteCount);
    });

    private static async Task<DurableTaskResponse> WaitForResponseAsync(IScheduledTaskHandle handle) =>
        await handle.WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(CancellationTestTimeout, TestContext.Current.CancellationToken);
}
