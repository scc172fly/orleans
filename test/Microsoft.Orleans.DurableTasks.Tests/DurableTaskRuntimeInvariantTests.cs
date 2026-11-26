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

        // 信箱里写着一个信息, 其水槽拒绝它; 水槽没有先分级。
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

        // 取消电话的手续已经安排妥当,向身体传播该通知等待作出该通知的文字。
        // 开始在写入前一步, 所以分阶段取消会拒绝该步骤 。
        Accept(CreateHandler(runtime), CancellationMessage(caller, runtime.GrainId, rootId, "caller stopped"));
        release.SetResult();

        // 该机构观察拒绝其步骤的取消,并给予补偿。
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

        // 信箱接受所有信息, 在写作之前, 并且接受每个信息, 所以任何消息都不可等待 。
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

        // 接受的完成阶段在任务状态下,一旦完成,就可完成,而且不会将任何东西送回给儿童。
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

        // 所列目的地在发信箱中有一个待完成的目的地,该发信箱将予以留存。
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

        // 中止, 由信箱交付的中止会使任务结果处于阶段, 从而立即结束保留 。
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

        // 分阶段身份安全,所以可以保留;失败日记被围起来,而不是复发。
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

        // 中止, 由信箱交付的中止将任务结果置于舞台 。
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

        // 打电话者取消孩子的电话可以通过收件箱超过孩子的援引。
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
            // 拖延状态及其回信是在这两件事情都写完之前就准备好的。
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

        // 完成阶段由收件箱结果进行,处理器在写完后才完成。
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

        // 重复的完成延迟恢复是无害的。
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

        // 重播要求延长期限,例如,在配置改变后,即保持承诺。
        var replay = await runtime.ScheduleDelayAsync(
            taskId,
            TimeSpan.FromMinutes(6),
            TestContext.Current.CancellationToken);

        Assert.Same(DurableTaskResponse.Pending, replay);
        Assert.Equal(recordedDueTime, storage.Get(taskId).DueTime);
        Assert.Equal(generation, storage.Get(taskId).ResumeGeneration);

        // 延迟发送的回信,重播不代表什么新内容。
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
            // 将拖延的写作和回信搁置。
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

        // 拖延执行的恢复是老旧的,所以它没有改变。
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

        // 只有延迟本身的任务记录了一个合适的时间; 其父( 父) 继续运行并按标识符重放 。
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

        // 恢复信息延迟后是持久的,因此收回留下的拖延没有延期,不再继续。
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

        // 取消时及其信息以一个字形式承诺;发件箱只发送承诺的信息。
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

        // 发件箱可能已经已将承诺的邮件退封。 发送身份后, 将会启动新的发送
        // 处理第一个拒绝的接收器, 处理第一个拒绝者, 所以回收的激活
        // 重放再次取消任务, 停止任何阶段 。
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

        // 树的删除会取消待决的远方孩子;
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

        // 请求书中发出的取消信息;被传唤人决定儿童的结果。
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
                    // 主机中止到达代表牌, 但这不是永久取消 。
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

        // 停止令每个服务员都失败, 就像一个在运行时间停止时到达的电话, 并且让任务处于等待状态。
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

        // 运行代码的正文是可运行的, 所以被禁用的 grain 必须醒过来恢复它 。
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

        // 拖拉机上停着的一具尸体等待回信,该回信重新启动 grain。
        await ((IDurableTaskServer)runtime).ScheduleAsync(delayId, CreateRequest(2, () => DurableTask.Delay(TimeSpan.FromDays(1))), TestContext.Current.CancellationToken);
        await transport.ResumeSent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitForNoWorkAsync();
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 启动调度器上排队的工作项目按顺序运行,所以让尝试停放或结束。
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

        // 发出呼叫后结果的写作恢复了根,其代码在以后的工作项目中继续。
        // 之前的咨询,例如后期的书和警醒, 将根作为可调用词来报告。
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

        // 回收重现运行时的波动状态, 重播日记任务状态, 并在激活前完成 。
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

        // 追回工作推迟执行,直到启动完成。
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

        // 在一个在启动完成之前 到达回收任务的一个电话
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
                // 重播到达了孩子 早期运行记录。
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

        // 执行的手柄将运行时间的单把手包起来 给孩子。
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
        // 时钟永远不能进步,所以只有等待候选人才能选出获胜者。
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

        // 第一批候选人只有两个候选人都参加竞选,而且没有获胜者被选中,才完成第一候选人的甄选,等待。
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await selectionWaits.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        first.SetResult(7);
        var response = await runtime.GetScheduledTaskHandle(rootId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(7, response.GetResult<int>());

        // 候选人的完成令选择获胜者,因此不记录任何决定。
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
                // 第一次尝试停在这里,中止了 所以下一次激活会重播
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

        // 操作运行从启动的调度器上运行, 它拥有尝试状态, 所以它没有失败
        // 到达运行时间,而不是被发送到调度器。
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

        // 工作流程改变了:重播计划了第一个执行过程记录种族选择的本地步骤。
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

        // 尝试的标记与取消分开发出
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

        // 中止取消尝试, 并且中止的操作在尝试标记发出前完成延迟 。
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

        // 本地服务员无法完成关机; 分阶段反应仍承诺为下次激活使用箱批次。
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

        // 第一步运行第一, 所以记录一步的写作 也记录了它的结果。
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

        // 步骤的最后代码队列最后代码组对激活工作, 作为一种 grain 操作, 需要同时写入日记
        // 这项工作在步骤延续之前进行,因此它要求的写作承诺
        // 步骤的状态变化; 它必须发现步骤的结果是和它们一起进行的。
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

        // 记录阶段没有自己写作, 并承诺延迟写作。
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

        // 重播到达延迟, 最后一次操作 第一次运行记录, 然后运行现场。
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
                    // 部署在记录前增加了效果。
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

                // 同一独立任务的第二个开始,达到第一个任务实现的记录。
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
            // 步骤的记录,它的延迟 孩子, 和延迟的回信 承诺一起。
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

        // 步骤没有记录,所以下一个启动重播一步并再次运行。
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

        // 失败的写作保留了分阶段的结果,背景文件承诺完成该步骤。
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

        // 被援引的儿童拥有自己的树,而树则拥有孙子,每棵树一旦过期就予以删除。
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

        // 发件箱的阶段 完成清除并调用源 在同一个同步区块。
        ((IOutboxMessageSource)runtime).OnRetired(completion.MessageId, OutboxMessageRetirement.Delivered.Instance);

        Assert.False(storage.Contains(taskId));
        Assert.False(storage.Contains(childId));
        Assert.False(storage.Contains(loserId));
        Assert.False(storage.Contains(decisionId));
        Assert.False(storage.LogicalTimes.ContainsKey(taskId));
        Assert.False(((IOutboxMessageSource)runtime).TryGetMessage(completion.MessageId, out _));
        Assert.Equal((runtime.GrainId, target, loserWireId), Assert.Single(transport.Cancellations));
        Assert.Equal(writes, manager.WriteCount);

        // 任务识别特征下的某一个职业保留了自己的生命周期。
        Assert.True(storage.Contains(nestedId));

        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(writes + 1, manager.WriteCount);

        // 而不是扫描每件任务
        Assert.Equal(enumerations, storage.TasksEnumerationCount);

        // 被取消的远程孩子的晚期完成,
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

        // 完成后重新重述,但信上未注明,保留的结果再次发送。
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

        // 发箱废信后完成 OutboxRetryTimeout,该数值超过留存期。
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

        // 任务识别符仅在结果保留期间重复时间表。
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

        // 保留结束后没有写或激活, 因此时间表本身适用并运行请求
        // 之后的民意调查又注意到删除的内容。
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

        // 任务识别符由调用者选择,因此另一个 grain 可以在外任务的识别符下援引一项任务。
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
                    // 删掉的尝试无法安排持久的工作, 这会比树长寿。
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

        // 删除后将取消运行中的尝试, 其符号将在以后的工作项目中显示 。
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var exception = Assert.IsType<DurableTaskAttemptCanceledException>(await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(DurableTaskAttemptCancellationReason.TaskDeleted, exception.Reason);
        Assert.Empty(storage.Tasks);
        var writes = manager.WriteCount;

        // 拆除工作使这一企图付诸东流,其结果被抛弃而不恢复状态。
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

        // 取消一项未知的任务, 例如因为删除而取消, 将取消记录在
        // 情况是,其援引仍在飞行中。
        Accept(CreateHandler(runtime), CancellationMessage(caller, runtime.GrainId, taskId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var record = storage.Get(taskId);
        Assert.NotNull(record.CancellationRequestedAt);
        Assert.Null(record.Result);

        // 记录比接收窗口的长度长一个窗口,它覆盖发件人的时钟在前面。
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
            // 第一个写作承诺该步骤及其结果; 下一个删除它。
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
            // 分阶段取消在作出承诺之前是显而易见的,其产生的任何影响在承诺之后也是很明显的。
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

        // 错误命名记录和请求操作, 因此不同的重放是可判断的 。
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

        // 工作流程改变了:重播计划了第一个执行时记录了一个 grain 呼叫的本地步骤。
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

        // 根的留存一旦完成,根的留存就结束,因此,写作结果的文字会删除树。
        storage.SetResponse(rootId, storage.Get(rootId), DurableTaskResponse.Completed);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => !storage.Contains(rootId) && !storage.Contains(taskId));

        // 根部的一卷后来记录了孩子再次,根据同一身份识别特征和另一个线标识。
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

        // 被召者将记录命名为其订阅人,不要求打电话。
        var invocation = Assert.Single(transport.DetachedInvocations);
        Assert.Equal(
            (runtime.GrainId, detachedRequest.Context!.TargetId, rootId, (TaskId?)runtime.GetWireId(recordId)),
            (invocation.Sender, invocation.Target, invocation.TaskId, invocation.SubscriberId));
        Assert.Empty(transport.Invocations);
        Assert.Equal(default, detachedRequest.Context.CallerId);
        Assert.False(detachedRequest.Context.SupportsDurableCompletion);

        // 请求及其记录以一个字写下;记录记录记录了任务,启动人不储存的任务。
        Assert.DoesNotContain((false, 1), writes);
        var record = storage.Get(recordId);
        Assert.Null(record.Result);
        Assert.Null(record.Request);
        Assert.True(record.RemoteTarget.IsDefault);
        Assert.False(storage.Contains(rootId));

        // 替换激活重放启动器, 它到达记录时不会发送再次的调用 。
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var (replacement, _, _, replacementTransport) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await posted[1].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Empty(replacementTransport.DetachedInvocations);
        Assert.Empty(replacementTransport.Invocations);

        // 分离任务的结果完成记录
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
                // 开始前未命名的孩子不会改变开始到达的记录 。
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

        // 根是单段{HostId}/{Id}/{separtment},其部件作为任务识别符而脱离了“/”的部件,所以
        // 使用同一标识符执行的 grain,产生不同的根标识符。
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

        // 独立开始观察目标的根任务,而其他当事方则处理这个目标。
        // 目标储存库,在此 grain范围下,当地儿童以自己的识别码存储;执行
        // 树是两者的主人,所以也没有地址。
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

        // 重播到达相同的记录,其手柄命名相同的任务。
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
                    // 清理时间比取消启动程序的时间长。
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

        // 删除启动器树删除记录,不向独立任务发送更多信息。
        await WaitUntilIdleAsync(runtime);
        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.Completions).MessageId, OutboxMessageRetirement.Delivered.Instance);
        Assert.False(storage.Contains(starterId));
        Assert.False(storage.Contains(recordId));
        Assert.Empty(transport.Cancellations);
        Assert.Single(transport.Subscriptions);

        // 尚未解决的雇用情况表仍然提供清理工作,但无记录退休。
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

        // 取消的启动器记录立刻结束订阅, 开始仍然等待目标。
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

        // 根和记录一起承诺;根没有呼唤者,结果直接记录下来。
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

        // 执行启动器的完成删除树, 树保留记录, 但不能保存分离根 。
        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.Completions).MessageId, OutboxMessageRetirement.Delivered.Instance);
        Assert.False(storage.Contains(starterId));
        Assert.False(storage.Contains(recordId));
        Assert.Equal(5, (await runtime.SubscribeOrPollAsync(rootId, new SubscribeOrPollOptions(), TestContext.Current.CancellationToken)).GetResult<int>());

        // 与客户排期根一样,独立根在完成后保留到留存期内。
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

        // 同一方法的一篇文章,其争论不同之处会到达存储的任务;另一种方法冲突。
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

        // 重播到记录 与相同的开始阶段 没有新的。
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

        // 任务及其用户是一起安排的。
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

        // 该 grain 将发送者的子女存放在发送者的范围之下,而另一请求已经发出。
        var storedId = CallerScope.GetTaskId(sender, taskId);
        runtime.ScheduleFromInbox(storedId, CreateRequest(1, () => DurableTask.Run(static async cancellationToken => await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken))));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var fingerprint = storage.Get(storedId).RequestFingerprint;
        var destinations = storage.Get(storedId).CompletionDestinations.ToArray();
        var request = CreateSerializableRequest(runtime.GrainId);

        // 所附的援引识别资料已经确定了另一个请求,所以收件箱中写着“
        // 冲突代码和讯息 发送者发件人发件人发件箱报告拒绝
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

        // 发件人的时钟在 grain的提前运行,所以发者在 grain时间之后即开始发通知。
        // 取消后先到的。
        var sentAt = clock.GetUtcNow() + skew;
        Accept(handler, CancellationMessage(sender, runtime.GrainId, taskId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        // 时间在 grain时钟上的一个窗口过去,该请求仍然在其允许窗口内,同样
        // 取消记录
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

        // 打电话者先进行祈祷,然后取消第一个。取消行动首先到来,
        // 后压将值数维持到接收窗口结束。
        Accept(handler, CancellationMessage(sender, runtime.GrainId, taskId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        clock.Advance(window - TimeSpan.FromTicks(1));
        Assert.False(InvokeDeleteExpiredTasks(runtime));

        Accept(handler, Invocation(taskId));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(storedId).Result is { IsCompleted: true });
        Assert.Equal(DurableTaskStatus.Canceled, storage.Get(storedId).Result!.Status);

        // 一旦窗口关闭, grain 信上写着死亡信 是一种不储存的祈祷 承认一个又被送来
        // 所储存的,
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

        // 水槽抛出而不是拒绝请求, 所以发件人发送信箱重试了送货。
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

        // 电话是参照和作出承诺的,时间表等待目标接受。
        var messageId = Assert.Single(transport.SentInvocations);
        Assert.Equal(transport.GetInvocationMessageId(runtime.GrainId, target, runtime.GetWireId(childId), subscriberId: null), messageId);
        Assert.False(scheduled.Task.IsCompleted);
        Assert.Null(storage.Get(childId).Accepted);

        // 一旦目标持久接受,发信箱就将交付的祈祷品退伍。
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

        // 目标用死字母来表示使用, 发箱则以目标代码和理由 拒绝退出。
        ((IOutboxMessageSource)runtime).OnRetired(
            Assert.Single(transport.SentInvocations),
            new OutboxMessageRetirement.Rejected("rejected", code));
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await attempts[0].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(storage.Get(recordId).Accepted);
        Assert.Equal(DurableTaskStatus.Failed, storage.Get(recordId).Result!.Status);

        // 重播观看记录中的拒绝,而不发送再次的援引。
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

        // 发信箱在重新尝试超时后, 发出无字的回音。
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

        // 替换启动重播了调用, 与其表上仍然持有的调用合并。
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var (replacement, _, _, replacementTransport) = CreateRuntime(storage, manager);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => replacementTransport.Invocations.Count == 1);

        Assert.Empty(replacementTransport.SentInvocations);
        var source = (IOutboxMessageSource)replacement;
        Assert.True(source.TryGetMessage(messageId, out var pending));
        Assert.Equal(target, pending.ReceiverId);

        // 发送的传票记录了目标接受的情况 通话结果完成启动程序
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

        // 根任务会保留其识别特征, 所以它援引的特性与每个职位相同。
        // 同一方法与待发的援引合并,而不论其论点如何。
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

        // 任务的结果可以在其调职退休之前就已经达到;它表明,目标承担了这项任务。
        runtime.AcceptResponseFromInbox(recordWireId, rootId, DurableTaskResponse.Completed, target);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, storage.Get(starterId).Result!.Status);

        // 退休后记录了接受,拒绝不能取代结果。
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

        // 处理器同步进行更改; 收件箱拥有批量写作。
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
    /// 创建 <paramref name="sender"/> 为 <paramref name="receiver"/>的远端子孙发送的补全
    /// 其线标识符 <paramref name="wireId"/>,存储在接收器的调用范围之下。
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
    /// 创建运行时与 <paramref name="manager"/>的连接时间。重新使用耐久存储和管理员模式a
    /// 替换激活: 新运行时间将取代前一个操作时间, 成为管理器日记钩 。
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
    /// <summary>创建一个替代激活,该启动可以作为非回复激活程序运行其本地请求。</summary>
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

        /// <summary>完成一个待处理的拖延 <paramref name="response"/>,作为承诺的结果。</summary>
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

        /// <summary>获取列出的 <see cref="Tasks"/> 次数。</summary>
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
        /// 储存一个待决的援引任务,其执行树将保存在试验记录下的一个节点,作为耐久性
        /// 执行部分的节点属于它所执行的任务的树。
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
    /// <see cref="TestStorage"/>的模拟任务状态。 它直接持有解码请求和答复, 即运行时间
    /// 测试可使用没有 Orleans 编码器的请求。
    /// </summary>
    private sealed class TestTaskState : IDurableTaskState
    {
        private IDurableTaskRequest? _request;

        public DurableTaskRecordKind Kind { get; set; }
        public DurableTaskResponse? Result { get; set; }
        public HashSet<DurableTaskSubscriber> CompletionDestinations { get; set; } = [];

        /// <summary>获取或设置存储的请求,该请求在设定时丢弃 <see cref="RequestDecodeFailure"/> 。</summary>
        public IDurableTaskRequest? Request
        {
            get => RequestDecodeFailure is { } failure ? throw failure : _request;
            set => _request = value;
        }

        /// <summary>获取或设置解码存储请求时的失败,因为不再编码的请求会被解码。</summary>
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
    /// 以《国家管理人员的编制协议:准备、捕获、储存确认和
    /// 完成回调。 准备失败保留了分阶段的改动;储存失败使管理人员成为围栏。
    /// </summary>
    private sealed class TestStateManager : IJournaledStateManager
    {
        private readonly Dictionary<string, IStateMachine> _stateMachines = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private Exception? _failure;
        public int WriteCount { get; private set; }
        public bool IsFenced => _failure is not null;

        /// <summary>运行前先于国家准备; 丢弃模型一个准备失败。</summary>
        public Action? BeforePrepare { get; set; }

        /// <summary>准备后和捕获前运行; 抛掷存储失败的模型。</summary>
        public Action? BeforeWrite { get; set; }

        /// <summary>捕获后运行, 储存确认前运行; 抛掷存储失败的模型。</summary>
        public Func<ValueTask>? AfterWriteStarted { get; set; }
        public Action<int>? AfterWriteCompleted { get; set; }

        /// <summary>
        /// 模型写作时没有日记条目,例如其先前的文字被记录到的更改:
        /// 却不承认这些物品是国家机器的
        /// </summary>
        public bool WritesAreEmpty { get; set; }

        /// <summary>
        /// 将每位作家重新列入比该作者对国有机器的确认书, 更晚的工作项目中, 称为《日刊》
        /// 而不是和他们同步
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
