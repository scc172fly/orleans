using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Orleans.Hosting;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Journaling.Json;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Durable.Messaging.Diagnostics;
using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Orleans.TestingHost;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

/// <summary>
/// 发信箱源和收件箱汇将应用程序拥有的有效载荷作为唯一日记副本。
/// </summary>
public abstract class MessageSourceAndSinkTests(MessageSourceAndSinkFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private InProcessTestCluster Cluster => fixture.Cluster;
    private RecordingJournalStorageProvider Storage => fixture.Storage;
    private SourceSinkProbe Probe => fixture.Probe;

    [Fact]
    public async Task SourceMessage_JournalsEachPayloadOnceAndRetiresInTheRemovalWrite()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var messageId = NewMessageId("delivered");
        var payload = NewPayload();

        await sender.StageFromSourceAsync(receiver.GetGrainId(), messageId, payload, write: true);

        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Writes(sender).Length == 2);
        var senderState = await OnGrainAsync(sender, context => Grain(context).GetState());
        Assert.Empty(senderState.Outgoing);
        Assert.Equal(nameof(OutboxMessageRetirement.Delivered), Assert.Single(senderState.Retired).Value);
        Assert.Empty(await OnGrainAsync(sender, context => Grain(context).GetRetirementReasons()));
        // 发送信箱将信息变成一次信息 以验证发送信号 并实现一次传送尝试
        Assert.Equal(2, Probe.For(sender).Materializations);

        // 有效载荷由每个 grain日记一次:发件人源状态和接收器汇状况。
        Assert.Equal(1, Storage.CountPayloadOccurrences(Journal(sender), payload));
        Assert.Equal(1, Storage.CountPayloadOccurrences(Journal(receiver), payload));

        // 将退出承诺写入删除发件箱条目的文字。
        var writes = Writes(sender);
        var retirement = Encoding.UTF8.GetBytes(nameof(OutboxMessageRetirement.Delivered));
        Assert.True(writes[0].AsSpan().IndexOf(retirement) < 0);
        Assert.True(writes[1].AsSpan().IndexOf(retirement) >= 0);

        // 水槽接收了信息,但没有收件箱条目;收件箱日记只接收其处理过的记录。
        var receiverState = await OnGrainAsync(receiver, context => (
            Grain(context).GetState(),
            Inbox(context).Count,
            InboxTestAccess.GetProcessedKeys(Inbox(context)).Contains(messageId)));
        Assert.Equal(payload, receiverState.Item1.Received[messageId.ToString()]);
        Assert.Equal(0, receiverState.Count);
        Assert.True(receiverState.Item3);
        Assert.Equal(1, Probe.For(receiver).SinkAcceptances);
        Assert.Equal(0, Probe.For(receiver).Handled);
        Assert.Single(Writes(receiver));

        // 追回的发件人同意:删除条目,记录其退休情况。
        Assert.True(Cluster.TryGetGrainContext(sender.GetGrainId(), out var senderContext));
        await sender.DeactivateAsync();
        await senderContext.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.Equal(0, await sender.GetOutboxCountAsync());
        var recovered = await OnGrainAsync(sender, context => Grain(context).GetState());
        Assert.Empty(recovered.Outgoing);
        Assert.Single(recovered.Retired);
        Assert.Equal(2, Writes(sender).Length);
    }

    [Fact]
    public async Task SourceMessage_RecoveredFromTheJournal_IsDeliveredFromItsSource()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var messageId = NewMessageId("recovered");
        var payload = NewPayload();
        _ = await sender.GetOutboxCountAsync();
        Assert.True(Cluster.TryGetGrainContext(sender.GetGrainId(), out var senderContext));

        await sender.StageFromSourceAndDeactivateAsync(receiver.GetGrainId(), messageId, payload);

        // 只检查从日记上回收的激活:停止引爆者处置其服务。
        await senderContext.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), Token);
        _ = await sender.GetOutboxCountAsync();
        await WaitUntilAsync(receiver, context => Grain(context).GetState().Received.ContainsKey(messageId.ToString()));
        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 1);
        Assert.Equal(1, Probe.For(receiver).SinkAcceptances);
        Assert.Equal(1, Storage.CountPayloadOccurrences(Journal(sender), payload));
        Assert.Equal(1, Storage.CountPayloadOccurrences(Journal(receiver), payload));
    }

    [Fact]
    public async Task SourceMessage_DeadLetteredByTheOutbox_MaterializesItsMessageBeforeRetiring()
    {
        var sender = NewGrain();
        var receiver = Cluster.Client.GetGrain<INoHandlerGrain>(Guid.NewGuid());
        await receiver.PingAsync();
        var messageId = NewMessageId("outbox-dead-letter");
        var payload = NewPayload();

        await sender.StageFromSourceAsync(receiver.GetGrainId(), messageId, payload, write: true);

        // 接收器没有操作器,所以传送尝试失败直到 OutboxRetryTimeout 发生。
        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 1);
        var (state, deadLetters) = await OnGrainAsync(sender, context => (
            Grain(context).GetState(),
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters));
        Assert.Empty(state.Outgoing);
        Assert.Equal(nameof(OutboxMessageRetirement.DeadLettered), state.Retired[messageId.ToString()]);
        var letter = Assert.Single(deadLetters);
        Assert.Equal(letter.Reason, (await OnGrainAsync(sender, context => Grain(context).GetRetirementReasons()))[messageId.ToString()]);
        Assert.Equal(messageId, letter.Message.Envelope.MessageId);
        Assert.Equal(receiver.GetGrainId(), letter.Message.ReceiverId);
        Assert.Equal(payload, letter.Message.Envelope.Payload);
        Assert.Equal(SourceSinkGrain.Subject, letter.Message.Envelope.Subject);
        Assert.True(letter.AttemptCount > 1);
        // 每次尝试都实现信息,最后的提醒也实现信息,因为发现重试超时已过期。
        Assert.InRange(Probe.For(sender).Materializations, letter.AttemptCount + 1, letter.AttemptCount + 2);

        // 死者的遗书有自己的副本供诊断
        await WaitUntilAsync(sender, _ => Storage.CountPayloadOccurrences(Journal(sender), payload) == 2);
    }

    [Fact]
    public async Task SourceMessage_DeadLetteredByTheOutbox_KeepsItsPayloadAndHeadersWhenTheSourceReleasesThem()
    {
        var sender = NewGrain();
        var receiver = Cluster.Client.GetGrain<INoHandlerGrain>(Guid.NewGuid());
        await receiver.PingAsync();
        var messageId = NewMessageId("released-dead-letter");
        var payload = NewPayload();

        // 源共享其有效载荷阵列作为页眉值,并将该阵列清除到 OnRetired。
        Probe.For(sender).ReleaseBuffersOnRetired = true;
        await sender.StageFromSourceAsync(receiver.GetGrainId(), messageId, payload, write: true);

        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 1);
        AssertCarriesPayload(Assert.Single(await OnGrainAsync(sender, context =>
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters)));

        // 来源国曾将有效载荷作一次日记,而死信则作为有效载荷和信头。
        await WaitUntilAsync(sender, _ => Storage.CountPayloadOccurrences(Journal(sender), payload) == 3);

        // 一份在退休日报之后的简况 死亡信自己的副本, 新的激活恢复。
        Storage.RequestSnapshot(Journal(sender));
        await sender.WriteAsync();
        Assert.Equal(5, Storage.CountPayloadOccurrences(Journal(sender), payload));
        Assert.True(Cluster.TryGetGrainContext(sender.GetGrainId(), out var activation));
        await sender.DeactivateAsync();
        await activation.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.Equal(0, await sender.GetOutboxCountAsync());
        AssertCarriesPayload(Assert.Single(await OnGrainAsync(sender, context =>
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters)));

        void AssertCarriesPayload(OutboxDeadLetter letter)
        {
            Assert.Equal(messageId, letter.Message.Envelope.MessageId);
            Assert.Equal(payload, letter.Message.Envelope.Payload);
            Assert.Equal(payload, Assert.Single(letter.Message.Envelope.Headers!, header => header.Key == SourceSinkGrain.PayloadHeader).Value);
        }
    }

    [Fact]
    public async Task SourceMessage_DeadLetteredByTheReceiver_RetiresAsRejectedWithTheReceiversReasonAndCode()
    {
        const string code = SourceSinkGrain.RejectionCode;
        var sender = NewGrain();
        var receiver = NewGrain();
        Probe.For(receiver).Mode = SinkMode.DeadLetter;
        var messageId = NewMessageId("receiver-dead-letter");
        var payload = NewPayload();

        await sender.StageFromSourceAsync(receiver.GetGrainId(), messageId, payload, write: true);

        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 1);
        var (state, outboxDeadLetters) = await OnGrainAsync(sender, context => (
            Grain(context).GetState(),
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters));
        Assert.Equal(nameof(OutboxMessageRetirement.Rejected), state.Retired[messageId.ToString()]);
        Assert.Equal(SourceSinkGrain.Rejection, (await OnGrainAsync(sender, context => Grain(context).GetRetirementReasons()))[messageId.ToString()]);
        Assert.Equal(code, (await OnGrainAsync(sender, context => Grain(context).GetRetirementCodes())).GetValueOrDefault(messageId.ToString()));
        Assert.Empty(state.Outgoing);
        Assert.Empty(outboxDeadLetters);
        // 接收器记录了水槽拒绝的信号 与信件处理的记录, 不是收件箱的死信。
        var (inboxDeadLetters, processed) = await OnGrainAsync(receiver, context => (
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().InboxDeadLetters,
            InboxTestAccess.GetProcessedKeys(Inbox(context)).Contains(messageId)));
        Assert.Empty(inboxDeadLetters);
        Assert.True(processed);
    }

    [Fact]
    public async Task SourceMessage_WhoseSinkThrowsTransiently_IsRetriedUntilTheSinkAcceptsIt()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        Probe.For(receiver).FailNextOffers(2);
        var messageId = NewMessageId("sink-transient-failure");
        var payload = NewPayload();

        await sender.StageFromSourceAsync(receiver.GetGrainId(), messageId, payload, write: true);

        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 1);
        var (state, outboxDeadLetters) = await OnGrainAsync(sender, context => (
            Grain(context).GetState(),
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters));
        Assert.Equal(nameof(OutboxMessageRetirement.Delivered), state.Retired[messageId.ToString()]);
        Assert.Empty(outboxDeadLetters);
        var (received, inboxDeadLetters) = await OnGrainAsync(receiver, context => (
            Grain(context).GetState().Received,
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().InboxDeadLetters));
        Assert.Equal(payload, received[messageId.ToString()]);
        Assert.Empty(inboxDeadLetters);
        Assert.Equal(3, Probe.For(receiver).SinkOffers);
        Assert.Equal(1, Probe.For(receiver).SinkAcceptances);
    }

    [Fact]
    public async Task SourceMessage_WhoseSinkKeepsThrowing_DeadLettersAtTheSenderWithTheLastErrorAfterTheRetryTimeout()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        Probe.For(receiver).Mode = SinkMode.Throw;
        var messageId = NewMessageId("sink-persistent-failure");

        await sender.StageFromSourceAsync(receiver.GetGrainId(), messageId, NewPayload(), write: true);

        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 1);
        var (state, outboxDeadLetters, reasons, codes) = await OnGrainAsync(sender, context => (
            Grain(context).GetState(),
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters,
            Grain(context).GetRetirementReasons(),
            Grain(context).GetRetirementCodes()));
        Assert.Equal(nameof(OutboxMessageRetirement.DeadLettered), state.Retired[messageId.ToString()]);
        var letter = Assert.Single(outboxDeadLetters);
        Assert.Contains(SourceSinkGrain.SinkFailure, letter.Reason, StringComparison.Ordinal);
        Assert.Equal(letter.Reason, reasons[messageId.ToString()]);
        Assert.Empty(codes);
        Assert.True(letter.AttemptCount > 1);

        var (received, inboxDeadLetters, inboxCount) = await OnGrainAsync(receiver, context => (
            Grain(context).GetState().Received,
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().InboxDeadLetters,
            Inbox(context).Count));
        Assert.Empty(received);
        Assert.Empty(inboxDeadLetters);
        Assert.Equal(0, inboxCount);
        Assert.Equal(letter.AttemptCount, Probe.For(receiver).SinkOffers);
    }

    [Fact]
    public async Task DiagnosticEvents_InjectADeliveryFaultAndReportTheRetryAndTheRetirement()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var messageId = NewMessageId("diagnostic-events");
        using var events = new DeliveryEventObserver(sender.GetGrainId(), receiver.GetGrainId(), messageId, faults: 1);

        await sender.StageFromSourceAsync(receiver.GetGrainId(), messageId, NewPayload(), write: true);

        // 观察员的例外情形在信箱将信息传递给其水槽之前第一次尝试失败,因此
        // 发件箱重新试了货,再试了货
        var retired = await events.Retired.WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.Equal(sender.GetGrainId(), retired.GrainContext.GrainId);
        Assert.IsType<OutboxMessageRetirement.Delivered>(retired.Retirement.Value);
        var retry = Assert.Single(events.Retries);
        Assert.Equal((sender.GetGrainId(), receiver.GetGrainId(), 1), (retry.GrainContext.GrainId, retry.ReceiverId, retry.AttemptCount));
        Assert.Contains(DeliveryEventObserver.Fault, retry.Error, StringComparison.Ordinal);
        Assert.Equal(2, events.Receivings);
        Assert.Equal((1, 1), (Probe.For(receiver).SinkOffers, Probe.For(receiver).SinkAcceptances));
    }

    [Fact]
    public async Task SourceMessage_CoalescesWithItsPendingReferenceAndRejectsConflicts()
    {
        var sender = NewGrain();
        var receiver = NewGrain().GetGrainId();
        var messageId = NewMessageId("coalesced");
        var sentId = NewMessageId("sent");

        await sender.StageFromSourceAsync(receiver, messageId, NewPayload(), write: false);
        await sender.SendFromSourceAsync(messageId);
        Assert.Equal(1, await sender.GetOutboxCountAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(receiver, messageId, NewPayload(), write: false));
        await sender.SendAsync(receiver, sentId, NewPayload(), write: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendFromSourceAsync(sentId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendFromSourceAsync(NewMessageId("unprovided")));
        await Assert.ThrowsAsync<ArgumentException>(() => sender.StageFromSourceAsync(sender.GetGrainId(), NewMessageId("self"), NewPayload(), write: false));
        Assert.Equal(2, await sender.GetOutboxCountAsync());

        await sender.WriteAsync();
        await WaitUntilAsync(sender, context => Outbox(context).Count == 0);

        // 用于验证第一发邮件、拒绝自发邮件和交付;
        // 信息从未实现。
        Assert.Equal(3, Probe.For(sender).Materializations);
    }

    [Fact]
    public async Task SourceMessage_WithdrawnBySource_IsRemovedWithoutDeliveryOrRetirement()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var messageId = NewMessageId("withdrawn");

        await sender.StageFromSourceAsync(receiver.GetGrainId(), messageId, NewPayload(), write: false);
        await sender.WithdrawAsync(messageId);
        Assert.Equal(0, await sender.GetOutboxCountAsync());
        Assert.False(await OnGrainAsync(sender, context => Outbox(context).Withdraw(messageId)));
        await sender.WriteAsync();

        // 撤回和释放来源 是在文件里承诺的 并进行阶段
        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Writes(sender).Length == 1);
        var state = await OnGrainAsync(sender, context => Grain(context).GetState());
        Assert.Empty(state.Outgoing);
        Assert.Empty(state.Retired);
        Assert.Equal(1, Probe.For(sender).Materializations);
        Assert.Equal(0, Probe.For(receiver).SinkOffers);

        Assert.True(Cluster.TryGetGrainContext(sender.GetGrainId(), out var senderContext));
        await sender.DeactivateAsync();
        await senderContext.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.Equal(0, await sender.GetOutboxCountAsync());
    }

    [Theory]
    [InlineData(SourceFault.Throw)]
    [InlineData(SourceFault.InvalidMessage)]
    [InlineData(SourceFault.MismatchedId)]
    public async Task SourceFailureAfterSend_DeadLettersOnlyThatEntryAndKeepsTheGrainWorking(SourceFault fault)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var first = NewMessageId("sibling");
        var failing = NewMessageId("failing");
        var second = NewMessageId("sibling");
        await sender.StageFromSourceAsync(receiver.GetGrainId(), first, NewPayload(), write: false);
        await sender.StageFromSourceAsync(receiver.GetGrainId(), failing, NewPayload(), write: false);
        await sender.StageFromSourceAsync(receiver.GetGrainId(), second, NewPayload(), write: false);
        Assert.True(Cluster.TryGetGrainContext(sender.GetGrainId(), out var activation));
        using var metrics = new MessageMetricListener(activation.ActivationServices.GetRequiredService<OrleansInstruments>().Meter);

        // 发信时,来文提交人提供了所有信息,但没有提供发送未遂信息。
        Probe.For(sender).Faults[failing] = fault;
        await sender.WriteAsync();

        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 3);
        var (state, deadLetters) = await OnGrainAsync(sender, context => (
            Grain(context).GetState(),
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters));
        Assert.Empty(state.Outgoing);
        Assert.Equal(nameof(OutboxMessageRetirement.DeadLettered), state.Retired[failing.ToString()]);
        Assert.Equal(nameof(OutboxMessageRetirement.Delivered), state.Retired[first.ToString()]);
        Assert.Equal(nameof(OutboxMessageRetirement.Delivered), state.Retired[second.ToString()]);
        var received = (await OnGrainAsync(receiver, context => Grain(context).GetState())).Received;
        Assert.Equal(new[] { first.ToString(), second.ToString() }.Order(), received.Keys.Order());
        AssertSourceFailureDeadLetter(Assert.Single(deadLetters), failing, fault);
        Assert.Equal(fixture.Clock.GetUtcNow(), Assert.Single(deadLetters).DeadLetteredAt);
        Assert.Equal(
            Assert.Single(deadLetters).Reason,
            Assert.Single(await OnGrainAsync(sender, context => Grain(context).GetRetirementReasons())).Value);

        // 死亡信及其退休承诺在一封写下,
        // 他们的尝试完成了
        var writes = Writes(sender);
        Assert.InRange(writes.Length, 3, 4);
        var deadLettered = Encoding.UTF8.GetBytes(nameof(OutboxMessageRetirement.DeadLettered));
        var delivered = Encoding.UTF8.GetBytes(nameof(OutboxMessageRetirement.Delivered));
        Assert.True(writes[1].AsSpan().IndexOf(deadLettered) >= 0);
        Assert.True(writes[1].AsSpan().IndexOf(delivered) < 0);
        Assert.All(writes[2..], write => Assert.True(write.AsSpan().IndexOf(deadLettered) < 0));
        Assert.All(writes[2..], write => Assert.True(write.AsSpan().IndexOf(delivered) >= 0));

        var grainType = sender.GetGrainId().Type.ToString();
        var outcomes = metrics.Read("outbox-messages-delivered");
        Assert.Equal(3, outcomes.Length);
        Assert.Single(outcomes, row => row.Tags.Contains(new("status", "source_error"))).AssertCounter(grainType, "source_error");
        Assert.Equal(2, outcomes.Count(row => row.Tags.Contains(new("status", "accepted"))));

        // 启动程序保存了处理信息;源代码被调用一次发送,每次发送尝试一次。
        var later = NewMessageId("later");
        await sender.StageFromSourceAsync(receiver.GetGrainId(), later, NewPayload(), write: true);
        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 4);
        Assert.Equal(nameof(OutboxMessageRetirement.Delivered), (await OnGrainAsync(sender, context => Grain(context).GetState())).Retired[later.ToString()]);
        Assert.True(Cluster.TryGetGrainContext(sender.GetGrainId(), out var current));
        Assert.Same(activation, current);
        Assert.Equal(1, Probe.For(sender).Activations);
        Assert.Equal(8, Probe.For(sender).Materializations);

        // 长期的错误永远不会失败 以后启动: 死亡信恢复了,源代码不再被调用。
        await sender.DeactivateAsync();
        await activation.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.Equal(0, await sender.GetOutboxCountAsync());
        var recovered = await OnGrainAsync(sender, context =>
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters);
        AssertSourceFailureDeadLetter(Assert.Single(recovered), failing, fault);
        Assert.Equal(fixture.Clock.GetUtcNow(), Assert.Single(recovered).DeadLetteredAt);
        Assert.Equal(2, Probe.For(sender).Activations);
        Assert.Equal(8, Probe.For(sender).Materializations);
    }

    [Fact]
    public async Task SourceFailures_WhoseRetirementWithdrawsAnotherFailedEntry_DeadLetterOnlyTheRetiredEntry()
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var first = NewMessageId("withdrawing");
        var second = NewMessageId("withdrawing");
        await sender.StageFromSourceAsync(receiver.GetGrainId(), first, NewPayload(), write: false);
        await sender.StageFromSourceAsync(receiver.GetGrainId(), second, NewPayload(), write: false);
        Assert.True(Cluster.TryGetGrainContext(sender.GetGrainId(), out var activation));

        // 两者的条目都一次失败。 哪些先退休者先退, 哪个再退, 然后再退, 结果没有结果: 没有死亡
        // 并且没有退休回馈。
        Probe.For(sender).Faults[first] = SourceFault.Throw;
        Probe.For(sender).Faults[second] = SourceFault.Throw;
        Probe.For(sender).WithdrawOnRetired[first] = second;
        Probe.For(sender).WithdrawOnRetired[second] = first;
        await sender.WriteAsync();

        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 1);
        var (state, deadLetters) = await OnGrainAsync(sender, context => (
            Grain(context).GetState(),
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters));
        var retired = Assert.Single(state.Retired);
        Assert.Equal(nameof(OutboxMessageRetirement.DeadLettered), retired.Value);
        Assert.Equal(retired.Key, Assert.Single(deadLetters).Message.Envelope.MessageId.ToString());
        Assert.Empty(state.Outgoing);
        Assert.True(Cluster.TryGetGrainContext(sender.GetGrainId(), out var current));
        Assert.Same(activation, current);
        Assert.Equal(1, Probe.For(sender).Activations);
    }

    [Fact]
    public async Task SourceFailureOnRecoveredEntry_DeadLettersItAndRetiresItWithTheFailingSource()
    {
        var grain = NewGrain();
        var receiver = NewGrain().GetGrainId();
        _ = await grain.GetOutboxCountAsync();
        var messageId = NewMessageId("recovered-failure");
        Probe.For(grain).Faults[messageId] = SourceFault.Throw;

        // 重放一个参考条目,其来源无法再提供该条目,然后运行送货通行证。
        await OnGrainAsync(grain, context =>
        {
            Grain(context).StoreOutgoing(receiver, messageId, NewPayload());
            var outbox = Outbox(context);
            ApplyReference(outbox, messageId);
            ((IStateMachine)outbox).OnRecoveryCompleted();
            var pump = context.ActivationServices.GetRequiredService<MessagingPump>();
            pump.EnsureStarted();
            pump.Signal();
            return true;
        });

        await WaitUntilAsync(grain, context => Outbox(context).Count == 0);
        var (state, deadLetters) = await OnGrainAsync(grain, context => (
            Grain(context).GetState(),
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters));
        Assert.Empty(state.Outgoing);
        Assert.Equal(nameof(OutboxMessageRetirement.DeadLettered), state.Retired[messageId.ToString()]);
        AssertSourceFailureDeadLetter(Assert.Single(deadLetters), messageId, SourceFault.Throw);

        // 启动程序会不断处理信息
        var later = NewMessageId("after-recovered-failure");
        await grain.StageFromSourceAsync(receiver, later, NewPayload(), write: true);
        await WaitUntilAsync(grain, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 2);
        Assert.Equal(nameof(OutboxMessageRetirement.Delivered), (await OnGrainAsync(grain, context => Grain(context).GetState())).Retired[later.ToString()]);
        Assert.Equal(1, Probe.For(grain).Activations);
    }

    [Theory]
    [InlineData(SourceFault.Throw)]
    [InlineData(SourceFault.InvalidMessage)]
    [InlineData(SourceFault.MismatchedId)]
    public async Task SourceFailureDuringSend_ThrowsToTheCallerAndStagesNothing(SourceFault fault)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var messageId = NewMessageId("send-failure");
        Probe.For(sender).Faults[messageId] = fault;

        var stage = sender.StageFromSourceAsync(receiver.GetGrainId(), messageId, NewPayload(), write: false);
        var error = fault == SourceFault.InvalidMessage
            ? await Assert.ThrowsAsync<ArgumentException>(() => stage)
            : (Exception)await Assert.ThrowsAsync<InvalidOperationException>(() => stage);
        Assert.Contains(ExpectedSourceError(fault), error.Message, StringComparison.Ordinal);
        Assert.Equal(0, await sender.GetOutboxCountAsync());

        var later = NewMessageId("after-send-failure");
        await sender.StageFromSourceAsync(receiver.GetGrainId(), later, NewPayload(), write: true);
        await WaitUntilAsync(sender, context => Outbox(context).Count == 0 && Grain(context).GetState().Retired.Count == 1);
        Assert.Empty(await OnGrainAsync(sender, context =>
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters));
        Assert.Equal(1, Probe.For(sender).Activations);
    }

    [Fact]
    public async Task RecoveredReferenceEntry_IsDeadLetteredWhenNoSourceProvidesItAndFailsWithoutSources()
    {
        var grain = NewGrain();
        var withoutSources = Cluster.Client.GetGrain<INoHandlerGrain>(Guid.NewGuid());
        _ = await grain.GetOutboxCountAsync();
        await withoutSources.PingAsync();
        var unprovided = NewMessageId("unprovided");

        // 重放参考条目, 验证回收状态与回收一样, 然后运行一个交货证 。
        await OnGrainAsync(grain, context =>
        {
            var outbox = Outbox(context);
            ApplyReference(outbox, unprovided);
            ((IStateMachine)outbox).OnRecoveryCompleted();
            var pump = context.ActivationServices.GetRequiredService<MessagingPump>();
            pump.EnsureStarted();
            pump.Signal();
            return true;
        });
        // 撤回是明确的,因此一个没有来源提供的条目即丢失数据:它用无名字母编号,没有任何所有人
        // 通知。
        await WaitUntilAsync(grain, context => Outbox(context).Count == 0);
        Assert.Empty((await OnGrainAsync(grain, context => Grain(context).GetState())).Retired);
        var letter = Assert.Single(await OnGrainAsync(grain, context =>
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().OutboxDeadLetters));
        Assert.Equal(unprovided, letter.Message.Envelope.MessageId);
        Assert.Equal("No registered outbox message source provides the message.", letter.Reason);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => OnGrainAsync(withoutSources, context =>
        {
            var outbox = Outbox(context);
            ApplyReference(outbox, unprovided);
            ((IStateMachine)outbox).OnRecoveryCompleted();
            return true;
        }));
        Assert.Contains("no outbox message source is registered", error.Message, StringComparison.Ordinal);
    }

    private void ApplyReference(Outbox outbox, MessageId messageId) =>
        ((IDurableValueCommandHandler<OutboxCommand>)outbox).ApplySet(
            new OutboxCommand.EnqueueReference(new() { MessageId = messageId }, new() { SentAt = fixture.JobNow }));

    private static void AssertSourceFailureDeadLetter(OutboxDeadLetter letter, MessageId messageId, SourceFault fault)
    {
        // 不存在有效的信件, 所以死信只随带信件 ID 。
        Assert.Equal(messageId, letter.Message.Envelope.MessageId);
        Assert.True(letter.Message.ReceiverId.IsDefault);
        Assert.Empty(letter.Message.Envelope.Payload);
        Assert.Null(letter.Message.Envelope.Subject);
        Assert.Null(letter.Message.Envelope.Headers);
        Assert.Null(letter.Message.Envelope.DeliverAt);
        Assert.StartsWith("The outbox message source failed to provide the message: ", letter.Reason, StringComparison.Ordinal);
        Assert.Contains(ExpectedSourceError(fault), letter.Reason, StringComparison.Ordinal);
        Assert.Equal(1, letter.AttemptCount);
    }

    private static string ExpectedSourceError(SourceFault fault) => fault switch
    {
        SourceFault.Throw => SourceSinkGrain.SourceFailure,
        SourceFault.InvalidMessage => "The payload must not be null.",
        _ => "The outbox message source returned message ID"
    };

    [Fact]
    public async Task SinkAcceptance_TrackedSentAtDeduplicatesWithoutReofferingTheSink()
    {
        var receiver = NewGrain();
        var message = NewInboxMessage("deduplicated", NewPayload());
        var extension = receiver.AsReference<IMessagingExtension>();

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await extension.DeliverAsync(message, Token));
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await extension.DeliverAsync(message, Token));
        Assert.Equal(1, Probe.For(receiver).SinkOffers);
        Assert.Equal(1, Probe.For(receiver).SinkAcceptances);
        Assert.Equal(0, await OnGrainAsync(receiver, context => Inbox(context).Count));

        fixture.Clock.Advance(MessageSourceAndSinkFixture.DeduplicationWindow);
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await extension.DeliverAsync(message, Token));
        Assert.Equal(1, Probe.For(receiver).SinkOffers);
        Assert.Equal(1, Probe.For(receiver).SinkAcceptances);
        Assert.Equal(0, Probe.For(receiver).Handled);
        Assert.Equal(0, await OnGrainAsync(receiver, context => Inbox(context).Count));
    }

    [Fact]
    public async Task SinkRejection_RecordsTheRejectionAndAnswersItsDuplicatesWithIt()
    {
        const string reason = SourceSinkGrain.Rejection;
        const string code = SourceSinkGrain.RejectionCode;
        var receiver = NewGrain();
        Probe.For(receiver).Mode = SinkMode.DeadLetter;
        var payload = NewPayload();
        var message = NewInboxMessage("rejected", payload);
        var extension = receiver.AsReference<IMessagingExtension>();
        await receiver.WriteAsync();
        using var metrics = new MessageMetricListener(await OnGrainAsync(receiver, context => context.ActivationServices.GetRequiredService<OrleansInstruments>().Meter));

        var result = await extension.DeliverAsync(message, Token);

        var rejection = DeliveryResultAssert.IsCase<DeliveryResult.DeadLettered>(result);
        Assert.Equal(reason, rejection.Reason);
        Assert.Equal(code, rejection.Code);
        var grainType = receiver.GetGrainId().Type.ToString();
        Assert.Single(metrics.Read("inbox-messages-received")).AssertCounter(grainType, "dead_lettered");
        Assert.Single(metrics.Read("inbox-messages-processed")).AssertCounter(grainType, "dead_lettered");
        var (state, count, letters) = await OnGrainAsync(receiver, context => (
            Grain(context).GetState(),
            Inbox(context).Count,
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().InboxDeadLetters));
        Assert.Empty(state.Received);
        Assert.Equal(0, count);
        // 发送者持有拒绝的电文, 因此接收者只保留拒绝。
        Assert.Empty(letters);
        Assert.Equal(0, Storage.CountPayloadOccurrences(Journal(receiver), payload));

        // 复制件在未到达汇时从处理过的记录中接收拒绝。
        Probe.For(receiver).Mode = SinkMode.Accept;
        AssertRejected(await extension.DeliverAsync(message, Token));
        Assert.Equal(1, Probe.For(receiver).SinkOffers);
        Assert.Equal(0, Probe.For(receiver).SinkAcceptances);
        Assert.Equal(0, Probe.For(receiver).Handled);

        // 拒绝后,仍是一个答案,直到一个公认的地板压缩了发送时间。
        fixture.Clock.Advance(MessageSourceAndSinkFixture.DeduplicationWindow);
        AssertRejected(await extension.DeliverAsync(message, Token));
        Assert.Equal(1, Probe.For(receiver).SinkOffers);
        Assert.Equal(0, Probe.For(receiver).SinkAcceptances);

        // 拒绝能活下来
        await receiver.DeactivateAsync();
        AssertRejected(await extension.DeliverAsync(message, Token));
        Assert.Equal(1, Probe.For(receiver).SinkOffers);

        static void AssertRejected(DeliveryResult duplicate)
        {
            var rejection = DeliveryResultAssert.IsCase<DeliveryResult.DeadLettered>(duplicate);
            Assert.Equal(reason, rejection.Reason);
            Assert.Equal(code, rejection.Code);
        }
    }

    [Theory]
    [InlineData(SinkMode.Throw, SourceSinkGrain.SinkFailure)]
    [InlineData(SinkMode.UnsupportedResult, "The inbox message sink reported the unsupported delivery result 'HandlerNotFound'.")]
    public async Task SinkFailure_FailsTheDeliveryWithoutStagingAndTheRetryIsAccepted(SinkMode mode, string error)
    {
        var receiver = NewGrain();
        Probe.For(receiver).Mode = mode;
        var payload = NewPayload();
        var message = NewInboxMessage("sink-failure", payload);
        var extension = receiver.AsReference<IMessagingExtension>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await extension.DeliverAsync(message, Token));

        Assert.Equal(error, exception.Message);
        var (state, count, letters) = await OnGrainAsync(receiver, context => (
            Grain(context).GetState(),
            Inbox(context).Count,
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().InboxDeadLetters));
        Assert.Empty(state.Received);
        Assert.Equal(0, count);
        Assert.Empty(letters);
        Assert.Equal(0, Storage.CountPayloadOccurrences(Journal(receiver), payload));

        Probe.For(receiver).Mode = SinkMode.Accept;
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await extension.DeliverAsync(message, Token));
        Assert.Equal(2, Probe.For(receiver).SinkOffers);
        Assert.Equal(1, Probe.For(receiver).SinkAcceptances);
        Assert.Equal(1, Storage.CountPayloadOccurrences(Journal(receiver), payload));
    }

    [Fact]
    public async Task SinkBackpressure_StagesNothingAndTheRetryIsAccepted()
    {
        var receiver = NewGrain();
        Probe.For(receiver).Mode = SinkMode.Backpressure;
        var payload = NewPayload();
        var message = NewInboxMessage("backpressured", payload);
        var extension = receiver.AsReference<IMessagingExtension>();

        DeliveryResultAssert.IsCase<DeliveryResult.Backpressured>(await extension.DeliverAsync(message, Token));
        var (count, letters) = await OnGrainAsync(receiver, context => (
            Inbox(context).Count,
            context.ActivationServices.GetRequiredService<IMessagingDiagnostics>().InboxDeadLetters));
        Assert.Equal(0, count);
        Assert.Empty(letters);
        Assert.Equal(0, Storage.CountPayloadOccurrences(Journal(receiver), payload));

        Probe.For(receiver).Mode = SinkMode.Accept;
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await extension.DeliverAsync(message, Token));
        Assert.Equal(2, Probe.For(receiver).SinkOffers);
        Assert.Equal(1, Probe.For(receiver).SinkAcceptances);
        Assert.Equal(0, Probe.For(receiver).Handled);
    }

    [Fact]
    public async Task ScheduledMessage_IsOfferedToTheSinkBeforeItIsDue()
    {
        var receiver = NewGrain();
        var extension = receiver.AsReference<IMessagingExtension>();
        var payload = NewPayload();
        var scheduled = NewInboxMessage("scheduled", payload, DateTimeOffset.UtcNow.AddDays(1));

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await extension.DeliverAsync(scheduled, Token));

        var (state, count) = await OnGrainAsync(receiver, context => (Grain(context).GetState(), Inbox(context).Count));
        Assert.Equal(payload, state.Received[scheduled.Envelope.MessageId.ToString()]);
        Assert.Equal(0, count);
        Assert.Equal(1, Probe.For(receiver).SinkAcceptances);
        Assert.Equal(0, Probe.For(receiver).Handled);
    }

    [Fact]
    public async Task DeclinedMessages_UseInboxAcceptanceAndHandlers()
    {
        var receiver = NewGrain();
        var extension = receiver.AsReference<IMessagingExtension>();
        Probe.For(receiver).Mode = SinkMode.Decline;

        var scheduled = NewInboxMessage("scheduled", NewPayload(), DateTimeOffset.UtcNow.AddDays(1));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await extension.DeliverAsync(scheduled, Token));
        Assert.Equal(1, await OnGrainAsync(receiver, context => Inbox(context).Count));

        var declinedPayload = NewPayload();
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await extension.DeliverAsync(NewInboxMessage("declined", declinedPayload), Token));
        await WaitUntilAsync(receiver, _ => Probe.For(receiver).Handled == 1);
        Assert.Equal(2, Probe.For(receiver).SinkOffers);
        Assert.Equal(0, Probe.For(receiver).SinkAcceptances);
        Assert.Empty((await OnGrainAsync(receiver, context => Grain(context).GetState())).Received);
        Assert.Equal(1, await OnGrainAsync(receiver, context => Inbox(context).Count));
        Assert.Equal(1, Storage.CountPayloadOccurrences(Journal(receiver), declinedPayload));
    }

    [Fact]
    public async Task SelfSentMessage_IsAcceptedByTheSinkInTheSendingWrite()
    {
        var grain = NewGrain();
        _ = await grain.GetOutboxCountAsync();
        var writes = Writes(grain).Length;
        var messageId = NewMessageId("self");
        var payload = NewPayload();

        await grain.SendAsync(grain.GetGrainId(), messageId, payload, write: true);

        var (state, inboxCount, outboxCount) = await OnGrainAsync(grain, context => (Grain(context).GetState(), Inbox(context).Count, Outbox(context).Count));
        Assert.Equal(payload, state.Received[messageId.ToString()]);
        Assert.Equal(0, inboxCount);
        Assert.Equal(0, outboxCount);
        Assert.Equal(1, Probe.For(grain).SinkAcceptances);
        Assert.Equal(writes + 1, Writes(grain).Length);
        Assert.Equal(1, Storage.CountPayloadOccurrences(Journal(grain), payload));
    }

    [Theory]
    [InlineData(SinkMode.Backpressure)]
    [InlineData(SinkMode.Throw)]
    [InlineData(SinkMode.UnsupportedResult)]
    public async Task SelfSentMessage_FailsTheSendWithoutStagingWhenTheSinkBackpressuresOrFails(SinkMode mode)
    {
        var grain = NewGrain();
        _ = await grain.GetOutboxCountAsync();
        Probe.For(grain).Mode = mode;
        var writes = Writes(grain).Length;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => grain.SendAsync(grain.GetGrainId(), NewMessageId("self-backpressured"), NewPayload(), write: true));

        var (state, inboxCount, outboxCount) = await OnGrainAsync(grain, context => (Grain(context).GetState(), Inbox(context).Count, Outbox(context).Count));
        Assert.Empty(state.Received);
        Assert.Equal(0, inboxCount);
        Assert.Equal(0, outboxCount);
        Assert.Equal(writes, Writes(grain).Length);
    }

    private ISourceSinkGrain NewGrain() => Cluster.Client.GetGrain<ISourceSinkGrain>(Guid.NewGuid());

    private static MessageId NewMessageId(string name) => MessageId.Create("source-sink", name, Guid.NewGuid().ToString("N"));

    private static byte[] NewPayload() => RandomNumberGenerator.GetBytes(48);

    private InboxMessage NewInboxMessage(string name, byte[] payload, DateTimeOffset? deliverAt = null) => new()
    {
        Envelope = new Envelope { MessageId = NewMessageId(name), Payload = payload, Subject = SourceSinkGrain.Subject, DeliverAt = deliverAt },
        SentAt = fixture.JobNow
    };

    private static JournalId Journal(IAddressable grain) => JournalId.FromGrainId(grain.GetGrainId());

    private byte[][] Writes(IAddressable grain) => Storage.GetWrites(Journal(grain));

    private static SourceSinkGrain Grain(IGrainContext context) => Assert.IsType<SourceSinkGrain>(context.GrainInstance);

    private static Inbox Inbox(IGrainContext context) => context.ActivationServices.GetRequiredService<Inbox>();

    private static Outbox Outbox(IGrainContext context) => context.ActivationServices.GetRequiredService<Outbox>();

    private Task<T> OnGrainAsync<T>(IAddressable grain, Func<IGrainContext, T> func)
    {
        if (!Cluster.TryGetGrainContext(grain.GetGrainId(), out var context))
        {
            throw new InvalidOperationException($"Grain '{grain.GetGrainId()}' is not active.");
        }

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Scheduler.QueueAction(() =>
        {
            try
            {
                done.SetResult(func(context));
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });
        return done.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
    }

    private async Task WaitUntilAsync(IAddressable grain, Func<IGrainContext, bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!Cluster.TryGetGrainContext(grain.GetGrainId(), out _) || !await OnGrainAsync(grain, predicate))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition was not met for grain '{grain.GetGrainId()}'.");
            }

            await Task.Delay(10, Token);
        }
    }
}

