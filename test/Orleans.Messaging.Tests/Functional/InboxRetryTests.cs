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

    // 尝试分别在 0、1、3、7、15、31 和 63 个重试延迟单位时运行；第八次也是最后一次，在超时到期时运行。
    private static readonly TimeSpan RetryTimeout = RetryDelay * 100;

    // 重试截止时间以 Durable Jobs 时钟为准，这些测试独立于 Grain 时钟推进该时钟。
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

        // 将 Grain 时钟推进至重试超时之后，既不会重试，也不会将消息转为死信。
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

        // 重试到期之前的唤醒不得运行处理程序。
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

        // 不存在待处理工作时，虚假唤醒立即完成，不改变任何状态。
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
    /// 等待尝试在没有结果时结束的激活实例停用，再重新激活 Grain，由此计入
    /// 尝试次数并返回消息计次后的状态。
    /// </summary>
    private async Task<InboxMessageState> RecoverChargedAttemptAsync(IMessagingTestGrain receiver, IGrainContext failed, MessageId messageId)
    {
        await failed.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        _ = await receiver.GetSnapshotAsync();
        var inbox = Fixture.GetInbox(receiver);
        return await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetMessageState(inbox, messageId));
    }

    // 每次失败的尝试都会使其激活实例失败，恢复时计入该次尝试。验证每次计次后的重试状态，并将
    // 作业时钟推进到下一次尝试的时间，由其唤醒触发该尝试，直到时钟到达最后一次失败
    // 之后的尝试时间。返回运行该次尝试的激活实例。
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

        // 每次尝试的退避时间翻倍，最后一次尝试在首次失败后经过 InboxRetryTimeout 时运行。
        failed = await RunFailedAttemptsAsync(receiver, failed, messageId, start, failedAttempts: 7);
        Assert.Equal(start + RetryTimeout, JobClock.GetUtcNow());

        // 最后一次尝试也失败，计入此次尝试的恢复过程将消息转为死信。
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

            // 停用会取消正在进行的尝试，该尝试在没有结果的情况下结束。
            await receiver.RequestDeactivationAsync();
            await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        // 即使没有处理程序，恢复过程也会计入被中断的尝试，此次计次会开启重试窗口，
        // 而不会将消息转为死信。
        var withoutHandler = await receiver.GetSnapshotAsync();
        Assert.Equal(1, withoutHandler.InboxCount);
        Assert.Empty(withoutHandler.Effects);
        Assert.Empty(withoutHandler.InboxDeadLetters);
        var inbox = Fixture.GetInbox(receiver);
        var state = await Fixture.OnGrainAsync(receiver, () => InboxTestAccess.GetMessageState(inbox, envelope.Envelope.MessageId));
        Assert.Equal(1, state.AttemptCount);

        // 没有处理程序时，挂起消息不保有唤醒作业。
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(receiver)).Status);
        Assert.Equal(1, (await receiver.GetSnapshotAsync()).InboxCount);

        // 在同一激活实例上注册处理程序后，挂起消息将在重试到期时恢复处理。
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

        // 已调度的作业在调度调用返回之前就已运行完毕，且没有剩余工作。
        Fixture.JobManagerProbe.SkipNext(MessagingPump.JobName);
        using var schedule = Fixture.JobManagerProbe.BlockNext(MessagingPump.JobName);
        var scheduling = await Fixture.OnGrainAsync(receiver, () => pump.EnsureWakeupAsync(DateTimeOffset.MinValue, TestContext.Current.CancellationToken).AsTask());
        await schedule.WaitUntilEnteredAsync();
        Assert.Equal(DurableJobRunStatus.Completed, (await Fixture.RunPumpAsync(receiver)).Status);
        schedule.Continue();
        await scheduling.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // 后续工作会调度自己的唤醒作业。
        var attempts = Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, target);
        var envelope = CreateEnvelope(receiver, NewMessage(195, "later-work"));
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        Assert.Equal(attempts + 1, Fixture.JobManagerProbe.GetAttemptCount(MessagingPump.JobName, target));
        Assert.Equal(1, Assert.Single((await Fixture.WaitForEffectCountAsync(receiver, 1)).Effects).Count);
    }

    private sealed class RetryFixture : MessagingClusterFixture
    {
        // 与 Grain 时钟相差数天，因此使用错误时钟的测试会得到错误的截止时间。
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
