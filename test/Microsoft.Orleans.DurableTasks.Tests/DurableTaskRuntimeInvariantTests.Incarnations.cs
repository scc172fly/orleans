using Microsoft.Extensions.Time.Testing;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 根任务 ID 运行的运行过程: 每个运行都有自己的化身, 由它来命名结点的信息的身份
/// 之后的运行既不是用较早的运行方式分享消息, 也不观察早期运行模式的老套答复。
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

        // 第一个运行的第一个调序调用另一个 grain , 然后取消调用。 它的结果没有保留, 所以下面的写
        // 呼声的结果是删除了跑树
        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, CreateRequest(1, CallAndCancel), cancellationToken);
        await WaitUntilAsync(() => transport.Invocations.Count == 1 && transport.Cancellations.Count == 1);
        var firstWireId = transport.Invocations[0].TaskId;
        var firstInvocationId = Assert.Single(transport.SentInvocations);
        Assert.Equal(CallerScope.GetWireId(storage.Get(rootId).Incarnation, callId), firstWireId);
        Assert.Equal(firstWireId, transport.Cancellations[0].TaskId);
        runtime.AcceptResponseFromInbox(firstWireId, CallerScope.GetTaskId(runtime.GrainId, firstWireId), DurableTaskResponse.Canceled, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);
        await WaitUntilAsync(() => !storage.Contains(rootId) && !storage.Contains(callId));

        // 同样的根码在Message的解码窗口内再次运行, 并记录在同一ID下调用。
        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, CreateRequest(1, CallAndCancel), cancellationToken);
        var handle = runtime.GetScheduledTaskHandle(rootId);
        await WaitUntilAsync(() => transport.Invocations.Count == 2 && transport.Cancellations.Count == 2);
        var secondWireId = transport.Invocations[1].TaskId;

        // 跑跑的调用和取消使用 以跑自己的电线识别码拨打的电话名 所以他们的留言身份与
        // 第一跑的,目标 储存调用 在另一个身份。
        Assert.Equal(CallerScope.GetWireId(storage.Get(rootId).Incarnation, callId), secondWireId);
        Assert.NotEqual(firstWireId, secondWireId);
        Assert.Equal(secondWireId, transport.Cancellations[1].TaskId);
        Assert.Equal(2, transport.SentInvocations.Count);
        Assert.NotEqual(firstInvocationId, transport.SentInvocations[1]);
        Assert.NotEqual(
            DurableTaskMessageTransport.CreateMessageId(runtime.GrainId, RemoteTarget, firstWireId, DurableTaskMessageTypes.CancellationSubject),
            DurableTaskMessageTransport.CreateMessageId(runtime.GrainId, RemoteTarget, secondWireId, DurableTaskMessageTypes.CancellationSubject));
        Assert.NotEqual(CallerScope.GetTaskId(runtime.GrainId, firstWireId), CallerScope.GetTaskId(runtime.GrainId, secondWireId));

        // 第一跑的招工退休, 第一次跑的电话完成日期晚到。
        // 到达第二波的呼叫
        ((IOutboxMessageSource)runtime).OnRetired(
            firstInvocationId,
            new OutboxMessageRetirement.Rejected("conflict", DurableTaskRejectionCodes.Conflict));
        runtime.AcceptResponseFromInbox(firstWireId, CallerScope.GetTaskId(runtime.GrainId, firstWireId), DurableTaskResponse.Completed, RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);
        Assert.Null(storage.Get(callId).Accepted);
        Assert.Null(storage.Get(callId).Result);
        Assert.Null(storage.Get(rootId).Result);

        // 第二波的完成完成
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

        // 运行端, 结果没有保留, 所以后面的写作会删除树 。
        await runtime.AbortTaskAsync(rootId, "finished", cancellationToken);
        await WaitUntilAsync(() => !storage.Contains(rootId) && !storage.Contains(delayId));

        // 后期记录也出现同样的延迟, 同一代人和适当时间, 由另一个化身。
        storage.AddInvoked(rootId);
        await runtime.ScheduleDelayAsync(delayId, TimeSpan.FromMinutes(5), cancellationToken);
        var current = transport.Resumes[1];
        Assert.Equal((stale.TaskId, stale.Generation, stale.DueTime), (current.TaskId, current.Generation, current.DueTime));
        Assert.Equal(storage.Get(rootId).Incarnation, current.Incarnation);
        Assert.NotEqual(stale.Incarnation, current.Incarnation);
        Assert.NotEqual(
            DurableTaskMessageTransport.CreateResumeMessageId(runtime.GrainId, delayId, stale.Incarnation, stale.Generation, stale.DueTime),
            DurableTaskMessageTransport.CreateResumeMessageId(runtime.GrainId, delayId, current.Incarnation, current.Generation, current.DueTime));

        // 早期运行恢复后,没有改变任何东西; 后一个运行的自身恢复完成延迟。
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

        // 之前的一棵来电树 取消了它的电话 电话的祈祷没有到 所以这个 grain 记录
        // 在前一运行的范围代号下取消 。
        Accept(handler, CancellationMessage(sender, runtime.GrainId, earlierWireId));
        Assert.NotNull(storage.Get(CallerScope.Resolve(sender, earlierWireId)).CancellationRequestedAt);

        // 稍后的调用者用相同的节点识别码来呼唤其呼叫,而这个 grain 在另一个身份识别码下储存。
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

        // 只有另一个 grain的调试在 grain的范围中存储任务。
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