/// <summary>
/// 观测到一条信息的信息传递事件,
/// <see cref="MessagingEvents.InboxMessageReceiving"/>.
/// </summary>
internal sealed class DeliveryEventObserver : IObserver<MessagingEvents.MessagingEvent>, IDisposable
{
    public const string Fault = "The test injected a delivery fault.";

    private readonly GrainId _sender;
    private readonly GrainId _receiver;
    private readonly MessageId _messageId;
    private readonly IDisposable _subscription;
    private readonly ConcurrentQueue<MessagingEvents.OutboxRetryScheduled> _retries = new();
    private readonly TaskCompletionSource<MessagingEvents.OutboxRetryScheduled> _retryScheduled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<MessagingEvents.OutboxMessageRetired> _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _faults;
    private int _receivings;

    public DeliveryEventObserver(GrainId sender, GrainId receiver, MessageId messageId, int faults)
    {
        (_sender, _receiver, _messageId, _faults) = (sender, receiver, messageId, faults);
        _subscription = MessagingEvents.AllEvents.Subscribe(this);
    }

    public Task<MessagingEvents.OutboxMessageRetired> Retired => _retired.Task;
    public Task<MessagingEvents.OutboxRetryScheduled> RetryScheduled => _retryScheduled.Task;
    public IReadOnlyCollection<MessagingEvents.OutboxRetryScheduled> Retries => _retries;
    public int Receivings => Volatile.Read(ref _receivings);

