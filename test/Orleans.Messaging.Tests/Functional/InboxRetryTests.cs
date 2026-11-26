using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class InboxRetryTests() : MessagingBehaviorTestBase(new RetryFixture())
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    // 尝试在0、1、3、7、15、31和63次重试延迟时运行;第八和最后一次的重试在超时后运行。
    private static readonly TimeSpan RetryTimeout = RetryDelay * 100;

    // 考试的进度与 grain 时无关,在“持久工作钟”之后重新试定期限。
    private ManualJobClock JobClock => ((RetryFixture)Fixture).JobClock;

    [Fact]
    public async Task HandlerFailure_RetriesOnTheDurableJobsClockAndNotTheGrainClock()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var failed = Fixture.GetGrainContext(receiver);
        var envelope = CreateEnvelope(receiver, NewMessage(197, "job-clock") with { ThrowOnceDuringPreparation = true });
        var messageId = envelope.Envelope.MessageId;
        var start = JobClock.GetUtcNow();

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        var state = await RecoverChargedAttemptAsync(receiver, failed, messageId);
        Assert.Equal(1, state.AttemptCount);
        Assert.Equal(start + RetryDelay, state.NextAttemptAt);
        Assert.Equal(start + RetryTimeout, state.ExpirationTime);

        // 将 grain 时钟移过重试超时,既不重写也不对信件进行废字母。
        Fixture.Clock.Advance(RetryTimeout * 2);
        Assert.Equal(DurableJobRunStatus.RescheduleRequested, (await Fixture.RunPumpAsync(receiver)).Status);
        var pending = await receiver.GetSnapshotAsync();
        Assert.Equal(1, pending.InboxCount);
        Assert.Empty(pending.Effects);
        Assert.Empty(pending.InboxDeadLetters);
        var inbox = Fixture.GetInbox(receiver);
        Assert.Equal(1, (await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetMessageState(inbox, messageId))).AttemptCount);

        JobClock.Advance(RetryDelay);
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(receiver)).Status);
        Assert.Equal(1, Assert.Single((await Fixture.WaitForEffectCountAsync(receiver, 1)).Effects).Count);
    }

    [Fact]
    public async Task HandlerFailure_RetriesAfterBackoffAndEarlyWakeupsAreHarmless()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var failed = Fixture.GetGrainContext(receiver);
        var envelope = CreateEnvelope(receiver, NewMessage(192, "retry") with { ThrowOnceDuringPreparation = true });
        var messageId = envelope.Envelope.MessageId;
        var now = JobClock.GetUtcNow();

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        var state = await RecoverChargedAttemptAsync(receiver, failed, messageId);
        Assert.Equal(1, state.AttemptCount);
        Assert.Equal(now + RetryDelay, state.NextAttemptAt);

        // 重试到期前的醒过来时, 不能运行操作器 。
        var early = await Fixture.RunPumpAsync(receiver);
        Assert.Equal(DurableJobRunStatus.RescheduleRequested, early.Status);
        _ = await Fixture.RunPumpAsync(receiver);
        var pending = await receiver.GetSnapshotAsync();
        Assert.Equal(1, pending.InboxCount);
        Assert.Empty(pending.Effects);

        JobClock.Advance(RetryDelay);
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(receiver)).Status);
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 1);
        Assert.Equal(1, Assert.Single(completed.Effects).Count);
        Assert.Equal(0, completed.InboxCount);
        Assert.Empty(completed.InboxDeadLetters);

        // 净化的觉醒,没有工作 立即完成 并且什么也没有改变。
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(receiver)).Status);
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, envelope));
        Assert.Equal(1, Assert.Single((await receiver.GetSnapshotAsync()).Effects).Count);
    }

    [Fact]
    public async Task TransientHandlerFailures_RetryUntilTheHandlerSucceedsWithinTheRetryTimeout()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var failed = Fixture.GetGrainContext(receiver);
        var envelope = CreateEnvelope(receiver, NewMessage(196, "transient") with { ThrowForAttempts = 6 });
        var messageId = envelope.Envelope.MessageId;
        var start = JobClock.GetUtcNow();

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        _ = await RunFailedAttemptsAsync(receiver, failed, messageId, start, failedAttempts: 6);

        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(receiver)).Status);
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 1);
        Assert.Equal(1, Assert.Single(completed.Effects).Count);
        Assert.Equal(0, completed.InboxCount);
        Assert.Empty(completed.InboxDeadLetters);
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, envelope));
    }

    /// <summary>
    /// 等待启动,其尝试结束,但无结果解除作用, grain,该费用
    /// 并返回信息电源状态
    /// </summary>
    private async Task<InboxMessageState> RecoverChargedAttemptAsync(IMessagingTestGrain receiver, IGrainContext failed, MessageId messageId)
    {
        await failed.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        _ = await receiver.GetSnapshotAsync();
        var inbox = Fixture.GetInbox(receiver);
        return await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetMessageState(inbox, messageId));
    }

    // 每次失败的尝试都未能成功启动并收回其收费。 验证每笔诉讼的重试状态和预付款
    // 下次尝试的工作时间, 其唤醒它的时间, 直到时间到达最后一次尝试
    // 失败一。返回该尝试运行的激活。
    private async Task<IGrainContext> RunFailedAttemptsAsync(
        IMessagingTestGrain receiver, IGrainContext failed, MessageId messageId, DateTimeOffset start, int failedAttempts)
    {
        var expirationTime = start + RetryTimeout;
        for (var attempt = 1; ; attempt++)
        {
            var state = await RecoverChargedAttemptAsync(receiver, failed, messageId);
            Assert.Equal(attempt, state.AttemptCount);
            Assert.Equal(expirationTime, state.ExpirationTime);
            var backoff = start + RetryDelay * ((1 << attempt) - 1);
            Assert.Equal(backoff < expirationTime ? backoff : expirationTime, state.NextAttemptAt);
            failed = Fixture.GetGrainContext(receiver);
            JobClock.Advance(state.NextAttemptAt!.Value - JobClock.GetUtcNow());
            if (attempt == failedAttempts)
            {
                return failed;
            }
        }
    }

    [Fact]
    public async Task OrdinaryHandlerFailures_RetryUntilTheRetryTimeoutThenDeadLetter()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var failed = Fixture.GetGrainContext(receiver);
        var envelope = CreateEnvelope(receiver, NewMessage(193, "always-fails") with { ThrowDuringPreparation = true });
        var messageId = envelope.Envelope.MessageId;
        var start = JobClock.GetUtcNow();

        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));

        // 每次尝试都加倍后退, 上次尝试在第一次失败之后 InboxRetryTimeout 倒数时进行。
        failed = await RunFailedAttemptsAsync(receiver, failed, messageId, start, failedAttempts: 7);
        Assert.Equal(start + RetryTimeout, JobClock.GetUtcNow());

        // 最后一次尝试也失败了 收取费用后收回的钱 也给信息写了死字
        await failed.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        _ = await receiver.GetSnapshotAsync();
        var deadLettered = await Fixture.WaitForDeadLetterCountAsync(receiver, 1);
        var deadLetter = Assert.Single(deadLettered.InboxDeadLetters);
        Assert.Equal(messageId, deadLetter.MessageId);
        Assert.Equal(InboxTestAccess.InterruptedAttemptReason, deadLetter.Reason);
        Assert.Equal(8, deadLetter.AttemptCount);
        Assert.Equal(0, deadLettered.InboxCount);
        Assert.Empty(deadLettered.Effects);
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, envelope));

        await receiver.RequestDeactivationAsync();
        var recovered = await receiver.GetSnapshotAsync();
        Assert.Equal(deadLetter, Assert.Single(recovered.InboxDeadLetters));
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, envelope));
    }

    [Fact]
    public async Task AcceptedMessage_WaitsForHandlerRegistrationWithoutDeadLettering()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var context = Fixture.GetGrainContext(receiver);
        var envelope = CreateEnvelope(receiver, NewMessage(194, "awaits-handler"), "messages/awaits-handler");
        using (var barrier = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), "messages/awaits-handler"))
        {
            DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
            await WaitForBarrierAsync(receiver, barrier);
            await receiver.ConfigureHandlerAsync(false);

            // 停止活动取消飞行企图,但结果毫无结果。
            await receiver.RequestDeactivationAsync();
            await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        // 回收费用 重复 尝试中断 即使没有处理器, 和收费开始重新试窗口
        // 而不是用死字字母来写信息
        var withoutHandler = await receiver.GetSnapshotAsync();
        Assert.Equal(1, withoutHandler.InboxCount);
        Assert.Empty(withoutHandler.Effects);
        Assert.Empty(withoutHandler.InboxDeadLetters);
        var inbox = Fixture.GetInbox(receiver);
        var state = await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetMessageState(inbox, envelope.Envelope.MessageId));
        Assert.Equal(1, state.AttemptCount);

        // 没有处理器, 待定信件将无法提醒 。
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(receiver)).Status);
        Assert.Equal(1, (await receiver.GetSnapshotAsync()).InboxCount);

        // 在同一激活上注册处理器的操作器, 将在其重试到期后恢复信件的未执行 。
        await receiver.ConfigureHandlerAsync(true);
        JobClock.Advance(state.NextAttemptAt!.Value - JobClock.GetUtcNow());
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(receiver)).Status);
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 1);
        Assert.Equal(withoutHandler.ActivationId, completed.ActivationId);
        Assert.Equal("awaits-handler", Assert.Single(completed.Effects).Value);
        DeliveryResultAssert.IsCase<DeliveryResult.Duplicate>(await DeliverAsync(receiver, envelope));
    }

    [Fact]
    public async Task WakeupWhoseJobCompletesBeforeSchedulingReturns_DoesNotSuppressLaterWakeups()
    {
        var receiver = NewGrain();
        _ = await receiver.GetSnapshotAsync();
        var pump = Fixture.GetPump(receiver);
        var target = receiver.GetGrainId();

        // 排定的工作在接到呼叫返回前完成,不再有剩余工作。
        Fixture.JobManagerProbe.SkipNext(MessagingPump.JobName);
        using var schedule = Fixture.JobManagerProbe.BlockNext(MessagingPump.JobName);
        var scheduling = await Fixture.OnGrainAsync(receiver, () => pump.EnsureWakeupAsync(DateTimeOffset.MinValue, TestContext.Current.CancellationToken).AsTask());
        await schedule.WaitUntilEnteredAsync();
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(receiver)).Status);
        schedule.Continue();
        await scheduling.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // 以后的工作安排是自己自己在工作时的觉醒时间。
        var attempts = Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, target);
        var envelope = CreateEnvelope(receiver, NewMessage(195, "later-work"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        Assert.Equal(attempts + 1, Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, target));
        Assert.Equal(1, Assert.Single((await Fixture.WaitForEffectCountAsync(receiver, 1)).Effects).Count);
    }

    private sealed class RetryFixture : MessagingClusterFixture
    {
        // 与 grain 时间,所以一个读错钟的测试 就会看到错误的最后期限
        public ManualJobClock JobClock { get; } = new(DateTimeOffset.UtcNow + TimeSpan.FromDays(3));

        protected override void ConfigureOptions(DurableMessagingOptions options)
        {
            base.ConfigureOptions(options);
            options.InboxRetryTimeout = RetryTimeout;
            options.RetryInitialInterval = RetryDelay;
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddKeyedSingleton<TimeProvider>(DurableJobTimeProviderNames.DurableJobs, JobClock);
        }
    }
}
