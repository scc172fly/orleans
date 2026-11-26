using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

/// <summary>
/// 启动即停止,没有记录尝试的结果,因为一个处理者没有结果或
/// silo 崩溃, 其应发信件被电荷充电一次试图收回, 所以反转和 InboxRetryTimeout 绑定
/// 失败路径。此设置 InboxRetryTimeout 是零。
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

        // 第一次回收费用 中断尝试并开始重新试窗口; 重运行失败 同样的方式,
        // 下次恢复时, 将信件写成废字母, 因为窗口已经过期。 没有实施分阶段效果 。
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

        // 预定的邮件将原封不动地等待,因此解除功能不会中断。
        var envelope = CreateEnvelope(receiver, NewMessage(172, "graceful"));
        envelope = envelope with { Envelope = envelope.Envelope with { DeliverAt = Fixture.JobNow + TimeSpan.FromMinutes(1) } };
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        await receiver.ConfigureHandlerAsync(false);
        await receiver.RequestDeactivationAsync();
        await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // 信件在 grain 回收时到期,所以只有日记的干净停止才能收回。
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