    public void OnNext(MessagingEvents.MessagingEvent value)
    {
        switch (value)
        {
            case MessagingEvents.InboxMessageReceiving receiving
                when receiving.GrainContext.GrainId == _receiver && receiving.Message.Envelope.MessageId == _messageId:
                Interlocked.Increment(ref _receivings);
                if (Interlocked.Decrement(ref _faults) >= 0)
                {
                    throw new InvalidOperationException(Fault);
                }

                break;
            case MessagingEvents.OutboxRetryScheduled retry when retry.GrainContext.GrainId == _sender && retry.MessageId == _messageId:
                _retries.Enqueue(retry);
                _retryScheduled.TrySetResult(retry);
                break;
            case MessagingEvents.OutboxMessageRetired retired when retired.GrainContext.GrainId == _sender && retired.MessageId == _messageId:
                _retired.TrySetResult(retired);
                break;
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void Dispose() => _subscription.Dispose();
}

[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class JsonMessageSourceAndSinkTests(JsonMessageSourceAndSinkTests.Fixture fixture)
    : MessageSourceAndSinkTests(fixture), IClassFixture<JsonMessageSourceAndSinkTests.Fixture>
{
    public sealed class Fixture() : MessageSourceAndSinkFixture("json");
}

[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class BinaryMessageSourceAndSinkTests(BinaryMessageSourceAndSinkTests.Fixture fixture)
    : MessageSourceAndSinkTests(fixture), IClassFixture<BinaryMessageSourceAndSinkTests.Fixture>
{
    public sealed class Fixture() : MessageSourceAndSinkFixture("orleans-binary");
}

public abstract class MessageSourceAndSinkFixture(string journalFormatKey) : IAsyncLifetime
{
    public static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMinutes(10);

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public RecordingJournalStorageProvider Storage { get; } = new();
    public SourceSinkProbe Probe { get; } = new();
    public InProcessTestCluster Cluster { get; private set; } = null!;
    public DateTimeOffset JobNow => Cluster.Silos[0].ServiceProvider
        .GetRequiredKeyedService<TimeProvider>(DurableJobTimeProviderNames.DurableJobs).GetUtcNow();

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddVolatileJournalStorage();
            silo.Services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = journalFormatKey);
            silo.Services.Configure<JsonJournalOptions>(options => options.AddTypeInfoResolver(SourceSinkJsonContext.Default));
            silo.Services.AddSingleton<TimeProvider>(Clock);
            silo.Services.AddKeyedSingleton<TimeProvider>(DurableJobTimeProviderNames.DurableJobs,
                new MessagingClusterFixture.FollowingJobClock(Clock, Clock.GetUtcNow()));
            silo.Services.UseTimeProviderForBackgroundAreas(TimeProvider.System);
            silo.UseInMemoryDurableJobs();
            silo.AddMessaging(options =>
            {
                options.DeduplicationWindow = DeduplicationWindow;
                options.OutboxRetryTimeout = TimeSpan.FromSeconds(3);
                options.RetryInitialInterval = TimeSpan.FromMilliseconds(10);
            });
            silo.Services.AddSingleton(Probe);
            silo.Services.RemoveAll<IJournalStorageProvider>();
            silo.Services.RemoveAll<IJournalStorageCatalog>();
            silo.Services.AddSingleton<IJournalStorageProvider>(services =>
            {
                Storage.Configure(services.GetRequiredService<IOptions<JournaledStateManagerOptions>>());
                return Storage;
            });
            silo.Services.AddSingleton<IJournalStorageCatalog>(
                services => (IJournalStorageCatalog)services.GetRequiredService<IJournalStorageProvider>());
            silo.Services.UseDocumentationPaymentGrain();
        });
        Cluster = builder.Build();
        await Cluster.DeployAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await Cluster.DisposeAsync();
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(int))]
internal sealed partial class SourceSinkJsonContext : JsonSerializerContext;

