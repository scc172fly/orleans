using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Orleans.Configuration;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

/// <summary>
/// 在投递链的每个边界注入故障，并断言瞬时基础设施故障只会延迟
/// 消息，随后仍会投递并使处理效果恰好发生一次；只有显式拒绝和
/// 重试超时才是永久性的。
/// </summary>
/// <remarks>
/// 发件箱对接收方调用的故障通过 <see cref="DeliveryFaultInjector"/> 注入，
/// 日志写入故障通过 <see cref="ControlledJournalStorageProvider"/> 注入，<see cref="DurableJobManagerProbe"/> 用于唤醒
/// 调度故障注入，<see cref="HandlerProbe"/> 用于处理程序故障注入。时间仅通过
/// <see cref="FaultInjectionClusterFixture.JobClock"/> 推进，每次重试均通过显式运行消息泵执行。
/// </remarks>
[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class FaultInjectionTests : MessagingBehaviorTestBase
{
    private const string Route = "messages/record";
    private const string HeldRoute = "messages/held";
    private static readonly TimeSpan RetryInterval = FaultInjectionClusterFixture.RetryInterval;
    private static readonly TimeSpan RetryTimeout = FaultInjectionClusterFixture.OutboxRetryTimeout;
    private readonly FaultInjectionClusterFixture _cluster;

    public FaultInjectionTests() : this(new FaultInjectionClusterFixture())
    {
    }

    private FaultInjectionTests(FaultInjectionClusterFixture cluster) : base(cluster) => _cluster = cluster;

    private DeliveryFaultInjector Deliveries => _cluster.Deliveries;

    [Fact]
    public async Task OutboxWakeup_BareStagingWaitsForCommitAfterPriorDeliveryDrains()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        _ = await sender.GetSnapshotAsync();
        Fixture.JobManagerProbe.SkipNext(MessagingPump.JobName);
        await sender.SendAsync(receiver.GetGrainId(), Route, NewMessage(20, "committed"));
        await Fixture.OnGrainAsync(sender, () => Fixture.GetPump(sender).EnsureStarted());
        await WaitForDrainedOutboxAsync(sender);
        var pump = Fixture.GetPump(sender);
        Assert.NotNull(await Fixture.OnGrainAsync(sender, () => InboxTestAccess.WakeupJobId(pump)));
        var attempts = Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, sender.GetGrainId());
        await sender.StageWithoutCommitAsync(receiver.GetGrainId(), Route, NewMessage(21, "staged"));

        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(sender)).Status);
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(sender)).Status);
        Assert.Equal(attempts, Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, sender.GetGrainId()));
        Assert.Equal(1, (await sender.GetSnapshotAsync()).OutboxCount);
        Assert.Equal(1, Deliveries.ReceiverCalls);

        Fixture.JobManagerProbe.SkipNext(MessagingPump.JobName);
        await sender.RetryWriteStateAsync();
        Assert.Equal(attempts + 1, Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, sender.GetGrainId()));
        await WaitForDrainedOutboxAsync(sender);
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 2);
        Assert.Equal(new[] { "committed", "staged" }, completed.Effects.Select(effect => effect.Value));
        Assert.All(completed.Effects, effect => Assert.Equal(1, effect.Count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutboxWakeup_CapturedWriteRetainsCoverageUntilAcknowledgedAndRecovered(bool snapshot)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var before = await sender.GetSnapshotAsync();
        var previous = Fixture.GetGrainContext(sender);
        var journal = JournalId.FromGrainId(sender.GetGrainId());
        if (snapshot) Fixture.Storage.RequestSnapshot(journal);
        using var acknowledgement = Fixture.Storage.BlockAcknowledgement(journal);
        var message = NewMessage(22, "captured");
        var sending = sender.SendAsync(receiver.GetGrainId(), Route, message);
        await acknowledgement.WaitUntilEnteredAsync();
        Assert.False(sending.IsCompleted);
        Assert.Equal(0, Deliveries.ReceiverCalls);

        // 投递等待写入确认时，捕获的条目仍保有唤醒作业。
        var wakeup = await Fixture.RunPumpAsync(sender);
        Assert.Equal(DurableJobRunStatus.RescheduleRequested, wakeup.Status);
        Assert.Equal(_cluster.JobClock.GetUtcNow() + TimeSpan.FromSeconds(1), wakeup.RescheduleTime);
        Assert.Equal(0, Deliveries.ReceiverCalls);

        acknowledgement.Fail();
        await Assert.ThrowsAsync<IOException>(() => sending);
        await previous.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var recovered = await sender.GetSnapshotAsync();
        Assert.NotEqual(before.ActivationId, recovered.ActivationId);
        await WaitForDrainedOutboxAsync(sender);
        await AssertProcessedOnceAsync(receiver, message);
        Assert.Equal(1, Deliveries.ReceiverCalls);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
    }

    [Fact]
    public async Task OutboxWakeup_CompletedJobDuringPreparationIsReplacedBeforeCapture()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        _ = await sender.GetSnapshotAsync();
        var journal = JournalId.FromGrainId(sender.GetGrainId());
        using var write = Fixture.Storage.BlockWrite(journal);
        Fixture.JobManagerProbe.SkipNext(MessagingPump.JobName);
        using var scheduling = Fixture.JobManagerProbe.BlockNext(MessagingPump.JobName);
        var message = NewMessage(23, "prepared");
        var sending = sender.SendAsync(receiver.GetGrainId(), Route, message);
        await scheduling.WaitUntilEnteredAsync();

        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(sender)).Status);
        Assert.False(sending.IsCompleted);
        Assert.Equal(0, Fixture.Storage.GetSuccessfulWriteCount(journal));
        scheduling.Continue();
        await write.WaitUntilEnteredAsync();
        Assert.Equal(2, Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, sender.GetGrainId()));
        Assert.Single(Fixture.JobManagerProbe.GetScheduledJobs(MessagingPump.JobName, sender.GetGrainId()));
        Assert.Equal(0, Deliveries.ReceiverCalls);
        Assert.Equal(DurableJobRunStatus.RescheduleRequested, (await Fixture.RunPumpAsync(sender)).Status);

        write.Release();
        await sending;
        await WaitForDrainedOutboxAsync(sender);
        await AssertProcessedOnceAsync(receiver, message);
        Assert.Equal(1, Deliveries.ReceiverCalls);
    }

    [Theory]
    [InlineData(DeliveryFaultKind.Throw, "Injected transient delivery failure.")]
    [InlineData(DeliveryFaultKind.TimeOut, "TimeoutException")]
    public async Task TransientDeliveryFault_DelaysTheMessageThenDeliversItOnce(DeliveryFaultKind kind, string error)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        Deliveries.Enqueue(kind == DeliveryFaultKind.Throw ? DeliveryFault.Throw() : DeliveryFault.TimeOut());
        var startedAt = _cluster.JobClock.GetUtcNow();
        var message = NewMessage(1, "transient");

        var id = await sender.SendAsync(receiver.GetGrainId(), Route, message);

        // 故障只调度重试；从未调用接收方。
        var attempt = await WaitForFailedAttemptAsync(sender, id, 1);
        Assert.Contains(error, attempt.LastError);
        Assert.Equal(startedAt + RetryInterval, attempt.NextAttemptAt);
        Assert.Equal(startedAt, attempt.SentAt);
        Assert.Equal(0, Deliveries.ReceiverCalls);

        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);

        await WaitForDrainedOutboxAsync(sender);
        await AssertProcessedOnceAsync(receiver, message);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
        Assert.Empty((await sender.GetSnapshotAsync()).OutboxDeadLetters);
    }

    [Fact]
    public async Task UncleanStop_ChargesEachDueMessageOneAttemptWithoutDeadLetteringIt()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var running = CreateEnvelope(receiver, NewMessage(180, "interrupted"), "messages/interrupted");
        var queued = CreateEnvelope(receiver, NewMessage(181, "queued"));
        using (var barrier = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), "messages/interrupted"))
        {
            DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, running));
            await WaitForBarrierAsync(receiver, barrier);
            DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, queued));
            await receiver.ConfigureHandlerAsync(false);

            // 停用会取消正在进行的尝试；该尝试在没有结果的情况下结束，并使消息传递失败，因此
            // 激活实例不会记录正常停止，就像进程在尝试期间崩溃一样。
            await _cluster.CrashAsync(receiver);
        }

        // 恢复时，为中断的尝试和尚未运行的排队消息各计入一次尝试。这两条消息
        // 此前均未失败，因此即使此测试夹具的 InboxRetryTimeout 为零，也不会将任一消息转为死信。
        var recovered = await receiver.GetSnapshotAsync();
        var inbox = Fixture.GetInbox(receiver);
        var states = await Fixture.OnGrainAsync(receiver, () => new[]
        {
            InboxTestAccess.GetMessageState(inbox, running.Envelope.MessageId),
            InboxTestAccess.GetMessageState(inbox, queued.Envelope.MessageId)
        });
        Assert.All(states, static state => Assert.Equal(1, state.AttemptCount));
        Assert.Equal(2, recovered.InboxCount);
        Assert.Empty(recovered.InboxDeadLetters);

        await receiver.ConfigureHandlerAsync(true);
        await _cluster.AdvanceAndRunPumpAsync(receiver, TimeSpan.Zero);
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 2);
        Assert.All(completed.Effects, static effect => Assert.Equal(1, effect.Count));
        Assert.Empty(completed.InboxDeadLetters);
    }

    [Fact]
    public async Task FailedAttemptRetry_StaysCoveredByTheSendsWakeupThroughACrash()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        Deliveries.Enqueue(DeliveryFault.Throw());
        var message = NewMessage(9, "retry-wakeup");

        var id = await sender.SendAsync(receiver.GetGrainId(), Route, message);
        var attempt = await WaitForFailedAttemptAsync(sender, id, 1);

        // 重试写入不会单独调度作业：发送操作在持久化前确保的唤醒作业
        // 在发件箱持有条目期间继续运行，并将其下一次运行重新调度到重试时间。
        Assert.Single(Fixture.JobManagerProbe.GetScheduledJobs(MessagingPump.JobName, sender.GetGrainId()));
        var wakeup = await Fixture.RunPumpAsync(sender);
        Assert.Equal(DurableJobRunStatus.RescheduleRequested, wakeup.Status);
        Assert.Equal(attempt.NextAttemptAt, wakeup.RescheduleTime);

        // 崩溃会丢失激活实例；恢复时根据日志中的重试时间重新安排唤醒。
        await _cluster.CrashAsync(sender);
        _ = await sender.GetSnapshotAsync();
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);

        await WaitForDrainedOutboxAsync(sender);
        await AssertProcessedOnceAsync(receiver, message);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
    }

    [Fact]
    public async Task FailedAttemptRetry_DuplicateWakeupWaitsForTheRetryWrite()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        _ = await sender.GetSnapshotAsync();

        // 发送是发送方的第一次写入，失败尝试的重试记录是第二次写入。
        using var retryWrite = Fixture.Storage.BlockWrite(JournalId.FromGrainId(sender.GetGrainId()), matchingWrite: 2);
        Deliveries.Enqueue(DeliveryFault.Throw());
        var message = NewMessage(24, "retry-write");
        await sender.SendAsync(receiver.GetGrainId(), Route, message);
        await retryWrite.WaitUntilEnteredAsync();

        // 每次唤醒都向消息泵唯一的处理循环发出信号。该循环正在等待重试写入，因此
        // 重试时间之后的重复唤醒不会根据暂存的重试状态启动尝试。
        _cluster.JobClock.Advance(RetryInterval);
        var duplicate = Fixture.RunDuplicatePumpAsync(sender);
        _ = await Fixture.OnGrainAsync(sender, static () => true);
        Assert.False(duplicate.IsCompleted);
        Assert.Equal(1, Deliveries.Executions);

        // 重试记录持久化后，重复唤醒所触发的处理轮次投递消息。
        retryWrite.Release();
        await duplicate;
        await WaitForDrainedOutboxAsync(sender);
        await AssertProcessedOnceAsync(receiver, message);
        Assert.Equal(2, Deliveries.Executions);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
    }

    [Fact]
    public async Task LostResponse_AfterTheReceiverCommitted_IsADuplicateAtTheReceiverAndDeliveredAtTheSender()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var lost = DeliveryFault.LoseResponse();
        Deliveries.Enqueue(lost);
        var message = NewMessage(2, "lost-response");

        var id = await sender.SendAsync(receiver.GetGrainId(), Route, message);

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await lost.ReceiverAnswer);
        Assert.Contains("Injected loss of the receiver's response.", (await WaitForFailedAttemptAsync(sender, id, 1)).LastError);
        await Fixture.WaitForEffectCountAsync(receiver, 1);

        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);

        await WaitForDrainedOutboxAsync(sender);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance, DeliveryResult.Duplicate.Instance], Deliveries.Answers);
        await AssertProcessedOnceAsync(receiver, message);
        Assert.Empty((await sender.GetSnapshotAsync()).OutboxDeadLetters);
    }

    [Fact]
    public async Task LostResponse_AfterTheSinkAccepted_RetiresTheSourceMessageAsDelivered()
    {
        var sender = NewSinkGrain();
        var receiver = NewSinkGrain();
        var lost = DeliveryFault.LoseResponse();
        Deliveries.Enqueue(lost);
        var id = NewSourceMessageId("lost-response");
        var payload = RandomNumberGenerator.GetBytes(32);

        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, payload, write: true);

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await lost.ReceiverAnswer);
        await WaitForFailedAttemptAsync(sender, id, 1);
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);

        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetState().Retired.Count == 1);
        Assert.Equal(nameof(OutboxMessageRetirement.Delivered), (await OnSinkAsync(sender, static grain => grain.GetState())).Retired[id.ToString()]);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance, DeliveryResult.Duplicate.Instance], Deliveries.Answers);
        Assert.Equal(payload, (await OnSinkAsync(receiver, static grain => grain.GetState())).Received[id.ToString()]);

        // 接收方通过去重机制答复重试，而未再次将消息提供给消息接收器。
        var probe = _cluster.SourceSinkProbe.For(receiver);
        Assert.Equal(1, probe.SinkOffers);
        Assert.Equal(1, probe.SinkAcceptances);
    }

    [Theory]
    [InlineData(ReceiverRecovery.None)]
    [InlineData(ReceiverRecovery.Journal)]
    [InlineData(ReceiverRecovery.Snapshot)]
    public async Task LostResponse_AfterTheSinkRejected_RetiresTheSourceMessageAsRejected(ReceiverRecovery recovery)
    {
        var sender = NewSinkGrain();
        var receiver = NewSinkGrain();
        var probe = _cluster.SourceSinkProbe.For(receiver);
        probe.Mode = SinkMode.DeadLetter;

        // 拒绝记录的写入是接收方的第一次写入，因此所请求的快照包含其已处理记录。
        if (recovery == ReceiverRecovery.Snapshot) Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(receiver.GetGrainId()));
        var lost = DeliveryFault.LoseResponse();
        Deliveries.Enqueue(lost);
        var id = NewSourceMessageId("lost-rejection");

        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, RandomNumberGenerator.GetBytes(32), write: true);

        var rejection = await lost.ReceiverAnswer;
        DeliveryResultAssert.Equal(new DeliveryResult.DeadLettered(SourceSinkGrain.Rejection, SourceSinkGrain.RejectionCode), rejection);
        await WaitForFailedAttemptAsync(sender, id, 1);
        if (recovery != ReceiverRecovery.None)
        {
            var previous = Fixture.GetGrainContext(receiver);
            await receiver.DeactivateAsync();
            await previous.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        // 接收方根据已处理记录答复重试，不会再次将消息提供给消息接收器。
        probe.Mode = SinkMode.Accept;
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);

        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetState().Retired.Count == 1);
        var (retired, reasons, codes) = await OnSinkAsync(sender, static grain => (grain.GetState().Retired, grain.GetRetirementReasons(), grain.GetRetirementCodes()));
        Assert.Equal(nameof(OutboxMessageRetirement.Rejected), retired[id.ToString()]);
        Assert.Equal(SourceSinkGrain.Rejection, reasons[id.ToString()]);
        Assert.Equal(SourceSinkGrain.RejectionCode, codes[id.ToString()]);
        DeliveryResultAssert.Equal([rejection, rejection], Deliveries.Answers);
        Assert.Equal(1, probe.SinkOffers);
        Assert.Equal(0, probe.SinkAcceptances);
    }

    public enum ReceiverRecovery
    {
        None,
        Journal,
        Snapshot
    }

    [Fact]
    public async Task TimedOutDelivery_WhoseCallIsStillCommitting_IsDeduplicatedAgainstTheRetry()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        using var acceptance = Fixture.Storage.BlockWrite(JournalId.FromGrainId(receiver.GetGrainId()));
        var timeout = DeliveryFault.TimeOutInFlight();
        Deliveries.Enqueue(timeout);
        var message = NewMessage(3, "in-flight");

        var id = await sender.SendAsync(receiver.GetGrainId(), Route, message);

        // 当接收方的接受记录仍在写入时，发送方已放弃等待调用，随后重试。
        await acceptance.WaitUntilEnteredAsync();
        await WaitForFailedAttemptAsync(sender, id, 1);
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
        await Deliveries.WaitForReceiverCallsAsync(2);
        acceptance.Release();

        var answers = await Deliveries.WaitForAnswersAsync(2);
        Assert.Equal(2, answers.Count);
        DeliveryResultAssert.Contains(DeliveryResult.Accepted.Instance, answers);
        DeliveryResultAssert.Contains(DeliveryResult.Duplicate.Instance, answers);
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await timeout.ReceiverAnswer);
        await WaitForDrainedOutboxAsync(sender);
        await AssertProcessedOnceAsync(receiver, message);
    }

    [Fact]
    public async Task PersistentDeliveryFault_DeadLettersAtTheSenderWithTheLastErrorAfterTheRetryTimeout()
    {
        var sender = NewSinkGrain();
        var receiver = NewSinkGrain();
        Deliveries.Repeat(static () => DeliveryFault.Throw(new IOException("Injected outage of the receiver's silo.")));
        var startedAt = _cluster.JobClock.GetUtcNow();
        var id = NewSourceMessageId("persistent-fault");

        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, RandomNumberGenerator.GetBytes(32), write: true);

        await WaitForFailedAttemptAsync(sender, id, 1);
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
        var second = await WaitForFailedAttemptAsync(sender, id, 2);
        Assert.Equal(startedAt, second.SentAt);
        Assert.Equal(startedAt + RetryInterval + 2 * RetryInterval, second.NextAttemptAt);

        // 重试超时后，下一次到期的尝试直接将消息转为死信，不再尝试投递。
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryTimeout);

        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetState().Retired.Count == 1);
        var (retired, reasons, deadLetter) = await Fixture.OnGrainAsync(sender, () => (
            Sink(sender).GetState().Retired,
            Sink(sender).GetRetirementReasons(),
            _cluster.GetOutboxState(sender).DeadLetter(id)));
        Assert.Equal(nameof(OutboxMessageRetirement.DeadLettered), retired[id.ToString()]);
        Assert.Contains("Injected outage of the receiver's silo.", reasons[id.ToString()]);
        Assert.Equal(reasons[id.ToString()], deadLetter.Reason);
        Assert.Equal(2, deadLetter.AttemptCount);
        Assert.Equal(2, Deliveries.Executions);
        Assert.Equal(0, Deliveries.ReceiverCalls);
        Assert.Equal(0, _cluster.SourceSinkProbe.For(receiver).SinkOffers);
    }

    [Fact]
    public async Task SinkBackpressure_ThroughTheOutbox_ExpiresAtTheOriginalAbsoluteDeadline()
    {
        var sender = NewSinkGrain();
        var receiver = NewSinkGrain();
        var probe = _cluster.SourceSinkProbe.For(receiver);
        probe.Mode = SinkMode.Backpressure;
        var id = NewSourceMessageId("backpressure");
        var payload = RandomNumberGenerator.GetBytes(32);

        var sentAt = _cluster.JobClock.GetUtcNow();
        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, payload, write: true);

        var first = await WaitForFailedAttemptAsync(sender, id, 1);
        Assert.Equal(sentAt, first.SentAt);
        Assert.Equal("The receiver is backpressured.", first.LastError);

        // 背压绝不会重置逻辑发送操作的有限重试预算。
        var step = RetryTimeout * 3 / 4;
        await _cluster.AdvanceAndRunPumpAsync(sender, step);
        var second = await WaitForFailedAttemptAsync(sender, id, 2);
        Assert.Equal(sentAt, second.SentAt);
        Assert.Empty(await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).DeadLetterKeys.ToArray()));

        await _cluster.AdvanceAndRunPumpAsync(sender, RetryTimeout - step);
        Assert.Equal(sentAt + RetryTimeout, _cluster.JobClock.GetUtcNow());

        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetState().Retired.Count == 1);
        Assert.Equal(nameof(OutboxMessageRetirement.DeadLettered), (await OnSinkAsync(sender, static grain => grain.GetState())).Retired[id.ToString()]);
        DeliveryResultAssert.Equal([DeliveryResult.Backpressured.Instance, DeliveryResult.Backpressured.Instance], Deliveries.Answers);
        Assert.Equal(2, probe.SinkOffers);
        Assert.Equal(0, probe.SinkAcceptances);
        Assert.Empty((await OnSinkAsync(receiver, static grain => grain.GetState())).Received);
        var letter = await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).DeadLetter(id));
        Assert.Contains("retry timeout elapsed", letter.Reason, StringComparison.Ordinal);
        Assert.Empty(await OnSinkAsync(sender, static grain => grain.GetRetirementCodes()));
    }

    [Fact]
    public async Task DeliveryAttempts_KeepTheOriginalSentAtAcrossRetryAndRecovery()
    {
        var sender = NewSinkGrain();
        var receiver = NewSinkGrain();
        var probe = _cluster.SourceSinkProbe.For(receiver);
        probe.Mode = SinkMode.Backpressure;
        var id = NewSourceMessageId("redelivery-deadline");
        var sentAt = _cluster.JobClock.GetUtcNow();
        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, RandomNumberGenerator.GetBytes(32), write: true);
        await WaitForFailedAttemptAsync(sender, id, 1);
        var step = RetryInterval;
        await _cluster.AdvanceAndRunPumpAsync(sender, step);
        await WaitForFailedAttemptAsync(sender, id, 2);
        var beforeRecovery = await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).Attempt(id));
        Assert.Equal(sentAt, beforeRecovery.SentAt);
        var previous = Fixture.GetGrainContext(sender);
        Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(sender.GetGrainId()));
        await sender.WriteAsync();
        await sender.DeactivateAsync();
        await previous.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(1, await sender.GetOutboxCountAsync());
        var recovered = await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).Attempt(id));
        Assert.Equal(sentAt, recovered.SentAt);
        probe.Mode = SinkMode.Accept;
        await _cluster.AdvanceAndRunPumpAsync(sender, 2 * step);
        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetState().Retired.Count == 1);

        // 传输超时和恢复都不能改变已记入日志的逻辑发送时间。
        Assert.Equal(
            [sentAt, sentAt, sentAt],
            Deliveries.SentTimes);
        DeliveryResultAssert.Equal([DeliveryResult.Backpressured.Instance, DeliveryResult.Backpressured.Instance, DeliveryResult.Accepted.Instance], Deliveries.Answers);
        Assert.Equal(1, probe.SinkAcceptances);
    }

    [Fact]
    public async Task SourceMessage_TransportsAndRetainsItsActualStagingSentAt()
    {
        var sender = NewSinkGrain();
        var receiver = NewSinkGrain();
        var sentAt = _cluster.JobClock.GetUtcNow();
        var id = NewSourceMessageId("beyond-window");

        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, RandomNumberGenerator.GetBytes(32), write: true);

        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetState().Retired.Count == 1);
        var (retired, reasons, codes, deadLetters) = await Fixture.OnGrainAsync(sender, () => (
            Sink(sender).GetState().Retired,
            Sink(sender).GetRetirementReasons(),
            Sink(sender).GetRetirementCodes(),
            _cluster.GetOutboxState(sender).DeadLetterKeys.ToArray()));
        Assert.Equal(nameof(OutboxMessageRetirement.Delivered), retired[id.ToString()]);
        Assert.Empty(reasons);
        Assert.Empty(codes);
        Assert.Empty(deadLetters);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
        Assert.Equal([sentAt], Deliveries.SentTimes);
        var inbox = Fixture.GetGrainContext(receiver).ActivationServices.GetRequiredService<Inbox>();
        Assert.Equal(sentAt, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedSentAt(inbox, id)));
        Assert.Equal(1, _cluster.SourceSinkProbe.For(receiver).SinkOffers);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiverAcceptanceWriteFailure_IsRetriedBySenderAndProcessedOnce(bool ambiguous)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var before = await receiver.GetSnapshotAsync();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        if (ambiguous)
        {
            Fixture.Storage.FailAfterWrite(journal);
        }
        else
        {
            Fixture.Storage.FailWrite(journal);
        }

        var message = NewMessage(4, ambiguous ? "ambiguous-acceptance" : "failed-acceptance");

        var id = await sender.SendAsync(receiver.GetGrainId(), Route, message);

        // 接收方使调用失败并停用；发送方保留消息以待重试。
        Assert.Contains("Injected", (await WaitForFailedAttemptAsync(sender, id, 1)).LastError);
        Assert.Empty(Deliveries.Answers);
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);

        await WaitForDrainedOutboxAsync(sender);
        // 尽管调用失败，已持久化的接受记录仍会恢复，因此重试属于重复投递。
        DeliveryResultAssert.Equal([ambiguous ? DeliveryResult.Duplicate.Instance : DeliveryResult.Accepted.Instance], Deliveries.Answers);
        var processed = await AssertProcessedOnceAsync(receiver, message);
        Assert.NotEqual(before.ActivationId, processed.ActivationId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SenderRetryStateWriteFailure_RecoversAndDeliversOnce(bool ambiguous)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var hold = DeliveryFault.HoldBeforeCall();
        Deliveries.Enqueue(hold);
        var startedAt = _cluster.JobClock.GetUtcNow();
        var message = NewMessage(5, ambiguous ? "ambiguous-retry-state" : "failed-retry-state");
        var id = await sender.SendAsync(receiver.GetGrainId(), Route, message);
        await hold.WaitUntilEnteredAsync();
        ArmSenderWriteFailure(sender, ambiguous);
        var previous = Fixture.GetGrainContext(sender);

        // 投递失败，记录该失败尝试的写入也失败。
        hold.Fail(new IOException("Injected delivery failure."));
        await previous.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        _ = await sender.GetSnapshotAsync();

        if (ambiguous)
        {
            // 尝试记录已持久化，因此恢复后的消息等待其退避期结束。
            var recovered = await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).Attempt(id));
            Assert.Equal(1, recovered.AttemptCount);
            Assert.Equal(startedAt + RetryInterval, recovered.NextAttemptAt);
            await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
        }

        await WaitForDrainedOutboxAsync(sender);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
        Assert.Equal(1, Deliveries.ReceiverCalls);
        await AssertProcessedOnceAsync(receiver, message);
        Assert.Empty((await sender.GetSnapshotAsync()).OutboxDeadLetters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SenderRemovalWriteFailure_AfterTheReceiverAccepted_DeliversOnceInEffect(bool ambiguous)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var hold = DeliveryFault.HoldAfterCall();
        Deliveries.Enqueue(hold);
        var message = NewMessage(6, ambiguous ? "ambiguous-removal" : "failed-removal");
        _ = await sender.SendAsync(receiver.GetGrainId(), Route, message);
        await hold.WaitUntilEnteredAsync();
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await hold.ReceiverAnswer);
        ArmSenderWriteFailure(sender, ambiguous);
        var previous = Fixture.GetGrainContext(sender);

        hold.Release();
        await previous.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        _ = await sender.GetSnapshotAsync();

        await WaitForDrainedOutboxAsync(sender);
        // 若移除操作从未持久化，消息会重新投递，接收方报告重复投递。
        DeliveryResult[] expected = ambiguous ? [DeliveryResult.Accepted.Instance] : [DeliveryResult.Accepted.Instance, DeliveryResult.Duplicate.Instance];
        DeliveryResultAssert.Equal(expected, Deliveries.Answers);
        await AssertProcessedOnceAsync(receiver, message);
        Assert.Empty((await sender.GetSnapshotAsync()).OutboxDeadLetters);
    }

    [Fact]
    public async Task SenderRemovalWriteFailure_RecoveredAfterTheRetryTimeout_DeadLettersWithoutRedelivery()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var hold = DeliveryFault.HoldAfterCall();
        Deliveries.Enqueue(hold);
        var message = NewMessage(7, "late-recovery");
        _ = await sender.SendAsync(receiver.GetGrainId(), Route, message);
        await hold.WaitUntilEnteredAsync();
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await hold.ReceiverAnswer);
        ArmSenderWriteFailure(sender, ambiguous: false);
        var previous = Fixture.GetGrainContext(sender);
        hold.Release();
        await previous.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // 恢复后条目的重试超时自发送时刻开始计算，因此此时再尝试可能在接收方的
        // 去重窗口结束之后才抵达。发件箱不再投递，而是将消息转为死信。
        _cluster.JobClock.Advance(RetryTimeout);
        var snapshot = await Fixture.WaitForDeadLetterCountAsync(sender, 1);

        var deadLetter = Assert.Single(snapshot.OutboxDeadLetters);
        Assert.Contains("retry timeout elapsed", deadLetter.Reason, StringComparison.Ordinal);
        Assert.Equal(0, snapshot.OutboxCount);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
        await AssertProcessedOnceAsync(receiver, message);
    }

    [Fact]
    public async Task HeldReceiver_DelaysOnlyItsOwnMessage()
    {
        var sender = NewGrain();
        var slow = NewGrain();
        var fast = NewGrain();
        var held = DeliveryFault.HoldBeforeCall();
        Deliveries.Enqueue(held);
        var slowMessage = NewMessage(20, "slow");
        var fastMessage = NewMessage(21, "fast");

        _ = await sender.SendAsync(slow.GetGrainId(), Route, slowMessage);
        await held.WaitUntilEnteredAsync();
        _ = await sender.SendAsync(fast.GetGrainId(), Route, fastMessage);

        // 各次尝试独立完成，因此第一条消息被阻塞时，发往另一接收方的消息仍可投递。
        await AssertProcessedOnceAsync(fast, fastMessage);
        Assert.Equal(1, (await sender.GetSnapshotAsync()).OutboxCount);

        held.Release();
        await WaitForDrainedOutboxAsync(sender);
        await AssertProcessedOnceAsync(slow, slowMessage);
        Assert.Equal(2, Deliveries.Executions);
    }

    [Fact]
    public async Task Withdrawal_DuringAnAttempt_IgnoresTheAttemptsOutcome()
    {
        var sender = NewSinkGrain();
        var receiver = NewSinkGrain();
        var hold = DeliveryFault.HoldAfterCall();
        Deliveries.Enqueue(hold);
        var id = NewSourceMessageId("withdrawn-in-flight");

        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, RandomNumberGenerator.GetBytes(32), write: true);
        await hold.WaitUntilEnteredAsync();
        await sender.WithdrawAsync(id);
        await sender.WriteAsync();

        // 此次尝试已经到达接收方，但撤回的条目既不会执行终结处理，也不会再次投递。
        hold.Release();
        var outbox = Fixture.GetGrainContext(sender).ActivationServices.GetRequiredService<Outbox>();
        await Fixture.WaitUntilAsync(sender, () => !outbox.IsDelivering);
        Assert.Empty((await OnSinkAsync(sender, static grain => grain.GetState())).Retired);
        Assert.Equal(0, await Fixture.OnGrainAsync(sender, () => outbox.Count));
        Assert.Empty(await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).DeadLetterKeys.ToArray()));
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
        Assert.Equal(1, Deliveries.Executions);
    }
    [Fact]
    public async Task SenderCrash_AfterTheReceiverCommitted_RedeliversAsADuplicate()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var hold = DeliveryFault.HoldAfterCall();
        Deliveries.Enqueue(hold);
        var message = NewMessage(7, "sender-crash");
        _ = await sender.SendAsync(receiver.GetGrainId(), Route, message);
        await hold.WaitUntilEnteredAsync();
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await hold.ReceiverAnswer);

        var wakeup = await Fixture.RunPumpAsync(sender);
        Assert.Equal(DurableJobRunStatus.RescheduleRequested, wakeup.Status);
        Assert.Equal(_cluster.JobClock.GetUtcNow() + TimeSpan.FromSeconds(1), wakeup.RescheduleTime);
        Assert.Equal(1, Deliveries.ReceiverCalls);

        // 停用取消正在进行的投递；其结果既不应用，也不计入尝试次数。
        await _cluster.CrashAsync(sender);
        _ = await sender.GetSnapshotAsync();

        await WaitForDrainedOutboxAsync(sender);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance, DeliveryResult.Duplicate.Instance], Deliveries.Answers);
        await AssertProcessedOnceAsync(receiver, message);
        Assert.Empty((await sender.GetSnapshotAsync()).OutboxDeadLetters);
    }

    [Fact]
    public async Task ReceiverCrash_MidBatch_RecoveryProcessesEachMessageOnce()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var deliverAt = _cluster.JobClock.GetUtcNow() + RetryTimeout;
        var first = NewMessage(8, "first");
        var held = NewMessage(9, "held");
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, Schedule(CreateEnvelope(receiver, first, "messages/first"), deliverAt)));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, Schedule(CreateEnvelope(receiver, held, HeldRoute), deliverAt)));
        var barrier = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), HeldRoute);

        // 两条消息在同一批次到期；崩溃时第一条已处理，第二条仍在处理程序中。
        _cluster.JobClock.Advance(RetryTimeout);
        var run = Fixture.RunPumpAsync(receiver);
        await WaitForBarrierAsync(receiver, barrier);
        Assert.Equal(1, Assert.Single((await receiver.GetSnapshotAsync()).Effects).Count);
        await _cluster.CrashAsync(receiver);
        barrier.Dispose();
        _ = run.ContinueWith(static task => task.Exception, TaskScheduler.Default);

        _ = await receiver.GetSnapshotAsync();
        var recovered = await Fixture.SnapshotProbe.WaitAsync(
            receiver.GetGrainId(),
            static snapshot => snapshot.Effects.Count == 2 && snapshot.InboxCount == 0);
        Assert.All(recovered.Effects, static effect => Assert.Equal(1, effect.Count));
        Assert.Equal([first.LogicalId, held.LogicalId], recovered.Effects.Select(static effect => effect.LogicalId));
        Assert.Empty(recovered.InboxDeadLetters);
    }

    [Fact]
    public async Task CanceledPumpRun_KeepsItsJobAndTheActivationKeepsProcessing()
    {
        var receiver = NewGrain();
        var before = await receiver.GetSnapshotAsync();
        var barrier = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), HeldRoute);
        var message = NewMessage(10, "canceled-run");
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, CreateEnvelope(receiver, message, HeldRoute)));
        await WaitForBarrierAsync(receiver, barrier);
        var attempts = Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, receiver.GetGrainId());

        // Durable Jobs 仅在其分片转移时取消一次运行，作业仍存储以供下一所有者执行，因此
        // 激活实例无需调度任何操作，并继续工作。
        using var cancellation = new CancellationTokenSource();
        var run = Fixture.RunCancelablePumpAsync(receiver, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(attempts, Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, receiver.GetGrainId()));

        barrier.Dispose();
        var processed = await AssertProcessedOnceAsync(receiver, message);
        Assert.Equal(before.ActivationId, processed.ActivationId);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduledDeliveryWakeupSchedulingFailure_IsRetriedBySenderAndProcessedAtDeliverAt(bool scheduledBeforeFailure)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var receiverContext = Fixture.GetGrainContext(receiver);
        var hold = DeliveryFault.HoldBeforeCall();
        Deliveries.Enqueue(hold);
        var deliverAt = _cluster.JobClock.GetUtcNow() + RetryTimeout + RetryTimeout;
        var message = NewMessage(11, "scheduled");
        var outgoing = TestApplicationProtocol.Create(Sessions, sender.GetGrainId(), receiver.GetGrainId(), Route, message);
        outgoing = outgoing with { Envelope = outgoing.Envelope with { DeliverAt = deliverAt } };
        await sender.StageOutputAsync(outgoing);
        await sender.RetryWriteStateAsync();
        await hold.WaitUntilEnteredAsync();

        // 接收方接受消息的写入过程会调度该消息的唤醒作业，但调度失败。
        if (scheduledBeforeFailure)
        {
            Fixture.JobManagerProbe.FailAfterNext(MessagingPump.JobName);
        }
        else
        {
            Fixture.JobManagerProbe.FailNext(MessagingPump.JobName);
        }

        hold.Release();
        Assert.Contains("Injected", (await WaitForFailedAttemptAsync(sender, outgoing.Envelope.MessageId, 1)).LastError);
        await receiverContext.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Empty(Deliveries.Answers);

        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
        await WaitForDrainedOutboxAsync(sender);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
        var pending = await Fixture.WaitForInboxCountAsync(receiver, 1);
        Assert.Empty(pending.Effects);

        await _cluster.AdvanceAndRunPumpAsync(receiver, RetryTimeout + RetryTimeout);
        await AssertProcessedOnceAsync(receiver, message);
    }

    [Fact]
    public async Task OutboxDepth_IsReleasedByDeactivationAndRestoredByRecovery()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        Deliveries.Enqueue(DeliveryFault.Throw());
        var message = NewMessage(12, "depth-across-activations");
        var id = await sender.SendAsync(receiver.GetGrainId(), Route, message);
        _ = await WaitForFailedAttemptAsync(sender, id, 1);
        await Fixture.WaitForOutboxDepthAsync(1);

        await _cluster.CrashAsync(sender);
        await Fixture.WaitForOutboxDepthAsync(0);

        // 恢复时再次报告挂起的消息，终结该消息时释放对应的深度计数。
        Assert.Equal(1, (await sender.GetSnapshotAsync()).OutboxCount);
        await Fixture.WaitForOutboxDepthAsync(1);
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
        await WaitForDrainedOutboxAsync(sender);
        await Fixture.WaitForOutboxDepthAsync(0);
        await AssertProcessedOnceAsync(receiver, message);
    }

    [Fact]
    public async Task OutboxDepth_AfterAFailedWrite_CountsOnlyDurableMessages()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        Deliveries.Enqueue(DeliveryFault.Throw());
        var durable = NewMessage(13, "durable");
        var id = await sender.SendAsync(receiver.GetGrainId(), Route, durable);
        _ = await WaitForFailedAttemptAsync(sender, id, 1);
        await Fixture.WaitForOutboxDepthAsync(1);
        var previous = Fixture.GetGrainContext(sender);
        ArmSenderWriteFailure(sender, ambiguous: false);

        // 写入失败会隔离激活实例，从而释放两条消息对应的深度计数。
        await Assert.ThrowsAnyAsync<Exception>(() => sender.SendAsync(receiver.GetGrainId(), Route, NewMessage(14, "lost")));
        await previous.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await Fixture.WaitForOutboxDepthAsync(0);

        Assert.Equal(1, (await sender.GetSnapshotAsync()).OutboxCount);
        await Fixture.WaitForOutboxDepthAsync(1);
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
        await WaitForDrainedOutboxAsync(sender);
        await Fixture.WaitForOutboxDepthAsync(0);
        await AssertProcessedOnceAsync(receiver, durable);
    }

    [Fact]
    public async Task EquivalentDuplicate_OfACommittedMessage_LeavesItsDeliveryUnchanged()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        Deliveries.Enqueue(DeliveryFault.Throw());
        var message = NewMessage(15, "duplicate-after-commit");
        var outgoing = TestApplicationProtocol.Create(Sessions, sender.GetGrainId(), receiver.GetGrainId(), Route, message);
        var id = outgoing.Envelope.MessageId;
        await sender.StageOutputAsync(outgoing);
        await sender.RetryWriteStateAsync();
        var attempt = await WaitForFailedAttemptAsync(sender, id, 1);
        var before = await sender.GetSnapshotAsync();

        // Grain 调用投递的是信封的副本，与已提交的信封等价。
        await sender.StageOutputAsync(outgoing);
        await sender.RetryWriteStateAsync();

        Assert.Single(Fixture.GetStagedOutput(sender));
        var unchanged = await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).Attempt(id));
        Assert.Equal(attempt.AttemptCount, unchanged.AttemptCount);
        Assert.Equal(attempt.NextAttemptAt, unchanged.NextAttemptAt);
        Assert.Equal(attempt.SentAt, unchanged.SentAt);
        Assert.Equal(before.ActivationId, (await sender.GetSnapshotAsync()).ActivationId);

        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
        await WaitForDrainedOutboxAsync(sender);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
        Assert.Equal(1, Deliveries.ReceiverCalls);
        await AssertProcessedOnceAsync(receiver, message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictingDuplicate_IsRejectedWithoutChangingThePendingMessage(bool commitFirst)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        Deliveries.Enqueue(DeliveryFault.Throw());
        var message = NewMessage(16, "original");
        var outgoing = TestApplicationProtocol.Create(Sessions, sender.GetGrainId(), receiver.GetGrainId(), Route, message);
        var id = outgoing.Envelope.MessageId;
        var conflicting = TestApplicationProtocol.Create(
            Sessions, sender.GetGrainId(), receiver.GetGrainId(), Route, message with { Value = "conflicting" });
        Assert.Equal(id, conflicting.Envelope.MessageId);
        await sender.StageOutputAsync(outgoing);
        if (commitFirst)
        {
            await sender.RetryWriteStateAsync();
            _ = await WaitForFailedAttemptAsync(sender, id, 1);
        }

        var before = await sender.GetSnapshotAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.StageOutputAsync(conflicting));
        Assert.Contains(id.ToString(), error.Message);
        Assert.Equal(outgoing.Envelope.Payload, Assert.Single(Fixture.GetStagedOutput(sender)).Envelope.Payload);
        if (!commitFirst)
        {
            await sender.RetryWriteStateAsync();
            _ = await WaitForFailedAttemptAsync(sender, id, 1);
        }

        Assert.Equal(before.ActivationId, (await sender.GetSnapshotAsync()).ActivationId);
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
        await WaitForDrainedOutboxAsync(sender);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance], Deliveries.Answers);
        var processed = await AssertProcessedOnceAsync(receiver, message);
        Assert.Equal(message.Value, Assert.Single(processed.Effects).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdmissionFloor_ForwardThenBackwardClockRemainsFencedAcrossRecovery(bool snapshot)
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var originalTime = _cluster.JobClock.GetUtcNow();
        _cluster.JobClock.Advance(TimeSpan.FromDays(2));
        var floor = _cluster.JobClock.GetUtcNow() - TimeSpan.FromDays(1);
        var expired = CreateEnvelope(receiver, NewMessage(80, "floor-equality")) with { SentAt = floor };
        if (snapshot) Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(receiver.GetGrainId()));
        var result = await DeliverAsync(receiver, expired);
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(result);
        Assert.Empty((await receiver.GetSnapshotAsync()).Effects);

        _cluster.JobClock.Advance(originalTime - _cluster.JobClock.GetUtcNow());
        await ReactivateAsync(receiver);
        var inbox = Fixture.GetInbox(receiver);
        Assert.Equal(floor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.AdmissionFloor(inbox)));
        Assert.Equal(floor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));
        foreach (var delta in new[] { -1L, 0L })
        {
            var expiredMessage = CreateEnvelope(receiver, NewMessage(81, "expired-by-floor")) with { SentAt = floor.AddTicks(delta) };
            DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, expiredMessage));
        }

        var context = Fixture.GetGrainContext(receiver);
        var receiverClock = context.ActivationServices.GetRequiredKeyedService<TimeProvider>(DurableJobTimeProviderNames.DurableJobs);
        var receiverTime = await Fixture.OnGrainAsync(receiver, receiverClock.GetUtcNow);
        Assert.Equal(originalTime, receiverTime);
        var window = context.ActivationServices.GetRequiredService<IOptions<Configuration.DurableMessagingOptions>>().Value.DeduplicationWindow;
        var manager = context.ActivationServices.GetRequiredService<IJournaledStateManager>();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        var before = await Fixture.OnGrainAsync(receiver, () => (
            Writes: Fixture.Storage.GetSuccessfulWriteCount(journal),
            StagedBytes: manager.PendingWriteByteCount,
            Keys: InboxTestAccess.GetProcessedKeys(inbox),
            Floor: InboxTestAccess.AdmissionFloor(inbox),
            DurableFloor: InboxTestAccess.DurableAdmissionFloor(inbox)));
        foreach (var sentAt in new[] { floor.AddTicks(1), floor + window / 2 })
        {
            Assert.True(sentAt > receiverTime + window);
            var tooFuture = CreateEnvelope(receiver, NewMessage(82, "too-future-after-rollback")) with { SentAt = sentAt };
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => DeliverAsync(receiver, tooFuture));
            var after = await Fixture.OnGrainAsync(receiver, () => (
                Writes: Fixture.Storage.GetSuccessfulWriteCount(journal),
                StagedBytes: manager.PendingWriteByteCount,
                Keys: InboxTestAccess.GetProcessedKeys(inbox),
                Floor: InboxTestAccess.AdmissionFloor(inbox),
                DurableFloor: InboxTestAccess.DurableAdmissionFloor(inbox)));
            Assert.Equal(before.Writes, after.Writes);
            Assert.Equal(before.StagedBytes, after.StagedBytes);
            Assert.True(before.Keys.SetEquals(after.Keys));
            Assert.Equal(before.Floor, after.Floor);
            Assert.Equal(before.DurableFloor, after.DurableFloor);
            Assert.Equal(receiverTime, await Fixture.OnGrainAsync(receiver, receiverClock.GetUtcNow));
        }

        // 在此前时钟回拨后，使下限 + 1 的边界值保持在允许的未来时间上限内。
        _cluster.JobClock.Advance(TimeSpan.FromTicks(1));
        var available = CreateEnvelope(receiver, NewMessage(82, "above-floor")) with { SentAt = floor.AddTicks(1) };
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, available));
        await Fixture.WaitForInboxCountAsync(receiver, 0);
        var effect = Assert.Single((await receiver.GetSnapshotAsync()).Effects);
        Assert.Equal("above-floor", effect.Value);
        Assert.Equal(1, effect.Count);
        Assert.Equal(floor.AddTicks(1), await Fixture.OnGrainAsync(receiver, () =>
            InboxTestAccess.GetProcessedSentAt(inbox, available.Envelope.MessageId)));
    }

    [Fact]
    public async Task AdmissionFloor_AtMinimumClock_ClampsWithoutUnderflow()
    {
        var receiver = NewSinkGrain();
        _cluster.JobClock.Advance(DateTimeOffset.MinValue - _cluster.JobClock.GetUtcNow());
        var boundary = new InboxMessage
        {
            Envelope = new() { MessageId = NewSourceMessageId("minimum"), Payload = [85], Subject = SourceSinkGrain.Subject },
            SentAt = DateTimeOffset.MinValue
        };
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await receiver.AsReference<IMessagingExtension>()
            .DeliverAsync(boundary, TestContext.Current.CancellationToken));
        var firstAvailable = boundary with
        {
            Envelope = boundary.Envelope with { MessageId = NewSourceMessageId("first-tick"), Payload = [86] },
            SentAt = DateTimeOffset.MinValue.AddTicks(1)
        };
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await receiver.AsReference<IMessagingExtension>()
            .DeliverAsync(firstAvailable, TestContext.Current.CancellationToken));
        var received = Assert.Single((await OnSinkAsync(receiver, static grain => grain.GetState())).Received);
        Assert.Equal(firstAvailable.Envelope.MessageId.ToString(), received.Key);
        Assert.Equal([86], received.Value);
        Assert.Equal(1, _cluster.SourceSinkProbe.For(receiver).SinkOffers);
    }

    [Fact]
    public async Task SnapshotFloor_OmittedEvidenceRemainsUntilTheSnapshotAcknowledgement()
    {
        var receiver = NewGrain();
        var sentAt = _cluster.JobClock.GetUtcNow();
        var message = CreateEnvelope(receiver, NewMessage(83, "snapshot-fence")) with { SentAt = sentAt };
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, message));
        await Fixture.WaitForInboxCountAsync(receiver, 0);
        var inbox = Fixture.GetInbox(receiver);
        var oldFloor = await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox));
        _cluster.JobClock.Advance(TimeSpan.FromDays(2));
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        Fixture.Storage.RequestSnapshot(journal);
        using var acknowledgement = Fixture.Storage.BlockAcknowledgement(journal);
        var writing = receiver.RetryWriteStateAsync();
        await acknowledgement.WaitUntilEnteredAsync();

        Assert.False(writing.IsCompleted);
        Assert.Contains(message.Envelope.MessageId, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox)));
        Assert.Equal(oldFloor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));
        acknowledgement.Release();
        await writing;
        Assert.DoesNotContain(message.Envelope.MessageId, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox)));
        Assert.Equal(_cluster.JobClock.GetUtcNow() - TimeSpan.FromDays(1),
            await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, message));
        await ReactivateAsync(receiver);
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, message));
        Assert.Equal(1, Assert.Single((await receiver.GetSnapshotAsync()).Effects).Count);
    }

    [Fact]
    public async Task ProcessedHistory_FloorOnlyAndOrdinaryAcksRetainItUntilAnUnchangedFloorSnapshotCommits()
    {
        var receiver = NewSinkGrain();
        var started = _cluster.JobClock.GetUtcNow();
        var finite = Enumerable.Range(0, 8).Select(index => new InboxMessage
        {
            Envelope = new() { MessageId = NewSourceMessageId($"finite-{index}"), Payload = [(byte)index], Subject = SourceSinkGrain.Subject },
            SentAt = started
        }).ToArray();
        var extension = receiver.AsReference<IMessagingExtension>();
        foreach (var message in finite)
        {
            DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await extension.DeliverAsync(message, TestContext.Current.CancellationToken));
        }
        _cluster.JobClock.Advance(TimeSpan.FromHours(12));
        var recent = Enumerable.Range(0, 8).Select(index => new InboxMessage
        {
            Envelope = new() { MessageId = NewSourceMessageId($"recent-{index}"), Payload = [(byte)(index + 8)], Subject = SourceSinkGrain.Subject },
            SentAt = _cluster.JobClock.GetUtcNow()
        }).ToArray();
        var future = new InboxMessage
        {
            Envelope = new() { MessageId = NewSourceMessageId("retained-recent"), Payload = [16], Subject = SourceSinkGrain.Subject },
            SentAt = _cluster.JobClock.GetUtcNow()
        };
        foreach (var message in recent.Append(future))
        {
            DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await extension.DeliverAsync(message, TestContext.Current.CancellationToken));
        }

        var inbox = Fixture.GetGrainContext(receiver).ActivationServices.GetRequiredService<Inbox>();
        var retained = finite.Concat(recent).Append(future).Select(static message => message.Envelope.MessageId).ToHashSet();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        var writes = Fixture.Storage.GetSuccessfulWriteCount(journal);
        _cluster.JobClock.Advance(started + TimeSpan.FromDays(1) + TimeSpan.FromHours(1) - _cluster.JobClock.GetUtcNow());
        DateTimeOffset floor = default;
        for (var index = 0; index < 32; index++)
        {
            _cluster.JobClock.Advance(TimeSpan.FromTicks(1));
            floor = _cluster.JobClock.GetUtcNow() - TimeSpan.FromDays(1);
            var stale = new InboxMessage
            {
                Envelope = new() { MessageId = NewSourceMessageId($"floor-only-{index}"), Payload = [], Subject = SourceSinkGrain.Subject },
                SentAt = floor
            };
            DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await extension.DeliverAsync(stale, TestContext.Current.CancellationToken));
            Assert.Equal(writes + index + 1, Fixture.Storage.GetSuccessfulWriteCount(journal));
            Assert.Equal(floor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));
            // 这些具有有限时间的记录已符合清理条件。它们仍存在，证明标量确认未触发压缩清理。
            Assert.True(retained.SetEquals(await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox))));
        }

        var ordinary = new InboxMessage
        {
            Envelope = new() { MessageId = NewSourceMessageId("ordinary-append"), Payload = [17], Subject = SourceSinkGrain.Subject },
            SentAt = _cluster.JobClock.GetUtcNow()
        };
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await extension.DeliverAsync(ordinary, TestContext.Current.CancellationToken));
        retained.Add(ordinary.Envelope.MessageId);
        Assert.Equal(writes + 33, Fixture.Storage.GetSuccessfulWriteCount(journal));
        Assert.True(retained.SetEquals(await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox))));

        // 下限已经持久化。即使候选下限无法推进，这次快照也必须回收历史记录。
        _cluster.JobClock.Advance(started - _cluster.JobClock.GetUtcNow());
        Fixture.Storage.RequestSnapshot(journal);
        using var acknowledgement = Fixture.Storage.BlockAcknowledgement(journal);
        var snapshot = receiver.WriteAsync();
        await acknowledgement.WaitUntilEnteredAsync();
        Assert.False(snapshot.IsCompleted);
        Assert.Equal(writes + 34, Fixture.Storage.GetSuccessfulWriteCount(journal));
        Assert.Equal(floor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.AdmissionFloor(inbox)));
        Assert.Equal(floor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));
        Assert.True(retained.SetEquals(await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox))));
        acknowledgement.Release();
        await snapshot;

        retained.ExceptWith(finite.Select(static message => message.Envelope.MessageId));
        Assert.True(retained.SetEquals(await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox))));
        Assert.Equal(floor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));
        await _cluster.CrashAsync(receiver);
        Assert.Equal(0, await receiver.GetOutboxCountAsync());
        inbox = Fixture.GetGrainContext(receiver).ActivationServices.GetRequiredService<Inbox>();
        Assert.Equal(floor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));
        Assert.True(retained.SetEquals(await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox))));
        foreach (var message in finite)
        {
            DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await extension.DeliverAsync(message, TestContext.Current.CancellationToken));
        }
        // 时钟回拨后，不要使后续提交的保留记录超出接收方新的未来时间上限。
        _cluster.JobClock.Advance(ordinary.SentAt - _cluster.JobClock.GetUtcNow());
        foreach (var message in recent.Append(future).Append(ordinary))
        {
            DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await extension.DeliverAsync(message, TestContext.Current.CancellationToken));
        }
        Assert.Equal(18, _cluster.SourceSinkProbe.For(receiver).SinkOffers);
        Assert.Equal(18, _cluster.SourceSinkProbe.For(receiver).SinkAcceptances);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostResponse_AfterCompactionAndClockRollback_CannotApplyTheMessageTwice(bool realSink)
    {
        var sender = NewSinkGrain();
        IAddressable receiver = realSink ? NewSinkGrain() : NewGrain();
        var id = NewSourceMessageId("late-lost-response");
        var payload = realSink
            ? RandomNumberGenerator.GetBytes(32)
            : TestApplicationProtocol.Encode(Sessions, new TestApplicationMessage(Route, new TestMessage(id, 84, "late-redelivery")));
        using var events = new DeliveryEventObserver(sender.GetGrainId(), receiver.GetGrainId(), id, faults: 0);
        var retryScheduled = events.RetryScheduled.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var retired = events.Retired.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var lost = DeliveryFault.LoseResponse();
        var delayed = DeliveryFault.HoldBeforeCall();
        Deliveries.Enqueue(lost, delayed);
        var started = _cluster.JobClock.GetUtcNow();
        // 在消息接收器场景中，源提供的是原始有效载荷；在处理程序场景中，则是真正的应用程序信封。
        if (realSink)
        {
            await sender.StageFromSourceAsync(receiver.GetGrainId(), id, payload, write: true);
        }
        else
        {
            _ = await sender.GetOutboxCountAsync();
            // 使用普通发件箱信封，以保留应用程序的路由。
            await Fixture.OnGrainAsync(sender, () => Fixture.GetGrainContext(sender).ActivationServices.GetRequiredService<IOutbox>().Send(new OutboxMessage
            {
                ReceiverId = receiver.GetGrainId(),
                Envelope = new() { MessageId = id, Payload = payload, Subject = Route }
            }));
            await sender.WriteAsync();
        }

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await lost.ReceiverAnswer);
        Assert.Equal(id, (await retryScheduled).MessageId);
        if (!realSink) await Fixture.WaitForInboxCountAsync((IMessagingTestGrain)receiver, 0);
        await sender.WriteAsync();
        // 在原始预算内启动重试，再延迟其抵达时间，使之晚于接收方的压缩清理。
        // 否则，当共享时钟快进两天时，挂起的重试可能正常过期。
        await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
        await delayed.WaitUntilEnteredAsync();
        Assert.Equal(1, Deliveries.ReceiverCalls);
        _cluster.JobClock.Advance(TimeSpan.FromDays(2));
        await Fixture.RunPumpAsync(sender);
        var pending = await Fixture.OnGrainAsync(sender, () =>
        {
            Assert.True(Fixture.GetGrainContext(sender).ActivationServices.GetRequiredService<Outbox>().IsDelivering);
            return _cluster.GetOutboxState(sender).Attempt(id);
        });
        Assert.Equal(1, pending.AttemptCount);
        Assert.Equal(started, pending.SentAt);
        Assert.False(retired.IsCompleted);
        Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(receiver.GetGrainId()));
        await WriteJournalAsync(receiver);
        var inbox = Fixture.GetGrainContext(receiver).ActivationServices.GetRequiredService<Inbox>();
        Assert.DoesNotContain(id, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox)));
        await _cluster.CrashAsync(receiver);
        _cluster.JobClock.Advance(started - _cluster.JobClock.GetUtcNow());

        var replay = new InboxMessage
        {
            Envelope = new() { MessageId = id, Payload = payload, Subject = realSink ? SourceSinkGrain.Subject : Route },
            SentAt = started
        };
        var expired = await receiver.AsReference<IMessagingExtension>().DeliverAsync(replay, TestContext.Current.CancellationToken);
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(expired);
        if (realSink)
        {
            Assert.Equal(payload, (await OnSinkAsync(receiver, static grain => grain.GetState())).Received[id.ToString()]);
            Assert.Equal(1, _cluster.SourceSinkProbe.For(receiver).SinkOffers);
            Assert.Equal(1, _cluster.SourceSinkProbe.For(receiver).SinkAcceptances);
        }
        else
        {
            var effect = Assert.Single((await ((IMessagingTestGrain)receiver).GetSnapshotAsync()).Effects);
            Assert.Equal("late-redelivery", effect.Value);
            Assert.Equal(1, effect.Count);
        }

        // 观察这次延迟尝试的答复及终结处理，而非先前超时导致的空发件箱。
        // 此前的接受确实发生了，但源必须收到 UNKNOWN，而不是错误的拒绝结果。
        var answers = Deliveries.WaitForAnswersAsync(2);
        delayed.Release();
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await delayed.ReceiverAnswer);
        DeliveryResultAssert.Equal([DeliveryResult.Accepted.Instance, DeliveryResult.Expired.Instance],
            await answers.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.IsType<OutboxMessageRetirement.DeadLettered>((await retired).Retirement.Value);
        await sender.WriteAsync();
        Assert.Empty(await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).AttemptKeys.ToArray()));
        var terminal = await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).DeadLetter(id));
        Assert.Equal(id, terminal.Message.Envelope.MessageId);
        if (realSink)
        {
            Assert.Equal(nameof(OutboxMessageRetirement.DeadLettered),
                (await OnSinkAsync(sender, static grain => grain.GetState())).Retired[id.ToString()]);
            Assert.Empty(await OnSinkAsync(sender, static grain => grain.GetRetirementCodes()));
            Assert.Equal(1, _cluster.SourceSinkProbe.For(receiver).SinkAcceptances);
            Assert.Equal(1, _cluster.SourceSinkProbe.For(receiver).SinkOffers);
        }
        else
        {
            Assert.Equal(1, Assert.Single((await ((IMessagingTestGrain)receiver).GetSnapshotAsync()).Effects).Count);
        }
    }

    [Fact]
    public async Task SnapshotFloor_CompletionAfterCaptureRemainsUntilItsOwnWriteCommits()
    {
        var receiver = NewGrain();
        const string route = "messages/snapshot-interleaved-completion";
        using var handler = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), route);
        var message = CreateEnvelope(receiver, NewMessage(87, "interleaved"), route) with
        {
            SentAt = _cluster.JobClock.GetUtcNow()
        };
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, message));
        await handler.WaitUntilEnteredAsync();
        _cluster.JobClock.Advance(TimeSpan.FromDays(2));
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        Fixture.Storage.RequestSnapshot(journal);
        using var snapshotAcknowledgement = Fixture.Storage.BlockAcknowledgement(journal);
        var snapshot = receiver.RetryWriteStateAsync();
        await snapshotAcknowledgement.WaitUntilEnteredAsync();
        using var completionWrite = Fixture.Storage.BlockWrite(journal);
        handler.Release();
        var inbox = Fixture.GetInbox(receiver);
        await Fixture.WaitUntilAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox).Contains(message.Envelope.MessageId));
        snapshotAcknowledgement.Release();
        await snapshot;
        await completionWrite.WaitUntilEnteredAsync();
        Assert.Contains(message.Envelope.MessageId, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox)));
        var duplicate = await Fixture.OnGrainAsync(receiver,
            () => inbox.DeliverAsync(message, TestContext.Current.CancellationToken).AsTask());
        Assert.False(duplicate.IsCompleted);
        completionWrite.Release();
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await duplicate);
        await ReactivateAsync(receiver);
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, message));
        Assert.Equal("interleaved", Assert.Single((await receiver.GetSnapshotAsync()).Effects).Value);
        Assert.Equal(1, Assert.Single((await receiver.GetSnapshotAsync()).Effects).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdmissionFloor_ExpiredAnswerWaitsForDurabilityAndRecoversAfterWriteFailure(bool ambiguous)
    {
        var receiver = NewSinkGrain();
        _ = await receiver.GetOutboxCountAsync();
        var previous = Fixture.GetGrainContext(receiver);
        _cluster.JobClock.Advance(TimeSpan.FromDays(2));
        var floor = _cluster.JobClock.GetUtcNow() - TimeSpan.FromDays(1);
        var stale = new InboxMessage
        {
            Envelope = new() { MessageId = NewSourceMessageId("floor-write"), Payload = [88], Subject = SourceSinkGrain.Subject },
            SentAt = floor
        };
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        using var write = ambiguous ? Fixture.Storage.BlockAcknowledgement(journal) : Fixture.Storage.BlockWrite(journal);
        var delivery = receiver.AsReference<IMessagingExtension>().DeliverAsync(stale, TestContext.Current.CancellationToken).AsTask();
        await write.WaitUntilEnteredAsync();
        Assert.False(delivery.IsCompleted);
        Assert.Equal(0, _cluster.SourceSinkProbe.For(receiver).SinkOffers);
        write.Fail();
        await Assert.ThrowsAsync<IOException>(() => delivery);
        await previous.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(0, await receiver.GetOutboxCountAsync());
        var inbox = Fixture.GetGrainContext(receiver).ActivationServices.GetRequiredService<Inbox>();
        Assert.Equal(ambiguous ? floor : DateTimeOffset.MinValue,
            await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await receiver.AsReference<IMessagingExtension>()
            .DeliverAsync(stale, TestContext.Current.CancellationToken));
        Assert.Equal(floor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));
        Assert.Empty((await OnSinkAsync(receiver, static grain => grain.GetState())).Received);
        Assert.Equal(0, _cluster.SourceSinkProbe.For(receiver).SinkOffers);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RealOutbox_TimestampOnlyResendDuplicatesOnItsFirstAttemptAndRetiresDelivered(
        bool completed, bool incomingLater)
    {
        var sender = NewSinkGrain();
        var receiver = NewGrain();
        var started = _cluster.JobClock.GetUtcNow();
        const string route = "messages/real-outbox-timestamp-only";
        using var handler = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), route);
        if (!incomingLater) _cluster.JobClock.Advance(TimeSpan.FromHours(2));
        var original = CreateEnvelope(receiver, NewMessage(94, "one-real-outbox-effect"), route);
        var id = original.Envelope.MessageId;
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, original));
        await handler.WaitUntilEnteredAsync();
        if (completed)
        {
            handler.Release();
            await Fixture.WaitForInboxCountAsync(receiver, 0);
        }
        await receiver.RetryWriteStateAsync();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        var writes = Fixture.Storage.GetSuccessfulWriteCount(journal);
        if (incomingLater) _cluster.JobClock.Advance(TimeSpan.FromHours(2));
        else _cluster.JobClock.Advance(started - _cluster.JobClock.GetUtcNow());
        var incomingSentAt = _cluster.JobClock.GetUtcNow();
        Assert.True(incomingLater ? incomingSentAt > original.SentAt : incomingSentAt < original.SentAt);
        _cluster.SourceSinkProbe.For(sender).SubjectOverride = route;
        using var events = new DeliveryEventObserver(sender.GetGrainId(), receiver.GetGrainId(), id, faults: 0);
        var retired = events.Retired.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var held = DeliveryFault.HoldAfterCall();
        var firstAnswer = held.ReceiverAnswer;
        Deliveries.Enqueue(held);
        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, original.Envelope.Payload, write: true);
        await held.WaitUntilEnteredAsync();
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await firstAnswer);
        Assert.Equal(1, Deliveries.Executions);
        Assert.Equal(1, Deliveries.ReceiverCalls);
        DeliveryResultAssert.Equal([DeliveryResult.Duplicate.Instance], Deliveries.Answers);
        Assert.Equal([incomingSentAt], Deliveries.SentTimes);
        Assert.Empty(events.Retries);
        Assert.Equal(0, (await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).Attempt(id))).AttemptCount);

        // 接收方根据记录答复，该记录保留其接受的发送时间，且不进行任何写入。
        Assert.Equal(writes, Fixture.Storage.GetSuccessfulWriteCount(journal));
        var inbox = Fixture.GetInbox(receiver);
        Assert.Equal(original.SentAt, await Fixture.OnGrainAsync(receiver, () => completed
            ? InboxTestAccess.GetProcessedSentAt(inbox, id)
            : InboxTestAccess.GetPendingMessages(inbox)[id].Message.SentAt));
        held.Release();
        Assert.IsType<OutboxMessageRetirement.Delivered>((await retired).Retirement.Value);
        await sender.WriteAsync();
        var state = await OnSinkAsync(sender, static grain => grain.GetState());
        Assert.Empty(state.Outgoing);
        Assert.Equal(nameof(OutboxMessageRetirement.Delivered), Assert.Single(state.Retired).Value);
        Assert.Empty(await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).AttemptKeys.ToArray()));
        Assert.Empty(await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).DeadLetterKeys.ToArray()));
        Assert.Empty(events.Retries);

        handler.Release();
        await Fixture.WaitForInboxCountAsync(receiver, 0);
        Assert.Equal(original.SentAt, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedSentAt(inbox, id)));
        // 无论重新发送的时间为何，都将此记录保留至其接受的发送时间加 DeduplicationWindow。
        _cluster.JobClock.Advance(original.SentAt + TimeSpan.FromDays(1) - TimeSpan.FromTicks(1) - _cluster.JobClock.GetUtcNow());
        Fixture.Storage.RequestSnapshot(journal);
        await receiver.RetryWriteStateAsync();
        await ReactivateAsync(receiver);
        inbox = Fixture.GetInbox(receiver);
        Assert.Equal(original.SentAt, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedSentAt(inbox, id)));
        var effect = Assert.Single((await receiver.GetSnapshotAsync()).Effects);
        Assert.Equal("one-real-outbox-effect", effect.Value);
        Assert.Equal(1, effect.Count);
        Assert.Equal(1, Deliveries.Executions);
        Assert.Equal(1, Deliveries.ReceiverCalls);
        Assert.Empty(events.Retries);
    }

    public enum RetirementFault { CallbackStaging, BeforeCommit, LostCommitAcknowledgement }
    public enum TerminalOutcome { Delivered, Rejected, Expired }

    [Theory]
    [InlineData(RetirementFault.CallbackStaging, TerminalOutcome.Delivered, false)]
    [InlineData(RetirementFault.CallbackStaging, TerminalOutcome.Delivered, true)]
    [InlineData(RetirementFault.CallbackStaging, TerminalOutcome.Rejected, false)]
    [InlineData(RetirementFault.CallbackStaging, TerminalOutcome.Rejected, true)]
    [InlineData(RetirementFault.CallbackStaging, TerminalOutcome.Expired, false)]
    [InlineData(RetirementFault.CallbackStaging, TerminalOutcome.Expired, true)]
    [InlineData(RetirementFault.BeforeCommit, TerminalOutcome.Delivered, false)]
    [InlineData(RetirementFault.BeforeCommit, TerminalOutcome.Delivered, true)]
    [InlineData(RetirementFault.BeforeCommit, TerminalOutcome.Rejected, false)]
    [InlineData(RetirementFault.BeforeCommit, TerminalOutcome.Rejected, true)]
    [InlineData(RetirementFault.BeforeCommit, TerminalOutcome.Expired, false)]
    [InlineData(RetirementFault.BeforeCommit, TerminalOutcome.Expired, true)]
    [InlineData(RetirementFault.LostCommitAcknowledgement, TerminalOutcome.Delivered, false)]
    [InlineData(RetirementFault.LostCommitAcknowledgement, TerminalOutcome.Delivered, true)]
    [InlineData(RetirementFault.LostCommitAcknowledgement, TerminalOutcome.Rejected, false)]
    [InlineData(RetirementFault.LostCommitAcknowledgement, TerminalOutcome.Rejected, true)]
    [InlineData(RetirementFault.LostCommitAcknowledgement, TerminalOutcome.Expired, false)]
    [InlineData(RetirementFault.LostCommitAcknowledgement, TerminalOutcome.Expired, true)]
    public async Task SourceRetirement_RecoveryCommitsOneOutcomeAndExecutesOneSelfInboxFollowUp(
        RetirementFault fault, TerminalOutcome outcome, bool snapshot)
    {
        var sender = NewSinkGrain();
        var receiver = NewSinkGrain();
        _ = await sender.GetOutboxCountAsync();
        _ = await receiver.GetOutboxCountAsync();
        var started = _cluster.JobClock.GetUtcNow();
        var sourceProbe = _cluster.SourceSinkProbe.For(sender);
        sourceProbe.FollowUpAt = started + TimeSpan.FromHours(2);
        if (outcome == TerminalOutcome.Rejected) _cluster.SourceSinkProbe.For(receiver).Mode = SinkMode.DeadLetter;
        if (outcome == TerminalOutcome.Expired)
        {
            // 接收方已持久化地将准入下限推进到此发送时间之后，随后其时钟回拨。
            _cluster.JobClock.Advance(TimeSpan.FromDays(2));
            var stale = new InboxMessage
            {
                Envelope = new() { MessageId = NewSourceMessageId("advance-floor"), Payload = [], Subject = SourceSinkGrain.Subject },
                SentAt = _cluster.JobClock.GetUtcNow() - TimeSpan.FromDays(1)
            };
            DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await receiver.AsReference<IMessagingExtension>().DeliverAsync(stale, TestContext.Current.CancellationToken));
            _cluster.JobClock.Advance(started - _cluster.JobClock.GetUtcNow());
        }

        var held = DeliveryFault.HoldAfterCall();
        Deliveries.Enqueue(held);
        var id = NewSourceMessageId("atomic-retirement");
        var payload = RandomNumberGenerator.GetBytes(32);
        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, payload, write: true);
        await held.WaitUntilEnteredAsync();
        var answer = await held.ReceiverAnswer;
        Assert.IsType(outcome switch
        {
            TerminalOutcome.Delivered => typeof(DeliveryResult.Accepted),
            TerminalOutcome.Rejected => typeof(DeliveryResult.DeadLettered),
            _ => typeof(DeliveryResult.Expired)
        }, answer.Value);
        var journal = JournalId.FromGrainId(sender.GetGrainId());
        var oldContext = Fixture.GetGrainContext(sender);
        if (snapshot) Fixture.Storage.RequestSnapshot(journal);
        switch (fault)
        {
            case RetirementFault.CallbackStaging: sourceProbe.FailNextRetirements(1); break;
            case RetirementFault.BeforeCommit: Fixture.Storage.FailWrite(journal); break;
            case RetirementFault.LostCommitAcknowledgement: Fixture.Storage.FailAfterWrite(journal); break;
        }

        held.Release();
        await oldContext.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        _ = await sender.GetOutboxCountAsync();
        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetTerminalCounts().ContainsKey(id.ToString())
            && !_cluster.GetOutboxState(sender).AttemptKeys.Any());
        await sender.WriteAsync();
        var expectedRetirement = outcome switch
        {
            TerminalOutcome.Delivered => nameof(OutboxMessageRetirement.Delivered),
            TerminalOutcome.Rejected => nameof(OutboxMessageRetirement.Rejected),
            _ => nameof(OutboxMessageRetirement.DeadLettered)
        };
        var state = await OnSinkAsync(sender, static grain => grain.GetState());
        Assert.Empty(state.Outgoing);
        Assert.Equal(expectedRetirement, Assert.Single(state.Retired).Value);
        Assert.Equal(new KeyValuePair<string, int>(id.ToString(), 1),
            Assert.Single(await OnSinkAsync(sender, static grain => grain.GetTerminalCounts())));
        Assert.Empty(await OnSinkAsync(sender, static grain => grain.GetFollowUpCounts()));
        var pending = await Fixture.OnGrainAsync(sender, () =>
            InboxTestAccess.GetPendingMessages(Fixture.GetGrainContext(sender).ActivationServices.GetRequiredService<Inbox>()).Values.ToArray());
        var work = Assert.Single(pending);
        Assert.Equal(SourceSinkGrain.FollowUpSubject, work.Message.Envelope.Subject);
        Assert.Equal(id.ToString(), System.Text.Encoding.UTF8.GetString(work.Message.Envelope.Payload));
        Assert.Equal(sourceProbe.FollowUpAt, work.Message.Envelope.DeliverAt);
        Assert.Equal(fault == RetirementFault.LostCommitAcknowledgement ? 1 : 2, sourceProbe.RetirementCallbacks);
        var codes = await OnSinkAsync(sender, static grain => grain.GetRetirementCodes());
        if (outcome == TerminalOutcome.Rejected) Assert.Equal(SourceSinkGrain.RejectionCode, codes[id.ToString()]);
        else Assert.Empty(codes);
        if (outcome == TerminalOutcome.Expired)
        {
            Assert.Equal(0, _cluster.SourceSinkProbe.For(receiver).SinkOffers);
            var letter = await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).DeadLetter(id));
            Assert.Equal(id, letter.Message.Envelope.MessageId);
        }
        else
        {
            Assert.Equal(1, _cluster.SourceSinkProbe.For(receiver).SinkOffers);
            Assert.Equal(outcome == TerminalOutcome.Delivered ? 1 : 0, _cluster.SourceSinkProbe.For(receiver).SinkAcceptances);
        }

        // 在仍有持久化挂起工作时再次崩溃，然后通过恢复后的实际收件箱消息泵执行该工作。
        await _cluster.CrashAsync(sender);
        Assert.Equal(0, await sender.GetOutboxCountAsync());
        await _cluster.AdvanceAndRunPumpAsync(sender, sourceProbe.FollowUpAt!.Value - _cluster.JobClock.GetUtcNow());
        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetFollowUpCounts().Count == 1);
        await _cluster.CrashAsync(sender);
        Assert.Equal(0, await sender.GetOutboxCountAsync());
        await Fixture.RunPumpAsync(sender);
        Assert.Equal(new KeyValuePair<string, int>(id.ToString(), 1),
            Assert.Single(await OnSinkAsync(sender, static grain => grain.GetFollowUpCounts())));
        Assert.Equal(1, Assert.Single(await OnSinkAsync(sender, static grain => grain.GetTerminalCounts())).Value);
        Assert.Equal(1, sourceProbe.Handled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceFailure_AtomicallyRetiresAndRecoversExecutableSelfInboxFollowUp(bool snapshot)
    {
        var sender = NewSinkGrain();
        var receiver = NewSinkGrain();
        var probe = _cluster.SourceSinkProbe.For(sender);
        probe.FollowUpAt = _cluster.JobClock.GetUtcNow() + TimeSpan.FromHours(2);
        var id = NewSourceMessageId("source-error-follow-up");
        await sender.StageFromSourceAsync(receiver.GetGrainId(), id, RandomNumberGenerator.GetBytes(32), write: false);
        probe.Faults[id] = SourceFault.Throw;
        if (snapshot) Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(sender.GetGrainId()));
        await sender.WriteAsync();
        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetTerminalCounts().Count == 1);
        if (snapshot) Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(sender.GetGrainId()));
        await sender.WriteAsync();
        await _cluster.CrashAsync(sender);
        Assert.Equal(0, await sender.GetOutboxCountAsync());
        var state = await OnSinkAsync(sender, static grain => grain.GetState());
        Assert.Empty(state.Outgoing);
        Assert.Equal(nameof(OutboxMessageRetirement.DeadLettered), state.Retired[id.ToString()]);
        Assert.Contains(SourceSinkGrain.SourceFailure,
            (await OnSinkAsync(sender, static grain => grain.GetRetirementReasons()))[id.ToString()], StringComparison.Ordinal);
        Assert.Empty(await OnSinkAsync(sender, static grain => grain.GetRetirementCodes()));
        Assert.Equal(1, Assert.Single(await OnSinkAsync(sender, static grain => grain.GetTerminalCounts())).Value);
        Assert.Empty(await OnSinkAsync(sender, static grain => grain.GetFollowUpCounts()));
        await _cluster.AdvanceAndRunPumpAsync(sender, probe.FollowUpAt.Value - _cluster.JobClock.GetUtcNow());
        await Fixture.WaitUntilAsync(sender, () => Sink(sender).GetFollowUpCounts().Count == 1);
        Assert.Equal(new KeyValuePair<string, int>(id.ToString(), 1),
            Assert.Single(await OnSinkAsync(sender, static grain => grain.GetFollowUpCounts())));
        Assert.Equal(0, Deliveries.ReceiverCalls);
    }

    private async Task WriteJournalAsync(IAddressable grain)
    {
        var write = await Fixture.OnGrainAsync(grain, () =>
            Fixture.GetGrainContext(grain).ActivationServices.GetRequiredService<IJournaledStateManager>()
                .WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        await write;
    }

    private void ArmSenderWriteFailure(IMessagingTestGrain sender, bool ambiguous)
    {
        var journal = JournalId.FromGrainId(sender.GetGrainId());
        if (ambiguous)
        {
            Fixture.Storage.FailAfterWrite(journal);
        }
        else
        {
            Fixture.Storage.FailWrite(journal);
        }
    }

    private async Task<OutboxMessageState> WaitForFailedAttemptAsync(IAddressable sender, MessageId id, int attempts)
    {
        await _cluster.WaitForFailedAttemptsAsync(sender, id, attempts);
        return await Fixture.OnGrainAsync(sender, () => _cluster.GetOutboxState(sender).Attempt(id));
    }

    private Task WaitForDrainedOutboxAsync(IMessagingTestGrain sender) =>
        Fixture.WaitUntilAsync(sender, () => Fixture.GetStagedOutput(sender).Length == 0);

    private async Task<EndpointSnapshot> AssertProcessedOnceAsync(IMessagingTestGrain receiver, TestMessage message)
    {
        var processed = await Fixture.SnapshotProbe.WaitAsync(
            receiver.GetGrainId(),
            snapshot => snapshot.Effects.Any(effect => effect.LogicalId == message.LogicalId) && snapshot.InboxCount == 0);
        var current = await receiver.GetSnapshotAsync();
        Assert.Equal(1, Assert.Single(current.Effects).Count);
        Assert.Equal(message.LogicalId, current.Effects[0].LogicalId);
        Assert.Equal(0, current.InboxCount);
        Assert.Empty(current.InboxDeadLetters);
        return processed;
    }

    private ISourceSinkGrain NewSinkGrain() => Fixture.Client.GetGrain<ISourceSinkGrain>(Guid.NewGuid());

    private SourceSinkGrain Sink(IAddressable grain) => Assert.IsType<SourceSinkGrain>(Fixture.GetGrainContext(grain).GrainInstance);

    private Task<T> OnSinkAsync<T>(IAddressable grain, Func<SourceSinkGrain, T> func) =>
        Fixture.OnGrainAsync(grain, () => func(Sink(grain)));

    private static MessageId NewSourceMessageId(string name) => MessageId.Create("fault-injection", name, Guid.NewGuid().ToString("N"));

    private static InboxMessage Schedule(InboxMessage message, DateTimeOffset deliverAt) =>
        message with { Envelope = message.Envelope with { DeliverAt = deliverAt } };
}
