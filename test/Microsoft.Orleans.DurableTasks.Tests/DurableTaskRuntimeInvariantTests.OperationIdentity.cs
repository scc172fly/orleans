using Orleans.CodeGeneration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 操作身份: 重放或重试根任务, 当有记录时, 其重播或重试是和记录相同的操作
/// 参数没有比较,所以是部署
/// 参数如何序列化重播相同操作,则改变该参数的顺序。
/// </summary>
public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Fact]
    public Task ReplayedCallWithOtherArgumentsReusesTheRecordedCall() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        var callId = taskId.Child("charge");
        storage.AddInvoked(TaskId.Parse("root"));
        var recordedCall = CreateRemoteChild(1);
        var recorded = storage.GetOrCreate(callId, DurableTaskRecordKind.RemoteCall);
        storage.SetRemoteRequest(
            callId,
            recorded,
            recordedCall.Context!.TargetId,
            IDurableTaskRequest.GetMethod(recordedCall),
            IDurableTaskRequest.GetFingerprint(recordedCall, CreateSerializer()));
        storage.SetResponse(callId, recorded, DurableTaskResponse.FromResult(42));
        var fingerprint = recorded.RemoteRequestFingerprint;
        using var stalls = new StallRecorder(taskId);

        // 部署改变了调用参数的顺序:重播对同一 grain采用相同方法。
        var handle = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(async () => await CreateRemoteChild(2).WithId("charge")),
            TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(taskId).Result is { IsCompleted: true });
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, (await handle.WaitAsync(TestContext.Current.CancellationToken)).Status);
        Assert.False(stalls.Stalled.IsCompleted);
        Assert.Empty(transport.Invocations);
        Assert.Equal(fingerprint, storage.Get(callId).RemoteRequestFingerprint);
        Assert.Equal(42, storage.Get(callId).Result!.GetResult<int>());
    });

    [Fact]
    public Task ReplayedPendingCallWithOtherArgumentsCoalescesWithItsInvocation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var childId = starterId.Child("call");
        var argument = 1;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ => await CreateRemoteChild(argument).WithId("call")));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.SentInvocations.Count == 1);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var messageId = Assert.Single(transport.SentInvocations);
        var fingerprint = storage.Get(childId).RemoteRequestFingerprint;

        // 替换激活程序重放调用参数, 其序列化不同。 调用到它
        // 与表格的引用合并,并按日记记录和合并,保留其请求。
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        argument = 2;
        var (replacement, _, _, replacementTransport) = CreateRuntime(storage, manager);
        using var stalls = new StallRecorder(starterId);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => replacementTransport.Invocations.Count == 1);

        Assert.Equal(2, replacementTransport.Invocations[0].Request.GetArgument(0));
        Assert.Empty(replacementTransport.SentInvocations);
        Assert.True(storage.Invocations.ContainsKey(messageId));
        Assert.Equal(fingerprint, storage.Get(childId).RemoteRequestFingerprint);
        Assert.False(stalls.Stalled.IsCompleted);
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task ReplayedCallToAnotherTargetStallsTheAttempt() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        var callId = taskId.Child("charge");
        storage.AddInvoked(TaskId.Parse("root"));
        var recordedCall = CreateRemoteChild(1);
        storage.SetRemoteRequest(
            callId,
            storage.GetOrCreate(callId, DurableTaskRecordKind.RemoteCall),
            recordedCall.Context!.TargetId,
            IDurableTaskRequest.GetMethod(recordedCall),
            IDurableTaskRequest.GetFingerprint(recordedCall, CreateSerializer()));
        var otherTarget = CreateRemoteRequest(1);
        otherTarget.Context!.TargetId = GrainId.Create("target", "two");
        using var stalls = new StallRecorder(taskId);

        // 另一种 grain 的同一方法是另一种操作。
        _ = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(async () => await new TestStateManager.TestRemoteDurableTask(otherTarget).WithId("charge")),
            TestContext.Current.CancellationToken);

        var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
        Assert.Contains("on grain 'target/two'", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Null(storage.Get(callId).Result);
        Assert.Empty(transport.Invocations);
    });

    [Fact]
    public Task ReplayedDetachedStartWithOtherArgumentsReusesTheRecordAndAnotherMethodDiverges() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("starter"));
        var recordId = TaskId.Parse("starter/$detach:target:first");
        var rootId = TaskId.CreateRoot("first");
        runtime.PostDetached(recordId, rootId, CreateRemoteRequest(1), shielded: false);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var fingerprint = storage.Get(recordId).RequestFingerprint;

        runtime.PostDetached(recordId, rootId, CreateRemoteRequest(2), shielded: false);
        Assert.Single(transport.DetachedInvocations);
        Assert.Equal(fingerprint, storage.Get(recordId).RequestFingerprint);

        var exception = Assert.Throws<DurableTaskDivergenceException>(
            () => runtime.PostDetached(recordId, rootId, CreateOtherMethodRequest(1, GrainId.Create("target", "one")), shielded: false));
        Assert.Contains("different operation", exception.Message, StringComparison.Ordinal);
        Assert.Single(transport.DetachedInvocations);
    });

    [Fact]
    public Task RootScheduledAgainWithOtherArgumentsReturnsTheStoredTask() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var rootId = TaskId.CreateRoot("order");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CreateRequest(1, () => DurableTask.Run(async _ => await release.Task));
        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, first, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => first.CreateTaskCallCount == 1);
        var fingerprint = storage.Get(rootId).RequestFingerprint;

        // 客户端在同一身份识别特征下重试同一方法,无论论点如何,都可达到存储的任务。
        var retry = CreateRequest(2);
        var response = await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, retry, TestContext.Current.CancellationToken);

        Assert.Equal(DurableTaskStatus.Pending, response.Status);
        Assert.Equal(0, retry.CreateTaskCallCount);
        Assert.Same(first, storage.Get(rootId).Request);
        Assert.Equal(fingerprint, storage.Get(rootId).RequestFingerprint);

        // 同一识别特征冲突下的另一个方法。
        var conflict = await Assert.ThrowsAsync<DurableTaskConflictException>(
            async () => await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, CreateOtherMethodRequest(1), TestContext.Current.CancellationToken));
        Assert.Contains($"Durable task '{rootId}' is already associated with a different request.", conflict.Message, StringComparison.Ordinal);
        Assert.Equal(rootId, conflict.TaskId);

        release.SetResult();
        await WaitUntilAsync(() => storage.Get(rootId).Result is { IsCompleted: true });
        Assert.Equal(1, first.CreateTaskCallCount);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task InboxInvocationOfTheStoredMethodIsAcceptedAndAnotherMethodConflicts() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var sender = GrainId.Create("caller", "one");
        var taskId = TaskId.Parse("starter/call");
        var storedId = CallerScope.GetTaskId(sender, taskId);
        var handler = CreateHandler(runtime);
        Assert.True(handler.TryAccept(Invocation(CreateSerializableRequest(runtime.GrainId)), out var first));
        Assert.IsType<DeliveryResult.Accepted>(first.Value);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var fingerprint = storage.Get(storedId).RequestFingerprint;

        // 请求重复交付的重新交付,其顺序不同,这里与其他援引选择一起使用同一方法。
        var reserialized = CreateSerializableRequest(runtime.GrainId);
        reserialized.AddInvokeMethodOptions(InvokeMethodOptions.OneWay);
        Assert.True(handler.TryAccept(Invocation(reserialized), out var redelivered));
        Assert.IsType<DeliveryResult.Accepted>(redelivered.Value);
        Assert.Equal(fingerprint, storage.Get(storedId).RequestFingerprint);

        // 使用另一种方法 在打电话者的身份识别冲突, 和收件箱的死字母。
        var other = new TestOtherMethodDurableTaskRequest
        {
            Context = new DurableTaskRequestContext { CallerId = sender, TargetId = runtime.GrainId, SupportsDurableCompletion = true },
        };
        Assert.True(handler.TryAccept(Invocation(other), out var conflict));
        Assert.Equal(DurableTaskRejectionCodes.Conflict, Assert.IsType<DeliveryResult.DeadLettered>(conflict.Value).Code);
        Assert.Contains($"Durable task '{storedId}' is already associated with a different request.", Assert.IsType<DeliveryResult.DeadLettered>(conflict.Value).Reason, StringComparison.Ordinal);
        Assert.Equal(fingerprint, storage.Get(storedId).RequestFingerprint);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        InboxMessage Invocation(IDurableTaskRequest request) =>
            MessagingTestHelpers.CreateInvocationMessage(
                sender,
                runtime.GrainId,
                taskId,
                new DurableTaskInvocationMessage { TaskId = taskId, Request = request, Sender = sender },
                runtime.UtcNow);
    });

    [Fact]
    public void PostedRootCoalescesWithAPendingInvocationOfTheSameMethodWhoseRequestSerializesDifferently()
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var rootId = TaskId.CreateRoot("posted");
        var target = GrainId.Create("target", "one");
        var messageId = transport.GetInvocationMessageId(runtime.GrainId, target, rootId, subscriberId: null);
        var pendingRequest = CreateSerializableRequest(target);
        pendingRequest.Context!.CallerId = default;
        var payload = CreateSerializer().SerializeToArray(
            new DurableTaskInvocationMessage { TaskId = rootId, Request = pendingRequest, Sender = runtime.GrainId, Detached = true });
        storage.Invocations[messageId] = new PendingInvocation(target, payload, RecordId: null);

        runtime.PostRoot(rootId, CreateSerializableRequest(target));

        Assert.Empty(transport.SentInvocations);
        Assert.Same(payload, storage.Invocations[messageId].Payload);
        var exception = Assert.Throws<InvalidOperationException>(
            () => runtime.PostRoot(rootId, new TestOtherMethodDurableTaskRequest { Context = new DurableTaskRequestContext { TargetId = target } }));
        Assert.Contains(messageId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>创建一个比 <see cref="CreateRequest"/>的,地址类似它的其他方法的请求。</summary>
    private static RuntimeTestOtherMethodRequest CreateOtherMethodRequest(int argument, GrainId? target = null) =>
        new(null, argument)
        {
            Context = new DurableTaskRequestContext
            {
                CallerId = GrainId.Create("caller", "one"),
                TargetId = target ?? GrainId.Create("test", "one"),
                SupportsDurableCompletion = true,
            },
        };
}