public enum SinkMode
{
    Accept,
    Decline,
    Throw,
    DeadLetter,
    Backpressure,
    UnsupportedResult
}

/// <summary>源代码如何无法提供其存储的信息 。</summary>
public enum SourceFault
{
    Throw,
    InvalidMessage,
    MismatchedId
}

public sealed class SourceSinkProbe
{
    private readonly ConcurrentDictionary<GrainId, Counters> _counters = new();

    public Counters For(IAddressable grain) => For(grain.GetGrainId());

    public Counters For(GrainId grainId) => _counters.GetOrAdd(grainId, static _ => new());

    public sealed class Counters
    {
        private int _activations;
        private int _materializations;
        private int _sinkOffers;
        private int _sinkAcceptances;
        private int _handled;
        private int _failuresRemaining;
        private int _retirementFailuresRemaining;
        private int _retirementCallbacks;

        public volatile SinkMode Mode;
        public volatile string? SubjectOverride;

        /// <summary>使源共享其有效载荷阵列作为头值,并在电文退去时清除该阵列。</summary>
        public volatile bool ReleaseBuffersOnRetired;
        /// <summary>选择应用政策:日记终端结果和真正的自收信箱后续跟进。</summary>
        public DateTimeOffset? FollowUpAt { get; set; }
        public int RetirementCallbacks => Volatile.Read(ref _retirementCallbacks);
        public ConcurrentDictionary<MessageId, SourceFault> Faults { get; } = new();
        /// <summary>使源代码在密钥信件退出时撤回对齐信件,如果该信件尚未撤销的话。</summary>
        public ConcurrentDictionary<MessageId, MessageId> WithdrawOnRetired { get; } = new();
        public int Activations => Volatile.Read(ref _activations);
        public int Materializations => Volatile.Read(ref _materializations);
        public int SinkOffers => Volatile.Read(ref _sinkOffers);
        public int SinkAcceptances => Volatile.Read(ref _sinkAcceptances);
        public int Handled => Volatile.Read(ref _handled);

