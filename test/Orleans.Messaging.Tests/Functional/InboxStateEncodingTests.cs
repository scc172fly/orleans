using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Tests.Support;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class InboxStateEncodingTests : MessagingBehaviorTestBase
{
    public InboxStateEncodingTests() : base(new FaultingInboxCodecFixture()) { }

    [Fact]
    public async Task CompleteCommandCodecFailure_PreservesOriginalCauseAndReplaysAcknowledgedState()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var context = Fixture.GetGrainContext(receiver);
        var grain = Assert.IsType<MessagingTestGrain>(context.GrainInstance);
        var outbox = (JournaledTestOutbox)context.ActivationServices.GetRequiredService<IOutbox>();
        using var handler = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), "messages/complete-codec-fault");
        var envelope = CreateEnvelope(receiver, NewMessage(204, "complete-codec-fault"), "messages/complete-codec-fault");
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        await handler.WaitUntilEnteredAsync();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        var writes = Fixture.Storage.GetSuccessfulWriteCount(journal);
        var failure = new IOException("Completion remove command failed.");
        ((FaultingInboxCodecFixture)Fixture).NextFailure = failure;
        handler.Release();
        Assert.Same(failure, await grain.DeactivationFailure.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(writes, Fixture.Storage.GetSuccessfulWriteCount(journal));
        Assert.Empty(outbox.Messages);
        _ = await receiver.GetSnapshotAsync();
        var fresh = Assert.IsType<MessagingTestGrain>(Fixture.GetGrainContext(receiver).GrainInstance);
        var replayed = Assert.IsType<EndpointSnapshot>(fresh.ReplayedSnapshot);
        Assert.Equal(1, replayed.InboxCount);
        Assert.Equal(0, replayed.ProcessedMessageCount);
        Assert.Empty(replayed.Effects);
        Assert.Equal(0, replayed.OutboxCount);
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 1);
        Assert.Equal(1, Assert.Single(completed.Effects).Count);
        Assert.Equal(1, completed.ProcessedMessageCount);
        Assert.Empty(completed.InboxDeadLetters);
    }

    [Fact]
    public async Task InterruptedAttemptCodecFailure_FailsRecoveryAndALaterActivationReplaysTheMessage()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var context = Fixture.GetGrainContext(receiver);
        var grain = Assert.IsType<MessagingTestGrain>(context.GrainInstance);
        using var handler = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), "messages/error-codec-fault");
        var message = NewMessage(205, "error-codec-fault") with { ThrowOnceDuringPreparation = true };
        var envelope = CreateEnvelope(receiver, message, "messages/error-codec-fault");
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        await handler.WaitUntilEnteredAsync();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        var writes = Fixture.Storage.GetSuccessfulWriteCount(journal);

        // 失败的尝试没有进入命令, 所以下一个是回收阶段的收费。
        var faulting = (FaultingInboxCodecFixture)Fixture;
        faulting.NextFailure = new IOException("Interrupted attempt command failed.");
        handler.Release();
        var failure = Assert.IsType<InvalidOperationException>(
            await grain.DeactivationFailure.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Contains("ended without an outcome", failure.Message, StringComparison.Ordinal);
        await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(writes, Fixture.Storage.GetSuccessfulWriteCount(journal));

        // 启动程序,其电荷未能编码 无法启动, 后一个 指控试图和重播
        // 已接受消息, 成功的消息 。
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 1);
        Assert.False(faulting.FailedSnapshot);
        Assert.Null(faulting.NextFailure);
        Assert.Equal(1, Assert.Single(completed.Effects).Count);
        Assert.Equal(1, completed.ProcessedMessageCount);
        Assert.Empty(completed.InboxDeadLetters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InboxCodecFailure_PreventsStorageAndRetriedDeliveryProcessesOnce(bool snapshot)
    {
        var receiver = NewGrain();
        var before = await receiver.GetSnapshotAsync();
        var context = Fixture.GetGrainContext(receiver);
        var grain = Assert.IsType<MessagingTestGrain>(context.GrainInstance);
        var outbox = (JournaledTestOutbox)context.ActivationServices.GetRequiredService<IOutbox>();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        var writes = Fixture.Storage.GetSuccessfulWriteCount(journal);
        var failure = new IOException("Injected inbox command codec failure.");
        var faulting = (FaultingInboxCodecFixture)Fixture;
        faulting.NextFailure = failure;
        faulting.FailOnSnapshot = snapshot;
        if (snapshot) Fixture.Storage.RequestSnapshot(journal);
        var envelope = CreateEnvelope(receiver, NewMessage(203, "codec"));
        await Assert.ThrowsAsync<IOException>(() => DeliverAsync(receiver, envelope));
        Assert.Equal(snapshot, faulting.FailedSnapshot);
        Assert.Empty(outbox.Messages);
        Assert.Equal(writes, Fixture.Storage.GetSuccessfulWriteCount(journal));
        if (snapshot)
        {
            // 笔记本在接受后被编码时失败,所以放弃激活。
            Assert.Same(failure, await grain.DeactivationFailure.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.Equal(1, grain.GetSnapshotForTest().InboxCount);
            Assert.Empty(grain.GetSnapshotForTest().Effects);
            var recovered = await receiver.GetSnapshotAsync();
            Assert.NotEqual(before.ActivationId, recovered.ActivationId);
            Assert.Equal(0, recovered.InboxCount);
            Assert.Empty(recovered.Effects);
            Assert.Equal(0, recovered.ProcessedMessageCount);
        }
        else
        {
            // 命令在应用前就编码,所以被拒绝的接受使得激活状态保持不变。
            Assert.False(grain.DeactivationFailure.Task.IsCompleted);
            var current = await receiver.GetSnapshotAsync();
            Assert.Equal(before.ActivationId, current.ActivationId);
            Assert.Equal(0, current.InboxCount);
            Assert.Empty(current.Effects);
            Assert.Equal(0, current.ProcessedMessageCount);
        }

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        Assert.Equal(1, Assert.Single((await Fixture.WaitForEffectCountAsync(receiver, 1)).Effects).Count);
    }
}
