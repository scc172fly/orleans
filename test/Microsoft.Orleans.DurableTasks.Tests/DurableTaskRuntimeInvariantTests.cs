using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using System.Reflection;
using System.Runtime.CompilerServices;
using NSubstitute;
using Orleans.CodeGeneration;
using Orleans.Configuration;
using Orleans.Invocation;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Fact]
    public void RuntimeJournalHookWritesNoEntries()
    {
        var (runtime, _, manager, _) = CreateRuntime();

        Assert.True(manager.TryGetStateMachine(DurableTaskGrainRuntime.StateMachineName, out var stateMachine));
        Assert.Same(runtime, stateMachine);
        var exception = Assert.Throws<InvalidOperationException>(
            () => stateMachine.ReplayEntry(default, default));
        Assert.Contains(DurableTaskGrainRuntime.StateMachineName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public Task OrdinaryGrainActivationFailsBeforeUsingUninitializedJournalAdapter() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, _, _) = CreateRuntime(initialize: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.SignalCancellationAsync(TaskId.Parse("root"), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(DurableGrain), exception.Message, StringComparison.Ordinal);
    });

    [Fact]
    public Task OrdinaryGrainActivationRejectsPollingAndDiagnostics() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, _, _) = CreateRuntime(initialize: false);
        var extension = (IDurableTaskGrainExtension)runtime;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.SubscribeOrPollAsync(TaskId.Parse("root"), default, TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await extension.GetTasksAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken).MoveNextAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await extension.GetRunningTasksAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken).MoveNextAsync());
    });

    [Fact]
    public Task InboxSchedulingStartsOnlyAfterCommit() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, manager, _) = CreateRuntime();
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(1, () => DurableTask.Run(_ => invoked.TrySetResult()));
        var taskId = TaskId.Parse("root");

        var response = runtime.ScheduleFromInbox(taskId, request);

        Assert.Equal(DurableTaskResponseKind.Subscribed, response.ResponseKind);
        Assert.False(invoked.Task.IsCompleted);
        Assert.Equal(0, manager.WriteCount);

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, request.CreateTaskCallCount);
    });

    [Fact]
    public Task DeferredStartRechecksCommittedCancellationBeforeInvocation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var request = CreateRequest(1);
        var taskId = TaskId.Parse("root");
        runtime.ScheduleFromInbox(taskId, request);
        manager.AfterWriteStarted = () =>
        {
            manager.AfterWriteStarted = null;
            var state = storage.Get(taskId);
            storage.RequestCancellation(taskId, state);
            return default;
        };

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(taskId).Result is { IsCompleted: true });

        Assert.Equal(0, request.CreateTaskCallCount);
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(taskId).Result!.Status);
    });

    [Fact]
    public Task EquivalentDuplicateDeliveryExecutesOnce() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, manager, _) = CreateRuntime();
        var first = CreateRequest(1);
        var retry = CreateRequest(1);
        var taskId = TaskId.Parse("root");

        runtime.ScheduleFromInbox(taskId, first);
        runtime.ScheduleFromInbox(taskId, retry);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => first.CreateTaskCallCount + retry.CreateTaskCallCount == 1);

        Assert.Equal(1, first.CreateTaskCallCount + retry.CreateTaskCallCount);
    });

    [Fact]
    public void ConflictingRequestForSameTaskIdFailsBeforeExecution()
    {
        var (runtime, _, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("root");
        runtime.ScheduleFromInbox(taskId, CreateRequest(1));

        var exception = Assert.Throws<DurableTaskConflictException>(
            () => runtime.ScheduleFromInbox(taskId, CreateOtherMethodRequest(1)));

        Assert.Contains("different request", exception.Message, StringComparison.Ordinal);
        Assert.Equal(taskId, exception.TaskId);
    }

    [Fact]
    public void InvocationWithoutRequestContextFailsBeforeChangingDurableState()
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var sender = GrainId.Create("caller", "one");
        var receiver = GrainId.Create("test", "one");
        var message = InvocationMessage(sender, receiver, TaskId.Parse("no-context"), new TestDurableTaskRequest());
        var handler = CreateHandler(runtime);

        // 收件箱将其接收处理器拒绝的消息转入死信；接收处理器事先不暂存任何更改。
        var exception = Assert.Throws<InvalidOperationException>(
            () => handler.TryAccept(message, out _));

        Assert.Equal("The durable task invocation request has no context.", exception.Message);
        Assert.Empty(storage.Tasks);
        Assert.Equal(0, manager.WriteCount);
        Assert.Empty(transport.Invocations);
        Assert.Empty(transport.Completions);
        Assert.Empty(transport.Cancellations);
        Assert.Empty(transport.Resumes);
    }

    [Fact]
    public void InboxInvocationRequiresMatchingTargetAndSender()
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var sender = GrainId.Create("caller", "one");
        var receiver = GrainId.Create("test", "one");
        var request = CreateSerializableRequest(GrainId.Create("target", "other"));
        var handler = CreateHandler(runtime);

        var wrongTarget = InvocationMessage(sender, receiver, TaskId.Parse("wrong-target"), request);
        var targetException = Assert.Throws<InvalidOperationException>(
            () => handler.TryAccept(wrongTarget, out _));
        Assert.Contains("not receiver", targetException.Message, StringComparison.Ordinal);

        request.Context!.TargetId = receiver;
        var missingSender = InvocationMessage(default, receiver, TaskId.Parse("missing-sender"), request, messageIdSender: sender);
        var senderException = Assert.Throws<InvalidOperationException>(
            () => handler.TryAccept(missingSender, out _));
        Assert.Contains("no sender", senderException.Message, StringComparison.Ordinal);

        var selfInvocation = InvocationMessage(receiver, receiver, TaskId.Parse("self"), request);
        var selfException = Assert.Throws<InvalidOperationException>(() => handler.TryAccept(selfInvocation, out _));
        Assert.Contains("owning grain", selfException.Message, StringComparison.Ordinal);

        Assert.Empty(storage.Tasks);
        Assert.Empty(transport.Completions);
    }

    [Fact]
    public void InboxSinkAcceptsDurableRpcCallsAndHandlerHandlesOnlyResumes()
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var handler = CreateHandler(runtime);
        var cancellation = CancellationMessage(GrainId.Create("caller", "one"), GrainId.Create("test", "one"), TaskId.Parse("root"));
        var resume = MessagingTestHelpers.CreateResumeMessage(runtime.GrainId, TaskId.Parse("delay"), Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
        var other = new InboxMessage
        {
            Envelope = new Envelope
            {
                MessageId = MessageId.Create("other", "one"),
                Payload = [],
                Subject = "orders/reserve",
            },
            SentAt = DateTimeOffset.UtcNow,
        };
        var raw = new InboxMessage
        {
            Envelope = new Envelope
            {
                MessageId = MessageId.Create("raw", "one"),
                Payload = [],
            },
            SentAt = DateTimeOffset.UtcNow,
        };

        Assert.False(handler.TryAccept(resume, out _));
        Assert.False(handler.TryAccept(other, out _));
        Assert.False(handler.TryAccept(raw, out _));
        Assert.Empty(storage.Tasks);
        Assert.True(handler.TryAccept(cancellation, out _));
        Assert.True(handler.CanHandle(resume));
        Assert.False(handler.CanHandle(cancellation));
        Assert.False(handler.CanHandle(other));
        Assert.False(handler.CanHandle(raw));
    }

    [Fact]
    public Task InboxCancellationBindsTaskToSender() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var receiver = GrainId.Create("test", "one");
        var caller = GrainId.Create("caller", "one");
        var otherCaller = GrainId.Create("caller", "two");
        var taskId = TaskId.Parse("bound-cancellation");
        var handler = CreateHandler(runtime);

        Accept(handler, CancellationMessage(caller, receiver, taskId));

        Assert.Equal(caller, storage.Get(taskId).CallerId);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var otherCancellation = CancellationMessage(otherCaller, receiver, taskId);
        var cancellationException = Assert.Throws<DurableTaskConflictException>(
            () => handler.TryAccept(otherCancellation, out _));
        Assert.Contains("already associated with caller", cancellationException.Message, StringComparison.Ordinal);

        var request = CreateSerializableRequest(receiver);
        var otherInvocation = InvocationMessage(otherCaller, receiver, taskId, request);
        Assert.True(handler.TryAccept(otherInvocation, out var result));
        Assert.Equal(DurableTaskRejectionCodes.Conflict, Assert.IsType<DeliveryResult.DeadLettered>(result.Value).Code);
        Assert.Contains("already associated with caller", Assert.IsType<DeliveryResult.DeadLettered>(result.Value).Reason, StringComparison.Ordinal);
    });

    [Fact]
    public Task InboxCancellationDefersTaskAndMessageCommitToOuterWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var receiver = GrainId.Create("test", "one");
        var caller = GrainId.Create("caller", "one");
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/remote");
        storage.AddInvoked(rootId);
        var child = storage.GetOrCreate(childId);
        var target = GrainId.Create("target", "one");
        storage.SetRemoteRequest(childId, child, target, "method", "fingerprint");
        var handler = CreateHandler(runtime);

        Accept(handler, CancellationMessage(caller, receiver, rootId));

        Assert.Equal(0, manager.WriteCount);
        Assert.NotNull(storage.Get(rootId).CancellationRequestedAt);
        Assert.NotNull(storage.Get(childId).CancellationRequestedAt);
        var cancellation = Assert.Single(transport.Cancellations);
        Assert.Equal(runtime.GetWireId(childId), cancellation.TaskId);
        Assert.Equal(target, cancellation.Target);
    });

    [Fact]
    public Task RunningInboxCancellationDefersTerminalResponseUntilOuterWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var receiver = GrainId.Create("test", "one");
        var caller = GrainId.Create("caller", "one");
        var taskId = TaskId.Parse("root");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async cancellationToken =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }));
        runtime.ScheduleFromInbox(taskId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var writesBeforeCancellation = manager.WriteCount;
        var handler = CreateHandler(runtime);

        Accept(handler, CancellationMessage(caller, receiver, taskId));

        Assert.Equal(writesBeforeCancellation, manager.WriteCount);
        Assert.Null(storage.Get(taskId).Result);

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(taskId).Result is { IsCompleted: true });
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(taskId).Result!.Status);
    });

    [Fact]
    public Task InboxCancellationPropagatesToRunningDescendantsAfterOuterWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var receiver = GrainId.Create("test", "one");
        var caller = GrainId.Create("caller", "one");
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/child");
        storage.AddInvoked(rootId);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childHandle = await runtime.ScheduleChildAsync(childId, DurableTask.Run(async cancellationToken =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await runtime.ScheduleDelayAsync(
                        TaskId.Parse("root/callback-write"),
                        TimeSpan.Zero,
                        default);
                    throw;
                }
            }), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var handler = CreateHandler(runtime);

        Accept(handler, CancellationMessage(caller, receiver, rootId));
        Assert.False((await childHandle.PollAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken)).IsCompleted);

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            DurableTaskStatus.Canceled,
            (await childHandle.WaitAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Status);
    });

    [Fact]
    public Task BodyWhoseStartAStagedCancellationRefusesObservesTheCancellation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var caller = GrainId.Create("caller", "one");
        var rootId = TaskId.Parse("root");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(1, () => DurableTask.Run(async _ =>
        {
            started.TrySetResult();
            await release.Task;
            try
            {
                await DurableTask.Run(static _ => 2).WithId("step");
                return "completed";
            }
            catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
            {
                var compensation = await DurableTask.Run(static _ => "compensated").WithId("compensate").Shielded();
                return $"{compensation}: {DurableTask.Current.TaskCancellationReason}";
            }
        }));
        runtime.ScheduleFromInbox(rootId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 调用方的取消已暂存，但必须等到提交取消的写入完成后，才将取消传播给任务体。
        // 任务体在该次写入之前启动步骤，因此已暂存的取消会拒绝该步骤。
        Accept(CreateHandler(runtime), CancellationMessage(caller, runtime.GrainId, rootId, "caller stopped"));
        release.SetResult();

        // 任务体观察到导致其步骤被拒绝的取消，并执行补偿操作。
        var response = await runtime.GetScheduledTaskHandle(rootId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal("compensated: caller stopped", response.GetResult<string>());
        Assert.False(storage.Contains(TaskId.Parse("root/step")));
    });

    [Fact]
    public Task InboxBatchStagesCancellationsAndCompletionsWithoutWaitingForBatchWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime(reentrant: true);
        var receiver = GrainId.Create("test", "one");
        var caller = GrainId.Create("caller", "one");
        var target = GrainId.Create("target", "one");
        var handler = CreateHandler(runtime);
        TaskId[] rootIds = [TaskId.Parse("root-a"), TaskId.Parse("root-b"), TaskId.Parse("root-c")];
        TaskId[] remoteIds = [TaskId.Parse("caller/remote-a"), TaskId.Parse("caller/remote-b")];
        storage.AddInvoked(TaskId.Parse("caller"));
        var started = rootIds.ToDictionary(
            static id => id,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        foreach (var rootId in rootIds)
        {
            var rootStarted = started[rootId];
            runtime.ScheduleFromInbox(
                rootId,
                CreateRequest(
                    Array.IndexOf(rootIds, rootId),
                    () => DurableTask.Run(async cancellationToken =>
                    {
                        rootStarted.TrySetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    })));
        }

        var remoteHandles = new List<IScheduledTaskHandle>();
        foreach (var remoteId in remoteIds)
        {
            storage.SetRemoteRequest(remoteId, storage.GetOrCreate(remoteId), target, "method", $"fingerprint-{remoteId}");
            remoteHandles.Add(runtime.GetScheduledTaskHandle(remoteId));
        }

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(started.Values.Select(static source => source.Task))
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var writesBeforeBatch = manager.WriteCount;

        // 收件箱在提交这些消息的写入之前接纳所有消息，因此任何消息的处理均不得等待该次写入。
        InboxMessage[] batch =
        [
            CancellationMessage(caller, receiver, rootIds[0]),
            CompletionMessage(target, receiver, runtime.GetWireId(remoteIds[0]), DurableTaskResponse.FromResult(10)),
            CancellationMessage(caller, receiver, rootIds[1]),
            CompletionMessage(target, receiver, runtime.GetWireId(remoteIds[1]), DurableTaskResponse.FromResult(20)),
            CancellationMessage(caller, receiver, rootIds[2]),
            CancellationMessage(caller, receiver, rootIds[0]),
        ];
        foreach (var message in batch)
        {
            Accept(handler, message);
        }

        Assert.Equal(writesBeforeBatch, manager.WriteCount);
        Assert.All(rootIds, rootId => Assert.Null(storage.Get(rootId).Result));
        foreach (var handle in remoteHandles)
        {
            Assert.False((await handle.PollAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken)).IsCompleted);
        }

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(10, (await remoteHandles[0].WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        Assert.Equal(20, (await remoteHandles[1].WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        await WaitUntilAsync(() => rootIds.All(rootId => storage.Get(rootId).Result is { IsCompleted: true }));
        Assert.All(rootIds, rootId => Assert.Equal(DurableTaskStatus.Canceled, storage.Get(rootId).Result!.Status));
    });

    [Fact]
    public Task CompletionRequiresRecordedRemoteTarget() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var receiver = GrainId.Create("test", "one");
        var request = CreateRemoteRequest(1);
        var expectedTarget = request.Context!.TargetId;
        var taskId = TaskId.Parse("root/remote");
        storage.AddInvoked(TaskId.Parse("root"));
        await runtime.ScheduleChildAsync(taskId, new TestStateManager.TestRemoteDurableTask(request), TestContext.Current.CancellationToken);
        var handler = CreateHandler(runtime);
        var response = DurableTaskResponse.FromResult(42);

        var wrongSender = CompletionMessage(GrainId.Create("target", "other"), receiver, runtime.GetWireId(taskId), response);
        var exception = Assert.Throws<InvalidOperationException>(
            () => handler.TryAccept(wrongSender, out _));
        Assert.Contains("doesn't observe task", exception.Message, StringComparison.Ordinal);
        Assert.Null(storage.Get(taskId).Result);

        Accept(handler, CompletionMessage(expectedTarget, receiver, runtime.GetWireId(taskId), response));

        // 已接受的完成通知仅在任务状态中暂存一次，不会向子任务回发任何内容。
        Assert.Equal(42, storage.Get(taskId).Result!.GetResult<int>());
        Assert.Empty(transport.Completions);
        Assert.Empty(transport.Cancellations);
        Assert.Single(transport.Invocations);
    });

    [Fact]
    public void CompletedRequestReplayDoesNotResendPendingCompletion()
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root");
        var request = CreateRequest(1);
        var state = storage.GetOrCreate(taskId);
        storage.SetRequest(taskId, state, request);
        storage.SetRequestFingerprint(taskId, state, IDurableTaskRequest.GetFingerprint(request, CreateSerializer()), IDurableTaskRequest.GetMethod(request));
        storage.AddCompletionDestination(taskId, state, new(request.Context!.CallerId, taskId));
        storage.SetResponse(taskId, state, DurableTaskResponse.FromResult(42));

        // 列出的某个目标在发件箱中有一条待投递的完成消息；发件箱会在该消息退役时移除该目标。
        var response = runtime.ScheduleFromInbox(taskId, request);

        Assert.Equal(42, response.GetResult<int>());
        Assert.Empty(transport.Completions);
    }

    [Fact]
    public void CompletedRequestReplayResendsCompletionAfterItWasDeadLettered()
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root");
        var request = CreateRequest(1);
        var caller = request.Context!.CallerId;
        var state = storage.GetOrCreate(taskId);
        storage.SetRequest(taskId, state, request);
        storage.SetRequestFingerprint(taskId, state, IDurableTaskRequest.GetFingerprint(request, CreateSerializer()), IDurableTaskRequest.GetMethod(request));
        storage.SetResponse(taskId, state, DurableTaskResponse.FromResult(42));

        var response = runtime.ScheduleFromInbox(taskId, request);

        Assert.Equal(42, response.GetResult<int>());
        var completion = Assert.Single(transport.Completions);
        Assert.Equal(caller, completion.Target);
        Assert.Equal(taskId, completion.TaskId);
        Assert.Contains(new DurableTaskSubscriber(caller, taskId), storage.Get(taskId).CompletionDestinations);
        Assert.Equal(42, completion.Materialize(runtime).Response.GetResult<int>());
    }

    [Fact]
    public Task RemoteChildScheduleCreatesCallerStateBeforeAdvertisingCompletion() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/remote");
        storage.AddInvoked(TaskId.Parse("root"));
        var request = CreateRemoteChild(1);
        var target = request.Context!.TargetId;
        transport.BeforeSendInvocation = (_, sentTarget, sentTaskId, _) =>
        {
            Assert.True(CallerScope.TryParseWireId(sentTaskId, out _, out var sentNodeId));
            var state = storage.Get(sentNodeId);
            Assert.Equal(target, sentTarget);
            Assert.Equal(target, state.RemoteTarget);
            Assert.NotNull(state.RemoteRequestFingerprint);
        };

        await runtime.ScheduleChildAsync(taskId, request, TestContext.Current.CancellationToken);

        Assert.Equal(1, manager.WriteCount);
        Assert.True(request.Context.SupportsDurableCompletion);
    });

    [Fact]
    public Task RemoteChildScheduleRejectsSelfTargetBeforeCreatingState() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/self");
        var request = new TestStateManager.TestRemoteDurableTask(CreateRequest(1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ScheduleChildAsync(taskId, request, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("owning grain", exception.Message, StringComparison.Ordinal);
        Assert.False(storage.Contains(taskId));
        Assert.Empty(transport.Invocations);
        Assert.Equal(0, manager.WriteCount);
    });

    [Fact]
    public Task RemoteChildScheduleReturnsPersistedTerminalResultWithoutResending() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/remote");
        var request = CreateRemoteChild(1);
        var state = storage.GetOrCreate(taskId);
        storage.SetRemoteRequest(
            taskId,
            state,
            request.Context!.TargetId,
            IDurableTaskRequest.GetMethod(request),
            IDurableTaskRequest.GetFingerprint(request, CreateSerializer()));
        storage.SetResponse(taskId, state, DurableTaskResponse.FromResult(42));

        var handle = await runtime.ScheduleChildAsync(taskId, request, TestContext.Current.CancellationToken);

        Assert.Equal(42, (await handle.WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        Assert.Empty(transport.Invocations);
        Assert.Equal(0, manager.WriteCount);
    });

    [Fact]
    public Task CompletedHandlesHonorCanceledPollsAndWaits() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/completed");
        var state = storage.GetOrCreate(taskId);
        storage.SetResponse(taskId, state, DurableTaskResponse.FromResult(42));
        var handle = runtime.GetScheduledTaskHandle(taskId);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle.PollAsync(default, canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle.WaitAsync(canceled.Token).AsTask());
        Assert.Equal(42, (await handle.PollAsync(default, TestContext.Current.CancellationToken)).GetResult<int>());
        Assert.Equal(42, (await handle.WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
    });

    [Fact]
    public Task CompletionHandleAdvancesOnlyAfterCommit() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/remote");
        var target = GrainId.Create("target", "one");
        storage.AddInvoked(TaskId.Parse("root"));
        var state = storage.GetOrCreate(taskId);
        storage.SetRemoteRequest(taskId, state, target, "method", "fingerprint");
        var handle = runtime.GetScheduledTaskHandle(taskId);

        runtime.AcceptResponseFromInbox(taskId, DurableTaskResponse.FromResult(42), target);

        Assert.False((await handle.PollAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken)).IsCompleted);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(42, (await handle.WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle.PollAsync(default, canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle.WaitAsync(canceled.Token).AsTask());
    });

    [Fact]
    public Task InboxCompletionPersistsEligiblePruningAfterOuterWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime(TimeSpan.Zero);
        var taskId = TaskId.Parse("root");
        var caller = GrainId.Create("caller", "one");
        storage.AddInvoked(taskId);
        var pruningWriteCompleted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AfterWriteCompleted = writeCount =>
        {
            if (writeCount == 2)
            {
                pruningWriteCompleted.TrySetResult(writeCount);
            }
        };

        // 收件箱投递的中止消息会暂存任务结果，立即结束任务的保留期。
        runtime.AbortFromInbox(taskId, "stopped", caller);
        Assert.True(storage.Contains(taskId));

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.True(
            await pruningWriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) >= 2);

        Assert.False(storage.Contains(taskId));
        Assert.Empty(transport.Invocations);
        Assert.Equal(2, manager.WriteCount);
    });

    [Fact]
    public Task FailedScheduleWriteFencesWithoutRevertingStagedIdentity() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/existing-pending");
        var request = CreateRequest(1);
        var state = storage.GetOrCreate(taskId);
        storage.SetRequest(taskId, state, request);
        _ = runtime.GetScheduledTaskHandle(taskId);
        manager.BeforeWrite = () =>
        {
            manager.BeforeWrite = null;
            throw new IOException("Expected existing pending write failure.");
        };

        await Assert.ThrowsAsync<IOException>(
            () => ((IDurableTaskServer)runtime).ScheduleAsync(
                taskId,
                request,
                TestContext.Current.CancellationToken).AsTask());

        // 已暂存的标识可以安全地提交，因此予以保留；发生故障的日志被隔离以阻止后续操作，而非回滚。
        Assert.Equal(IDurableTaskRequest.GetFingerprint(request, CreateSerializer()), storage.Get(taskId).RequestFingerprint);
        Assert.True(manager.IsFenced);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IDurableTaskServer)runtime).ScheduleAsync(
                taskId,
                request,
                TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, request.CreateTaskCallCount);
        Assert.Equal(0, manager.WriteCount);
    });

    [Fact]
    public Task ResponseStagedAfterWriteSnapshotIsCommittedBeforePruning() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime(TimeSpan.Zero);
        var firstId = TaskId.Parse("first");
        var secondId = TaskId.Parse("second");
        var caller = GrainId.Create("caller", "one");
        storage.AddInvoked(firstId);
        storage.AddInvoked(secondId);
        var secondHandle = runtime.GetScheduledTaskHandle(secondId);
        var pruningWriteCompleted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AfterWriteCompleted = writeCount =>
        {
            if (writeCount == 2 && !storage.Contains(secondId))
            {
                pruningWriteCompleted.TrySetResult(writeCount);
            }
        };

        // 收件箱投递的中止消息会暂存各任务的结果。
        runtime.AbortFromInbox(firstId, "first", caller);
        manager.AfterWriteStarted = () =>
        {
            runtime.AbortFromInbox(secondId, "second", caller);
            return default;
        };

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        manager.AfterWriteStarted = null;

        var secondResponse = await secondHandle.WaitAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("second", Assert.IsType<DurableTaskAbortedException>(secondResponse.Exception).Message);
        Assert.Equal(
            2,
            await pruningWriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.False(storage.Contains(secondId));
        Assert.False(storage.Contains(firstId));
    });

    [Fact]
    public Task JournalResetDiscardsStagedCompletionEffects() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/remote");
        var target = GrainId.Create("target", "one");
        storage.AddInvoked(TaskId.Parse("root"));
        var state = storage.GetOrCreate(taskId);
        storage.SetRemoteRequest(taskId, state, target, "method", "fingerprint");

        runtime.AcceptResponseFromInbox(taskId, DurableTaskResponse.FromResult(42), target);
        var handle = runtime.GetScheduledTaskHandle(taskId);
        state.Result = null;
        state.CompletedAt = null;
        ((IStateMachine)runtime).Reset(default);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.False((await handle.PollAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken)).IsCompleted);
    });

    [Fact]
    public Task LongPollDoesNotBlockCancellation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("root");
        storage.GetOrCreate(taskId);
        var polling = runtime.SubscribeOrPollAsync(taskId, new SubscribeOrPollOptions { PollTimeout = TimeSpan.FromMinutes(1) }, TestContext.Current.CancellationToken).AsTask();

        await runtime.SignalCancellationAsync(taskId, TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(DurableTaskStatus.Canceled, (await polling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Status);
    });

    [Fact]
    public Task PreCanceledInboxInvocationStagesOneCompletion() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, manager, transport) = CreateRuntime();
        var receiver = GrainId.Create("test", "one");
        var caller = GrainId.Create("caller", "one");
        var taskId = TaskId.Parse("pre-canceled");
        var handler = CreateHandler(runtime);

        Accept(handler, CancellationMessage(caller, receiver, taskId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var request = CreateSerializableRequest(receiver);
        Accept(handler, InvocationMessage(caller, receiver, taskId, request));

        var completion = Assert.Single(transport.Completions);
        Assert.Equal(caller, completion.Target);
        Assert.Equal(DurableTaskStatus.Canceled, completion.Materialize(runtime).Response.Status);
    });

    [Fact]
    public Task CancellationBeforeInvocationPreventsExecution() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var request = CreateRequest(1);
        var caller = request.Context!.CallerId;
        var taskId = CallerScope.GetTaskId(caller, TaskId.Parse("starter/call"));

        // 调用方对子任务的取消可能经收件箱先于子任务的调用到达。
        runtime.SignalCancellationFromInbox(taskId, caller, reason: null);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var response = runtime.ScheduleFromInbox(taskId, request);

        Assert.Equal(DurableTaskStatus.Canceled, response.Status);
        Assert.Equal(0, request.CreateTaskCallCount);
        Assert.True(storage.Get(taskId).CancellationRequestedAt.HasValue);
    });

    [Fact]
    public Task DirectScheduleRejectsMismatchedTargetBeforeStateMutation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var request = CreateRemoteRequest(1);
        var taskId = TaskId.Parse("wrong-target");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IDurableTaskServer)runtime).ScheduleAsync(taskId, request, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("not receiver", exception.Message, StringComparison.Ordinal);
        Assert.Empty(storage.Tasks);
        Assert.Equal(0, manager.WriteCount);
        Assert.Equal(0, request.CreateTaskCallCount);
    });

    [Fact]
    public Task DirectScheduleClearsForgedCallerIdentity() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var request = CreateRequest(1);
        request.Context!.CallerId = GrainId.Create("forged", "caller");
        request.Context.SupportsDurableCompletion = true;
        var taskId = TaskId.Parse("root");

        _ = await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, request, TestContext.Current.CancellationToken);

        Assert.Equal(default, storage.Get(taskId).CallerId);
        Assert.Empty(storage.Get(taskId).CompletionDestinations);
    });

    [Fact]
    public Task DelayStagesScheduledSelfResumeInItsWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/scheduled-delay");
        manager.BeforeWrite = () =>
        {
            // 延迟操作的状态及其恢复消息在提交两者的写入之前暂存。
            var resume = Assert.Single(transport.Resumes);
            Assert.Equal(storage.Get(taskId).ResumeGeneration, resume.Generation);
            Assert.Equal(storage.Get(taskId).DueTime, resume.DueTime);
        };

        Assert.Same(
            DurableTaskResponse.Pending,
            await runtime.ScheduleDelayAsync(taskId, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));

        var state = storage.Get(taskId);
        var sent = Assert.Single(transport.Resumes);
        Assert.Equal(runtime.GrainId, sent.GrainId);
        Assert.Equal(taskId, sent.TaskId);
        Assert.Equal(1, sent.Generation);
        Assert.Equal(1, state.ResumeGeneration);
        Assert.Equal(state.DueTime, sent.DueTime);
        Assert.Equal(1, manager.WriteCount);
        Assert.Null(state.Result);
    });

    [Fact]
    public Task DelayCompletesWhenItsResumeMessageIsHandled() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/delay");
        await runtime.ScheduleDelayAsync(taskId, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        var handle = runtime.GetScheduledTaskHandle(taskId);
        var resume = Assert.Single(transport.Resumes);

        var context = ResumeContext(runtime, taskId, resume.Generation, resume.DueTime);
        await HandleAsync(CreateHandler(runtime), context);

        // 完成结果与收件箱处理结果一起暂存，句柄仅在提交它们的写入完成后才完成。
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(taskId).Result!.Status);
        Assert.Null(storage.Get(taskId).DueTime);
        Assert.False((await handle.PollAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken)).IsCompleted);
        Assert.Equal(1, manager.WriteCount);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, (await handle.WaitAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Single(transport.Resumes);
    });

    [Fact]
    public Task StaleResumeGenerationCannotCompleteDelay() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/delay");
        await runtime.ScheduleDelayAsync(taskId, TimeSpan.Zero, TestContext.Current.CancellationToken);
        var generation = storage.Get(taskId).ResumeGeneration;
        var dueTime = storage.Get(taskId).DueTime!.Value;
        var handler = CreateHandler(runtime);

        await HandleAsync(handler, ResumeContext(runtime, taskId, generation + 1, dueTime));
        await HandleAsync(handler, ResumeContext(runtime, taskId, generation - 1, dueTime));
        Assert.Null(storage.Get(taskId).Result);
        Assert.Equal(1, manager.WriteCount);

        await HandleAsync(handler, ResumeContext(runtime, taskId, generation, dueTime));
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(taskId).Result!.Status);

        // 重复投递已完成延迟操作的恢复消息不会造成影响。
        await HandleAsync(handler, ResumeContext(runtime, taskId, generation, dueTime));
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(taskId).Result!.Status);
    });

    [Fact]
    public Task DelayCanceledBeforeDueTimeTerminalizesAsCanceledAndLaterResumeIsNoOp() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/delay");
        await runtime.ScheduleDelayAsync(taskId, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        var handle = runtime.GetScheduledTaskHandle(taskId);
        var resume = Assert.Single(transport.Resumes);

        await handle.CancelAsync(TestContext.Current.CancellationToken);

        var canceled = storage.Get(taskId);
        Assert.Equal(DurableTaskStatus.Canceled, canceled.Result!.Status);
        Assert.Null(canceled.DueTime);
        Assert.True(canceled.ResumeGeneration > resume.Generation);
        Assert.Equal(DurableTaskStatus.Canceled, (await handle.WaitAsync(TestContext.Current.CancellationToken)).Status);
        var writes = manager.WriteCount;

        await HandleAsync(CreateHandler(runtime), ResumeContext(runtime, taskId, resume.Generation, resume.DueTime));

        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(taskId).Result!.Status);
        Assert.Equal(canceled.CompletedAt, storage.Get(taskId).CompletedAt);
        Assert.Equal(writes, manager.WriteCount);
        Assert.Empty(transport.Completions);
        Assert.Single(transport.Resumes);
    });

    [Fact]
    public Task ResumeOfDelayWithRequestedCancellationTerminalizesAsCanceled() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/delay");
        await runtime.ScheduleDelayAsync(taskId, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        var handle = runtime.GetScheduledTaskHandle(taskId);
        var resume = Assert.Single(transport.Resumes);
        storage.Get(taskId).CancellationRequestedAt = runtime.UtcNow;

        await HandleAsync(CreateHandler(runtime), ResumeContext(runtime, taskId, resume.Generation, resume.DueTime));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(taskId).Result!.Status);
        Assert.Equal(DurableTaskStatus.Canceled, (await handle.WaitAsync(TestContext.Current.CancellationToken)).Status);
    });

    [Fact]
    public Task ResumeFromAnotherGrainIsRejectedBeforeChangingState() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/delay");
        await runtime.ScheduleDelayAsync(taskId, TimeSpan.Zero, TestContext.Current.CancellationToken);
        var resume = Assert.Single(transport.Resumes);
        var forged = GrainId.Create("forged", "grain");
        var context = MessagingTestHelpers.CreateHandlerContext(MessagingTestHelpers.MessageTypes.Resume.Create(
            DurableTaskMessageTransport.CreateResumeMessageId(forged, taskId, resume.Incarnation, resume.Generation, resume.DueTime),
            runtime.GrainId,
            new DurableTaskResumeMessage { TaskId = taskId, Incarnation = resume.Incarnation, Generation = resume.Generation, Sender = forged },
            resume.DueTime).ToInboxMessage());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(runtime).HandleAsync(context, CancellationToken.None));

        context.DidNotReceive().Complete();
        Assert.Null(storage.Get(taskId).Result);
        Assert.Equal(1, manager.WriteCount);
    });

    [Fact]
    public Task DelayReplayKeepsTheRecordedDueTime() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/replayed-delay");

        await runtime.ScheduleDelayAsync(
            taskId,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);
        var recordedDueTime = storage.Get(taskId).DueTime;
        var generation = storage.Get(taskId).ResumeGeneration;

        // 重放请求不同的延迟时长（例如配置改变后）时，仍保留原先确定的到期时间。
        var replay = await runtime.ScheduleDelayAsync(
            taskId,
            TimeSpan.FromMinutes(6),
            TestContext.Current.CancellationToken);

        Assert.Same(DurableTaskResponse.Pending, replay);
        Assert.Equal(recordedDueTime, storage.Get(taskId).DueTime);
        Assert.Equal(generation, storage.Get(taskId).ResumeGeneration);

        // 恢复消息已与延迟操作一同提交，因此重放不会发送新消息。
        var resume = Assert.Single(transport.Resumes);
        Assert.Equal(recordedDueTime, resume.DueTime);
        await HandleAsync(CreateHandler(runtime), ResumeContext(runtime, taskId, resume.Generation, resume.DueTime));
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(taskId).Result!.Status);
    });

    [Fact]
    public Task CancellationWhileDelayWriteIsInFlightCancelsTheDelayPromptly() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        var delayWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelayWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AfterWriteStarted = async () =>
        {
            // 阻塞提交延迟操作及其恢复消息的写入。
            if (transport.Resumes.Count == 1 && delayWriteStarted.TrySetResult())
            {
                await releaseDelayWrite.Task;
            }
        };
        var request = CreateRequest(1, () => DurableTask.Run(async _ => await DurableTask.Delay(TimeSpan.FromDays(1))));

        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, request, TestContext.Current.CancellationToken);
        await delayWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var canceling = ((IDurableTaskServer)runtime).CancelAsync(rootId, TestContext.Current.CancellationToken).AsTask();
        releaseDelayWrite.SetResult();
        await canceling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var response = await runtime.GetScheduledTaskHandle(rootId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(DurableTaskStatus.Canceled, response.Status);
        var resume = Assert.Single(transport.Resumes);
        var delay = storage.Get(resume.TaskId);
        Assert.Equal(DurableTaskStatus.Canceled, delay.Result!.Status);
        Assert.Null(delay.DueTime);

        // 与延迟操作一起提交的恢复消息已经过期，因此不会改变任何状态。
        var writes = manager.WriteCount;
        await HandleAsync(CreateHandler(runtime), ResumeContext(runtime, resume.TaskId, resume.Generation, resume.DueTime));
        Assert.Equal(writes, manager.WriteCount);
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(resume.TaskId).Result!.Status);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DelayAwaitedWithoutAnIdentifierIsItsOwnChildTask(bool inAsyncMethod) => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        static async DurableTask WaitADay() => await DurableTask.Delay(TimeSpan.FromDays(1));
        var request = CreateRequest(
            1,
            () => inAsyncMethod
                ? WaitADay()
                : DurableTask.Run(async _ => await DurableTask.Delay(TimeSpan.FromDays(1))));

        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, request, TestContext.Current.CancellationToken);
        await transport.ResumeSent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 只有延迟操作自身的任务记录到期时间；其父任务继续运行，并按标识符重放该延迟操作。
        var resume = Assert.Single(transport.Resumes);
        Assert.True(rootId.IsParentOf(resume.TaskId));
        Assert.All(storage.Tasks, task => Assert.Equal(task.Id == resume.TaskId, task.State.DueTime is not null));
    });

    [Fact]
    public Task DiagnosticsDescribeEachTaskStatusAndResult() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var dueTime = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var target = GrainId.Create("target", "one");
        void Stage(string id, Action<TaskId, TestTaskState> stage)
        {
            var taskId = TaskId.Parse(id);
            stage(taskId, storage.GetOrCreate(taskId));
        }

        Stage("root/completed", (id, state) => storage.SetResponse(id, state, DurableTaskResponse.FromResult(42)));
        Stage("root/faulted", (id, state) => storage.SetResponse(id, state, DurableTaskResponse.FromException(new InvalidOperationException("boom"))));
        Stage("root/canceled", (id, state) => storage.SetResponse(id, state, DurableTaskResponse.Canceled));
        Stage("root/canceling", (id, state) => storage.RequestCancellation(id, state));
        Stage("root/delayed", (id, state) => storage.SetDelay(id, state, dueTime, generation: 1));
        Stage("root/remote", (id, state) => storage.SetRemoteRequest(id, state, target, "method", "fingerprint"));

        var diagnostics = new Dictionary<string, DurableTaskDiagnosticState>();
        await foreach (var (taskId, state) in ((IDurableTaskGrainExtension)runtime).GetTasksAsync(TestContext.Current.CancellationToken))
        {
            diagnostics[taskId.ToString()] = state;
        }

        Assert.Equal(("Completed", "42"), (diagnostics["root/completed"].Status, diagnostics["root/completed"].Response));
        Assert.Equal(
            ("Faulted", "InvalidOperationException: boom"),
            (diagnostics["root/faulted"].Status, diagnostics["root/faulted"].Response));
        Assert.Equal(("Canceled", null), (diagnostics["root/canceled"].Status, diagnostics["root/canceled"].Response));
        Assert.Equal("Canceling", diagnostics["root/canceling"].Status);
        Assert.Equal(("Delayed", dueTime), (diagnostics["root/delayed"].Status, diagnostics["root/delayed"].DueTime));
        Assert.Equal(("Pending", target.ToString()), (diagnostics["root/remote"].Status, diagnostics["root/remote"].RemoteTarget));
        Assert.Equal(6, diagnostics.Count);
    });

    [Fact]
    public Task ResumeForMissingTaskCompletesWithoutCreatingState() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/missing");

        await HandleAsync(CreateHandler(runtime), ResumeContext(runtime, taskId, generation: 1, DateTimeOffset.UtcNow, Guid.NewGuid()));

        Assert.False(storage.Contains(taskId));
        Assert.Equal(0, manager.WriteCount);
    });

    [Fact]
    public Task LocalDelayRemainsPendingUntilResumeAndRecoveryDoesNotResendIt() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var localTaskId = TaskId.Parse("root/local");
        var localHandle = await runtime.ScheduleChildAsync(localTaskId, new TestStateManager.PendingDurableTask(), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskStatus.Pending, (await localHandle.PollAsync(default, TestContext.Current.CancellationToken)).Status);
        Assert.Null(storage.Get(localTaskId).Result);
        Assert.Null(storage.Get(localTaskId).CompletedAt);

        var taskId = TaskId.Parse("root/delay");
        Assert.Equal(
            DurableTaskStatus.Pending,
            (await runtime.ScheduleDelayAsync(taskId, TimeSpan.Zero, TestContext.Current.CancellationToken)).Status);
        var handle = runtime.GetScheduledTaskHandle(taskId);

        Assert.Equal(
            DurableTaskStatus.Pending,
            (await handle.PollAsync(default, TestContext.Current.CancellationToken)).Status);
        Assert.Null(storage.Get(taskId).Result);
        Assert.Null(storage.Get(taskId).CompletedAt);

        // 恢复消息与延迟操作一起持久化，因此恢复过程使延迟操作保持待完成状态，而不重新发送消息。
        var writes = manager.WriteCount;
        await runtime.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        Assert.Single(transport.Resumes);
        Assert.Equal(writes, manager.WriteCount);
        Assert.Null(storage.Get(taskId).CompletedAt);

        var state = storage.Get(taskId);
        await HandleAsync(CreateHandler(runtime), ResumeContext(runtime, taskId, state.ResumeGeneration, state.DueTime!.Value));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, (await handle.WaitAsync(TestContext.Current.CancellationToken)).Status);
        Assert.NotNull(storage.Get(taskId).CompletedAt);
    });

    [Fact]
    public Task RecoveredCanceledDelayTerminalizesAndStaleResumesAreHarmless() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/delay");
        await runtime.ScheduleDelayAsync(taskId, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        var resume = Assert.Single(transport.Resumes);

        storage.Get(taskId).CancellationRequestedAt = runtime.UtcNow;
        await runtime.ResumePendingTasksAsync(TestContext.Current.CancellationToken);

        var canceled = storage.Get(taskId);
        Assert.Equal(DurableTaskStatus.Canceled, canceled.Result!.Status);
        Assert.NotNull(canceled.CompletedAt);
        Assert.Null(canceled.DueTime);
        Assert.True(canceled.ResumeGeneration > resume.Generation);
        Assert.Single(transport.Resumes);

        var handler = CreateHandler(runtime);
        await HandleAsync(handler, ResumeContext(runtime, taskId, resume.Generation, resume.DueTime));
        await HandleAsync(handler, ResumeContext(runtime, taskId, resume.Generation, resume.DueTime));
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(taskId).Result!.Status);
        Assert.Equal(canceled.CompletedAt, storage.Get(taskId).CompletedAt);
    });

    [Fact]
    public Task PollingOnlyCallerIsNotRegisteredAsCompletionDestination() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var request = CreateRequest(1);
        request.Context!.CallerId = GrainId.Create("client", "one");
        request.Context.SupportsDurableCompletion = false;
        var taskId = TaskId.Parse("client-request");

        var response = await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, request, TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskResponseKind.Pending, response.ResponseKind);
        await WaitUntilAsync(() => storage.Get(taskId).Result is { IsCompleted: true });

        Assert.Empty(storage.Get(taskId).CompletionDestinations);
        Assert.Empty(transport.Completions);
        Assert.Equal(
            DurableTaskStatus.CompletedSuccessfully,
            (await runtime.SubscribeOrPollAsync(taskId, default, TestContext.Current.CancellationToken)).Status);
    });

    [Fact]
    public Task RecoveredRemoteChildRetainsCancellationTargetAndReplayIdentity() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/remote");
        storage.AddInvoked(rootId);
        var request = CreateRemoteRequest(7);
        var target = request.Context!.TargetId;

        await runtime.ScheduleChildAsync(childId, new TestStateManager.TestRemoteDurableTask(request), TestContext.Current.CancellationToken);
        var child = storage.Get(childId);
        var fingerprint = child.RemoteRequestFingerprint;
        var wireId = runtime.GetWireId(childId);
        Assert.Equal(target, child.RemoteTarget);
        Assert.NotNull(fingerprint);

        var (recovered, _, _, recoveredTransport) = CreateRuntime(storage, manager);
        manager.BeforeWrite = () => Assert.Contains(
            recoveredTransport.Cancellations,
            cancellation => cancellation.TaskId == wireId && cancellation.Target == target);
        await recovered.SignalCancellationAsync(rootId, TestContext.Current.CancellationToken);

        var cancellation = Assert.Single(recoveredTransport.Cancellations);
        Assert.Equal(wireId, cancellation.TaskId);
        Assert.Equal(target, cancellation.Target);
        Assert.Equal(fingerprint, storage.Get(childId).RemoteRequestFingerprint);
    });

    [Fact]
    public Task RemoteCancellationMarksStateBeforeStagingMessage() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/remote");
        var target = GrainId.Create("target", "one");
        var state = storage.GetOrCreate(taskId);
        storage.SetRemoteRequest(taskId, state, target, "method", "fingerprint");
        transport.BeforeSendCancellation = (_, sentTarget, sentTaskId) =>
        {
            Assert.Equal(target, sentTarget);
            Assert.Equal(runtime.GetWireId(taskId), sentTaskId);
            Assert.NotNull(storage.Get(taskId).CancellationRequestedAt);
            Assert.Equal(0, manager.WriteCount);
        };

        await runtime.CancelRemoteAsync(taskId, target, reason: null, TestContext.Current.CancellationToken);

        // 取消及其消息在一次写入中提交；发件箱只投递已提交的消息。
        Assert.Equal(1, manager.WriteCount);
        Assert.Single(transport.Cancellations);
        Assert.NotNull(storage.Get(taskId).CancellationRequestedAt);
    });

    [Fact]
    public Task ReplayedRemoteCancellationDoesNotSendItsMessageAgain() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/remote");
        var target = GrainId.Create("target", "one");
        var state = storage.GetOrCreate(taskId);
        storage.SetRemoteRequest(taskId, state, target, "method", "fingerprint");
        await runtime.CancelRemoteAsync(taskId, target, reason: "first", TestContext.Current.CancellationToken);
        var requestedAt = storage.Get(taskId).CancellationRequestedAt;
        Assert.Single(transport.Cancellations);

        // 发件箱可能已经使提交的消息退役。再次发送相同标识的消息会开始一次新的发送，
        // 使用更晚的接纳截止时间，已处理过首次发送的接收方会拒绝它，因此恢复后的激活实例在
        // 重放过程中再次取消任务时，不会暂存任何新消息。
        var (recovered, _, _, recoveredTransport) = CreateRuntime(storage, manager);
        await recovered.CancelRemoteAsync(taskId, target, reason: "replayed", TestContext.Current.CancellationToken);
        await runtime.CancelRemoteAsync(taskId, target, reason: "repeated", TestContext.Current.CancellationToken);

        Assert.Empty(recoveredTransport.Cancellations);
        Assert.Single(transport.Cancellations);
        Assert.Equal(requestedAt, storage.Get(taskId).CancellationRequestedAt);
        Assert.Equal("first", storage.Get(taskId).CancellationReason);
    });

    [Fact]
    public Task DeletingTreeDoesNotSendRecordedRemoteCancellationAgain() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime(TimeSpan.FromDays(1));
        var taskId = TaskId.Parse("root");
        var canceledId = TaskId.Parse("root/canceled");
        var pendingId = TaskId.Parse("root/pending");
        var target = GrainId.Create("target", "one");
        runtime.ScheduleFromInbox(taskId, CreateRequest(1));
        var canceled = storage.GetOrCreate(canceledId);
        storage.SetRemoteRequest(canceledId, canceled, target, "method", "canceled");
        storage.SetRemoteRequest(pendingId, storage.GetOrCreate(pendingId), target, "method", "pending");
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await runtime.CancelRemoteAsync(canceledId, target, reason: null, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.Completions.Count == 1);
        await WaitUntilIdleAsync(runtime);
        var completion = Assert.Single(transport.Completions);
        var canceledWireId = runtime.GetWireId(canceledId);
        var pendingWireId = runtime.GetWireId(pendingId);
        Assert.Equal(canceledWireId, Assert.Single(transport.Cancellations).TaskId);

        // 删除执行树会取消其待完成的远程子任务；已提交的子任务取消消息归发件箱负责。
        ((IOutboxMessageSource)runtime).OnRetired(completion.MessageId, OutboxMessageRetirement.Delivered.Instance);

        Assert.False(storage.Contains(canceledId));
        Assert.False(storage.Contains(pendingId));
        Assert.Equal(
            new[] { canceledWireId, pendingWireId },
            transport.Cancellations.Select(static cancellation => cancellation.TaskId));
    });

    [Fact]
    public Task FailedCancellationWriteFencesAndKeepsMonotonicCancellation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/cancellation-retry");
        storage.GetOrCreate(taskId);
        storage.GetOrCreate(TaskId.Parse("root/other"));
        manager.BeforeWrite = () =>
        {
            manager.BeforeWrite = null;
            throw new IOException("Expected cancellation write failure.");
        };

        await Assert.ThrowsAsync<IOException>(
            () => runtime.SignalCancellationAsync(
                taskId,
                TestContext.Current.CancellationToken).AsTask());

        Assert.NotNull(storage.Get(taskId).CancellationRequestedAt);
        Assert.True(manager.IsFenced);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.SignalCancellationAsync(
                TaskId.Parse("root/other"),
                TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, manager.WriteCount);
    });

    [Fact]
    public Task RecoveryLeavesCanceledRemoteChildAwaitingItsCallee() => ActivationScheduler.RunAsync(async () =>
    {
        var (_, storage, manager, _) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var childId = TaskId.Parse("root/remote");
        var target = GrainId.Create("target", "one");
        var child = storage.GetOrCreate(childId);
        storage.SetRemoteRequest(childId, child, target, "method", "fingerprint");
        storage.RequestCancellation(childId, child);
        var (recovered, _, _, transport) = CreateRuntime(storage, manager);

        await recovered.ResumePendingTasksAsync(TestContext.Current.CancellationToken);

        // 取消消息与请求一同提交；被调用方决定子任务的结果。
        Assert.Equal(0, manager.WriteCount);
        Assert.Null(storage.Get(childId).Result);
        Assert.Empty(transport.Cancellations);
        recovered.AcceptResponseFromInbox(childId, DurableTaskResponse.FromResult(5), target);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, storage.Get(childId).Result!.GetResult<int>());
    });

    [Fact]
    public Task LocalAndRemoteRequestIdentitiesCannotShareTaskId() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("identity");
        runtime.ScheduleFromInbox(taskId, CreateRequest(1));

        var remoteException = await Assert.ThrowsAsync<DurableTaskDivergenceException>(
            () => runtime.ScheduleChildAsync(taskId, new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(2)), TestContext.Current.CancellationToken).AsTask());
        Assert.Contains("different request", remoteException.Message, StringComparison.Ordinal);

        var remoteTaskId = TaskId.Parse("identity/remote");
        await runtime.ScheduleChildAsync(remoteTaskId, new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(3)), TestContext.Current.CancellationToken);
        var topLevelException = Assert.Throws<DurableTaskConflictException>(
            () => runtime.ScheduleFromInbox(remoteTaskId, CreateRequest(4)));
        Assert.Contains("remote child request", topLevelException.Message, StringComparison.Ordinal);
        Assert.Equal(remoteTaskId, topLevelException.TaskId);
        Assert.NotNull(storage.Get(remoteTaskId).RemoteRequestFingerprint);

        var delayTaskId = TaskId.Parse("identity/delay");
        await runtime.ScheduleDelayAsync(delayTaskId, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        var delayException = await Assert.ThrowsAsync<DurableTaskDivergenceException>(
            () => runtime.ScheduleChildAsync(delayTaskId, new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(5)), TestContext.Current.CancellationToken).AsTask());
        Assert.Contains("different request", delayException.Message, StringComparison.Ordinal);
    });

    [Fact]
    public Task CustomScheduledChildIsRejected() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/custom");
        var task = new TestSchedulableTask(static (_, _) => new(DurableTaskResponse.Pending));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => runtime.ScheduleChildAsync(taskId, task, TestContext.Current.CancellationToken).AsTask());
        Assert.Throws<NotSupportedException>(() => runtime.PostChild(taskId, task, shielded: false));

        Assert.Equal(0, task.ScheduleAsyncCallCount);
        Assert.False(storage.Contains(taskId));
    });

    [Fact]
    public Task ReplayedCanceledRemoteChildAwaitsItsCalleeOutcome() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/remote");
        var request = CreateRemoteRequest(1);
        var original = new TestStateManager.TestRemoteDurableTask(request);
        _ = await runtime.ScheduleChildAsync(taskId, original, TestContext.Current.CancellationToken);
        storage.RequestCancellation(taskId, storage.Get(taskId));
        var (recovered, _, _, transport) = CreateRuntime(storage, manager);
        var retry = new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(1));

        var handle = await recovered.ScheduleChildAsync(taskId, retry, TestContext.Current.CancellationToken);

        Assert.False((await handle.PollAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken)).IsCompleted);
        Assert.Empty(transport.Cancellations);
        Assert.Equal(0, retry.ScheduleAsyncCallCount);
        recovered.AcceptResponseFromInbox(taskId, DurableTaskResponse.FromResult(9), request.Context!.TargetId);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(9, (await handle.WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
    });

    [Fact]
    public Task CancelingLocalChildSignalsActiveDurableContext() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, stateManager, _) = CreateRuntime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCountObservedDuringCancellation = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var taskId = TaskId.Parse("root/local-cancel");
        var handle = await runtime.ScheduleChildAsync(taskId, DurableTask.Run(async cancellationToken =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    writeCountObservedDuringCancellation.TrySetResult(stateManager.WriteCount);
                    throw;
                }
            }), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var writesBeforeCancellation = stateManager.WriteCount;

        await handle.CancelAsync(TestContext.Current.CancellationToken);

        var observedWriteCount = await writeCountObservedDuringCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(observedWriteCount > writesBeforeCancellation);
        Assert.Equal(DurableTaskStatus.Canceled, (await handle.WaitAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(taskId).Result!.Status);
    });

    [Fact]
    public Task StopAbortsRunningAttemptDiscardsItsResultAndReplaysInNextActivation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var caller = GrainId.Create("caller", "one");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource<(bool ExecutionAborted, bool DurablyCanceled)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async executionToken =>
            {
                if (Interlocked.Increment(ref attempts) > 1)
                {
                    return 7;
                }

                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, executionToken);
                }
                catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
                {
                    // 宿主中止会传递到委托的令牌，但不属于持久化取消。
                    var context = DurableTask.Current;
                    aborted.TrySetResult((executionToken.IsCancellationRequested, context.IsTaskCancellationRequested));
                }

                return 42;
            }));
        var taskId = TaskId.Parse("root");

        runtime.ScheduleFromInbox(taskId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var stopping = runtime.StopAsync(TestContext.Current.CancellationToken);
        var observed = await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(observed.ExecutionAborted);
        Assert.False(observed.DurablyCanceled);
        Assert.Null(storage.Get(taskId).Result);
        Assert.Null(storage.Get(taskId).CancellationRequestedAt);
        Assert.Empty(transport.Completions);

        var (replacement, _, _, replacementTransport) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        var response = await replacement.GetScheduledTaskHandle(taskId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.Equal(7, response.GetResult<int>());
        Assert.Equal(7, storage.Get(taskId).Result!.GetResult<int>());
        var completion = Assert.Single(replacementTransport.Completions);
        Assert.Equal(caller, completion.Target);
        Assert.Equal(7, completion.Materialize(replacement).Response.GetResult<int>());
        Assert.Empty(transport.Completions);
    });

    [Fact]
    public Task StopDiscardsFailureProducedAfterExecutionShutdown() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ =>
            {
                started.TrySetResult();
                try
                {
                    await DurableTask.Delay(TimeSpan.FromDays(1));
                }
                catch (OperationCanceledException exception)
                {
                    throw new InvalidOperationException("Failure after shutdown.", exception);
                }
            }));
        var taskId = TaskId.Parse("root");

        await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, request, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await transport.ResumeSent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var stopping = runtime.StopAsync(TestContext.Current.CancellationToken);
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Null(storage.Get(taskId).Result);
        Assert.Empty(transport.Completions);
    });

    [Fact]
    public Task StopFailsPendingPollsInsteadOfReportingTheTaskCanceled() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async executionToken =>
            {
                if (Interlocked.Increment(ref attempts) > 1)
                {
                    return 7;
                }

                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, executionToken);
                return 0;
            }));
        var taskId = TaskId.Parse("root");
        await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, request, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var polling = runtime.SubscribeOrPollAsync(taskId, new SubscribeOrPollOptions { PollTimeout = TimeSpan.FromMinutes(1) }, TestContext.Current.CancellationToken).AsTask();
        var waiting = runtime.GetScheduledTaskHandle(taskId).WaitAsync(TestContext.Current.CancellationToken).AsTask();

        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 停止过程使每个等待方失败，如同在运行时停止期间到达的调用，并使任务保持待完成状态。
        var pollFailure = await Assert.ThrowsAsync<DurableTaskAttemptCanceledException>(() => polling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(DurableTaskAttemptCancellationReason.ActivationStopping, pollFailure.Reason);
        await Assert.ThrowsAsync<DurableTaskAttemptCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Null(storage.Get(taskId).Result);

        var (replacement, _, _, _) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        var response = await replacement.SubscribeOrPollAsync(taskId, new SubscribeOrPollOptions { PollTimeout = TimeSpan.FromSeconds(5) }, TestContext.Current.CancellationToken);
        Assert.Equal(7, response.GetResult<int>());
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task WorkSourceReportsRunnableAttemptsAndNotParkedOnes() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, _, transport) = CreateRuntime();
        var workSource = runtime;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stepId = TaskId.Parse("step");
        var delayId = TaskId.Parse("delay");
        Assert.Null(workSource.GetWakeupDelay());

        // 正在运行代码的任务体属于可运行状态，因此已停用的 Grain 必须被唤醒以恢复执行。
        await ((IDurableTaskServer)runtime).ScheduleAsync(stepId, CreateRequest(1, () => DurableTask.Run(async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return 1;
        })), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(new DurableTaskOptions().RunnableExecutionWakeupDelay, workSource.GetWakeupDelay());
        release.SetResult();
        await runtime.GetScheduledTaskHandle(stepId).WaitAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitForNoWorkAsync();

        // 在延迟上挂起的任务体等待恢复消息，该消息会重新激活 Grain。
        await ((IDurableTaskServer)runtime).ScheduleAsync(delayId, CreateRequest(2, () => DurableTask.Delay(TimeSpan.FromDays(1))), TestContext.Current.CancellationToken);
        await transport.ResumeSent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitForNoWorkAsync();
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 在激活实例调度器上排队的工作项按顺序执行，因此让出执行权可使执行尝试挂起或结束。
        async Task WaitForNoWorkAsync()
        {
            for (var yields = 0; workSource.GetWakeupDelay() is not null; yields++)
            {
                Assert.True(yields < 100, "The work source kept reporting work.");
                await Task.Yield();
            }
        }
    });

    [Fact]
    public Task WorkSourceReportsAnAttemptWhichACommittedResultResumesBeforeItsCodeContinues() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, _, manager, transport) = CreateRuntime();
        await runtime.RecoverAsync(cancellationToken);
        var workSource = runtime;
        var rootId = TaskId.Parse("root");
        var resumed = false;
        runtime.ScheduleFromInbox(rootId, CreateRequest(1, () => DurableTask.Run(async _ =>
        {
            await new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(2)).WithId("call");
            resumed = true;
            return 3;
        })));
        await manager.WriteStateAsync(cancellationToken);
        for (var yields = 0; workSource.GetWakeupDelay() is not null; yields++)
        {
            Assert.True(yields < 100, "The root didn't park on its call.");
            await Task.Yield();
        }

        // 提交调用结果的写入恢复根任务，根任务的代码在后续工作项中继续运行。在此之前的每次
        // 查询（例如后续写入和唤醒操作中的查询）均报告根任务处于可运行状态。
        (bool Resumed, TimeSpan? First, TimeSpan? Second)? consults = null;
        manager.AfterWriteCompleted = _ => consults ??= (resumed, workSource.GetWakeupDelay(), workSource.GetWakeupDelay());
        runtime.AcceptResponseFromInbox(rootId.Child("call"), DurableTaskResponse.FromResult(2), RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);
        manager.AfterWriteCompleted = null;

        Assert.Equal((false, new DurableTaskOptions().RunnableExecutionWakeupDelay, new DurableTaskOptions().RunnableExecutionWakeupDelay), consults);
        Assert.Equal(3, (await WaitForResponseAsync(runtime, rootId)).GetResult<int>());
        await runtime.StopAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    });

    [Fact]
    public Task RecoveredPendingRequestStartsOnlyOnActivation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var stateMachine = (IStateMachine)runtime;
        var taskId = TaskId.Parse("root/initial-recovery");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(1, () => DurableTask.Run(_ => started.TrySetResult()));

        // 恢复过程重置运行时的易失状态，重放已写入日志的任务状态，并在激活之前完成。
        stateMachine.Reset(default);
        var state = storage.GetOrCreate(taskId);
        storage.SetRequest(taskId, state, request);
        storage.SetRequestFingerprint(
            taskId,
            state,
            IDurableTaskRequest.GetFingerprint(request, CreateSerializer()), IDurableTaskRequest.GetMethod(request));
        stateMachine.OnRecoveryCompleted();

        Assert.False(started.Task.IsCompleted);
        Assert.Equal(0, request.CreateTaskCallCount);

        await runtime.RecoverAsync(TestContext.Current.CancellationToken);

        // 恢复过程将执行推迟至激活完成之后。
        Assert.Equal(0, request.CreateTaskCallCount);

        runtime.StartRecoveredExecutions();
        runtime.StartRecoveredExecutions();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, request.CreateTaskCallCount);
    });

    [Fact]
    public Task RecoveredRequestWhichACallStartsFirstStartsOnce() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var stateMachine = (IStateMachine)runtime;
        var taskId = TaskId.Parse("root/call-starts-recovered");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(1, () => DurableTask.Run(async _ =>
        {
            started.TrySetResult();
            await release.Task;
        }));

        stateMachine.Reset(default);
        var state = storage.GetOrCreate(taskId);
        storage.SetRequest(taskId, state, request);
        storage.SetRequestFingerprint(
            taskId,
            state,
            IDurableTaskRequest.GetFingerprint(request, CreateSerializer()), IDurableTaskRequest.GetMethod(request));
        stateMachine.OnRecoveryCompleted();
        await runtime.RecoverAsync(TestContext.Current.CancellationToken);

        // 若调用在激活完成之前到达已恢复的任务，则启动该任务。
        var response = await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, request, TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskStatus.Pending, response.Status);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, request.CreateTaskCallCount);

        runtime.StartRecoveredExecutions();
        release.SetResult();
        var result = await runtime.GetScheduledTaskHandle(taskId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, result.Status);
        Assert.Equal(1, request.CreateTaskCallCount);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task RecoveryPreservesLocalChildHandleIdentityAcrossStorageOrder(bool parentFirst) => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var parentId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/child");
        ScheduledTask<int>? capturedChild = null;
        var childRuns = 0;
        var parentRequest = CreateRequest(
            1,
            () => DurableTask.Run(async cancellationToken =>
            {
                // 重放到达先前运行已记录的子任务。
                capturedChild = await DurableTask.Run(_ => Interlocked.Increment(ref childRuns) * 42).WithId("child").ScheduleAsync(cancellationToken);
                return await capturedChild;
            }));

        if (parentFirst)
        {
            AddRequest(parentId, parentRequest);
            storage.GetOrCreate(childId);
        }
        else
        {
            storage.GetOrCreate(childId);
            AddRequest(parentId, parentRequest);
        }

        await runtime.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        var response = await runtime.GetScheduledTaskHandle(parentId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 执行实例的句柄包装运行时为该子任务维护的唯一句柄。
        var executionHandle = GetInnerHandle(capturedChild!);
        var capturedInnerHandle = GetInnerHandle(executionHandle);

        Assert.Equal(42, response.GetResult<int>());
        Assert.Same(runtime.GetScheduledTaskHandle(childId), capturedInnerHandle);
        Assert.Equal(1, parentRequest.CreateTaskCallCount);
        Assert.Equal(1, childRuns);

        static IScheduledTaskHandle GetInnerHandle(object handle) => Assert.IsAssignableFrom<IScheduledTaskHandle>(
            handle.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(field => typeof(IScheduledTaskHandle).IsAssignableFrom(field.FieldType))
                .GetValue(handle));

        void AddRequest(TaskId taskId, RuntimeTestDurableTaskRequest request)
        {
            var state = storage.GetOrCreate(taskId);
            storage.SetRequest(taskId, state, request);
            storage.SetRequestFingerprint(
                taskId,
                state,
                IDurableTaskRequest.GetFingerprint(request, CreateSerializer()), IDurableTaskRequest.GetMethod(request));
        }
    });

    [Fact]
    public Task RecoveryReusesPersistedLogicalTime() => ActivationScheduler.RunAsync(async () =>
    {
        var initialTime = DateTimeOffset.UtcNow;
        var clock = new FakeTimeProvider(initialTime);
        var (runtime, storage, manager, _) = CreateRuntime(timeProvider: clock);
        var taskId = TaskId.Parse("root/logical-time");
        var observations = new ConcurrentQueue<DateTimeOffset>();
        var request = CreateRequest(
            1,
            () => DurableTask.Run(_ => observations.Enqueue(DurableTask.Current.UtcNow)));
        var state = storage.GetOrCreate(taskId);
        storage.SetRequest(taskId, state, request);
        storage.SetRequestFingerprint(
            taskId,
            state,
            IDurableTaskRequest.GetFingerprint(request, CreateSerializer()), IDurableTaskRequest.GetMethod(request));

        await runtime.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => observations.Count == 1);
        await WaitUntilAsync(() => state.Result is { IsCompleted: true });
        state.Result = null;
        state.CompletedAt = null;
        clock.Advance(TimeSpan.FromMinutes(10));

        var (replacement, _, _, _) = CreateRuntime(storage, manager, timeProvider: clock);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => observations.Count == 2);

        Assert.Equal([initialTime, initialTime], observations);
        Assert.Equal(initialTime, storage.LogicalTimes[taskId]);
    });

    [Fact]
    public Task CancelingLocalTaskCancelsRemoteDescendantsBeforeTerminalizingParent() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/local-child");
        var grandchildId = TaskId.Parse("root/local-child/remote-grandchild");
        var unrelatedId = TaskId.Parse("unrelated");
        var target = GrainId.Create("target", "one");
        var root = storage.AddInvoked(rootId);
        var child = storage.GetOrCreate(childId);
        var grandchild = storage.GetOrCreate(grandchildId);
        storage.SetRemoteRequest(grandchildId, grandchild, target, "method", "fingerprint");
        var grandchildWireId = runtime.GetWireId(grandchildId);
        var unrelated = storage.GetOrCreate(unrelatedId);
        var rootHandle = runtime.GetScheduledTaskHandle(rootId);
        transport.BeforeSendCancellation = (_, _, taskId) =>
        {
            Assert.Equal(grandchildWireId, taskId);
            Assert.Null(root.Result);
        };
        manager.BeforeWrite = () =>
        {
            Assert.NotNull(child.CancellationRequestedAt);
            Assert.NotNull(grandchild.CancellationRequestedAt);
            Assert.Single(transport.Cancellations);
        };

        await rootHandle.CancelAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(root.CancellationRequestedAt);
        Assert.Equal(DurableTaskStatus.Canceled, root.Result!.Status);
        Assert.NotNull(child.CancellationRequestedAt);
        Assert.NotNull(grandchild.CancellationRequestedAt);
        Assert.Null(unrelated.CancellationRequestedAt);
        Assert.Null(unrelated.Result);
        var cancellation = Assert.Single(transport.Cancellations);
        Assert.Equal(grandchildWireId, cancellation.TaskId);
        Assert.Equal(target, cancellation.Target);
        Assert.Equal(2, manager.WriteCount);
    });

    [Fact]
    public Task AncestorCancellationPreservesCachedSuccessfulChild() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/child");
        var root = storage.GetOrCreate(rootId);
        var firstHandle = await runtime.ScheduleChildAsync(
            childId,
            DurableTask.FromResult(42),
            TestContext.Current.CancellationToken);
        Assert.Equal(42, (await firstHandle.WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        storage.RequestCancellation(rootId, root);

        var reattached = await runtime.ScheduleChildAsync(
            childId,
            DurableTask.FromResult(42),
            TestContext.Current.CancellationToken);

        Assert.Same(firstHandle, reattached);
        Assert.Equal(42, (await reattached.WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(childId).Result!.Status);
    });

    [Fact]
    public Task WhenAnyAwaitsItsCandidatesWithoutPolling() => ActivationScheduler.RunAsync(async () =>
    {
        // 时钟始终不前进，因此只能通过等待候选任务来选出胜出任务。
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var (runtime, storage, _, _) = CreateRuntime(timeProvider: time, reentrant: true);
        var rootId = TaskId.Parse("root");
        var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selectionWaits = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var selection = DurableTaskEvents.AllEvents.Subscribe(new EventObserver(evt =>
        {
            if (evt is DurableTaskEvents.SelectionWaitStarted && ReferenceEquals(evt.GrainContext, runtime.GrainContext))
            {
                selectionWaits.TrySetResult();
            }
        }));
        var request = CreateRequest(1, () => DurableTask.Run(async _ =>
        {
            var (index, result) = await DurableTask.WhenAny(
            [
                DurableTask.Run(async _ =>
                {
                    started.TrySetResult();
                    return await first.Task;
                }),
                DurableTask.Run(async cancellationToken =>
                {
                    secondStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                }),
            ]);
            return index * 100 + result;
        }));

        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, request, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 只有在两个候选任务均已运行，且未找到胜出任务的选择操作开始等待后，才使第一个候选任务完成。
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await selectionWaits.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        first.SetResult(7);
        var response = await runtime.GetScheduledTaskHandle(rootId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(7, response.GetResult<int>());

        // 候选任务的完成顺序决定胜出任务，因此不记录选择决定。
        Assert.DoesNotContain(storage.Tasks, task => task.Id.ToString().EndsWith("/$winner", StringComparison.Ordinal));
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task LogicalTimeAdvancesToObservedChildCompletionsAndReplays() => ActivationScheduler.RunAsync(async () =>
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var start = time.GetUtcNow();
        var manager = new TestStateManager();
        var storage = new TestStorage(manager, time);
        var (runtime, _, _, _) = CreateRuntime(storage, manager, timeProvider: time);
        var rootId = TaskId.Parse("root");
        var observations = new ConcurrentQueue<(DateTimeOffset Before, DateTimeOffset After)>();
        var attempts = 0;
        var firstAttemptObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(1, () => DurableTask.Run(async cancellationToken =>
        {
            var context = DurableTask.Current;
            var before = context.UtcNow;
            await DurableTask.Run(_ => time.Advance(TimeSpan.FromHours(1))).WithId("step");
            observations.Enqueue((before, context.UtcNow));
            if (Interlocked.Increment(ref attempts) == 1)
            {
                // 第一次尝试在此处停止并被中止，因此下一个激活实例会重放它。
                firstAttemptObserved.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }));

        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, request, TestContext.Current.CancellationToken);
        await firstAttemptObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromHours(4));

        var (replacement, _, _, _) = CreateRuntime(storage, manager, timeProvider: time);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await replacement.GetScheduledTaskHandle(rootId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(
            [(start, start.AddHours(1)), (start, start.AddHours(1))],
            observations.ToArray());
    });

    [Fact]
    public Task ExecutionContextRejectsRuntimeOperationsOffItsScheduler() => ActivationScheduler.RunAsync(async () =>
    {
        var schedulerPair = new ConcurrentExclusiveSchedulerPair();
        var scheduler = schedulerPair.ExclusiveScheduler;
        var runtime = new SchedulerProbeRuntime(scheduler);
        var context = await Task.Factory.StartNew(
            () => new GrainDurableTaskContext(
                TaskId.Parse("root"),
                runtime,
                Substitute.For<IGrainContext>(),
                scheduler),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            scheduler);

        // 操作在拥有执行尝试状态的激活实例调度器之外运行，因此它不会被分派到该调度器，
        // 而是在到达运行时之前即失败。
        var response = await DurableTaskRuntimeHelper.RunAsync(DurableTask.Delay(TimeSpan.FromMinutes(1)), context);

        Assert.Contains(
            "ConfigureAwait(false)",
            Assert.IsType<InvalidOperationException>(response.Exception).Message,
            StringComparison.Ordinal);
        Assert.Equal(0, runtime.SchedulerInvocationCount);
        schedulerPair.Complete();
        await schedulerPair.Completion;
    });

    [Fact]
    public Task ExecutionContextRejectsUnrelatedCompletionSelectionIds() => ActivationScheduler.RunAsync(async () =>
    {
        var runtime = new SchedulerProbeRuntime(TaskScheduler.Current);
        var context = new GrainDurableTaskContext(
            TaskId.Parse("root"),
            runtime,
            Substitute.For<IGrainContext>(),
            TaskScheduler.Current);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeSelectCompletionCoreAsync(context, TaskId.Parse("other/decision"), [TaskId.Parse("root/child")], TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeSelectCompletionCoreAsync(context, TaskId.Parse("root/decision"), [TaskId.Parse("other/child")], TestContext.Current.CancellationToken).AsTask());
    });

    [Fact]
    public void ExecutionContextChildIdsSeparateGeneratedAndExplicitNamespaces()
    {
        var context = new GrainDurableTaskContext(
            TaskId.Parse("root"),
            new SchedulerProbeRuntime(TaskScheduler.Current),
            Substitute.For<IGrainContext>(),
            TaskScheduler.Current);

        var generated = InvokeCreateChildTaskId(context, null);
        var explicitNumeric = InvokeCreateChildTaskId(context, "0");
        var explicitSuffix = InvokeCreateChildTaskId(context, "job.1");
        _ = InvokeCreateChildTaskId(context, "job");
        var repeatedName = InvokeCreateChildTaskId(context, "job");

        Assert.NotEqual(generated, explicitNumeric);
        Assert.NotEqual(explicitSuffix, repeatedName);
        Assert.StartsWith("root/$child-", generated.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskId.Parse("root/job$2"), repeatedName);
        Assert.Equal(TaskId.Parse("root/0"), explicitNumeric);
        var exception = Assert.Throws<TargetInvocationException>(
            () => InvokeCreateChildTaskId(context, "$reserved"));
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public Task CompletionDecisionMustRetainCandidateIdentity() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var decisionId = TaskId.Parse("root/decision");
        var recordedWinner = TaskId.Parse("root/one");
        storage.GetOrCreate(recordedWinner).Result = DurableTaskResponse.Completed;
        Assert.Equal(
            recordedWinner,
            await runtime.SelectCompletionAsync(decisionId, [recordedWinner], TestContext.Current.CancellationToken));

        var exception = await Assert.ThrowsAsync<DurableTaskDivergenceException>(
            () => runtime.SelectCompletionAsync(decisionId, [TaskId.Parse("root/two")], TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(
            $"Durable completion decision '{decisionId}' is already associated with another operation. It records a selection of the first task to complete, and this attempt requested a selection of the first task to complete among 1 candidate. A replay schedules the same operation under each identifier: a call or request invokes the same method on the same target, whatever its arguments; new workflow steps use new identifiers.",
            exception.Message);
    });

    [Fact]
    public Task CompletionDecisionRejectsExistingChildOperation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var decisionId = TaskId.Parse("root/decision");
        await runtime.ScheduleDelayAsync(decisionId, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        var dueTime = storage.Get(decisionId).DueTime!.Value;

        var exception = await Assert.ThrowsAsync<DurableTaskDivergenceException>(
            () => runtime.SelectCompletionAsync(
                decisionId,
                [TaskId.Parse("root/candidate/0"), TaskId.Parse("root/candidate/1")],
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(
            $"Durable completion decision '{decisionId}' is already associated with another operation. It records a delay due at {dueTime:O}, and this attempt requested a selection of the first task to complete among 2 candidates. A replay schedules the same operation under each identifier: a call or request invokes the same method on the same target, whatever its arguments; new workflow steps use new identifiers.",
            exception.Message);
    });

    [Fact]
    public Task ChildScheduledUnderARecordedSelectionNamesTheSelection() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var decisionId = TaskId.Parse("root/race/$winner");
        var winner = TaskId.Parse("root/race/0");
        storage.GetOrCreate(winner).Result = DurableTaskResponse.Completed;
        await runtime.SelectCompletionAsync(decisionId, [winner], TestContext.Current.CancellationToken);

        // 工作流已改变：重放在首次执行记录了竞速选择的位置调度了本地步骤。
        var exception = await Assert.ThrowsAsync<DurableTaskDivergenceException>(
            () => runtime.ScheduleChildAsync(decisionId, DurableTask.FromResult(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(
            "It records a selection of the first task to complete, and this attempt requested a local child task.",
            exception.Message,
            StringComparison.Ordinal);
    });

    [Fact]
    public Task AttemptCancellationEndsAdapterSchedulingWithoutRequestingTaskCancellation() => ActivationScheduler.RunAsync(async () =>
    {
        var runtime = new ShutdownProbeRuntime();
        var context = new GrainDurableTaskContext(
            TaskId.Parse("root"),
            runtime,
            Substitute.For<IGrainContext>(),
            TaskScheduler.Current);
        var invocation = DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Delay(TimeSpan.FromDays(1)),
            context).AsTask();
        var operationCancellation = await runtime.SchedulingStarted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(context.CancellationToken.IsCancellationRequested);
        Assert.False(operationCancellation.IsCancellationRequested);
        Assert.True(context.EndAttempt(DurableTaskAttemptCancellationReason.ActivationStopping));
        Assert.False(context.EndAttempt(DurableTaskAttemptCancellationReason.TaskAborted));

        // 执行尝试的令牌与其取消操作分开触发。
        Assert.False(operationCancellation.IsCancellationRequested);
        context.ApplyCancellation();
        var response = await invocation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(DurableTaskStatus.Canceled, response.Status);
        var exception = Assert.IsType<DurableTaskAttemptCanceledException>(response.Exception);
        Assert.Equal(DurableTaskAttemptCancellationReason.ActivationStopping, exception.Reason);
        Assert.Equal(context.AttemptCancellationToken, exception.CancellationToken);
        Assert.True(operationCancellation.IsCancellationRequested);
        Assert.True(context.AttemptCancellationToken.IsCancellationRequested);
        Assert.True(context.CancellationToken.IsCancellationRequested);
        Assert.False(context.IsTaskCancellationRequested);
    });

    [Fact]
    public Task OperationWhichCompletesAfterItsAttemptIsCanceledEndsWithTheAttemptsCancellation() => ActivationScheduler.RunAsync(async () =>
    {
        var runtime = new ShutdownProbeRuntime();
        var context = new GrainDurableTaskContext(
            TaskId.Parse("root"),
            runtime,
            Substitute.For<IGrainContext>(),
            TaskScheduler.Current);
        var invocation = DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Delay(TimeSpan.FromDays(1)),
            context).AsTask();
        await runtime.SchedulingStarted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 中止会取消执行尝试，且中止的提交会在执行尝试的令牌触发前使延迟操作完成。
        Assert.True(context.EndAttempt(DurableTaskAttemptCancellationReason.TaskAborted));
        var aborted = new DurableTaskAbortedException("operator");
        runtime.CompleteDelay(DurableTaskResponse.FromException(aborted));
        var response = await invocation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var exception = Assert.IsType<DurableTaskAttemptCanceledException>(response.Exception);
        Assert.Equal(DurableTaskAttemptCancellationReason.TaskAborted, exception.Reason);
        Assert.False(context.AttemptCancellationToken.IsCancellationRequested);
    });

    [Fact]
    public Task DurableCancellationStillTerminalizesRunningExecution() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async durableCancellation =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, durableCancellation);
            }));
        var taskId = TaskId.Parse("root");

        await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, request, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await runtime.SignalCancellationAsync(taskId, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(taskId).Result is { IsCompleted: true });

        Assert.NotNull(storage.Get(taskId).CancellationRequestedAt);
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(taskId).Result!.Status);
        await runtime.StopAsync(TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task StopEstablishesShutdownDespiteCanceledCallerToken() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, _, _) = CreateRuntime();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await runtime.StopAsync(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.ResumePendingTasksAsync(TestContext.Current.CancellationToken));
    });

    [Fact]
    public Task StopDoesNotWaitForStagedInboxResponseCommit() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/remote");
        var target = GrainId.Create("target", "one");
        var state = storage.GetOrCreate(taskId);
        storage.SetRemoteRequest(taskId, state, target, "method", "fingerprint");
        var handle = runtime.GetScheduledTaskHandle(taskId);
        runtime.AcceptResponseFromInbox(taskId, DurableTaskResponse.FromResult(42), target);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await runtime.StopAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 本地等待方因关闭而失败；已暂存的响应仍会随收件箱批次一起提交，供下一个激活实例使用。
        await Assert.ThrowsAsync<DurableTaskAttemptCanceledException>(async () => await handle.WaitAsync(TestContext.Current.CancellationToken));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(42, storage.Get(taskId).Result!.GetResult<int>());
        await Assert.ThrowsAsync<DurableTaskAttemptCanceledException>(async () => await handle.WaitAsync(TestContext.Current.CancellationToken));
    });

    [Fact]
    public Task StopDrainsUncooperativeExecutionBeforeReplacementReplay() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maxActive = 0;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ =>
            {
                var count = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, count);
                started.TrySetResult();
                try
                {
                    await release.Task;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }));
        var taskId = TaskId.Parse("root");

        await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, request, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var firstStop = runtime.StopAsync(TestContext.Current.CancellationToken);
        Assert.Same(firstStop, runtime.StopAsync(TestContext.Current.CancellationToken));
        Assert.False(firstStop.IsCompleted);
        release.TrySetResult();
        await firstStop.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, active);
        Assert.Null(storage.Get(taskId).Result);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.ResumePendingTasksAsync(TestContext.Current.CancellationToken));

        started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var (replacement, _, _, _) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, maxActive);
        var replacementStop = replacement.StopAsync(TestContext.Current.CancellationToken);
        release.TrySetResult();
        await replacementStop.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(storage.Get(taskId).Result);
    });

    [Fact]
    public Task FirstTerminalResponseWinsAndRetirementRemovesOnlyItsDestination() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/child");
        var target = GrainId.Create("target", "one");
        var waiter = GrainId.Create("caller", "one");
        var other = GrainId.Create("caller", "two");
        var state = storage.GetOrCreate(taskId);
        storage.SetRemoteRequest(taskId, state, target, "method", "fingerprint");
        state.RequestFingerprint = "invocation";
        state.Incarnation = Guid.NewGuid();
        state.CompletionDestinations.Add(new(waiter, taskId));
        state.CompletionDestinations.Add(new(other, taskId));
        var handle = runtime.GetScheduledTaskHandle(taskId);

        runtime.AcceptResponseFromInbox(taskId, DurableTaskResponse.FromResult(7), target);
        runtime.AcceptResponseFromInbox(taskId, DurableTaskResponse.FromResult(8), target);
        var waiterCompletion = DurableTaskMessageTransport.CreateMessageId(runtime.GrainId, waiter, taskId, DurableTaskMessageTypes.CompletionSubject);
        Assert.True(((IOutboxMessageSource)runtime).TryGetMessage(waiterCompletion, out _));
        ((IOutboxMessageSource)runtime).OnRetired(waiterCompletion, OutboxMessageRetirement.Delivered.Instance);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7, storage.Get(taskId).Result!.GetResult<int>());
        Assert.Equal(7, (await handle.WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        Assert.DoesNotContain(new DurableTaskSubscriber(waiter, taskId), storage.Get(taskId).CompletionDestinations);
        Assert.Contains(new DurableTaskSubscriber(other, taskId), storage.Get(taskId).CompletionDestinations);
        Assert.False(((IOutboxMessageSource)runtime).TryGetMessage(waiterCompletion, out _));
        Assert.Empty(transport.Completions);
    });

    [Fact]
    public Task ReplayedLocalChildReplacesRehydratedPlaceholder() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        storage.GetOrCreate(taskId);

        var handle = await runtime.ScheduleChildAsync(taskId, DurableTask.Run(_ => { }), TestContext.Current.CancellationToken);

        Assert.Equal(taskId, handle.TaskId);
    });

    [Fact]
    public Task SynchronousLocalStepCommitsItsRecordWithItsResultInOneWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        var invocations = 0;

        // 步骤先运行，因此记录步骤的写入也会记录其结果。
        manager.BeforeWrite = () => Assert.Equal(42, storage.Get(taskId).Result!.GetResult<int>());
        var handle = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(_ =>
            {
                Interlocked.Increment(ref invocations);
                return 42;
            }),
            TestContext.Current.CancellationToken);
        var response = await handle.WaitAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(42, response.GetResult<int>());
        Assert.Equal(1, invocations);
        Assert.Equal(1, manager.WriteCount);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task LocalStepStagesItsResultInTheWorkItemWhichRunsItsFinalCode(bool asyncMethod) => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        var suspended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? probe = null;

        // 步骤的最后一段代码在激活实例上排队一个工作项，模拟 Grain 操作在
        // 步骤完成期间请求日志写入的情形。该工作项在步骤的任何延续代码之前运行，因此它请求的写入会提交
        // 步骤的状态更改，并且必须发现步骤的结果已经与这些更改一起暂存。
        int FinalCode()
        {
            probe = Task.Factory.StartNew(
                () => storage.Get(taskId).Result is { IsCompleted: true },
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Current);
            return 42;
        }

        async Task Suspend()
        {
            suspended.SetResult();
            await resume.Task;
        }

        async DurableTask<int> Step()
        {
            await Suspend();
            return FinalCode();
        }

        var definition = asyncMethod
            ? Step()
            : DurableTask.Run(async _ =>
            {
                await Suspend();
                return FinalCode();
            });
        var handle = await runtime.ScheduleChildAsync(taskId, definition, TestContext.Current.CancellationToken);
        await suspended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        resume.SetResult();
        var response = await handle.WaitAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(42, response.GetResult<int>());
        Assert.NotNull(probe);
        Assert.True(await probe);
    });

    [Fact]
    public Task RunInlineCommitsItsRecordWithTheNextWriteAndReplayReturnsItWithoutRunningTheEffect() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root");
        var effectId = TaskId.Parse("root/effect");
        var runs = 0;
        var observed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ =>
            {
                var value = DurableTask.RunInline("effect", () => 40 + Interlocked.Increment(ref runs));
                observed.TrySetResult(value);
                await DurableTask.Delay(TimeSpan.FromDays(1));
                return value;
            }));

        // 记录仅暂存，不触发独立写入，而是随延迟操作的写入一起提交。
        manager.BeforeWrite = () =>
        {
            if (storage.Contains(effectId))
            {
                Assert.True(storage.Contains(TaskId.Parse("root/$child-1")));
            }
        };
        runtime.ScheduleFromInbox(taskId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(41, await observed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await transport.ResumeSent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(41, storage.Get(effectId).Result!.GetResult<int>());
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        observed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (replacement, _, _, _) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);

        Assert.Equal(41, await observed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(1, runs);
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task ReplayReportsReplayingUntilItReachesEveryRecordedOperation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var observations = new ConcurrentQueue<(int Run, string Point, bool Replaying)>();
        TaskCompletionSource<DurableTaskContext>[] waiting = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        var runs = 0;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ =>
            {
                var run = Interlocked.Increment(ref runs);
                var context = DurableTask.Current;
                observations.Enqueue((run, "start", context.IsReplaying));
                DurableTask.RunInline("effect", () => 1);
                observations.Enqueue((run, "effect", context.IsReplaying));
                await DurableTask.Run(static _ => 2).WithId("step");
                observations.Enqueue((run, "step", context.IsReplaying));
                waiting[run - 1].TrySetResult(context);
                await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("delay");
            }));

        runtime.ScheduleFromInbox(TaskId.Parse("root"), request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var first = await waiting[0].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Contains(TaskId.Parse("root/delay")));
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var (replacement, _, _, _) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        var replay = await waiting[1].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 重放到达延迟操作（首次运行记录的最后一个操作），随后进入实际执行阶段。
        await WaitUntilAsync(() => !replay.IsReplaying);
        Assert.False(first.IsReplaying);
        Assert.Equal(
            [
                (1, "start", false), (1, "effect", false), (1, "step", false),
                (2, "start", true), (2, "effect", true), (2, "step", true),
            ],
            observations);
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task ReplayWhichStartsAnUnrecordedOperationStopsReplaying() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var observations = new ConcurrentQueue<(int Run, string Point, bool Replaying)>();
        TaskCompletionSource[] waiting = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        var runs = 0;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ =>
            {
                var run = Interlocked.Increment(ref runs);
                var context = DurableTask.Current;
                if (run > 1)
                {
                    // 一次部署在已记录的效果操作之前添加了一个效果操作。
                    observations.Enqueue((run, "start", context.IsReplaying));
                    DurableTask.RunInline("added", () => 0);
                }

                DurableTask.RunInline("effect", () => 1);
                observations.Enqueue((run, "effect", context.IsReplaying));
                waiting[run - 1].TrySetResult();
                await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("delay");
            }));

        runtime.ScheduleFromInbox(TaskId.Parse("root"), request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await waiting[0].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Contains(TaskId.Parse("root/delay")));
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var (replacement, _, _, _) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await waiting[1].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal([(1, "effect", false), (2, "start", true), (2, "effect", false)], observations);
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task ReplayWhichReachesARecordedOperationAgainKeepsReplaying() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var detachedRequest = CreateRemoteRequest(1);
        var observations = new ConcurrentQueue<(int Run, string Point, bool Replaying)>();
        TaskCompletionSource[] waiting = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        var runs = 0;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async executionToken =>
            {
                var run = Interlocked.Increment(ref runs);
                var context = DurableTask.Current;

                // 第二次启动同一个分离任务时，会到达首次启动所到达的记录。
                _ = new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("detached").Post();
                _ = new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("detached").Post();
                observations.Enqueue((run, "detached", context.IsReplaying));
                DurableTask.RunInline("effect", () => 1);
                observations.Enqueue((run, "effect", context.IsReplaying));
                waiting[run - 1].TrySetResult();
                await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("delay");
            }));

        runtime.ScheduleFromInbox(TaskId.Parse("root"), request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await waiting[0].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Contains(TaskId.Parse("root/delay")));
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var (replacement, _, _, _) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await waiting[1].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal([(1, "detached", false), (1, "effect", false), (2, "detached", true), (2, "effect", true)], observations);
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task RunInlineUnderAnIdentifierRecordedForAnotherOperationDiverges() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");
        var effectId = TaskId.Parse("root/local/effect");
        storage.SetRequestFingerprint(effectId, storage.GetOrCreate(effectId), "$detach:other");
        var runs = 0;
        using var stalls = new StallRecorder(taskId);

        var handle = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(_ => DurableTask.RunInline("effect", () => ++runs)),
            TestContext.Current.CancellationToken);

        var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
        Assert.Contains("a DurableTask.RunInline effect", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.False(storage.Get(taskId).Result is { IsCompleted: true });
        Assert.Equal(0, runs);
    });

    [Fact]
    public Task LocalStepAwaitingADelayCommitsItsRecordWithTheDelay() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("root"));
        var taskId = TaskId.Parse("root/wait");
        manager.BeforeWrite = () =>
        {
            // 步骤记录、其延迟子任务和延迟操作的恢复消息一起提交。
            Assert.True(storage.Contains(taskId));
            Assert.Single(transport.Resumes);
        };

        _ = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(async _ => await DurableTask.Delay(TimeSpan.FromDays(1))),
            TestContext.Current.CancellationToken);
        await transport.ResumeSent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => manager.WriteCount == 1);

        Assert.Equal(1, manager.WriteCount);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task LocalChildHandleIsTheHandleWhichPollingReturns() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/local");

        var scheduledHandle = await runtime.ScheduleChildAsync(taskId, new TestStateManager.PendingDurableTask(), TestContext.Current.CancellationToken);

        Assert.Same(runtime.GetScheduledTaskHandle(taskId), scheduledHandle);
    });

    [Fact]
    public Task FailedLocalStepWriteFencesTheGrainWithoutCompletingTheStep() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, manager, _) = CreateRuntime();
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskId = TaskId.Parse("root/local");
        manager.BeforeWrite = () => throw new InvalidOperationException("Expected write failure.");

        var handle = await runtime.ScheduleChildAsync(taskId, DurableTask.Run(_ => invoked.TrySetResult()), TestContext.Current.CancellationToken);
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => manager.IsFenced);

        // 步骤执行的内容均未记录，因此下一个激活实例会重放该步骤并再次运行它。
        Assert.Equal(0, manager.WriteCount);
        Assert.False((await handle.PollAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken)).IsCompleted);
    });

    [Fact]
    public Task LocalStepPreparationFailureRetainsItsResultForTheNextWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/local-retry");
        var invocations = 0;
        manager.BeforePrepare = () =>
        {
            manager.BeforePrepare = null;
            throw new IOException("Expected initial preparation failure.");
        };

        var handle = await runtime.ScheduleChildAsync(
            taskId,
            DurableTask.Run(_ =>
            {
                Interlocked.Increment(ref invocations);
                return 42;
            }),
            TestContext.Current.CancellationToken);

        // 失败的写入保留了已暂存结果，后台写入提交该结果并使步骤完成。
        var response = await handle.WaitAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(42, response.GetResult<int>());
        Assert.Equal(1, invocations);
        Assert.Equal(1, manager.WriteCount);
        Assert.Equal(42, storage.Get(taskId).Result!.GetResult<int>());
    });
    [Fact]
    public Task RootSchedulingPreparationFailureRetainsRequestAndRetryInvokesOnce() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/retry");
        var invoked = 0;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(_ => Interlocked.Increment(ref invoked)));
        manager.BeforePrepare = () =>
        {
            manager.BeforePrepare = null;
            throw new IOException("Expected initial preparation failure.");
        };

        await Assert.ThrowsAsync<JournaledStatePreCommitException>(
            () => ((IDurableTaskServer)runtime).ScheduleAsync(
                taskId,
                request,
                TestContext.Current.CancellationToken).AsTask());

        Assert.True(storage.Contains(taskId));
        Assert.Equal(0, invoked);

        _ = await ((IDurableTaskServer)runtime).ScheduleAsync(
            taskId,
            request,
            TestContext.Current.CancellationToken);
        var response = await runtime.GetScheduledTaskHandle(taskId)
            .WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, invoked);
        Assert.Equal(1, response.GetResult<int>());
        Assert.Equal(2, manager.WriteCount);
    });

    [Fact]
    public Task RetentionDeletesEachExpiredInvokedTaskWithItsExecutionTree() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime(TimeSpan.Zero);
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/child");
        var grandchildId = TaskId.Parse("root/child/grandchild");
        var triggerId = TaskId.Parse("trigger");
        var root = storage.GetOrCreate(rootId);
        storage.SetResponse(rootId, root, DurableTaskResponse.Completed);
        root.RequestFingerprint = "root";
        var child = storage.GetOrCreate(childId);
        storage.SetResponse(childId, child, DurableTaskResponse.Completed);
        child.RequestFingerprint = "child";
        var grandchild = storage.GetOrCreate(grandchildId);
        storage.SetResponse(grandchildId, grandchild, DurableTaskResponse.Completed);
        var observedDeletionWrite = false;
        manager.BeforeWrite = () => observedDeletionWrite |=
            !storage.Contains(rootId) && !storage.Contains(childId) && !storage.Contains(grandchildId);

        var handle = await runtime.ScheduleChildAsync(triggerId, DurableTask.Run(_ => { }), TestContext.Current.CancellationToken);
        await handle.WaitAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => Volatile.Read(ref observedDeletionWrite));

        // 被调用的子任务拥有自己的任务树，其中包含孙任务；每棵任务树在其保留期结束后删除。
        Assert.False(storage.Contains(rootId));
        Assert.False(storage.Contains(childId));
        Assert.False(storage.Contains(grandchildId));
    });

    [Fact]
    public Task DeliveredCompletionDeletesTaskWithItsExecutionTreeInTheRetirementWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime(TimeSpan.FromDays(1));
        var taskId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/child");
        var loserId = TaskId.Parse("root/$when-any-1/0");
        var decisionId = TaskId.Parse("root/$when-any-1/$winner");
        var nestedId = TaskId.Parse("root/nested-invocation");
        var target = GrainId.Create("target", "one");
        var request = CreateRequest(1);
        var caller = request.Context!.CallerId;
        runtime.ScheduleFromInbox(taskId, request);
        storage.SetResponse(childId, storage.GetOrCreate(childId), DurableTaskResponse.Completed);
        storage.SetRemoteRequest(loserId, storage.GetOrCreate(loserId), target, "method", "loser");
        var decision = storage.GetOrCreate(decisionId);
        decision.RequestFingerprint = "$completion-decision:winner";
        storage.SetResponse(decisionId, decision, DurableTaskResponse.FromResult(childId));
        storage.GetOrCreate(nestedId).RequestFingerprint = "nested";
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.Completions.Count == 1 && manager.WriteCount >= 2);
        await WaitUntilIdleAsync(runtime);
        var loserWireId = runtime.GetWireId(loserId);
        var completion = Assert.Single(transport.Completions);
        Assert.Equal(caller, completion.Target);
        Assert.Equal(0, completion.Materialize(runtime).Response.GetResult<int>());
        Assert.True(storage.LogicalTimes.ContainsKey(taskId));
        var writes = manager.WriteCount;
        var enumerations = storage.TasksEnumerationCount;

        // 发件箱在同一个同步代码块内暂存完成消息的删除操作，并调用消息来源。
        ((IOutboxMessageSource)runtime).OnRetired(completion.MessageId, OutboxMessageRetirement.Delivered.Instance);

        Assert.False(storage.Contains(taskId));
        Assert.False(storage.Contains(childId));
        Assert.False(storage.Contains(loserId));
        Assert.False(storage.Contains(decisionId));
        Assert.False(storage.LogicalTimes.ContainsKey(taskId));
        Assert.False(((IOutboxMessageSource)runtime).TryGetMessage(completion.MessageId, out _));
        Assert.Equal((runtime.GrainId, target, loserWireId), Assert.Single(transport.Cancellations));
        Assert.Equal(writes, manager.WriteCount);

        // 嵌套在任务标识符下的调用任务保有自己的生命周期。
        Assert.True(storage.Contains(nestedId));

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(writes + 1, manager.WriteCount);

        // 删除过程沿执行树进行，而不是扫描所有任务。
        Assert.Equal(enumerations, storage.TasksEnumerationCount);

        // 已取消远程子任务迟到的完成消息会被接纳，但不改变状态。
        Accept(CreateHandler(runtime), CompletionMessage(target, runtime.GrainId, loserWireId, DurableTaskResponse.FromResult(5)));
        Assert.False(storage.Contains(loserId));
    });

    [Fact]
    public Task DeadLetteredCompletionRetainsResultForRetentionPolicy() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime(TimeSpan.FromDays(1));
        var taskId = TaskId.Parse("root");
        var request = CreateRequest(1);
        runtime.ScheduleFromInbox(taskId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.Completions.Count == 1 && manager.WriteCount >= 2);
        var completion = Assert.Single(transport.Completions);

        ((IOutboxMessageSource)runtime).OnRetired(completion.MessageId, new OutboxMessageRetirement.DeadLettered("The outbox dead-lettered the completion."));

        var retained = storage.Get(taskId);
        Assert.Equal(0, retained.Result!.GetResult<int>());
        Assert.Empty(retained.CompletionDestinations);
        Assert.Equal(0, (await runtime.SubscribeOrPollAsync(taskId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken)).GetResult<int>());

        // 完成消息转入死信后重新调用任务，会再次发送保留的结果。
        Assert.Equal(0, runtime.ScheduleFromInbox(taskId, request).GetResult<int>());
        Assert.Equal(2, transport.Completions.Count);
        Assert.Equal(completion.MessageId, transport.Completions[1].MessageId);
        Assert.True(((IOutboxMessageSource)runtime).TryGetMessage(completion.MessageId, out _));
    });

    [Fact]
    public Task LateDeadLetteredCompletionRetainsResultForRetentionPeriodAfterDeadLetter() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var retention = TimeSpan.FromHours(1);
        var (runtime, storage, manager, transport) = CreateRuntime(retention, timeProvider: clock);
        var taskId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/child");
        runtime.ScheduleFromInbox(taskId, CreateRequest(1));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.Completions.Count == 1 && manager.WriteCount >= 2);
        await WaitUntilIdleAsync(runtime);
        storage.SetResponse(childId, storage.GetOrCreate(childId), DurableTaskResponse.Completed);
        var completion = Assert.Single(transport.Completions);

        // 发件箱在 OutboxRetryTimeout 后将完成消息转入死信，该超时时长超过保留期。
        clock.Advance(TimeSpan.FromDays(1));
        ((IOutboxMessageSource)runtime).OnRetired(completion.MessageId, new OutboxMessageRetirement.DeadLettered("The outbox dead-lettered the completion."));
        Assert.Equal(clock.GetUtcNow(), storage.Get(taskId).RetainedSince);

        clock.Advance(retention - TimeSpan.FromTicks(1));
        Assert.False(InvokeDeleteExpiredTasks(runtime));
        Assert.True(storage.Contains(taskId));
        Assert.Equal(0, (await runtime.SubscribeOrPollAsync(taskId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken)).GetResult<int>());

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(InvokeDeleteExpiredTasks(runtime));
        Assert.False(storage.Contains(taskId));
        Assert.False(storage.Contains(childId));
        await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            async () => await runtime.SubscribeOrPollAsync(taskId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken));
    });

    [Fact]
    public Task ClientScheduledTaskIsDeletedWithItsExecutionTreeOnceItsRetentionEnds() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var retention = TimeSpan.FromHours(1);
        var (runtime, storage, _, _) = CreateRuntime(retention, timeProvider: clock);
        var taskId = TaskId.Parse("client-root");
        var childId = TaskId.Parse("client-root/child");
        var request = CreateRequest(1);
        await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, request, TestContext.Current.CancellationToken);
        Assert.Equal(0, (await runtime.GetScheduledTaskHandle(taskId).WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        await WaitUntilIdleAsync(runtime);
        storage.SetResponse(childId, storage.GetOrCreate(childId), DurableTaskResponse.Completed);

        clock.Advance(retention - TimeSpan.FromTicks(1));
        Assert.False(InvokeDeleteExpiredTasks(runtime));
        Assert.Equal(0, (await runtime.SubscribeOrPollAsync(taskId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken)).GetResult<int>());

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(InvokeDeleteExpiredTasks(runtime));
        Assert.False(storage.Contains(taskId));
        Assert.False(storage.Contains(childId));
        Assert.False(storage.LogicalTimes.ContainsKey(taskId));
        var failure = await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            async () => await runtime.SubscribeOrPollAsync(taskId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(taskId, failure.TaskId);

        // 任务标识符仅在结果保留期间对调度请求去重。
        var again = CreateRequest(1);
        await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, again, TestContext.Current.CancellationToken);
        Assert.Equal(0, (await runtime.GetScheduledTaskHandle(taskId).WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        Assert.Equal(1, request.CreateTaskCallCount);
        Assert.Equal(1, again.CreateTaskCallCount);
    });

    [Fact]
    public Task ScheduleOfAnIdleGrainObservesTheEndOfRetention() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var retention = TimeSpan.FromHours(1);
        var (runtime, storage, _, _) = CreateRuntime(retention, timeProvider: clock);
        var polledId = TaskId.Parse("polled-root");
        var scheduledId = TaskId.Parse("scheduled-root");
        var polled = CreateRequest(1);
        var scheduled = CreateRequest(2);
        await ((IDurableTaskServer)runtime).ScheduleAsync(polledId, polled, TestContext.Current.CancellationToken);
        await ((IDurableTaskServer)runtime).ScheduleAsync(scheduledId, scheduled, TestContext.Current.CancellationToken);
        Assert.Equal(0, (await runtime.GetScheduledTaskHandle(polledId).WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        Assert.Equal(0, (await runtime.GetScheduledTaskHandle(scheduledId).WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        await WaitUntilIdleAsync(runtime);

        // 保留期结束后没有发生写入或激活，因此调度操作自行处理保留期结束并再次
        // 运行请求，后续轮询会观察到删除结果。
        clock.Advance(retention);
        var again = CreateRequest(2);
        await ((IDurableTaskServer)runtime).ScheduleAsync(scheduledId, again, TestContext.Current.CancellationToken);
        Assert.Equal(0, (await runtime.GetScheduledTaskHandle(scheduledId).WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        Assert.Equal((1, 1), (scheduled.CreateTaskCallCount, again.CreateTaskCallCount));
        Assert.False(storage.Contains(polledId));

        var failure = await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            async () => await runtime.SubscribeOrPollAsync(polledId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(polledId, failure.TaskId);
    });

    [Fact]
    public Task PollOfAnIdleGrainObservesTheEndOfRetention() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var retention = TimeSpan.FromHours(1);
        var (runtime, storage, _, _) = CreateRuntime(retention, timeProvider: clock);
        var taskId = TaskId.Parse("polled-root");
        await ((IDurableTaskServer)runtime).ScheduleAsync(taskId, CreateRequest(1), TestContext.Current.CancellationToken);
        Assert.Equal(0, (await runtime.GetScheduledTaskHandle(taskId).WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        await WaitUntilIdleAsync(runtime);

        clock.Advance(retention);
        var failure = await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            async () => await runtime.SubscribeOrPollAsync(taskId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(taskId, failure.TaskId);
        Assert.False(storage.Contains(taskId));
    });

    [Fact]
    public Task InvokedTaskNestedUnderAnotherTaskKeepsItsOwnLifecycle() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var retention = TimeSpan.FromHours(1);
        var (runtime, storage, manager, transport) = CreateRuntime(retention, timeProvider: clock);
        var outerId = TaskId.Parse("outer");
        var localId = TaskId.Parse("outer/local");
        var nestedId = TaskId.Parse("outer/nested");
        var nestedChildId = TaskId.Parse("outer/nested/child");
        var outer = storage.GetOrCreate(outerId);
        outer.RequestFingerprint = "outer";
        storage.SetResponse(outerId, outer, DurableTaskResponse.Completed);
        storage.SetResponse(localId, storage.GetOrCreate(localId), DurableTaskResponse.Completed);

        // 任务标识符由调用方选择，因此另一个 Grain 可以在外层任务的标识符下调用任务。
        runtime.ScheduleFromInbox(nestedId, CreateRequest(1));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.Completions.Count == 1);
        await WaitUntilIdleAsync(runtime);
        storage.SetResponse(nestedChildId, storage.GetOrCreate(nestedChildId), DurableTaskResponse.Completed);

        clock.Advance(retention);
        Assert.True(InvokeDeleteExpiredTasks(runtime));
        Assert.False(storage.Contains(outerId));
        Assert.False(storage.Contains(localId));
        Assert.True(storage.Contains(nestedId));
        Assert.True(storage.Contains(nestedChildId));

        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.Completions).MessageId, OutboxMessageRetirement.Delivered.Instance);

        Assert.False(storage.Contains(nestedId));
        Assert.False(storage.Contains(nestedChildId));
    });

    [Fact]
    public Task DeletingTreeCancelsRunningLocalChildAndDiscardsItsResult() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime(TimeSpan.FromDays(1));
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/child");
        runtime.ScheduleFromInbox(rootId, CreateRequest(1));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.Completions.Count == 1);
        await WaitUntilIdleAsync(runtime);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var childHandle = await runtime.ScheduleChildAsync(childId, DurableTask.Run(async cancellationToken =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (DurableTask.Current.AttemptCancellationToken.IsCancellationRequested)
                {
                    // 已删除的执行尝试不能调度可能在其执行树删除后仍继续存在的持久化工作。
                    canceled.TrySetResult(await Record.ExceptionAsync(async () => await DurableTask.Delay(TimeSpan.FromDays(1))));
                    throw new InvalidOperationException("The deleted attempt fails after cancellation.");
                }
            }), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.Completions).MessageId, OutboxMessageRetirement.Delivered.Instance);

        Assert.False(storage.Contains(rootId));
        Assert.False(storage.Contains(childId));
        Assert.Equal(DurableTaskStatus.Canceled, (await childHandle.WaitAsync(TestContext.Current.CancellationToken)).Status);
        Assert.False(canceled.Task.IsCompleted);

        // 删除操作取消正在运行的执行尝试，其令牌将在后续工作项中触发。
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var exception = Assert.IsType<DurableTaskAttemptCanceledException>(await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(DurableTaskAttemptCancellationReason.TaskDeleted, exception.Reason);
        Assert.Empty(storage.Tasks);
        var writes = manager.WriteCount;

        // 停用过程等待执行尝试收尾，丢弃其结果而不恢复状态。
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(storage.Contains(childId));
        Assert.Equal(writes, manager.WriteCount);
        Assert.Empty(transport.Cancellations);
    });

    [Fact]
    public Task EarlyCancellationRecordIsDeletedOnceTwiceTheInvocationAdmissionWindowEnds() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var retryAge = TimeSpan.FromHours(2);
        var (runtime, storage, manager, _) = CreateRuntime(
            TimeSpan.FromHours(1),
            timeProvider: clock,
            deduplicationWindow: retryAge);
        var taskId = TaskId.Parse("root");
        var caller = GrainId.Create("caller", "one");

        // 针对未知任务（例如已删除任务）的取消会被记录，
        // 以防其调用消息仍在传输途中。
        Accept(CreateHandler(runtime), CancellationMessage(caller, runtime.GrainId, taskId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var record = storage.Get(taskId);
        Assert.NotNull(record.CancellationRequestedAt);
        Assert.Null(record.Result);

        // 该记录比接纳窗口多保留一个窗口的时长，以应对发送方时钟走在前面的情况。
        clock.Advance(retryAge * 2 - TimeSpan.FromTicks(1));
        Assert.False(InvokeDeleteExpiredTasks(runtime));
        Assert.True(storage.Contains(taskId));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(InvokeDeleteExpiredTasks(runtime));
        Assert.False(storage.Contains(taskId));
    });

    private static bool InvokeDeleteExpiredTasks(DurableTaskGrainRuntime runtime) =>
        (bool)typeof(DurableTaskGrainRuntime)
            .GetMethod("DeleteExpiredTasks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(runtime, null)!;

    private static Task WaitUntilIdleAsync(DurableTaskGrainRuntime runtime) =>
        WaitUntilAsync(() => !((IDurableTaskGrainExtension)runtime)
            .GetRunningTasksAsync(CancellationToken.None)
            .ToBlockingEnumerable()
            .Any());

    [Fact]
    public Task CompletionPruningIsPersistedAfterHandleCompletion() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime(TimeSpan.Zero);
        var taskId = TaskId.Parse("root/local");
        var observedPrunedWrite = false;
        manager.BeforeWrite = () =>
        {
            // 第一次写入提交步骤及其结果；下一次写入删除它。
            if (manager.WriteCount >= 1)
            {
                observedPrunedWrite = !storage.Contains(taskId);
            }
        };

        var handle = await runtime.ScheduleChildAsync(taskId, DurableTask.Run(_ => { }), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, (await handle.WaitAsync(TestContext.Current.CancellationToken)).Status);
        await WaitUntilAsync(() => manager.WriteCount >= 2);

        Assert.True(observedPrunedWrite);
        Assert.False(storage.Contains(taskId));
    });

    [Fact]
    public Task ScheduleChildAsync_ConcurrentAncestorCancellation_DoesNotPersistOrStartChild() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/remote-child");
        storage.GetOrCreate(rootId);
        var rootHandle = runtime.GetScheduledTaskHandle(rootId);
        var receiver = GrainId.Create("test", "one");
        var caller = GrainId.Create("caller", "one");
        var handler = CreateHandler(runtime);
        var cancellationWriteEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellationWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AfterWriteStarted = async () =>
        {
            manager.AfterWriteStarted = null;
            cancellationWriteEntered.TrySetResult();
            await releaseCancellationWrite.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        };

        Accept(handler, CancellationMessage(caller, receiver, rootId));
        var cancellationWrite = manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask();
        await cancellationWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var remoteRequest = CreateRemoteRequest(17);
        var childTask = new TestStateManager.TestRemoteDurableTask(remoteRequest);

        IScheduledTaskHandle handle;
        try
        {
            // 已暂存的取消在提交前即可见，由它引发的任何效果都会在它之后提交。
            handle = await runtime.ScheduleChildAsync(
                childId,
                childTask,
                TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(cancellationWrite.IsCompleted);
            Assert.False(storage.Contains(childId));
            Assert.Equal(0, childTask.ScheduleAsyncCallCount);
            Assert.Empty(transport.Invocations);
            Assert.Equal(0, manager.WriteCount);
        }
        finally
        {
            releaseCancellationWrite.TrySetResult();
        }

        await cancellationWrite;
        var rootResponse = Assert.IsType<CanceledDurableTaskResponse>(
            await rootHandle.WaitAsync(TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var response = Assert.IsType<CanceledDurableTaskResponse>(
            await handle.WaitAsync(TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Equal(childId, handle.TaskId);
        Assert.Equal(DurableTaskStatus.Canceled, rootResponse.Status);
        Assert.Equal(DurableTaskResponseKind.Canceled, response.ResponseKind);
        Assert.Equal(DurableTaskStatus.Canceled, response.Status);
        Assert.Equal("The operation was canceled.", response.Exception.Message);
        Assert.NotNull(storage.Get(rootId).CancellationRequestedAt);
        Assert.False(storage.Contains(childId));
        Assert.Equal(0, childTask.ScheduleAsyncCallCount);
        Assert.Empty(transport.Invocations);
        await WaitUntilAsync(() => manager.WriteCount == 2);
    });

    [Fact]
    public Task ScheduleChildAsync_CanceledAncestorPreservesCompletedChildResult() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/completed-child");
        var request = CreateRemoteRequest(17);
        var childTask = new TestStateManager.TestRemoteDurableTask(request);
        var child = storage.GetOrCreate(childId);
        storage.SetRemoteRequest(
            childId,
            child,
            request.Context!.TargetId,
            IDurableTaskRequest.GetMethod(childTask),
            IDurableTaskRequest.GetFingerprint(childTask, CreateSerializer()));
        child.Result = DurableTaskResponse.FromResult(42);
        child.CompletedAt = DateTimeOffset.UtcNow;
        storage.RequestCancellation(rootId, storage.GetOrCreate(rootId));

        var handle = await runtime.ScheduleChildAsync(
            childId,
            childTask,
            TestContext.Current.CancellationToken);

        Assert.Equal(42, (await handle.WaitAsync(TestContext.Current.CancellationToken)).GetResult<int>());
        Assert.Empty(transport.Invocations);
    });

    [Fact]
    public Task ScheduleChildAsync_CanceledAncestorStillRejectsConflictingChildIdentity() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        var childId = TaskId.Parse("root/conflicting-child");
        var originalRequest = CreateRemoteRequest(17);
        var originalTask = new TestStateManager.TestRemoteDurableTask(originalRequest);
        var child = storage.GetOrCreate(childId);
        storage.SetRemoteRequest(
            childId,
            child,
            originalRequest.Context!.TargetId,
            IDurableTaskRequest.GetMethod(originalTask),
            IDurableTaskRequest.GetFingerprint(originalTask, CreateSerializer()));
        storage.RequestCancellation(rootId, storage.GetOrCreate(rootId));

        var otherTarget = CreateRemoteRequest(17);
        otherTarget.Context!.TargetId = GrainId.Create("target", "two");
        var exception = await Assert.ThrowsAsync<DurableTaskDivergenceException>(
            () => runtime.ScheduleChildAsync(
                childId,
                new TestStateManager.TestRemoteDurableTask(otherTarget),
                TestContext.Current.CancellationToken).AsTask());

        // 错误信息列出已记录的操作和请求的操作，以便诊断发生偏离的重放。
        Assert.StartsWith(
            $"Durable child task '{childId}' is already associated with a different request. It records a call to method '{IDurableTaskRequest.GetMethod(originalTask)}' on grain '{originalRequest.Context!.TargetId}' (fingerprint ",
            exception.Message);
        Assert.Contains("and this attempt requested a call to method", exception.Message, StringComparison.Ordinal);
        Assert.Empty(transport.Invocations);
    });

    [Fact]
    public Task ScheduleChildAsync_DivergingReplayNamesRecordedAndRequestedOperations() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var childId = TaskId.Parse("root/step");
        var target = GrainId.Create("target", "one");
        storage.SetRemoteRequest(childId, storage.GetOrCreate(childId, DurableTaskRecordKind.RemoteCall), target, "method", "0123456789ABCDEF0123");

        // 工作流已改变：重放在首次执行记录 Grain 调用的位置调度本地步骤。
        var exception = await Assert.ThrowsAsync<DurableTaskDivergenceException>(
            () => runtime.ScheduleChildAsync(
                childId,
                DurableTask.FromResult(1),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(
            $"Durable child task '{childId}' is already associated with a different request. It records a call to method 'method' on grain '{target}' (fingerprint 0123456789AB), and this attempt requested a local child task. A replay schedules the same operation under each identifier: a call or request invokes the same method on the same target, whatever its arguments; new workflow steps use new identifiers.",
            exception.Message);
        Assert.Empty(transport.Invocations);
    });

    [Fact]
    public Task RemoteTaskIdReuseAfterDeletionSendsNewInvocation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime(TimeSpan.Zero);
        var rootId = TaskId.Parse("root");
        var taskId = TaskId.Parse("root/remote-reuse");
        var originalRequest = CreateRemoteChild(17);
        var target = originalRequest.Context!.TargetId;
        storage.AddInvoked(rootId);

        await runtime.ScheduleChildAsync(taskId, originalRequest, TestContext.Current.CancellationToken);
        var originalWireId = runtime.GetWireId(taskId);
        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.SentInvocations), OutboxMessageRetirement.Delivered.Instance);
        runtime.AcceptResponseFromInbox(taskId, DurableTaskResponse.FromResult(42), target);

        // 根任务完成后其保留期立即结束，因此提交其结果的写入会删除执行树。
        storage.SetResponse(rootId, storage.Get(rootId), DurableTaskResponse.Completed);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => !storage.Contains(rootId) && !storage.Contains(taskId));

        // 根任务的后续运行会再次记录子任务，使用相同标识符但不同的传输标识符。
        storage.AddInvoked(rootId);
        var replacementRequest = CreateRemoteChild(18);
        var handle = await runtime.ScheduleChildAsync(taskId, replacementRequest, TestContext.Current.CancellationToken);

        Assert.False((await handle.PollAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken)).IsCompleted);
        Assert.Collection(
            transport.Invocations,
            invocation => Assert.Equal((originalWireId, (IDurableTaskRequest)originalRequest), (invocation.TaskId, invocation.Request)),
            invocation =>
            {
                Assert.Same(replacementRequest, invocation.Request);
                Assert.Equal(runtime.GetWireId(taskId), invocation.TaskId);
                Assert.NotEqual(originalWireId, invocation.TaskId);
                Assert.Equal(target, invocation.Target);
            });
        Assert.Equal(
            IDurableTaskRequest.GetFingerprint(replacementRequest, CreateSerializer()),
            storage.Get(taskId).RemoteRequestFingerprint);
        Assert.Null(storage.Get(taskId).Result);
    });

    [Fact]
    public Task SubscribeOrPollAsync_MissingTaskThrowsNotFoundWithoutCreatingState() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var taskId = TaskId.Parse("root/missing");

        var failure = await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            () => runtime.SubscribeOrPollAsync(
                taskId,
                new SubscribeOrPollOptions { PollTimeout = TimeSpan.Zero },
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(taskId, failure.TaskId);
        Assert.False(storage.Contains(taskId));
        Assert.Equal(0, manager.WriteCount);
        Assert.Empty(transport.Invocations);
        Assert.Empty(transport.Resumes);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public Task TaskHandlePollAsync_PropagatesCallerCancellation(int pollTimeoutMilliseconds) => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var taskId = TaskId.Parse("root/canceled-poll");
        storage.GetOrCreate(taskId);
        var handle = runtime.GetScheduledTaskHandle(taskId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handle.PollAsync(
                new PollingOptions
                {
                    PollTimeout = TimeSpan.FromMilliseconds(pollTimeoutMilliseconds)
                },
                cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    });

    [Fact]
    public Task DetachedPostCommitsTheInvocationWithItsRecordAndReplayDoesNotResend() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var rootId = TaskId.CreateRoot("detached");
        var detachedRequest = CreateRemoteRequest(1);
        var recordId = starterId.Child($"$detach:{detachedRequest.Context!.TargetId}:detached");
        var writes = new ConcurrentQueue<(bool Record, int Invocations)>();
        manager.AfterWriteCompleted = _ => writes.Enqueue((storage.Contains(recordId), transport.DetachedInvocations.Count));
        TaskCompletionSource[] posted = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        var attempts = 0;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async executionToken =>
            {
                var attempt = Interlocked.Increment(ref attempts) - 1;
                _ = new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("detached").Post();
                posted[attempt].TrySetResult();
                await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("wait");
            }));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await posted[0].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => writes.Contains((true, 1)));

        // 调用消息将该记录指定为订阅者，且不声明任何调用方。
        var invocation = Assert.Single(transport.DetachedInvocations);
        Assert.Equal(
            (runtime.GrainId, detachedRequest.Context!.TargetId, rootId, (TaskId?)runtime.GetWireId(recordId)),
            (invocation.Sender, invocation.Target, invocation.TaskId, invocation.SubscriberId));
        Assert.Empty(transport.Invocations);
        Assert.Equal(default, detachedRequest.Context.CallerId);
        Assert.False(detachedRequest.Context.SupportsDurableCompletion);

        // 调用消息及其记录在一次写入中提交；记录观察该任务，而启动方不存储该任务。
        Assert.DoesNotContain((false, 1), writes);
        var record = storage.Get(recordId);
        Assert.Null(record.Result);
        Assert.Null(record.Request);
        Assert.True(record.RemoteTarget.IsDefault);
        Assert.False(storage.Contains(rootId));

        // 替代激活实例重放启动方时，会到达记录，而不再次发送调用消息。
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var (replacement, _, _, replacementTransport) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await posted[1].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Empty(replacementTransport.DetachedInvocations);
        Assert.Empty(replacementTransport.Invocations);

        // 分离任务的结果使该记录完成。
        replacement.AcceptResponseFromInbox(replacement.GetWireId(recordId), rootId, DurableTaskResponse.FromResult(3), detachedRequest.Context.TargetId);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, storage.Get(recordId).Result!.GetResult<int>());
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task DetachedStartsRecordOneRecordPerDetachedTask() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var detachedRequest = CreateRemoteRequest(1);
        var target = detachedRequest.Context!.TargetId;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async executionToken =>
            {
                // 启动操作之前的匿名子任务不会改变该启动操作所到达的记录。
                await DurableTask.Run(static _ => { });
                _ = new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("detached").Post();
                _ = new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("detached").Post();
                _ = new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("other:root").Post();
            }));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(starterId).Result!.Status);
        Assert.Equal(
            [TaskId.CreateRoot("detached"), TaskId.CreateRoot("other:root")],
            transport.DetachedInvocations.Select(static invocation => invocation.TaskId));
        Assert.True(storage.Contains(starterId.Child($"$detach:{target}:detached")));
        Assert.True(storage.Contains(starterId.Child($"$detach:{target}:other\\:root")));
    });

    [Fact]
    public Task UnnamedDetachedTaskDerivesItsRootIdFromItsStartersGrainAndExecution() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, _, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var request = CreateRequest(
            1,
            () => DurableTask.Run(cancellationToken => { _ = new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(1)).Detached().Post(); }));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.DetachedInvocations.Count == 1);

        // 根标识符是单一段 {HostId}/{Id}/{segment}，其中各部分的 '/' 与任务标识符中的斜杠一样进行转义，因此
        // 另一个 Grain 的同标识符执行实例会派生出不同的根标识符。
        Assert.Equal("test/one", runtime.GrainId.ToString());
        Assert.Equal(TaskId.CreateRoot(@"test\/one/starter/$child-1"), Assert.Single(transport.DetachedInvocations).TaskId);
    });

    [Fact]
    public Task HandlesNameTheTasksWhichTheirOperationsStartOnEveryAttempt() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var detachedRequest = CreateRemoteRequest(1);
        var callRequest = CreateRemoteRequest(2);
        var attempts = new ConcurrentQueue<(TaskId Id, TaskId TargetTaskId, DurableTaskAddress? Address)[]>();
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async executionToken =>
            {
                ScheduledTask[] handles =
                [
                    new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("detached").Post(),
                    new TestStateManager.TestRemoteDurableTask(callRequest).WithId("call").Post(),
                    DurableTask.Run(static _ => { }).WithId("local").Post(),
                ];
                attempts.Enqueue([.. handles.Select(static handle => (handle.Id, handle.TargetTaskId, handle.TryGetAddress(out var address) ? address : (DurableTaskAddress?)null))]);
                await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("wait");
            }));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => attempts.Count == 1);

        // 分离启动观察目标的根任务，其他方也可对该根任务寻址。调用则到达其
        // 目标在此 Grain 的作用域下存储的任务，本地子任务在自身标识符下存储；执行实例的
        // 执行树拥有这两类任务，因此两者都没有地址。
        (TaskId Id, TaskId TargetTaskId, DurableTaskAddress? Address)[] expected =
        [
            (
                starterId.Child($"$detach:{detachedRequest.Context!.TargetId}:detached"),
                TaskId.CreateRoot("detached"),
                new DurableTaskAddress(detachedRequest.Context.TargetId, TaskId.CreateRoot("detached"))),
            (starterId.Child("call"), CallerScope.GetTaskId(runtime.GrainId, runtime.GetWireId(starterId.Child("call"))), null),
            (starterId.Child("local"), starterId.Child("local"), null),
        ];
        Assert.Equal(expected, Assert.Single(attempts));

        // 重放到达相同的记录，其句柄指向相同的任务。
        await WaitUntilAsync(() => storage.Contains(starterId.Child("wait")));
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var (replacement, _, _, _) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => attempts.Count == 2);

        Assert.Equal(expected, attempts.Last());
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task CanceledStarterStillStartsTheDetachedTaskAndEndsOnlyItsSubscription() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var rootId = TaskId.CreateRoot("cleanup");
        var detachedRequest = CreateRemoteRequest(1);
        var target = detachedRequest.Context!.TargetId;
        var recordId = starterId.Child($"$detach:{target}:cleanup");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async executionToken =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, executionToken);
                }
                catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
                {
                    // 清理任务在启动方被取消后仍继续存在。
                    _ = new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("cleanup").Post();
                    throw;
                }
            }));
        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var recordWireId = runtime.GetWireId(recordId);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await runtime.SignalCancellationAsync(starterId, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });

        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(starterId).Result!.Status);
        var invocation = Assert.Single(transport.DetachedInvocations);
        Assert.Equal((rootId, (TaskId?)recordWireId), (invocation.TaskId, invocation.SubscriberId));
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(recordId).Result!.Status);
        Assert.Equal(new RecordedSubscription(runtime.GrainId, target, rootId, recordWireId, Unsubscribe: true), Assert.Single(transport.Subscriptions));

        // 删除启动方的执行树会删除记录，且不再向分离任务发送任何消息。
        await WaitUntilIdleAsync(runtime);
        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.Completions).MessageId, OutboxMessageRetirement.Delivered.Instance);
        Assert.False(storage.Contains(starterId));
        Assert.False(storage.Contains(recordId));
        Assert.Empty(transport.Cancellations);
        Assert.Single(transport.Subscriptions);

        // 待处理调用表仍提供清理任务的调用消息，该消息无需其记录即可退役。
        var source = (IOutboxMessageSource)runtime;
        var messageId = Assert.Single(transport.SentInvocations);
        Assert.True(source.TryGetMessage(messageId, out var pending));
        Assert.Equal((messageId, target), (pending.Envelope.MessageId, pending.ReceiverId));
        source.OnRetired(messageId, OutboxMessageRetirement.Delivered.Instance);
        Assert.False(storage.Invocations.ContainsKey(messageId));
        Assert.False(source.TryGetMessage(messageId, out _));
    });

    [Fact]
    public Task CanceledStartersDetachedScheduleReturnsOnceTheTargetAcceptsTheTask() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var detachedRequest = CreateRemoteRequest(1);
        var target = detachedRequest.Context!.TargetId;
        var recordId = starterId.Child($"$detach:{target}:cleanup");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async executionToken =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, executionToken);
                }
                catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
                {
                    await new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("cleanup").ScheduleAsync(CancellationToken.None);
                    scheduled.TrySetResult();
                    throw;
                }
            }));
        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 被取消的启动方的记录立即结束订阅，而启动操作仍等待目标任务。
        await runtime.SignalCancellationAsync(starterId, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.SentInvocations.Count == 1 && storage.Contains(recordId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(recordId).Result!.Status);
        Assert.False(scheduled.Task.IsCompleted);

        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.SentInvocations), OutboxMessageRetirement.Delivered.Instance);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });

        Assert.True(storage.Get(recordId).Accepted);
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(starterId).Result!.Status);
    });

    [Fact]
    public Task SameGrainDetachedTaskReachesItsStarterAndOutlivesTheStartersTree() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var retention = TimeSpan.FromHours(1);
        var (runtime, storage, manager, transport) = CreateRuntime(retention, timeProvider: clock);
        var starterId = TaskId.Parse("starter");
        var rootId = TaskId.CreateRoot("detached");
        var detachedRequest = CreateRequest(2, () => DurableTask.FromResult(5));
        var recordId = starterId.Child($"$detach:{detachedRequest.Context!.TargetId}:detached");
        var writes = new ConcurrentQueue<(bool Record, bool Root)>();
        manager.AfterWriteCompleted = _ => writes.Enqueue((storage.Contains(recordId), storage.Contains(rootId)));
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ =>
                await new TestStateManager.TestRemoteDurableTask(detachedRequest).Detached("detached")));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });
        await WaitUntilAsync(() => transport.Completions.Count == 1);
        await WaitUntilIdleAsync(runtime);

        // 根任务与记录一起提交；根任务没有调用方，其结果直接传递至记录。
        Assert.All(writes, write => Assert.Equal(write.Record, write.Root));
        var root = storage.Get(rootId);
        Assert.Equal(5, root.Result!.GetResult<int>());
        Assert.True(root.CallerId.IsDefault);
        Assert.Empty(root.CompletionDestinations);
        Assert.Equal(5, storage.Get(recordId).Result!.GetResult<int>());
        Assert.Equal(1, detachedRequest.CreateTaskCallCount);
        Assert.Empty(transport.Invocations);
        Assert.Empty(transport.DetachedInvocations);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(starterId).Result!.Status);

        // 投递启动方的完成消息会删除其执行树，执行树包含记录，但不包含分离的根任务。
        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.Completions).MessageId, OutboxMessageRetirement.Delivered.Instance);
        Assert.False(storage.Contains(starterId));
        Assert.False(storage.Contains(recordId));
        Assert.Equal(5, (await runtime.SubscribeOrPollAsync(rootId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken)).GetResult<int>());

        // 与客户端调度的根任务一样，分离的根任务在完成后保留至保留期结束。
        clock.Advance(retention);
        Assert.True(InvokeDeleteExpiredTasks(runtime));
        Assert.False(storage.Contains(rootId));
    });

    [Fact]
    public Task PostFromAnOrdinaryMethodCommitsWithTheGrainsNextWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var rootId = TaskId.CreateRoot("detached");
        var request = CreateRemoteRequest(1);

        runtime.PostRoot(rootId, request);

        var invocation = Assert.Single(transport.DetachedInvocations);
        Assert.Equal((request.Context!.TargetId, rootId, (TaskId?)null), (invocation.Target, invocation.TaskId, invocation.SubscriberId));
        Assert.Equal(default, request.Context.CallerId);
        Assert.Equal(0, manager.WriteCount);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Empty(storage.Tasks);
        Assert.Empty(transport.Invocations);
    });

    [Fact]
    public Task PostingTheSameRootAgainIsIdempotentAndConflictingReuseFails() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var rootId = TaskId.CreateRoot("detached");
        var first = CreateRequest(1);
        var again = CreateRequest(1);

        runtime.PostRoot(rootId, first);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(rootId).Result is { IsCompleted: true });
        runtime.PostRoot(rootId, again);

        Assert.Equal(1, first.CreateTaskCallCount);
        Assert.Equal(0, again.CreateTaskCallCount);

        // 提交参数不同但方法相同的任务请求会找到已存储的任务；提交不同方法则产生冲突。
        var differentArguments = CreateRequest(2);
        runtime.PostRoot(rootId, differentArguments);
        Assert.Equal(0, differentArguments.CreateTaskCallCount);
        var exception = Assert.Throws<DurableTaskConflictException>(() => runtime.PostRoot(rootId, CreateOtherMethodRequest(1)));
        Assert.Contains("different request", exception.Message, StringComparison.Ordinal);
        Assert.Equal(rootId, exception.TaskId);
    });

    [Fact]
    public Task DetachedRecordReusedForAnotherStartFailsBeforeStaging() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        storage.AddInvoked(TaskId.Parse("starter"));
        var recordId = TaskId.Parse("starter/$detach:target:first");
        runtime.PostDetached(recordId, TaskId.CreateRoot("first"), CreateRemoteRequest(1), shielded: false);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        // 重放以相同的启动操作到达记录时，不暂存任何新内容。
        runtime.PostDetached(recordId, TaskId.CreateRoot("first"), CreateRemoteRequest(1), shielded: false);
        Assert.Single(transport.DetachedInvocations);

        var exception = Assert.Throws<DurableTaskDivergenceException>(
            () => runtime.PostDetached(recordId, TaskId.CreateRoot("second"), CreateRemoteRequest(1), shielded: false));

        Assert.Contains("different operation", exception.Message, StringComparison.Ordinal);
        Assert.Single(transport.DetachedInvocations);
        Assert.Null(storage.Get(recordId).Result);
    });

    [Fact]
    public Task DetachedInboxInvocationSubscribesItsStarter() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var sender = GrainId.Create("caller", "one");
        var rootId = TaskId.CreateRoot("detached");
        var recordId = TaskId.Parse("starter/$detach:record");
        var request = CreateSerializableRequest(runtime.GrainId);

        Accept(
            CreateHandler(runtime),
            MessagingTestHelpers.CreateInvocationMessage(
                sender,
                runtime.GrainId,
                rootId,
                new DurableTaskInvocationMessage { TaskId = rootId, Request = request, Sender = sender, Detached = true, SubscriberId = recordId },
                runtime.UtcNow));

        // 任务及其订阅者一起暂存。
        var state = storage.Get(rootId);
        Assert.True(state.CallerId.IsDefault);
        Assert.Equal([new DurableTaskSubscriber(sender, recordId)], state.CompletionDestinations);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(rootId).Result is { IsCompleted: true });

        var completion = Assert.Single(transport.Completions);
        Assert.Equal(recordId, completion.Materialize(runtime).SubscriberId);
    });

    [Fact]
    public Task RootTaskCantUseTheReservedCallerScopeRoot() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await ((IDurableTaskServer)runtime).ScheduleAsync(CallerScope.Root, CreateRequest(1), TestContext.Current.CancellationToken));

        Assert.False(storage.Contains(CallerScope.Root));
    });

    [Fact]
    public Task ConflictingInvocationIsDeadLetteredAndChangesNothing() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var sender = GrainId.Create("caller", "one");
        var taskId = TaskId.Parse("starter/call");

        // 此 Grain 在发送方作用域下存储发送方的子执行实例，其中已有另一个请求正在运行。
        var storedId = CallerScope.GetTaskId(sender, taskId);
        runtime.ScheduleFromInbox(storedId, CreateRequest(1, () => DurableTask.Run(static async cancellationToken => await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken))));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var fingerprint = storage.Get(storedId).RequestFingerprint;
        var destinations = storage.Get(storedId).CompletionDestinations.ToArray();
        var request = CreateSerializableRequest(runtime.GrainId);

        // 附属调用的标识符已标识另一个请求，因此收件箱将其转入死信，并附上
        // 冲突的错误码及消息，发送方发件箱将其报告为拒绝原因。
        Assert.True(CreateHandler(runtime).TryAccept(
            MessagingTestHelpers.CreateInvocationMessage(
                sender,
                runtime.GrainId,
                taskId,
                new DurableTaskInvocationMessage { TaskId = taskId, Request = request, Sender = sender },
                runtime.UtcNow),
            out var result));

        Assert.Equal(DurableTaskRejectionCodes.Conflict, Assert.IsType<DeliveryResult.DeadLettered>(result.Value).Code);
        Assert.Contains($"Durable task '{storedId}' is already associated with a different request.", Assert.IsType<DeliveryResult.DeadLettered>(result.Value).Reason);
        Assert.Equal(fingerprint, storage.Get(storedId).RequestFingerprint);
        Assert.Equal(destinations, storage.Get(storedId).CompletionDestinations);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task InvocationFromASenderWhoseClockRunsAheadFindsTheCancellationWhichPrecededIt() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var window = TimeSpan.FromHours(2);
        var skew = TimeSpan.FromMinutes(30);
        var (runtime, storage, manager, _) = CreateRuntime(timeProvider: clock, deduplicationWindow: window);
        var handler = CreateHandler(runtime);
        var sender = GrainId.Create("caller", "one");
        var taskId = TaskId.Parse("starter/call");
        var storedId = CallerScope.Resolve(sender, taskId);

        // 发送方时钟快于 Grain 的时钟，因此随后发送的取消消息先到达时，
        // 调用消息上标记的发送时间晚于 Grain 当前时间。
        var sentAt = clock.GetUtcNow() + skew;
        Accept(handler, CancellationMessage(sender, runtime.GrainId, taskId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        // Grain 的时钟经过一个窗口时长后，调用仍处于其接纳窗口内，
        // 取消记录也仍有效。
        clock.Advance(window + skew / 2);
        Assert.False(InvokeDeleteExpiredTasks(runtime));
        Assert.True(handler.TryAccept(
            MessagingTestHelpers.CreateInvocationMessage(
                sender,
                runtime.GrainId,
                taskId,
                new DurableTaskInvocationMessage { TaskId = taskId, Request = CreateSerializableRequest(runtime.GrainId), Sender = sender },
                sentAt),
            out var result));
        Assert.IsType<DeliveryResult.Accepted>(result.Value);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(storedId).Result is { IsCompleted: true });

        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(storedId).Result!.Status);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task InvocationAdmittedWithinItsWindowFindsTheCancellationWhichPrecededIt() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var window = TimeSpan.FromHours(2);
        var (runtime, storage, manager, _) = CreateRuntime(timeProvider: clock, deduplicationWindow: window);
        var handler = CreateHandler(runtime);
        var sender = GrainId.Create("caller", "one");
        var taskId = TaskId.Parse("starter/call");
        var storedId = CallerScope.Resolve(sender, taskId);
        var lateId = TaskId.Parse("starter/late");
        var request = CreateSerializableRequest(runtime.GrainId);
        var sentAt = clock.GetUtcNow();

        // 调用方先暂存各调用，再暂存对第一个调用的取消。取消先到达，且
        // 背压使调用消息滞留至接纳窗口结束。
        Accept(handler, CancellationMessage(sender, runtime.GrainId, taskId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        clock.Advance(window - TimeSpan.FromTicks(1));
        Assert.False(InvokeDeleteExpiredTasks(runtime));

        Accept(handler, Invocation(taskId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(storedId).Result is { IsCompleted: true });
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(storedId).Result!.Status);

        // 窗口结束后，Grain 将未存储的调用转入死信，而接纳已存储调用的
        // 重新投递。
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(handler.TryAccept(Invocation(lateId), out var late));
        Assert.Equal(DurableTaskRejectionCodes.Expired, Assert.IsType<DeliveryResult.DeadLettered>(late.Value).Code);
        Assert.False(storage.Contains(CallerScope.Resolve(sender, lateId)));
        Assert.True(handler.TryAccept(Invocation(taskId), out var redelivered));
        Assert.IsType<DeliveryResult.Accepted>(redelivered.Value);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        InboxMessage Invocation(TaskId id) => MessagingTestHelpers.CreateInvocationMessage(
            sender,
            runtime.GrainId,
            id,
            new DurableTaskInvocationMessage { TaskId = id, Request = request, Sender = sender },
            sentAt);
    });

    [Fact]
    public Task UndecodableInvocationThrowsSoItsDeliveryIsRetriedAndChangesNothing() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var sender = GrainId.Create("caller", "one");
        var taskId = TaskId.Parse("starter/call");
        var invocation = MessagingTestHelpers.CreateInvocationMessage(
            sender,
            runtime.GrainId,
            taskId,
            new DurableTaskInvocationMessage { TaskId = taskId, Request = CreateSerializableRequest(runtime.GrainId), Sender = sender },
            runtime.UtcNow);
        var envelope = invocation.Envelope;
        var truncated = new OutboxMessage
        {
            Envelope = envelope with { Payload = envelope.Payload[..(envelope.Payload.Length / 2)] },
            ReceiverId = runtime.GrainId,
        }.ToInboxMessage();

        // 接收处理器抛出异常而非拒绝调用，因此发送方发件箱重试投递。
        Assert.ThrowsAny<Exception>(() => CreateHandler(runtime).TryAccept(truncated, out _));

        Assert.False(storage.Contains(taskId));
        Assert.Equal(0, manager.WriteCount);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task ScheduleWithinAnExecutionReturnsOnceTheTargetAcceptsTheCall() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var childId = starterId.Child("call");
        var remote = CreateRemoteRequest(1);
        var target = remote.Context!.TargetId;
        var scheduled = new TaskCompletionSource<TaskId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async cancellationToken =>
            {
                var handle = await new TestStateManager.TestRemoteDurableTask(remote).WithId("call").ScheduleAsync(cancellationToken);
                scheduled.TrySetResult(handle.Id);
                await handle;
            }));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Contains(childId) && transport.Invocations.Count == 1);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        // 调用消息按引用暂存并提交，调度操作等待目标接纳该调用。
        var messageId = Assert.Single(transport.SentInvocations);
        Assert.Equal(transport.GetInvocationMessageId(runtime.GrainId, target, runtime.GetWireId(childId), subscriberId: null), messageId);
        Assert.False(scheduled.Task.IsCompleted);
        Assert.Null(storage.Get(childId).Accepted);

        // 目标持久化接纳该调用后，发件箱将调用消息标记为已投递并使其退役。
        ((IOutboxMessageSource)runtime).OnRetired(messageId, OutboxMessageRetirement.Delivered.Instance);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(childId, await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(storage.Get(childId).Accepted);

        runtime.AcceptResponseFromInbox(childId, DurableTaskResponse.Completed, target);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(starterId).Result!.Status);
    });

    [Theory]
    [InlineData(DurableTaskRejectionCodes.Conflict, "DurableTaskConflictException place-order: rejected")]
    [InlineData("custom/unknown", "DurableTaskRejectedException place-order custom/unknown: Grain '{0}' rejected the invocation of durable task 'place-order' (custom/unknown): rejected")]
    [InlineData(null, "DurableTaskRejectedException place-order : Grain '{0}' rejected the invocation of durable task 'place-order': rejected")]
    public Task RejectedScheduleWithinAnExecutionFailsWithItsCodesExceptionOnEveryReplay(string? code, string expected) => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var remote = CreateRemoteRequest(1);
        var target = remote.Context!.TargetId;
        var recordId = starterId.Child($"$detach:{target}:place-order");
        var outcomes = new ConcurrentQueue<string>();
        TaskCompletionSource[] attempts = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        var attempt = 0;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async cancellationToken =>
            {
                var current = Interlocked.Increment(ref attempt) - 1;
                try
                {
                    await new TestStateManager.TestRemoteDurableTask(remote).Detached("place-order").ScheduleAsync(cancellationToken);
                    outcomes.Enqueue("accepted");
                }
                catch (DurableTaskConflictException conflict)
                {
                    outcomes.Enqueue($"{nameof(DurableTaskConflictException)} {conflict.TaskId}: {conflict.Message}");
                }
                catch (DurableTaskRejectedException rejected)
                {
                    Assert.Equal(target, rejected.Target);
                    outcomes.Enqueue($"{nameof(DurableTaskRejectedException)} {rejected.TaskId} {rejected.Code}: {rejected.Message}");
                }

                attempts[current].TrySetResult();
                await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("wait");
            }));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Contains(recordId) && transport.SentInvocations.Count == 1);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        // 目标已将调用转为死信，发件箱将其作为被拒绝的消息退役，并附带目标的错误代码和原因。
        ((IOutboxMessageSource)runtime).OnRetired(
            Assert.Single(transport.SentInvocations),
            new OutboxMessageRetirement.Rejected("rejected", code));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await attempts[0].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(storage.Get(recordId).Accepted);
        Assert.Equal(DurableTaskStatus.Failed, storage.Get(recordId).Result!.Status);

        // 重放观察已记录的拒绝结果，而不重新发送调用消息。
        await WaitUntilAsync(() => storage.Contains(starterId.Child("wait")));
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var (replacement, _, _, replacementTransport) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await attempts[1].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var outcome = string.Format(System.Globalization.CultureInfo.InvariantCulture, expected, target);
        Assert.Equal([outcome, outcome], outcomes);
        Assert.Empty(replacementTransport.SentInvocations);
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public Task UndeliveredCallFailsTheAwaitingExecution() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var childId = starterId.Child("call");
        var remote = CreateRemoteRequest(1);
        var target = remote.Context!.TargetId;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ =>
            {
                try
                {
                    await new TestStateManager.TestRemoteDurableTask(remote).WithId("call");
                    return "completed";
                }
                catch (DurableTaskDeliveryException undelivered)
                {
                    return $"{undelivered.TaskId} {undelivered.Target}: {undelivered.Message}";
                }
            }));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Contains(childId) && transport.Invocations.Count == 1);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        // 发件箱在重试超时后将调用消息转入死信。
        ((IOutboxMessageSource)runtime).OnRetired(
            transport.GetInvocationMessageId(runtime.GrainId, target, runtime.GetWireId(childId), subscriberId: null),
            new OutboxMessageRetirement.DeadLettered("The receiver is unavailable."));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });

        Assert.Equal(
            $"{childId} {target}: The delivery of durable task '{childId}' to grain '{target}' wasn't confirmed, so whether the grain ran it is unknown: The receiver is unavailable.",
            storage.Get(starterId).Result!.GetResult<string>());
        Assert.False(storage.Get(childId).Accepted);
    });

    [Fact]
    public Task ReplayedCallCoalescesWithItsPendingInvocation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var childId = starterId.Child("call");
        var remote = CreateRemoteRequest(1);
        var target = remote.Context!.TargetId;
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ => await new TestStateManager.TestRemoteDurableTask(remote).WithId("call")));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.SentInvocations.Count == 1);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var messageId = Assert.Single(transport.SentInvocations);

        // 替代激活实例重放调用，该调用与其表中仍保存的调用消息合并。
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var (replacement, _, _, replacementTransport) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => replacementTransport.Invocations.Count == 1);

        Assert.Empty(replacementTransport.SentInvocations);
        var source = (IOutboxMessageSource)replacement;
        Assert.True(source.TryGetMessage(messageId, out var pending));
        Assert.Equal(target, pending.ReceiverId);

        // 已投递的调用消息记录目标的接纳结果，调用结果使启动方完成。
        source.OnRetired(messageId, OutboxMessageRetirement.Delivered.Instance);
        Assert.True(storage.Get(childId).Accepted);
        Assert.False(storage.Invocations.ContainsKey(messageId));
        replacement.AcceptResponseFromInbox(childId, DurableTaskResponse.Completed, target);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(starterId).Result!.Status);
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    [Fact]
    public void PostedRootWhoseIdentityHasAPendingInvocationOfAnotherRequestFails()
    {
        var (runtime, storage, _, transport) = CreateRuntime();
        var rootId = TaskId.CreateRoot("posted");

        // 已提交的根任务请求保有其标识符，因此每次提交的调用消息标识相同。提交
        // 同一方法的请求时，无论参数是什么，都会与待处理的调用消息合并。
        runtime.PostRoot(rootId, CreateRemoteRequest(17));
        var messageId = Assert.Single(transport.SentInvocations);
        runtime.PostRoot(rootId, CreateRemoteRequest(18));
        Assert.Single(transport.SentInvocations);

        var exception = Assert.Throws<InvalidOperationException>(
            () => runtime.PostRoot(rootId, CreateOtherMethodRequest(17, GrainId.Create("target", "one"))));

        Assert.Contains(messageId.ToString(), exception.Message);
        Assert.Single(transport.SentInvocations);
        Assert.True(storage.Invocations.ContainsKey(messageId));
    }

    [Fact]
    public Task DetachedScheduleWithinAnExecutionReturnsOnceTheTargetAcceptsOrTheResultArrives() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var rootId = TaskId.CreateRoot("detached");
        var remote = CreateRemoteRequest(1);
        var target = remote.Context!.TargetId;
        var recordId = starterId.Child($"$detach:{target}:detached");
        var scheduled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async cancellationToken =>
            {
                var handle = await new TestStateManager.TestRemoteDurableTask(remote).Detached("detached").ScheduleAsync(cancellationToken);
                scheduled.TrySetResult();
                await handle;
            }));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.DetachedInvocations.Count == 1);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var recordWireId = runtime.GetWireId(recordId);
        var invocation = Assert.Single(transport.DetachedInvocations);
        Assert.Equal((rootId, (TaskId?)recordWireId), (invocation.TaskId, invocation.SubscriberId));
        Assert.False(scheduled.Task.IsCompleted);

        // 任务结果可能在其调用消息退役之前到达；这表明目标已持有该任务。
        runtime.AcceptResponseFromInbox(recordWireId, rootId, DurableTaskResponse.Completed, target);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(starterId).Result!.Status);

        // 随后的消息退役记录目标的接纳结果，且拒绝结果不能替换任务结果。
        ((IOutboxMessageSource)runtime).OnRetired(
            transport.GetInvocationMessageId(runtime.GrainId, target, rootId, recordWireId),
            OutboxMessageRetirement.Delivered.Instance);
        Assert.True(storage.Get(recordId).Accepted);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(recordId).Result!.Status);
    });

    [Fact]
    public Task DetachedInboxInvocationIsRetainedLikeAClientScheduledRoot() => ActivationScheduler.RunAsync(async () =>
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var retention = TimeSpan.FromHours(1);
        var (runtime, storage, manager, transport) = CreateRuntime(retention, timeProvider: clock);
        var sender = GrainId.Create("caller", "one");
        var rootId = TaskId.CreateRoot("detached");
        var request = CreateSerializableRequest(runtime.GrainId);

        Accept(
            CreateHandler(runtime),
            MessagingTestHelpers.CreateInvocationMessage(
                sender,
                runtime.GrainId,
                rootId,
                new DurableTaskInvocationMessage { TaskId = rootId, Request = request, Sender = sender, Detached = true },
                runtime.UtcNow));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(rootId).Result is { IsCompleted: true });
        await WaitUntilIdleAsync(runtime);

        var state = storage.Get(rootId);
        Assert.True(state.CallerId.IsDefault);
        Assert.Empty(state.CompletionDestinations);
        Assert.Empty(transport.Completions);
        Assert.Equal(
            DurableTaskStatus.CompletedSuccessfully,
            (await runtime.SubscribeOrPollAsync(rootId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken)).Status);

        clock.Advance(retention);
        Assert.True(InvokeDeleteExpiredTasks(runtime));
        Assert.False(storage.Contains(rootId));
    });

    [Fact]
    public Task TaskManagerObservesAndCancelsTheGrainsTasksWithoutAGrainCall() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, _) = CreateRuntime();
        var tasks = (IDurableTaskManager)runtime;
        var taskId = TaskId.CreateRoot("workflow");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.ScheduleFromInbox(taskId, CreateRequest(1, () => DurableTask.Run(async executionToken =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, executionToken);
            return 1;
        })));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var task = tasks.GetTask<int>(taskId);
        Assert.Equal(taskId, task.Id);
        Assert.Equal(
            DurableTaskStatus.Pending,
            await task.GetStatusAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken));

        await tasks.CancelAsync(taskId, TestContext.Current.CancellationToken);

        var response = await task.GetResponseAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, response.Status);
        Assert.NotNull(storage.Get(taskId).CancellationRequestedAt);
        Assert.Equal(DurableTaskStatus.Canceled, (await tasks.GetTask(taskId).GetResponseAsync(TestContext.Current.CancellationToken)).Status);
        await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            () => tasks.GetTask<int>(TaskId.CreateRoot("missing")).GetResponseAsync(
                new PollingOptions { PollTimeout = TimeSpan.Zero },
                TestContext.Current.CancellationToken));
    });

    private static RuntimeTestDurableTaskRequest CreateRequest(
        int argument,
        Func<DurableTask>? createTask = null) =>
        new(
            createTask,
            interfaceName: "ITestGrain",
            methodName: "Execute",
            arguments: [argument])
        {
            Context = new DurableTaskRequestContext
            {
                CallerId = GrainId.Create("caller", "one"),
                TargetId = GrainId.Create("test", "one"),
                SupportsDurableCompletion = true,
            },
        };

    private static TestStateManager.TestRemoteDurableTask CreateRemoteChild(int argument) => new(CreateRemoteRequest(argument));

    private static RuntimeTestDurableTaskRequest CreateRemoteRequest(int argument)
    {
        var request = CreateRequest(argument);
        request.Context!.TargetId = GrainId.Create("target", "one");
        return request;
    }

    private static TestDurableTaskRequest CreateSerializableRequest(GrainId target) =>
        new()
        {
            Context = new DurableTaskRequestContext
            {
                CallerId = GrainId.Create("caller", "one"),
                TargetId = target,
                SupportsDurableCompletion = true,
            },
        };

    private static DurableTaskMessageHandler CreateHandler(DurableTaskGrainRuntime runtime) =>
        new(runtime, MessagingTestHelpers.MessageTypes);

    private static void Accept(IInboxMessageSink sink, InboxMessage message)
    {
        Assert.True(sink.TryAccept(message, out var result));
        Assert.IsType<DeliveryResult.Accepted>(result.Value);
    }

    private static async Task HandleAsync(IInboxHandler handler, IInboxHandlerContext context)
    {
        Assert.True(handler.CanHandle(context.Message));
        var handling = handler.HandleAsync(context, TestContext.Current.CancellationToken);

        // 处理程序同步暂存各自的更改；收件箱负责批量写入。
        Assert.True(handling.IsCompletedSuccessfully);
        await handling;
        context.Received(1).Complete();
        context.DidNotReceive().Fail(Arg.Any<string>());
    }

    private static InboxMessage InvocationMessage(
        GrainId sender,
        GrainId receiver,
        TaskId taskId,
        IDurableTaskRequest request,
        GrainId? messageIdSender = null) =>
        MessagingTestHelpers.CreateInvocationMessage(
            messageIdSender ?? sender,
            receiver,
            taskId,
            new DurableTaskInvocationMessage { TaskId = taskId, Request = request, Sender = sender },
            DateTimeOffset.UtcNow);

    /// <summary>
    /// 创建 <paramref name="sender"/> 为 <paramref name="receiver"/> 的远程子任务发送的完成消息，
    /// 该子任务的传输标识符为 <paramref name="wireId"/>，发送方在接收方的调用方作用域下存储该任务。
    /// </summary>
    private static InboxMessage CompletionMessage(
        GrainId sender,
        GrainId receiver,
        TaskId wireId,
        DurableTaskResponse response)
    {
        var storedId = CallerScope.GetTaskId(receiver, wireId);
        return MessagingTestHelpers.CreateMessage(
            MessagingTestHelpers.MessageTypes.Completion,
            sender,
            receiver,
            storedId,
            new DurableTaskCompletionMessage { TaskId = storedId, SubscriberId = wireId, Response = response, Sender = sender });
    }

    private static InboxMessage CancellationMessage(GrainId sender, GrainId receiver, TaskId taskId, string? reason = null) =>
        MessagingTestHelpers.CreateMessage(
            MessagingTestHelpers.MessageTypes.Cancellation,
            sender,
            receiver,
            taskId,
            new DurableTaskCancellationMessage { TaskId = taskId, Sender = sender, CancellationReason = reason });
    private static ValueTask<TaskId> InvokeSelectCompletionCoreAsync(
        GrainDurableTaskContext context,
        TaskId decisionId,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken) =>
        (ValueTask<TaskId>)typeof(GrainDurableTaskContext)
            .GetMethod("SelectCompletionCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, [decisionId, candidates, cancellationToken])!;

    private static TaskId InvokeCreateChildTaskId(GrainDurableTaskContext context, string? name) =>
        (TaskId)typeof(GrainDurableTaskContext)
            .GetMethod("CreateChildTaskId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, [name])!;

    private static IInboxHandlerContext ResumeContext(
        DurableTaskGrainRuntime runtime,
        TaskId taskId,
        long generation,
        DateTimeOffset dueTime,
        Guid? incarnation = null) =>
        MessagingTestHelpers.CreateHandlerContext(
            MessagingTestHelpers.CreateResumeMessage(
                runtime.GrainId,
                taskId,
                incarnation ?? runtime.GetIncarnation(taskId),
                generation,
                dueTime));

    private static (
        DurableTaskGrainRuntime Runtime,
        TestStorage Storage,
        TestStateManager Manager,
        RecordingDurableTaskMessageTransport Transport) CreateRuntime(
            TimeSpan? resultRetentionPeriod = null,
            bool initialize = true,
            TimeProvider? timeProvider = null,
            TimeSpan? deduplicationWindow = null,
            bool reentrant = false)
    {
        var manager = new TestStateManager();
        var storage = new TestStorage(manager, timeProvider);
        return CreateRuntime(
            storage,
            manager,
            resultRetentionPeriod,
            initialize,
            timeProvider,
            deduplicationWindow,
            reentrant);
    }

    /// <summary>
    /// 创建绑定至 <paramref name="manager"/> 的运行时。复用持久化存储和管理器可模拟
    /// 替代激活实例：新运行时替代旧运行时，成为管理器的日志挂钩。
    /// </summary>
    private static (
        DurableTaskGrainRuntime Runtime,
        TestStorage Storage,
        TestStateManager Manager,
        RecordingDurableTaskMessageTransport Transport) CreateRuntime(
            TestStorage storage,
            TestStateManager manager,
            TimeSpan? resultRetentionPeriod = null,
            bool initialize = true,
            TimeProvider? timeProvider = null,
            TimeSpan? deduplicationWindow = null,
            bool reentrant = false)
    {
        var context = CreateTestActivation(reentrant);
        var accessor = Substitute.For<IGrainContextAccessor>();
        accessor.GrainContext.Returns(context);
        var shared = new DurableTaskGrainRuntimeShared(
            accessor,
            timeProvider ?? TimeProvider.System,
            NullLogger<DurableTaskGrainRuntime>.Instance,
            Options.Create(new DurableTaskOptions
            {
                ResultRetentionPeriod = resultRetentionPeriod ?? TimeSpan.FromHours(1),
            }),
            Options.Create(new DurableMessagingOptions
            {
                DeduplicationWindow = deduplicationWindow ?? TimeSpan.FromDays(1),
            }),
            CreateSerializer());
        var transport = new RecordingDurableTaskMessageTransport();
        var runtime = new DurableTaskGrainRuntime(
            storage,
            storage.LogicalTimes,
            storage.Invocations,
            shared,
            [transport]);
        if (initialize)
        {
            runtime.InitializeForActivation(context);
        }

        manager.RegisterStateMachine(DurableTaskGrainRuntime.StateMachineName, runtime);
        return (runtime, storage, manager, transport);
    }
    /// <summary>创建模拟激活实例，其本地请求的执行方式与不可重入的激活实例相同。</summary>
    private static IGrainContext CreateTestActivation(bool reentrant = false)
    {
        var context = Substitute.For<IGrainContext>();
        context.GrainId.Returns(GrainId.Create("test", "one"));
        TestLocalRequestQueue.Attach(context, reentrant);
        return context;
    }

    private static Serializer CreateSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(DurableTaskRuntimeInvariantTests).Assembly));
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    private static Task WaitUntilAsync(Func<bool> predicate, [CallerArgumentExpression(nameof(predicate))] string? description = null) =>
        StateChanges.WaitUntilAsync(predicate, description: description);

    private sealed class ShutdownProbeRuntime : IDurableTaskGrainRuntime
    {
        private readonly TaskCompletionSource<CancellationToken> _schedulingStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<DurableTaskResponse> _delayResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>使用 <paramref name="response"/> 完成待完成的延迟操作，模拟结果提交后的行为。</summary>
        public void CompleteDelay(DurableTaskResponse response) => _delayResponse.SetResult(response);

        public GrainId GrainId { get; } = GrainId.Create("test", "probe");

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public Task<CancellationToken> SchedulingStarted => _schedulingStarted.Task;

        public ValueTask<DurableTaskResponse> ScheduleDelayAsync(
            TaskId taskId,
            DateTimeOffset dueTime,
            CancellationToken cancellationToken)
        {
            _schedulingStarted.TrySetResult(cancellationToken);
            return WaitForCancellationAsync(_delayResponse.Task, cancellationToken);
        }

        public ValueTask<IScheduledTaskHandle> ScheduleChildAsync(
            TaskId taskId,
            DurableTask taskDefinition,
            bool shielded,
            bool waitForAcceptance,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask CancelRemoteAsync(
            TaskId taskId,
            GrainId target,
            string? reason,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IScheduledTaskHandle PostChild(TaskId taskId, DurableTask taskDefinition, bool shielded) =>
            throw new NotSupportedException();

        public ValueTask<IScheduledTaskHandle> StartDetachedAsync(
            TaskId recordId,
            TaskId rootId,
            IDurableTaskRequest request,
            bool shielded,
            bool waitForAcceptance,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IScheduledTaskHandle PostDetached(TaskId recordId, TaskId rootId, IDurableTaskRequest request, bool shielded) =>
            throw new NotSupportedException();

        public void PostRoot(TaskId rootId, IDurableTaskRequest request) => throw new NotSupportedException();

        public ValueTask<TaskId> SelectCompletionAsync(
            GrainDurableTaskContext attempt,
            TaskId decisionId,
            IReadOnlyList<TaskId> candidates,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IScheduledTaskHandle GetScheduledTaskHandle(TaskId taskId) => throw new NotSupportedException();

        public TaskId GetTargetTaskId(TaskId taskId, out GrainId addressedHost)
        {
            addressedHost = default;
            return taskId;
        }

        public DateTimeOffset? GetCompletedAt(TaskId taskId) => null;

        public Guid GetIncarnation(TaskId executionId) => Guid.Empty;

        private static async ValueTask<DurableTaskResponse> WaitForCancellationAsync(
            Task<DurableTaskResponse> response,
            CancellationToken cancellationToken)
        {
            try
            {
                return await response.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException exception)
            {
                return DurableTaskResponse.FromCanceled(exception);
            }
        }
    }

    private sealed class SchedulerProbeRuntime(TaskScheduler scheduler) : IDurableTaskGrainRuntime
    {
        private int _schedulerInvocationCount;

        public GrainId GrainId { get; } = GrainId.Create("test", "probe");

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public int SchedulerInvocationCount => Volatile.Read(ref _schedulerInvocationCount);

        public ValueTask<IScheduledTaskHandle> ScheduleChildAsync(
            TaskId taskId,
            DurableTask taskDefinition,
            bool shielded,
            bool waitForAcceptance,
            CancellationToken cancellationToken)
        {
            AssertScheduler();
            return new(new SchedulerProbeHandle(taskId));
        }

        public ValueTask<TaskId> SelectCompletionAsync(
            GrainDurableTaskContext attempt,
            TaskId decisionId,
            IReadOnlyList<TaskId> candidates,
            CancellationToken cancellationToken)
        {
            AssertScheduler();
            return new(candidates[0]);
        }

        public ValueTask<DurableTaskResponse> ScheduleDelayAsync(
            TaskId taskId,
            DateTimeOffset dueTime,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask CancelRemoteAsync(
            TaskId taskId,
            GrainId target,
            string? reason,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IScheduledTaskHandle PostChild(TaskId taskId, DurableTask taskDefinition, bool shielded) =>
            throw new NotSupportedException();

        public ValueTask<IScheduledTaskHandle> StartDetachedAsync(
            TaskId recordId,
            TaskId rootId,
            IDurableTaskRequest request,
            bool shielded,
            bool waitForAcceptance,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IScheduledTaskHandle PostDetached(TaskId recordId, TaskId rootId, IDurableTaskRequest request, bool shielded) =>
            throw new NotSupportedException();

        public void PostRoot(TaskId rootId, IDurableTaskRequest request) => throw new NotSupportedException();

        public IScheduledTaskHandle GetScheduledTaskHandle(TaskId taskId) =>
            new SchedulerProbeHandle(taskId);

        public TaskId GetTargetTaskId(TaskId taskId, out GrainId addressedHost)
        {
            addressedHost = default;
            return taskId;
        }

        public DateTimeOffset? GetCompletedAt(TaskId taskId) => null;

        public Guid GetIncarnation(TaskId executionId) => Guid.Empty;

        private void AssertScheduler()
        {
            Assert.Same(scheduler, TaskScheduler.Current);
            Interlocked.Increment(ref _schedulerInvocationCount);
        }
    }

    private sealed class SchedulerProbeHandle(TaskId taskId) : IScheduledTaskHandle
    {
        public TaskId TaskId { get; } = taskId;

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<DurableTaskResponse> PollAsync(
            PollingOptions options,
            CancellationToken cancellationToken) => new(DurableTaskResponse.Completed);

        public ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken) =>
            new(DurableTaskResponse.Completed);
    }

    private sealed class ThrowingAbortHandle(
        TaskId taskId,
        TaskCompletionSource waitStarted) : IScheduledTaskHandle
    {
        public TaskId TaskId { get; } = taskId;

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) => default;

        public ValueTask<DurableTaskResponse> PollAsync(
            PollingOptions options,
            CancellationToken cancellationToken) => new(DurableTaskResponse.Pending);

        public async ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(
                static () => throw new InvalidOperationException("Expected abort callback failure."));
            waitStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return DurableTaskResponse.Pending;
        }
    }

    private sealed class TestStorage(TestStateManager manager, TimeProvider? timeProvider = null) : IDurableTaskGrainStorage
    {
        private readonly Dictionary<TaskId, TestTaskState> _states = [];
        private readonly DurableTaskHierarchy _hierarchy = new();
        private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
        private long _lastCompletionSequence;

        public TestDurableDictionary<TaskId, DateTimeOffset> LogicalTimes { get; } = [];

        public TestDurableDictionary<MessageId, PendingInvocation> Invocations { get; } = [];

        /// <summary>获取 <see cref="Tasks"/> 被枚举的次数。</summary>
        public int TasksEnumerationCount { get; private set; }

        public IEnumerable<(TaskId Id, IDurableTaskState State)> Tasks
        {
            get
            {
                TasksEnumerationCount++;
                return _states.Select(entry => (entry.Key, (IDurableTaskState)entry.Value));
            }
        }

        public TestTaskState Get(TaskId id) => _states[id];
        public bool Contains(TaskId id) => _states.ContainsKey(id);
        public TestTaskState GetOrCreate(TaskId id, DurableTaskRecordKind kind = DurableTaskRecordKind.LocalStep) =>
            (TestTaskState)GetOrCreateTask(id, kind, null);

        /// <summary>
        /// 存储一个待完成的被调用任务，其执行树包含测试在该任务下记录的节点，正如持久化
        /// 执行实例的节点属于其所执行任务的执行树一样。
        /// </summary>
        public TestTaskState AddInvoked(TaskId id)
        {
            var state = GetOrCreate(id, DurableTaskRecordKind.Invocation);
            state.RequestFingerprint = "0123456789ABCDEF";
            state.Incarnation = Guid.NewGuid();
            return state;
        }

        public IEnumerable<(TaskId Id, IDurableTaskState State)> GetChildren(TaskId task) =>
            _hierarchy
                .GetNearestStoredDescendants(task, _states.ContainsKey)
                .Select(id => (id, (IDurableTaskState)_states[id]));

        public IDurableTaskState GetOrCreateTask(TaskId taskId, DurableTaskRecordKind kind, IDurableTaskRequest? request)
        {
            if (!_states.TryGetValue(taskId, out var state))
            {
                state = new TestTaskState
                {
                    Kind = kind,
                    Request = request,
                    CreatedAt = _timeProvider.GetUtcNow(),
                };
                _states.Add(taskId, state);
                _hierarchy.Add(taskId);
            }

            return state;
        }

        public void SetInvocation(TaskId taskId, IDurableTaskState state) =>
            ((TestTaskState)state).Kind = DurableTaskRecordKind.Invocation;

        public void SetRequest(TaskId taskId, IDurableTaskState state, IDurableTaskRequest request) =>
            ((TestTaskState)state).Request = request;

        public void SetRequestFingerprint(TaskId taskId, IDurableTaskState state, string fingerprint, string? method = null)
        {
            ((TestTaskState)state).RequestFingerprint = fingerprint;
            ((TestTaskState)state).Method = method ?? ((TestTaskState)state).Method;
        }

        public void SetRemoteRequest(TaskId taskId, IDurableTaskState state, GrainId target, string method, string fingerprint)
        {
            ((TestTaskState)state).RemoteTarget = target;
            ((TestTaskState)state).Method = method;
            ((TestTaskState)state).RemoteRequestFingerprint = fingerprint;
        }

        public void SetIncarnation(TaskId taskId, IDurableTaskState state, Guid incarnation) =>
            ((TestTaskState)state).Incarnation = incarnation;

        public void SetCallerId(TaskId taskId, IDurableTaskState state, GrainId callerId) =>
            ((TestTaskState)state).CallerId = callerId;

        public void SetResponse(TaskId taskId, IDurableTaskState state, DurableTaskResponse response)
        {
            ((TestTaskState)state).Result = response;
            ((TestTaskState)state).CompletedAt = _timeProvider.GetUtcNow();
            ((TestTaskState)state).CompletionSequence = ++_lastCompletionSequence;
            ((TestTaskState)state).DueTime = null;
            if (((TestTaskState)state).ResumeGeneration > 0)
            {
                ((TestTaskState)state).ResumeGeneration++;
            }
        }

        public void RequestCancellation(TaskId taskId, IDurableTaskState state, string? reason = null)
        {
            if (((TestTaskState)state).CancellationRequestedAt is null)
            {
                ((TestTaskState)state).CancellationRequestedAt = _timeProvider.GetUtcNow();
                ((TestTaskState)state).CancellationReason = reason;
            }
        }

        public void Shield(TaskId taskId, IDurableTaskState state) =>
            ((TestTaskState)state).Shielded = true;

        public void SetAccepted(TaskId taskId, IDurableTaskState state, bool accepted) =>
            ((TestTaskState)state).Accepted = accepted;

        public void SetDelay(TaskId taskId, IDurableTaskState state, DateTimeOffset dueTime, long generation)
        {
            ((TestTaskState)state).DueTime = dueTime;
            ((TestTaskState)state).ResumeGeneration = generation;
        }

        public void AddCompletionDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination) =>
            ((TestTaskState)state).CompletionDestinations.Add(destination);

        public void RemoveCompletionDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination) =>
            ((TestTaskState)state).CompletionDestinations.Remove(destination);

        public void RetainResult(TaskId taskId, IDurableTaskState state) =>
            ((TestTaskState)state).RetainedSince = _timeProvider.GetUtcNow();

        public bool TryGetTask(TaskId taskId, [NotNullWhen(true)] out IDurableTaskState? state)
        {
            var found = _states.TryGetValue(taskId, out var value);
            state = value;
            return found;
        }

        public bool RemoveTask(TaskId taskId)
        {
            if (!_states.Remove(taskId))
            {
                return false;
            }

            _hierarchy.Remove(taskId, _states.ContainsKey);
            return true;
        }

        public void Clear()
        {
            _states.Clear();
            _hierarchy.Clear();
        }
        public ValueTask WriteAsync(CancellationToken cancellationToken) => manager.WriteStateAsync(cancellationToken);
        public ValueTask ReadAsync(CancellationToken cancellationToken) => default;
    }

    /// <summary>
    /// <see cref="TestStorage"/> 的内存任务状态。它直接保存已解码的请求和响应，因此运行时
    /// 测试可以使用没有 Orleans 编解码器的请求。
    /// </summary>
    private sealed class TestTaskState : IDurableTaskState
    {
        private IDurableTaskRequest? _request;

        public DurableTaskRecordKind Kind { get; set; }
        public DurableTaskResponse? Result { get; set; }
        public HashSet<DurableTaskSubscriber> CompletionDestinations { get; set; } = [];

        /// <summary>获取或设置已存储的请求；若设置了 <see cref="RequestDecodeFailure"/>，读取请求时将抛出该异常。</summary>
        public IDurableTaskRequest? Request
        {
            get => RequestDecodeFailure is { } failure ? throw failure : _request;
            set => _request = value;
        }

        /// <summary>获取或设置解码已存储请求时抛出的异常，用于模拟请求不再能够解码的情况。</summary>
        public Exception? RequestDecodeFailure { get; set; }

        public bool HasRequest => _request is not null;
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? CancellationRequestedAt { get; set; }
        public string? CancellationReason { get; set; }
        public bool Shielded { get; set; }
        public bool? Accepted { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? DueTime { get; set; }
        public long ResumeGeneration { get; set; }
        public string? RequestFingerprint { get; set; }
        public string? Method { get; set; }
        public DateTimeOffset? RetainedSince { get; set; }
        public GrainId RemoteTarget { get; set; }
        public string? RemoteRequestFingerprint { get; set; }
        public GrainId CallerId { get; set; }

        public Guid Incarnation { get; set; }
        public long CompletionSequence { get; set; }
        IReadOnlySet<DurableTaskSubscriber> IDurableTaskState.CompletionDestinations => CompletionDestinations;
    }

    private sealed class TestDurableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IDurableDictionary<TKey, TValue>
        where TKey : notnull;

    /// <summary>
    /// 模拟日志化状态管理器的写入协议：准备、捕获、存储确认以及
    /// 完成回调。准备失败保留已暂存的更改；存储失败则隔离管理器以阻止后续操作。
    /// </summary>
    private sealed class TestStateManager : IJournaledStateManager
    {
        private readonly Dictionary<string, IStateMachine> _stateMachines = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private Exception? _failure;
        public int WriteCount { get; private set; }
        public bool IsFenced => _failure is not null;

        /// <summary>在准备状态前运行；抛出异常可模拟准备失败。</summary>
        public Action? BeforePrepare { get; set; }

        /// <summary>在准备之后、捕获之前运行；抛出异常可模拟存储失败。</summary>
        public Action? BeforeWrite { get; set; }

        /// <summary>在捕获之后、存储确认之前运行；抛出异常可模拟存储失败。</summary>
        public Func<ValueTask>? AfterWriteStarted { get; set; }
        public Action<int>? AfterWriteCompleted { get; set; }

        /// <summary>
        /// 模拟不包含日志条目的写入（例如其更改已被先前写入捕获的情形）：Journaling
        /// 不存储任何内容，也不向状态机确认这些写入。
        /// </summary>
        public bool WritesAreEmpty { get; set; }

        /// <summary>
        /// 与 Journaling 一样，在写入向状态机发出确认之后的后续工作项中恢复每个写入方，
        /// 而非与确认操作同步恢复。
        /// </summary>
        public bool ResumesWritersAsynchronously { get; set; }

        public ValueTask InitializeAsync(CancellationToken cancellationToken) => default;

        public void RegisterStateMachine(string name, IStateMachine stateMachine) => _stateMachines[name] = stateMachine;

        public bool TryGetStateMachine(string name, [NotNullWhen(true)] out IStateMachine? stateMachine) =>
            _stateMachines.TryGetValue(name, out stateMachine);

        public async ValueTask WriteStateAsync(CancellationToken cancellationToken)
        {
            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_failure is { } failure)
                {
                    throw new InvalidOperationException("Journaled state operations are fenced because an operation failed.", failure);
                }

                var stateMachines = _stateMachines.Values.ToArray();
                try
                {
                    BeforePrepare?.Invoke();
                    for (var prepared = false; !prepared;)
                    {
                        prepared = true;
                        foreach (var stateMachine in stateMachines)
                        {
                            var preparation = stateMachine.PrepareWriteAsync(cancellationToken);
                            if (!preparation.IsCompletedSuccessfully)
                            {
                                prepared = false;
                                await preparation;
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    throw new JournaledStatePreCommitException(exception);
                }

                try
                {
                    BeforeWrite?.Invoke();
                    foreach (var stateMachine in stateMachines)
                    {
                        stateMachine.WritePendingEntries(default);
                    }

                    if (AfterWriteStarted is { } afterWriteStarted)
                    {
                        await afterWriteStarted();
                    }
                }
                catch (Exception exception)
                {
                    _failure = exception;
                    StateChanges.Notify();
                    throw;
                }

                if (WritesAreEmpty)
                {
                    return;
                }

                WriteCount++;
                foreach (var stateMachine in stateMachines)
                {
                    stateMachine.OnWriteCompleted();
                }

                AfterWriteCompleted?.Invoke(WriteCount);
                StateChanges.Notify();
            }
            finally
            {
                _writeGate.Release();
            }

            if (ResumesWritersAsynchronously)
            {
                await Task.Yield();
            }
        }
        internal sealed class PendingDurableTask : DurableTask
        {
            protected override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context) =>
                new(DurableTaskResponse.Pending);
        }

        internal sealed class TestRemoteDurableTask(IDurableTaskRequest request) : DurableTask, ISchedulableTask, IDurableTaskRequest
        {
            private int _scheduleAsyncCallCount;

            public DurableTaskRequestContext? Context => request.Context;
            public int ScheduleAsyncCallCount => Volatile.Read(ref _scheduleAsyncCallCount);
            public InvokeMethodOptions Options => request.Options;
            public DurableTask CreateTask() => request.CreateTask();
            public object GetTarget() => request.GetTarget()!;
            public void SetTarget(ITargetHolder holder) => request.SetTarget(holder);
            public ValueTask<Response> Invoke() => request.Invoke();
            public int GetArgumentCount() => request.GetArgumentCount();
            public object GetArgument(int index) => request.GetArgument(index)!;
            public void SetArgument(int index, object value) => request.SetArgument(index, value);
            public void Dispose() => request.Dispose();
            public string GetMethodName() => request.GetMethodName();
            public string GetInterfaceName() => request.GetInterfaceName();
            public string GetActivityName() => request.GetActivityName();
            public Type GetInterfaceType() => request.GetInterfaceType();
            public System.Reflection.MethodInfo GetMethod() => request.GetMethod();
            public void AddInvokeMethodOptions(InvokeMethodOptions options) => request.AddInvokeMethodOptions(options);
            public ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _scheduleAsyncCallCount);
                return new(DurableTaskResponse.Pending);
            }
            public IScheduledTaskHandle GetHandle(TaskId taskId) => throw new NotSupportedException();
            protected override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context) =>
                throw new NotSupportedException();
        }

        public ValueTask DeleteStateAsync(CancellationToken cancellationToken) => default;
    }
}