        public void OnActivated() => Interlocked.Increment(ref _activations);
        public void OnMaterialized() => Interlocked.Increment(ref _materializations);
        public void OnSinkOffered() => Interlocked.Increment(ref _sinkOffers);
        public void OnSinkAccepted() => Interlocked.Increment(ref _sinkAcceptances);
        public void OnHandled() => Interlocked.Increment(ref _handled);

        /// <summary>下个下个水槽投 <paramref name="count"/> 申请前的报价 <see cref="Mode"/>.</summary>
        public void FailNextOffers(int count) => Volatile.Write(ref _failuresRemaining, count);

        public bool TryConsumeFailure() => Interlocked.Decrement(ref _failuresRemaining) >= 0;
        public void FailNextRetirements(int count) => Volatile.Write(ref _retirementFailuresRemaining, count);
        public void OnRetiring() => Interlocked.Increment(ref _retirementCallbacks);
        public bool TryConsumeRetirementFailure() => Interlocked.Decrement(ref _retirementFailuresRemaining) >= 0;
    }
}

public interface ISourceSinkGrain : IGrainWithGuidKey
{
    Task StageFromSourceAsync(GrainId receiver, MessageId messageId, byte[] payload, bool write);
    Task StageFromSourceAndDeactivateAsync(GrainId receiver, MessageId messageId, byte[] payload);
    Task SendFromSourceAsync(MessageId messageId);
    Task WithdrawAsync(MessageId messageId);
    Task SendAsync(GrainId receiver, MessageId messageId, byte[] payload, bool write);
    Task WriteAsync();
    Task<int> GetOutboxCountAsync();
    Task DeactivateAsync();
}

