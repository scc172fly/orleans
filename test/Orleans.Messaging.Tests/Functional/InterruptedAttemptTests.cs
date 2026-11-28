using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

/// <summary>
/// 激活实例因处理程序无结果结束或 Silo 崩溃而停止，且未记录尝试结果时，
/// 恢复会为其已到期的消息各计入一次尝试，因此退避及 InboxRetryTimeout 限制所有
/// 失败路径。此测试夹具的 InboxRetryTimeout 为零。
/// </summary>
[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class InterruptedAttemptTests : MessagingBehaviorTestBase
{
    private const string InterruptedReason = InboxTestAccess.InterruptedAttemptReason;

    [Fact]
    public async Task HandlerWhichAlwaysFailsAfterStaging_IsDeadLetteredOnceItsRetryWindowElapses()
    {
        var receiver = NewGrain();
        var envelope = CreateEnvelope(receiver, NewMessage(170, "stage-then-throw") with { ThrowAfterStaging = true });

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));

        // 第一次恢复计入中断的尝试并开启重试窗口；再次运行以同样方式失败，
        // 下一次恢复因窗口已结束而将消息转为死信。没有任何暂存效果被提交。
        var final = await Fixture.WaitForDeadLetterCountAsync(receiver, 1);
        var letter = Assert.Single(final.InboxDeadLetters);
        Assert.Equal(envelope.Envelope.MessageId, letter.MessageId);
        Assert.Equal(InterruptedReason, letter.Reason);
        Assert.Equal(2, letter.AttemptCount);
        Assert.Empty(final.Effects);
        Assert.Equal(0, final.InboxCount);
    }

    [Fact]
    public async Task GracefulDeactivationWithPendingMessages_ChargesNoAttempt()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var context = Fixture.GetGrainContext(receiver);

        // 已调度的消息仍然挂起，且尚未进行尝试，因此停用没有中断任何尝试。
        var envelope = CreateEnvelope(receiver, NewMessage(172, "graceful"));
        envelope = envelope with { Envelope = envelope.Envelope with { DeliverAt = Fixture.JobNow + TimeSpan.FromMinutes(1) } };
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        await receiver.ConfigureHandlerAsync(false);
        await receiver.RequestDeactivationAsync();
        await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Grain 恢复时消息已到期，因此只有日志中记录的正常停止才能避免恢复时计入此次尝试。
        Fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        _ = await receiver.GetSnapshotAsync();
        var inbox = Fixture.GetInbox(receiver);
        var state = await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetMessageState(inbox, envelope.Envelope.MessageId));
        Assert.Equal(0, state.AttemptCount);
        Assert.Null(state.ExpirationTime);

        await receiver.ConfigureHandlerAsync(true);
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 1);
        Assert.Empty(completed.InboxDeadLetters);
    }
}
