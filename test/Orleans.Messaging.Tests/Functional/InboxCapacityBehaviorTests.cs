using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Orleans.Serialization.Session;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InboxCapacityCollection : ICollectionFixture<InboxCapacityClusterFixture>
{
    public const string Name = "Messaging inbox capacity";
}

[Collection(InboxCapacityCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class InboxCapacityBehaviorTests(InboxCapacityClusterFixture fixture)
{
    [Fact]
    public async Task InboxAtCapacity_BackpressuresWithoutPersistenceAndRecoversWhenCapacityFrees()
    {
        var receiver = fixture.Client.GetGrain<IMessagingTestGrain>(Guid.NewGuid());
        var sessions = fixture.Client.ServiceProvider.GetRequiredService<SerializerSessionPool>();
        var sender = GrainId.Create("capacity-test-sender", Guid.NewGuid().ToString("N"));
        var poison = TestApplicationProtocol.CreateInbox(sessions, sender, "messages/capacity", new TestMessage(TestApplicationProtocol.NewMessageId(), 31, "poison", ThrowDuringPreparation: true), fixture.JobNow);
        var rejected = TestApplicationProtocol.CreateInbox(sessions, sender, "messages/capacity", new TestMessage(TestApplicationProtocol.NewMessageId(), 32, "accepted-after-capacity"), fixture.JobNow);
        const string processedInstrument = "orleans-messaging-inbox-messages-processed";
        var firstRetryAcknowledged = fixture.Metrics.WaitForCountAsync(processedInstrument, 1, "retry");
        _ = await receiver.GetSnapshotAsync();
        var failed = fixture.GetGrainContext(receiver);

        // 毒药的尝试失败了, 恢复费用也随之增加; 信息保留着唯一的位置。
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, poison));
        await failed.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        DeliveryResultAssert.IsCase<DeliveryResult.Backpressured>(await DeliverAsync(receiver, rejected));
        var full = await receiver.GetSnapshotAsync();
        Assert.Equal(1, full.InboxCount);
        Assert.Empty(full.Effects);

        await firstRetryAcknowledged;
        Assert.Equal(1, fixture.Metrics.GetCount(processedInstrument, "retry"));
        var inbox = fixture.GetInbox(receiver);
        var state = await fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetMessageState(inbox, poison.Envelope.MessageId));
        Assert.Equal(1, state.AttemptCount);
        var nextAttempt = Assert.IsType<DateTimeOffset>(state.NextAttemptAt);
        Assert.Equal(fixture.Clock.GetUtcNow() + TimeSpan.FromHours(1), nextAttempt);

        // 重新尝试在窗口关闭后运行,再次失败;收取的回收费将信息写成死字。
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        failed = fixture.GetGrainContext(receiver);
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, poison));
        await failed.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, rejected));
        var deadLettered = await receiver.GetSnapshotAsync();
        Assert.Equal(0, deadLettered.InboxCount);
        Assert.Equal(2, Assert.Single(deadLettered.InboxDeadLetters).AttemptCount);
        rejected = rejected with { SentAt = fixture.JobNow };
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, rejected));
        var recovered = await fixture.WaitForEffectCountAsync(receiver, 1);
        Assert.Equal("accepted-after-capacity", Assert.Single(recovered.Effects).Value);
        Assert.Equal(2, Assert.Single(recovered.InboxDeadLetters).AttemptCount);
    }

    private static async Task<DeliveryResult> DeliverAsync(
        IMessagingTestGrain receiver,
        InboxMessage envelope) =>
        await receiver.AsReference<IMessagingExtension>().DeliverAsync(envelope);
}
