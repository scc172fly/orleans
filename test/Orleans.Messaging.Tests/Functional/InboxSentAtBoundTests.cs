using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Durable.Messaging.Tests.Support;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

/// <summary>
/// 固定收件箱评估投递发送时间的规则：未来时间上限仅适用于本来会被准入的未跟踪标识；
/// 已记录的标识根据其记录答复；未跟踪标识的发送时间若处于持久化准入
/// 下限或更早，则视为过期。
/// </summary>
[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class InboxSentAtBoundTests() : MessagingBehaviorTestBase(new SentAtBoundFixture())
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    // 只有在读取时钟与投递之间保持时钟不变，才能精确验证该边界。
    private ManualJobClock JobClock => ((SentAtBoundFixture)Fixture).JobClock;

    [Fact]
    public async Task SentAtBeyondTheBound_ThrowsWithoutStagingAndTheBoundItselfIsAdmitted()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        var inbox = Fixture.GetInbox(receiver);
        var bound = Fixture.JobNow + Window;
        var message = CreateEnvelope(receiver, NewMessage(1, "future-bound")) with { SentAt = bound };
        var writes = Fixture.Storage.GetSuccessfulWriteCount(journal);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => DeliverAsync(receiver, message with { SentAt = bound.AddTicks(1) }));
        Assert.Equal(writes, Fixture.Storage.GetSuccessfulWriteCount(journal));
        Assert.Equal(0, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetPendingMessages(inbox).Count));
        Assert.Equal(0, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedCount(inbox)));

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, message));
        await Fixture.WaitForInboxCountAsync(receiver, 0);
        await receiver.RetryWriteStateAsync();
        writes = Fixture.Storage.GetSuccessfulWriteCount(journal);

        // 已记录的标识根据其记录答复，不论本次投递的发送时间为何。
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, message with { SentAt = bound.AddTicks(1) }));
        Assert.Equal(writes, Fixture.Storage.GetSuccessfulWriteCount(journal));
        Assert.Equal(bound, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedSentAt(inbox, message.Envelope.MessageId)));
        Assert.Equal(1, Assert.Single((await receiver.GetSnapshotAsync()).Effects).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedIdentity_IsAnsweredFromItsRecordAfterClockRollbackPutsItBeyondTheBound(bool completed)
    {
        var receiver = NewGrain();
        const string route = "messages/sent-time-rollback";
        using var handler = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), route);
        var message = CreateEnvelope(receiver, NewMessage(2, "rolled-back"), route);
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, message));
        await WaitForBarrierAsync(receiver, handler);
        if (completed)
        {
            handler.Release();
            await Fixture.WaitForInboxCountAsync(receiver, 0);
        }

        await receiver.RetryWriteStateAsync();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        var inbox = Fixture.GetInbox(receiver);
        var writes = Fixture.Storage.GetSuccessfulWriteCount(journal);
        JobClock.Advance(-(Window + TimeSpan.FromTicks(1)));
        Assert.True(message.SentAt > Fixture.JobNow + Window);

        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, message));
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, message with { SentAt = message.SentAt.AddTicks(-1) }));
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, message with { SentAt = message.SentAt.AddTicks(1) }));
        Assert.Equal(writes, Fixture.Storage.GetSuccessfulWriteCount(journal));
        Assert.Equal(message.SentAt, await Fixture.OnGrainAsync(receiver, () => completed
            ? InboxTestAccess.GetProcessedSentAt(inbox, message.Envelope.MessageId)
            : InboxTestAccess.GetPendingMessages(inbox)[message.Envelope.MessageId].Message.SentAt));

        handler.Release();
        await Fixture.WaitForInboxCountAsync(receiver, 0);
        Assert.Equal(1, Assert.Single((await receiver.GetSnapshotAsync()).Effects).Count);
    }

    [Fact]
    public async Task UntrackedIdentityAtTheDurableFloor_ExpiresAfterClockRollbackPutsItBeyondTheBound()
    {
        var receiver = NewGrain();
        var floor = Fixture.JobNow - Window;
        var advancing = CreateEnvelope(receiver, NewMessage(3, "advances-floor")) with { SentAt = floor };
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, advancing));
        var inbox = Fixture.GetInbox(receiver);
        Assert.Equal(floor, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox)));

        JobClock.Advance(-(Window + Window + TimeSpan.FromTicks(1)));
        var untracked = CreateEnvelope(receiver, NewMessage(4, "at-floor")) with { SentAt = floor };
        Assert.True(untracked.SentAt > Fixture.JobNow + Window);

        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, untracked));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => DeliverAsync(receiver, untracked with { SentAt = floor.AddTicks(1) }));
        Assert.Empty((await receiver.GetSnapshotAsync()).Effects);
    }

    private sealed class SentAtBoundFixture : MessagingClusterFixture
    {
        public ManualJobClock JobClock { get; } = new(DateTimeOffset.UtcNow);

        protected override void ConfigureOptions(DurableMessagingOptions options)
        {
            base.ConfigureOptions(options);
            options.DeduplicationWindow = Window;
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddKeyedSingleton<TimeProvider>(DurableJobTimeProviderNames.DurableJobs, JobClock);
        }
    }
}