/// <summary>将发送的信息作为一种发信箱源储存,并将有效载荷作为收信箱汇接收。</summary>
public sealed class SourceSinkGrain : Grain, ISourceSinkGrain, IOutboxMessageSource, IInboxMessageSink
{
    internal const string Subject = "source-sink/payload";
    internal const string PayloadHeader = "source-sink/payload-copy";
    internal const string Rejection = "The sink rejected the message.";
    internal const string RejectionCode = "source-sink/rejected";
    internal const string SinkFailure = "The sink failed before deciding the message's outcome.";
    internal const string SourceFailure = "The source can't decode the stored message.";
    internal const string FollowUpSubject = "source-sink/terminal-follow-up";
    internal const string RetirementFailure = "Injected failure after staging terminal outcome and follow-up.";
    private readonly GrainId _grainId;
    private readonly IJournaledStateManager _manager;
    private readonly IOutbox _outbox;
    private readonly IDurableDictionary<string, byte[]> _outgoing;
    private readonly IDurableDictionary<string, string> _receivers;
    private readonly IDurableDictionary<string, byte[]> _received;
    private readonly IDurableDictionary<string, string> _retired;
    private readonly IDurableDictionary<string, string> _reasons;
    private readonly IDurableDictionary<string, string> _codes;
    private readonly IDurableDictionary<string, int> _terminalCounts;
    private readonly IDurableDictionary<string, int> _followUpCounts;
    private readonly SourceSinkProbe.Counters _probe;

