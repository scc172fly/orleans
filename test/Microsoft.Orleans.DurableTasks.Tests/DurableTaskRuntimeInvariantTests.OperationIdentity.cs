using Orleans.CodeGeneration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 操作标识：根任务的重放或重试若与某条记录具有相同的
/// 类型和标识符，并对同一目标调用同一方法，则属于同一操作。不比较参数，因此
/// 即使部署改变了参数的序列化方式，重放仍会执行相同的操作。
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

        // 部署改变了调用参数的序列化方式：重放仍在同一个 Grain 上调用同一方法。
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

        // 替代激活实例使用序列化形式不同的参数重放调用。该调用找到其
        // 记录，并与表中保存的调用消息合并，保留该消息已写入日志的请求。
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

        // 在另一个 Grain 上调用同一方法属于另一个操作。
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

        // 客户端以相同标识符重试同一方法时，无论参数是什么，均找到已存储的任务。
        var retry = CreateRequest(2);
        var response = await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, retry, TestContext.Current.CancellationToken);

        Assert.Equal(DurableTaskStatus.Pending, response.Status);
        Assert.Equal(0, retry.CreateTaskCallCount);
        Assert.Same(first, storage.Get(rootId).Request);
        Assert.Equal(fingerprint, storage.Get(rootId).RequestFingerprint);

        // 在相同标识符下调用另一个方法会产生冲突。
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

        // 重新投递的请求具有不同的序列化形式（此处使用其他调用选项），但仍调用同一方法。
        var reserialized = CreateSerializableRequest(runtime.GrainId);
        reserialized.AddInvokeMethodOptions(InvokeMethodOptions.OneWay);
        Assert.True(handler.TryAccept(Invocation(reserialized), out var redelivered));
        Assert.IsType<DeliveryResult.Accepted>(redelivered.Value);
        Assert.Equal(fingerprint, storage.Get(storedId).RequestFingerprint);

        // 使用调用方的标识符调用其他方法会发生冲突，收件箱将其转为死信。
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

    /// <summary>创建一个寻址方式与 <see cref="CreateRequest"/> 相同、但调用不同方法的请求。</summary>
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
