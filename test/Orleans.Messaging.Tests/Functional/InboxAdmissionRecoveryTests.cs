using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text.Json;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Orleans.Serialization;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

/// <summary>通过两个支持的日记编码运行持续存取的允许边界。</summary>
[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class InboxAdmissionRecoveryTests
{
    [Theory]
    [InlineData("orleans-binary", false)]
    [InlineData("orleans-binary", true)]
    [InlineData("json", false)]
    [InlineData("json", true)]
    public async Task SentAtAndAdmissionFloor_JournalAndSnapshotRecoveryPreserveFencing(string format, bool snapshot)
    {
        await using var fixture = new FaultInjectionClusterFixture(format);
        await fixture.InitializeAsync();
        var receiver = fixture.Client.GetGrain<ISourceSinkGrain>(Guid.NewGuid());
        var started = fixture.JobClock.GetUtcNow();
        var original = Message("old", started);
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, original));
        await fixture.CrashAsync(receiver);
        Assert.Equal(0, await receiver.GetOutboxCountAsync());
        var inbox = fixture.GetGrainContext(receiver).ActivationServices.GetRequiredService<Inbox>();
        Assert.Equal(started, await fixture.OnGrainAsync(receiver, () =>
            InboxTestAccess.GetProcessedSentAt(inbox, original.Envelope.MessageId)));
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, original));

        fixture.JobClock.Advance(TimeSpan.FromDays(2));
        var floor = fixture.JobClock.GetUtcNow() - TimeSpan.FromDays(1);
        var retained = Message("recent", fixture.JobClock.GetUtcNow());
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, retained));
        if (snapshot) fixture.Storage.RequestSnapshot(JournalId.FromGrainId(receiver.GetGrainId()));
        // 日记命令(或其快照)可持久地推进同一层楼。
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, Message("expired-by-floor", floor)));
        await fixture.CrashAsync(receiver);
        fixture.JobClock.Advance(started - fixture.JobClock.GetUtcNow());
        Assert.Equal(0, await receiver.GetOutboxCountAsync());
        inbox = fixture.GetGrainContext(receiver).ActivationServices.GetRequiredService<Inbox>();
        Assert.Equal(floor, await fixture.OnGrainAsync(receiver, () => InboxTestAccess.AdmissionFloor(inbox)));
        Assert.DoesNotContain(original.Envelope.MessageId,
            await fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox)));
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, original));
        // 两天后,最近的记录才开始制作;在重播记录之前恢复一个合法的接收器钟。
        fixture.JobClock.Advance(retained.SentAt - fixture.JobClock.GetUtcNow());
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, retained));
        Assert.Equal(retained.SentAt, await fixture.OnGrainAsync(receiver, () =>
            InboxTestAccess.GetProcessedSentAt(inbox, retained.Envelope.MessageId)));
        Assert.Equal(2, fixture.SourceSinkProbe.For(receiver).SinkOffers);
        Assert.Equal(2, fixture.SourceSinkProbe.For(receiver).SinkAcceptances);
    }

    [Fact]
    public void PersistedOutboxSendTime_UsesAStableFieldId()
    {
        Assert.Equal(3U, typeof(OutboxMessageState).GetProperty(nameof(OutboxMessageState.SentAt))!.GetCustomAttribute<IdAttribute>()!.Id);
        Assert.Null(typeof(OutboxMessageState).GetProperty("ExpirationTime"));
    }

    private static InboxMessage Message(string name, DateTimeOffset sentAt) => new()
    {
        SentAt = sentAt,
        Envelope = new()
        {
            MessageId = MessageId.Create("admission-recovery", name, Guid.NewGuid().ToString("N")),
            Payload = [1, 2, 3],
            Subject = SourceSinkGrain.Subject
        }
    };

    private static async Task<DeliveryResult> DeliverAsync(ISourceSinkGrain receiver, InboxMessage message) =>
        await receiver.AsReference<IMessagingExtension>().DeliverAsync(message, TestContext.Current.CancellationToken);
}
