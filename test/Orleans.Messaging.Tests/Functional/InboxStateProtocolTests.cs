using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class InboxStateProtocolTests : MessagingBehaviorTestBase
{
    [Fact]
    public async Task RawWriteDuringLocalPreparation_CapturesNoHandlerEffects()
    {
        var receiver = NewGrain();
        const string route = "messages/local-preparation";
        using var handler = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), route);
        var envelope = CreateEnvelope(receiver, NewMessage(201, "local"), route);
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        await handler.WaitUntilEnteredAsync();
        var context = Fixture.GetGrainContext(receiver);
        var grain = Assert.IsType<MessagingTestGrain>(context.GrainInstance);
        var manager = context.ActivationServices.GetRequiredService<IJournaledStateManager>();
        await OnTurnAsync(context, () => context.ActivationServices.GetRequiredKeyedService<IDurableValue<string>>("inbox").Value = "raw-write");
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.False(grain.ApplyAttempted.Task.IsCompleted);
        var beforeApply = grain.GetSnapshotForTest();
        Assert.Empty(beforeApply.Effects);
        Assert.Equal(1, beforeApply.InboxCount);
        Assert.Equal(0, beforeApply.ProcessedMessageCount);
        var captured = grain.Captures[^1];
        Assert.Empty(captured.Effects);
        Assert.Equal(1, captured.InboxCount);
        Assert.Equal(0, captured.ProcessedMessageCount);
        handler.Release();
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 1);
        Assert.Equal(0, completed.InboxCount);
        Assert.Equal(1, completed.ProcessedMessageCount);
        Assert.Equal(1, Assert.Single(completed.Effects).Count);
        await receiver.RequestDeactivationAsync();
        var replayed = await receiver.GetSnapshotAsync();
        Assert.Equal(completed.Effects, replayed.Effects);
        Assert.Equal(1, replayed.ProcessedMessageCount);
    }

    [Fact]
    public async Task HandlerFailureBeforeComplete_FailsActivationAndRecoveryRunsTheMessageAgain()
    {
        var receiver = NewGrain();
        var before = await receiver.GetSnapshotAsync();
        const string route = "messages/no-earlier-write";
        using var handler = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), route);
        var envelope = CreateEnvelope(receiver, NewMessage(203, "no-earlier-write"), route);
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        await handler.WaitUntilEnteredAsync();
        var context = Fixture.GetGrainContext(receiver);
        var grain = Assert.IsType<MessagingTestGrain>(context.GrainInstance);
        var manager = context.ActivationServices.GetRequiredService<IJournaledStateManager>();
        var journal = JournalId.FromGrainId(receiver.GetGrainId());
        var writes = Fixture.Storage.GetSuccessfulWriteCount(journal);
        var failure = new IOException("Local preparation failed before Complete.");
        await OnTurnAsync(context, () =>
        {
            Assert.Equal(0, manager.PendingWriteByteCount);
            grain.NextApplyFailure = failure;
        });

        // 此次尝试在没有结果的情况下结束，使激活实例失败且不进行写入，即使它未暂存任何操作也是如此。
        handler.Release();
        var error = Assert.IsType<InvalidOperationException>(
            await grain.DeactivationFailure.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Same(failure, error.InnerException);
        await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(writes, Fixture.Storage.GetSuccessfulWriteCount(journal));

        // 恢复时计入此次尝试，开启其重试窗口但不将消息转为死信，即使此测试夹具的
        // InboxRetryTimeout 为零也是如此；再次运行成功。
        _ = await receiver.GetSnapshotAsync();
        var completed = await Fixture.WaitForEffectCountAsync(receiver, 1);
        Assert.NotEqual(before.ActivationId, completed.ActivationId);
        Assert.Equal(1, Assert.Single(completed.Effects).Count);
        Assert.Empty(completed.InboxDeadLetters);
        Assert.Equal(0, completed.InboxCount);
        Assert.Equal(1, completed.ProcessedMessageCount);
    }

    private static Task OnTurnAsync(IGrainContext context, Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Scheduler.QueueAction(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        return completion.Task;
    }
}
