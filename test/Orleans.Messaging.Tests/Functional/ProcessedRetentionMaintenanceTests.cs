using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class ProcessedRetentionMaintenanceTests : MessagingBehaviorTestBase
{
    [Fact]
    public async Task SnapshotWrite_PrunesExpiredProcessedIdsAndRecoveryRetainsCurrentOnes()
    {
        var receiver = NewGrain();
        var old = CreateEnvelope(receiver, NewMessage(120, "old"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, old));
        await Fixture.WaitForEffectCountAsync(receiver, 1);
        Fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var current = CreateEnvelope(receiver, NewMessage(121, "current"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, current));
        await Fixture.WaitForEffectCountAsync(receiver, 2);
        Fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(receiver.GetGrainId()));
        var fresh = CreateEnvelope(receiver, NewMessage(122, "fresh"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, fresh));
        await Fixture.WaitForEffectCountAsync(receiver, 3);

        var processed = await GetProcessedAsync(receiver);
        Assert.DoesNotContain(old.Envelope.MessageId, processed);
        Assert.Contains(current.Envelope.MessageId, processed);
        Assert.Contains(fresh.Envelope.MessageId, processed);

        await ReactivateAsync(receiver);
        var recovered = await GetProcessedAsync(receiver);
        Assert.True(processed.SetEquals(recovered));
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, old));
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, current));
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, fresh));
        Assert.All((await receiver.GetSnapshotAsync()).Effects, static effect => Assert.Equal(1, effect.Count));
    }

    [Fact]
    public async Task SnapshotCompaction_RemovesExpiredRecordsInterleavedWithRetainedOnesInOnePass()
    {
        // 压缩清除处理的记录,同时进行清点,在框架上予以词典支持
        // 完成命令首先存放过期记录,然后保存在保留的记录之间。
        var receiver = NewGrain();
        var first = CreateEnvelope(receiver, NewMessage(140, "first-expired"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, first));
        await Fixture.WaitForEffectCountAsync(receiver, 1);
        Fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var kept = CreateEnvelope(receiver, NewMessage(141, "kept"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, kept));
        await Fixture.WaitForEffectCountAsync(receiver, 2);
        var middle = CreateEnvelope(receiver, NewMessage(142, "middle-expired")) with { SentAt = first.SentAt };
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, middle));
        await Fixture.WaitForEffectCountAsync(receiver, 3);
        var alsoKept = CreateEnvelope(receiver, NewMessage(143, "also-kept"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, alsoKept));
        await Fixture.WaitForEffectCountAsync(receiver, 4);
        Assert.Equal(4, (await GetProcessedAsync(receiver)).Count);
        Fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(receiver.GetGrainId()));
        var fresh = CreateEnvelope(receiver, NewMessage(144, "fresh"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, fresh));
        await Fixture.WaitForEffectCountAsync(receiver, 5);

        var processed = await GetProcessedAsync(receiver);
        Assert.True(processed.SetEquals([kept.Envelope.MessageId, alsoKept.Envelope.MessageId, fresh.Envelope.MessageId]));
        var snapshot = await receiver.GetSnapshotAsync();
        Assert.Equal(3, snapshot.ProcessedMessageCount);
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, middle));
    }

    [Fact]
    public async Task SentAt_ExpiresOnlyAfterSnapshotAndRecoveryPreserveTheFloor()
    {
        var receiver = NewGrain();
        var message = CreateEnvelope(receiver, NewMessage(130, "expiring"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, message));
        await Fixture.WaitForEffectCountAsync(receiver, 1);

        Fixture.Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1));
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, message));

        Fixture.Clock.Advance(TimeSpan.FromTicks(1));
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, message));
        Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(receiver.GetGrainId()));
        await receiver.RetryWriteStateAsync();
        await ReactivateAsync(receiver);
        DeliveryResultAssert.IsCase<DeliveryResult.Expired>(await DeliverAsync(receiver, message));
        Assert.Equal(1, Assert.Single((await receiver.GetSnapshotAsync()).Effects).Count);
    }

    private Task<HashSet<MessageId>> GetProcessedAsync(IMessagingTestGrain receiver)
    {
        var inbox = Fixture.GetInbox(receiver);
        return Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox));
    }
}

[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class MaximumDeduplicationWindowTests() : MessagingBehaviorTestBase(new MaximumWindowClusterFixture())
{
    [Fact]
    public async Task MaximumWindow_ClampsTheAdmissionFloorThroughDeliveryAndSnapshotCompaction()
    {
        // 校验接受任何正窗口, 所以最大一个是有效的配置: 下层计算
        // 以时钟和夹子为单位,在最短的时间内将其减掉,而不是溢出。
        var receiver = NewGrain();
        var message = CreateEnvelope(receiver, NewMessage(160, "maximum-window"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, message));
        await Fixture.WaitForEffectCountAsync(receiver, 1);

        Fixture.Storage.RequestSnapshot(JournalId.FromGrainId(receiver.GetGrainId()));
        await receiver.RetryWriteStateAsync();
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, message));

        var inbox = Fixture.GetInbox(receiver);
        var floor = await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.DurableAdmissionFloor(inbox));
        Assert.Equal(DateTimeOffset.MinValue, floor);
        Assert.Contains(message.Envelope.MessageId, await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetProcessedKeys(inbox)));
    }

    private sealed class MaximumWindowClusterFixture : MessagingClusterFixture
    {
        protected override void ConfigureOptions(DurableMessagingOptions options)
        {
            base.ConfigureOptions(options);
            options.DeduplicationWindow = TimeSpan.MaxValue;
        }
    }
}