    public SourceSinkGrain(
        IGrainContext context,
        IJournaledStateManager manager,
        IInbox inbox,
        IOutbox outbox,
        [FromKeyedServices("outgoing")] IDurableDictionary<string, byte[]> outgoing,
        [FromKeyedServices("receivers")] IDurableDictionary<string, string> receivers,
        [FromKeyedServices("received")] IDurableDictionary<string, byte[]> received,
        [FromKeyedServices("retired")] IDurableDictionary<string, string> retired,
        [FromKeyedServices("reasons")] IDurableDictionary<string, string> reasons,
        [FromKeyedServices("codes")] IDurableDictionary<string, string> codes,
        [FromKeyedServices("terminal-counts")] IDurableDictionary<string, int> terminalCounts,
        [FromKeyedServices("follow-up-counts")] IDurableDictionary<string, int> followUpCounts,
        SourceSinkProbe probe)
    {
        _grainId = context.GrainId;
        _manager = manager;
        _outbox = outbox;
        _outgoing = outgoing;
        _receivers = receivers;
        _received = received;
        _retired = retired;
        _reasons = reasons;
        _codes = codes;
        _terminalCounts = terminalCounts;
        _followUpCounts = followUpCounts;
        _probe = probe.For(context.GrainId);
        _probe.OnActivated();
        outbox.Register(this);
        inbox.Register(this).Register(new Handler(_probe, _followUpCounts));
    }

    internal (Dictionary<string, byte[]> Outgoing, Dictionary<string, byte[]> Received, Dictionary<string, string> Retired) GetState() =>
        (new(_outgoing), new(_received), new(_retired));

