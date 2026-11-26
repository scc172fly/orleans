using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

/// <summary>
/// 发往自己的 grain 邮件被调用者日记中自己收听的收件箱所接受,没有发件人条目。
/// </summary>
[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class SelfAddressedMessageTests() : MessagingBehaviorTestBase(new SelfAddressedClusterFixture())
{
    private static readonly TimeSpan Delay = TimeSpan.FromHours(1);

    private ManualJobClock JobClock => ((SelfAddressedClusterFixture)Fixture).JobClock;

    [Fact]
    public async Task SelfSend_IsAcceptedWithoutOutboxEntryInTheCallersSingleWrite()
    {
        var grain = NewGrain();
        _ = await grain.GetSnapshotAsync();
        var instance = GetInstance(grain);
        var journal = JournalId.FromGrainId(grain.GetGrainId());
        const string route = "messages/self-single-write";
        using var held = Fixture.HandlerProbe.Arm(grain.GetGrainId(), route);
        var writes = Fixture.Storage.GetSuccessfulWriteCount(journal);
        var effect = new MessageEffect(TestApplicationProtocol.NewMessageId(), 1, 0, "caller-state");

        await grain.StageEffectAsync(effect);
        await grain.StageOutputAsync(SelfMessage(grain, NewMessage(1, "self"), route));
        var staged = await grain.GetSnapshotAsync();
        Assert.Equal(1, staged.InboxCount);
        Assert.Equal(0, staged.OutboxCount);
        Assert.Equal(writes, Fixture.Storage.GetSuccessfulWriteCount(journal));

        instance.ClearCaptures();
        await grain.RetryWriteStateAsync();
        var written = Assert.Single(instance.Captures);
        Assert.Equal(1, written.InboxCount);
        Assert.Equal(0, written.OutboxCount);
        Assert.Equal(effect, Assert.Single(written.Effects));
        Assert.Empty(instance.OutputCaptures[^1]);

        // 处理程序从此写作后开始; 处理者在它进行进一步写入之前就被扣留。
        await WaitForBarrierAsync(grain, held);
        Assert.Equal(writes + 1, Fixture.Storage.GetSuccessfulWriteCount(journal));
        held.Release();
        var completed = await Fixture.WaitForEffectCountAsync(grain, 2);
        Assert.Equal(0, completed.InboxCount);
        Assert.Equal(0, completed.OutboxCount);
        Assert.Equal(1, completed.ProcessedMessageCount);
        Assert.Equal(writes + 2, Fixture.Storage.GetSuccessfulWriteCount(journal));
    }

    [Fact]
    public async Task SelfSend_IsProcessedOnlyAfterItsWriteIsDurable()
    {
        var grain = NewGrain();
        _ = await grain.GetSnapshotAsync();
        var instance = GetInstance(grain);
        var storage = Fixture.Storage.BlockWrite(JournalId.FromGrainId(grain.GetGrainId()));

        var send = grain.SendAsync(grain.GetGrainId(), "messages/self-blocked", NewMessage(2, "blocked"));
        await storage.WaitUntilEnteredAsync();

        // 写作在飞行时的完整泵通行证留下接受的信息等待处理。
        Assert.Equal(DurableJobRunStatus.RescheduleRequested, (await Fixture.RunPumpAsync(grain)).Status);
        var pending = await Fixture.OnGrainAsync(grain, () => instance.GetSnapshotForTest());
        Assert.Equal(1, pending.InboxCount);
        Assert.Equal(0, pending.OutboxCount);
        Assert.Empty(pending.Effects);

        storage.Release();
        await send;
        var completed = await Fixture.WaitForEffectCountAsync(grain, 1);
        Assert.Equal("blocked", Assert.Single(completed.Effects).Value);
        Assert.Equal(0, completed.InboxCount);
    }

    [Fact]
    public async Task SelfSend_IsDiscardedWhenItsWriteFails()
    {
        var grain = NewGrain();
        var initial = await grain.GetSnapshotAsync();
        var context = Fixture.GetGrainContext(grain);
        Fixture.Storage.FailWrite(JournalId.FromGrainId(grain.GetGrainId()));

        await Assert.ThrowsAnyAsync<Exception>(() => grain.SendAsync(grain.GetGrainId(), "messages/self-failed", NewMessage(3, "failed")));

        await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var recovered = await grain.GetSnapshotAsync();
        Assert.NotEqual(initial.ActivationId, recovered.ActivationId);
        Assert.Equal(0, recovered.InboxCount);
        Assert.Equal(0, recovered.OutboxCount);
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(grain)).Status);
        var after = await grain.GetSnapshotAsync();
        Assert.Empty(after.Effects);
        Assert.Equal(0, after.ProcessedMessageCount);
    }

    [Fact]
    public async Task SelfSend_FollowsInboxIdentityRules()
    {
        var grain = NewGrain();
        _ = await grain.GetSnapshotAsync();
        const string route = "messages/self-identity";
        var message = SelfMessage(grain, NewMessage(4, "identity"), route);
        var differentBody = SelfMessage(grain, NewMessage(5, "different"), route, messageId: message.Envelope.MessageId);
        var differentDeliverAt = WithDeliverAt(message, JobClock.GetUtcNow() + Delay);

        using (var held = Fixture.HandlerProbe.Arm(grain.GetGrainId(), route))
        {
            await grain.StageOutputAsync(message);
            await grain.StageOutputAsync(message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.StageOutputAsync(differentBody));
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.StageOutputAsync(differentDeliverAt));
            Assert.Equal(1, (await grain.GetSnapshotAsync()).InboxCount);

            // 发箱密钥, 正在通过 ID 发送信件, 因此 ID 无法地址 grain 在那日,那件《古兰经》将等待它。
            var other = SelfMessage(grain, NewMessage(6, "other"), route);
            var otherReceiver = NewGrain().GetGrainId();
            await grain.StageOutputAsync(new OutboxMessage { ReceiverId = otherReceiver, Envelope = other.Envelope });
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.StageOutputAsync(other));

            await grain.RetryWriteStateAsync();
            await WaitForBarrierAsync(grain, held);
        }

        var completed = await Fixture.WaitForEffectCountAsync(grain, 1);
        Assert.Equal(1, completed.ProcessedMessageCount);

        // 填写完毕的ID无论内容如何都是重复的,而其完成记录则保留。
        await grain.StageOutputAsync(message);
        await grain.StageOutputAsync(differentBody);
        var repeated = await grain.GetSnapshotAsync();
        Assert.Equal(0, repeated.InboxCount);
        await grain.RetryWriteStateAsync();
        _ = await Fixture.RunPumpAsync(grain);
        var final = await grain.GetSnapshotAsync();
        Assert.Equal(1, Assert.Single(final.Effects).Count);
        Assert.Empty(final.InboxDeadLetters);
    }

    [Fact]
    public async Task SelfSend_IsAdmittedBeyondCapacityAndWithoutHandlers()
    {
        var grain = NewGrain();
        _ = await grain.GetSnapshotAsync();
        await grain.ConfigureHandlerAsync(false);
        await ReactivateAsync(grain);

        // MaxCapacity 是 2, 没有处理器被注册; 同步发送会接收每条消息 。
        for (var sequence = 10; sequence < 13; sequence++)
        {
            await grain.StageOutputAsync(SelfMessage(grain, NewMessage(sequence, $"self-{sequence}"), "messages/self-capacity"));
        }

        await grain.RetryWriteStateAsync();
        var admitted = await grain.GetSnapshotAsync();
        Assert.Equal(3, admitted.InboxCount);
        Assert.Equal(0, admitted.OutboxCount);

        // 未经事先排定的自发信息占用了其他 grains交付的能力。
        var external = CreateEnvelope(grain, NewMessage(20, "external"));
        DeliveryResultAssert.IsCase<DeliveryResult.Backpressured>(await DeliverAsync(grain, external));
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(grain)).Status);
        var waiting = await grain.GetSnapshotAsync();
        Assert.Equal(3, waiting.InboxCount);
        Assert.Empty(waiting.Effects);
        Assert.Empty(waiting.InboxDeadLetters);

        await grain.ConfigureHandlerAsync(true);
        var completed = await Fixture.WaitForEffectCountAsync(grain, 3);
        Assert.Equal(0, completed.InboxCount);
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(grain, external));
        await Fixture.WaitForEffectCountAsync(grain, 4);
    }

    [Fact]
    public async Task SelfSendFromHandler_IsProcessedInALaterBatch()
    {
        var grain = NewGrain();
        _ = await grain.GetSnapshotAsync();
        var instance = GetInstance(grain);
        instance.ClearCaptures();
        var message = NewMessage(30, "forwarding") with { ForwardTo = grain.GetGrainId() };

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(grain, CreateEnvelope(grain, message)));

        var completed = await Fixture.WaitForEffectCountAsync(grain, 2);
        Assert.Equal(2, Assert.Single(completed.Effects).Count);
        Assert.Equal(0, completed.InboxCount);
        Assert.Equal(0, completed.OutboxCount);
        Assert.Equal(2, completed.ProcessedMessageCount);

        // 处理原件的批次也接受了转发的信息,后一批则进行了处理。
        var captures = instance.Captures;
        var original = Assert.Single(captures, static capture => capture.Effects.Sum(static effect => effect.Count) == 1);
        Assert.Equal(1, original.InboxCount);
        Assert.Equal(0, original.OutboxCount);
        Assert.Equal(1, original.ProcessedMessageCount);
        Assert.All(instance.OutputCaptures, static output => Assert.Empty(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduledSelfSend_SurvivesRecoveryAndIsProcessedWhenDue(bool snapshot)
    {
        var grain = NewGrain();
        _ = await grain.GetSnapshotAsync();
        var journal = JournalId.FromGrainId(grain.GetGrainId());
        if (snapshot)
        {
            Fixture.Storage.RequestSnapshot(journal);
        }

        var deliverAt = JobClock.GetUtcNow() + Delay;
        var message = WithDeliverAt(SelfMessage(grain, NewMessage(40, "recovered"), "messages/self-recovered"), deliverAt);
        await grain.StageOutputAsync(message);
        await grain.RetryWriteStateAsync();

        await ReactivateAsync(grain);
        var recovered = await grain.GetSnapshotAsync();
        Assert.Equal(1, recovered.InboxCount);
        Assert.Equal(0, recovered.OutboxCount);
        Assert.Empty(recovered.Effects);
        var inbox = Fixture.GetInbox(grain);
        Assert.Equal(deliverAt, await Fixture.OnGrainAsync(grain,
            () => InboxTestAccess.GetPendingMessages(inbox)[message.Envelope.MessageId].Message.Envelope.DeliverAt));

        var held = await Fixture.RunPumpAsync(grain);
        Assert.Equal(DurableJobRunStatus.RescheduleRequested, held.Status);
        Assert.Equal(deliverAt, held.RescheduleTime);

        // 所回复的待回信件重复重复发送 。
        await grain.StageOutputAsync(message);
        Assert.Equal(1, (await grain.GetSnapshotAsync()).InboxCount);

        JobClock.Advance(Delay);
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(grain)).Status);
        var completed = await Fixture.WaitForEffectCountAsync(grain, 1);
        Assert.Equal("recovered", Assert.Single(completed.Effects).Value);
        Assert.Equal(0, completed.InboxCount);
        Assert.Equal(0, completed.OutboxCount);
    }

    [Fact]
    public async Task SelfSend_RecordsSendAndReceiptWithoutOutboxMetrics()
    {
        var grain = NewGrain();
        _ = await grain.GetSnapshotAsync();
        var services = Fixture.GetGrainContext(grain).ActivationServices;
        using var probe = new MessageMetricListener(services.GetRequiredService<OrleansInstruments>().Meter);
        var grainType = grain.GetGrainId().Type.ToString();
        const string route = "messages/self-metrics";
        var message = SelfMessage(grain, NewMessage(50, "metrics"), route);

        using (var held = Fixture.HandlerProbe.Arm(grain.GetGrainId(), route))
        {
            await grain.StageOutputAsync(message);
            await grain.StageOutputAsync(message);
            await grain.RetryWriteStateAsync();
            await WaitForBarrierAsync(grain, held);
        }

        await Fixture.WaitForEffectCountAsync(grain, 1);
        var sent = probe.Read("outbox-messages-sent");
        Assert.Equal(2, sent.Length);
        Assert.All(sent, row => row.AssertCounter(grainType));
        var received = probe.Read("inbox-messages-received");
        Assert.Equal(2, received.Length);
        received[0].AssertCounter(grainType, "accepted");
        received[1].AssertCounter(grainType, "duplicate");
        Assert.Equal([1d, -1d], probe.Read("inbox-depth").Select(static row => row.Value));
        Assert.Empty(probe.Read("outbox-depth"));
        Assert.Empty(probe.Read("outbox-messages-delivered"));
        Assert.Empty(probe.Read("outbox-delivery-duration"));
    }

    [Fact]
    public async Task SelfSend_KeepsItsAcceptedSentAtAcrossClockMotionSnapshotAndRecovery()
    {
        var grain = NewGrain();
        var started = JobClock.GetUtcNow();
        var due = started + Delay;
        var output = WithDeliverAt(SelfMessage(grain, NewMessage(61, "local-sent-time"), "messages/self-sent-time"), due);
        await grain.StageOutputAsync(output);
        await grain.RetryWriteStateAsync();

        // 以后每次自发同一条信息都是重复的,所以记录保留了它接受发送的时间。
        var inbox = Fixture.GetInbox(grain);
        Assert.Equal(started, await Fixture.OnGrainAsync(grain, () =>
            InboxTestAccess.GetPendingMessages(inbox)[output.Envelope.MessageId].Message.SentAt));
        JobClock.Advance(TimeSpan.FromMinutes(1));
        await grain.StageOutputAsync(output);
        await grain.RetryWriteStateAsync();
        Assert.Equal(started, await Fixture.OnGrainAsync(grain, () =>
            InboxTestAccess.GetPendingMessages(inbox)[output.Envelope.MessageId].Message.SentAt));

        JobClock.Advance(-TimeSpan.FromDays(2));
        await grain.StageOutputAsync(output);
        Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(grain.GetGrainId()));
        await grain.RetryWriteStateAsync();
        await ReactivateAsync(grain);
        inbox = Fixture.GetInbox(grain);
        var recovered = Assert.Single(await Fixture.OnGrainAsync(grain, () => InboxTestAccess.GetPendingMessages(inbox).Values.ToArray()));
        Assert.Equal(started, recovered.Message.SentAt);
        Assert.Equal(due, recovered.Message.Envelope.DeliverAt);
        Assert.Equal(0, (await grain.GetSnapshotAsync()).OutboxCount);
        await grain.StageOutputAsync(output);
        await grain.RetryWriteStateAsync();
        JobClock.Advance(due - JobClock.GetUtcNow());
        await Fixture.RunPumpAsync(grain);
        await Fixture.WaitForInboxCountAsync(grain, 0);
        var completed = await grain.GetSnapshotAsync();
        Assert.Equal("local-sent-time", Assert.Single(completed.Effects).Value);
        Assert.Equal(1, Assert.Single(completed.Effects).Count);
        Assert.Equal(0, completed.OutboxCount);
        Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(grain.GetGrainId()));
        await grain.RetryWriteStateAsync();
        var replay = output.ToInboxMessage(started);
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(grain, replay));
    }

    [Fact]
    public async Task SelfSend_AfterClockRollback_StampsSentAtAboveTheDurableFloor()
    {
        var grain = NewGrain();
        var original = JobClock.GetUtcNow();
        JobClock.Advance(TimeSpan.FromDays(2));
        var floor = JobClock.GetUtcNow() - TimeSpan.FromMinutes(10);
        var stale = CreateEnvelope(grain, NewMessage(62, "floor")) with { SentAt = floor };
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(grain, stale));
        JobClock.Advance(original - JobClock.GetUtcNow());
        var output = WithDeliverAt(SelfMessage(grain, NewMessage(63, "after-rollback"), "messages/self-above-floor"), original + Delay);
        await grain.StageOutputAsync(output);
        var inbox = Fixture.GetInbox(grain);
        var sentAt = await Fixture.OnGrainAsync(grain, () =>
            InboxTestAccess.GetPendingMessages(inbox)[output.Envelope.MessageId].Message.SentAt);
        Assert.True(sentAt > floor);
        Assert.Equal(0, (await grain.GetSnapshotAsync()).OutboxCount);
        await grain.StageOutputAsync(output);
        await grain.RetryWriteStateAsync();
        JobClock.Advance(Delay);
        await Fixture.RunPumpAsync(grain);
        await Fixture.WaitForInboxCountAsync(grain, 0);
        Assert.Equal(1, Assert.Single((await grain.GetSnapshotAsync()).Effects).Count);
    }

    private MessagingTestGrain GetInstance(IMessagingTestGrain grain) =>
        Assert.IsType<MessagingTestGrain>(Fixture.GetGrainContext(grain).GrainInstance);

    private OutboxMessage SelfMessage(IMessagingTestGrain grain, TestMessage message, string route, MessageId messageId = default) =>
        TestApplicationProtocol.Create(Sessions, grain.GetGrainId(), grain.GetGrainId(), route, message, messageId);

    private static OutboxMessage WithDeliverAt(OutboxMessage message, DateTimeOffset? deliverAt) =>
        new() { ReceiverId = message.ReceiverId, Envelope = message.Envelope with { DeliverAt = deliverAt } };

    private sealed class SelfAddressedClusterFixture() : MessagingClusterFixture(1, receiverOnly: false)
    {
        public ManualJobClock JobClock { get; } = new(DateTimeOffset.UtcNow);

        protected override void ConfigureServices(IServiceCollection services) =>
            services.AddKeyedSingleton<TimeProvider>(DurableJobTimeProviderNames.DurableJobs, JobClock);
    }
}