    /// <summary>通过电文 ID获取此源代码中未填字母消息的原因。</summary>
    internal Dictionary<string, string> GetRetirementReasons() => new(_reasons);

    /// <summary>获取接收者拒绝此源已拒发信件的代码,通过电文 ID 获得。</summary>
    internal Dictionary<string, string> GetRetirementCodes() => new(_codes);
    internal Dictionary<string, int> GetTerminalCounts() => new(_terminalCounts);
    internal Dictionary<string, int> GetFollowUpCounts() => new(_followUpCounts);

    /// <summary>储存源的发件国未发送发件人状态的发件信息。</summary>
    internal void StoreOutgoing(GrainId receiver, MessageId messageId, byte[] payload)
    {
        _outgoing[messageId.ToString()] = payload;
        _receivers[messageId.ToString()] = receiver.ToString();
    }

    public async Task StageFromSourceAsync(GrainId receiver, MessageId messageId, byte[] payload, bool write)
    {
        _outgoing[messageId.ToString()] = payload;
        _receivers[messageId.ToString()] = receiver.ToString();
        _outbox.Send(messageId);
        if (write) await _manager.WriteStateAsync(CancellationToken.None);
    }

    public async Task StageFromSourceAndDeactivateAsync(GrainId receiver, MessageId messageId, byte[] payload)
    {
        await StageFromSourceAsync(receiver, messageId, payload, write: true);
        DeactivateOnIdle();
    }

    public Task SendFromSourceAsync(MessageId messageId)
    {
        _outbox.Send(messageId);
        return Task.CompletedTask;
    }

    public Task WithdrawAsync(MessageId messageId)
    {
        Assert.True(_outgoing.Remove(messageId.ToString()));
        Assert.True(_receivers.Remove(messageId.ToString()));
        Assert.True(_outbox.Withdraw(messageId));
        return Task.CompletedTask;
    }

    public async Task SendAsync(GrainId receiver, MessageId messageId, byte[] payload, bool write)
    {
        _outbox.Send(new OutboxMessage
        {
            Envelope = new Envelope { MessageId = messageId, Payload = payload, Subject = Subject },
            ReceiverId = receiver
        });
        if (write) await _manager.WriteStateAsync(CancellationToken.None);
    }

    public async Task WriteAsync() => await _manager.WriteStateAsync(CancellationToken.None);

    public Task<int> GetOutboxCountAsync() => Task.FromResult(_outbox.Count());

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    bool IOutboxMessageSource.TryGetMessage(MessageId messageId, out OutboxMessage message)
    {
        var key = messageId.ToString();
        if (!_outgoing.TryGetValue(key, out var payload))
        {
            message = default;
            return false;
        }

        _probe.OnMaterialized();
        message = new()
        {
            Envelope = new()
            {
                MessageId = messageId,
                Payload = payload,
                Subject = _probe.SubjectOverride ?? Subject,
                Headers = _probe.ReleaseBuffersOnRetired ? new() { [PayloadHeader] = payload } : null
            },
            ReceiverId = GrainId.Parse(_receivers[key])
        };
        if (_probe.Faults.TryGetValue(messageId, out var fault))
        {
            message = fault switch
            {
                SourceFault.Throw => throw new InvalidOperationException(SourceFailure),
                SourceFault.InvalidMessage => message with { Envelope = message.Envelope with { Payload = null! } },
                _ => message with { Envelope = message.Envelope with { MessageId = new MessageId(messageId.Key.CreateChildKey("other")) } }
            };
        }

        return true;
    }

    void IOutboxMessageSource.OnRetired(MessageId messageId, OutboxMessageRetirement retirement)
    {
        _probe.OnRetiring();
        var key = messageId.ToString();
        if (_probe.ReleaseBuffersOnRetired) Array.Clear(_outgoing[key]);
        Assert.True(_outgoing.Remove(key));
        Assert.True(_receivers.Remove(key));
        _retired[key] = retirement.Value!.GetType().Name;
        switch (retirement)
        {
            case OutboxMessageRetirement.DeadLettered { Reason: var reason }:
                _reasons[key] = reason;
                break;
            case OutboxMessageRetirement.Rejected { Reason: var reason, Code: var code }:
                _reasons[key] = reason;
                if (code is not null) _codes[key] = code;
                break;
        }
        if (_probe.WithdrawOnRetired.TryGetValue(messageId, out var paired) && _outgoing.ContainsKey(paired.ToString()))
        {
            Assert.True(_outgoing.Remove(paired.ToString()));
            Assert.True(_receivers.Remove(paired.ToString()));
            Assert.True(_outbox.Withdraw(paired));
        }
        if (_probe.FollowUpAt is { } followUpAt)
        {
            _terminalCounts.TryGetValue(key, out var previous);
            _terminalCounts[key] = previous + 1;
            _outbox.Send(new OutboxMessage
            {
                ReceiverId = _grainId,
                Envelope = new()
                {
                    MessageId = new MessageId(messageId.Key.CreateChildKey("terminal-follow-up")),
                    Payload = Encoding.UTF8.GetBytes(key),
                    Subject = FollowUpSubject,
                    DeliverAt = followUpAt
                }
            });
            if (_probe.TryConsumeRetirementFailure()) throw new InvalidOperationException(RetirementFailure);
        }
    }

    bool IInboxMessageSink.TryAccept(InboxMessage message, out DeliveryResult result)
    {
        result = default;
        if (message.Envelope.Subject != Subject) return false;
        _probe.OnSinkOffered();
        var key = message.Envelope.MessageId.ToString();
        if (_probe.TryConsumeFailure()) throw new InvalidOperationException(SinkFailure);
        switch (_probe.Mode)
        {
            case SinkMode.Decline:
                return false;
            case SinkMode.Throw:
                throw new InvalidOperationException(SinkFailure);
            case SinkMode.DeadLetter:
                result = new DeliveryResult.DeadLettered(Rejection, RejectionCode);
                return true;
            case SinkMode.Backpressure:
                result = DeliveryResult.Backpressured.Instance;
                return true;
            case SinkMode.UnsupportedResult:
                result = DeliveryResult.HandlerNotFound.Instance;
                return true;
        }

        if (_received.ContainsKey(key))
        {
            result = DeliveryResult.Duplicate.Instance;
            return true;
        }

        _received[key] = message.Envelope.Payload;
        _probe.OnSinkAccepted();
        result = DeliveryResult.Accepted.Instance;
        return true;
    }

    private sealed class Handler(SourceSinkProbe.Counters probe, IDurableDictionary<string, int> followUpCounts) : IInboxHandler
    {
        public bool CanHandle(InboxMessage message) => message.Envelope.Subject is Subject or FollowUpSubject;

        public ValueTask HandleAsync(IInboxHandlerContext context, CancellationToken cancellationToken)
        {
            probe.OnHandled();
            if (context.Message.Envelope.Subject == FollowUpSubject)
            {
                var key = Encoding.UTF8.GetString(context.Message.Envelope.Payload);
                followUpCounts.TryGetValue(key, out var previous);
                followUpCounts[key] = previous + 1;
            }
            context.Complete();
            return ValueTask.CompletedTask;
        }
    }
}

public interface INoHandlerGrain : IGrainWithGuidKey
{
    Task PingAsync();
}

/// <summary>一条信息短信 grain 没有操作员或汇, 他们的收件箱报告每个交付 作为操作员 - 找不到。</summary>
public sealed class NoHandlerGrain : Grain, INoHandlerGrain
{
    // 解决发件箱可以发送启动信息,包括收件箱。
    public NoHandlerGrain(IOutbox outbox) => ArgumentNullException.ThrowIfNull(outbox);

    public Task PingAsync() => Task.CompletedTask;
}
