using Orleans.Durable.Tasks;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Fact]
    public Task DetachingASchedulableTaskWhichIsntAGrainRequestFailsTheOperation() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var starterId = TaskId.Parse("starter");
        var schedulable = new TestSchedulableTask((_, _) => new(DurableTaskResponse.Pending));
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ =>
            {
                var posted = Assert.Throws<NotSupportedException>(() => schedulable.Detached("posted").Post());
                var scheduled = await Assert.ThrowsAsync<NotSupportedException>(
                    async () => await schedulable.Detached("scheduled").ScheduleAsync(CancellationToken.None));
                return posted.Message == scheduled.Message ? posted.Message : null;
            }));

        runtime.ScheduleFromInbox(starterId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Get(starterId).Result is { IsCompleted: true });

        // 抽象层允许分离任何可调度任务，但此宿主仅启动 Grain 请求，目标会将其序列化请求
        // 恢复为根任务，因此每次启动都在执行实例记录或发送任何内容之前失败。
        Assert.Contains("durable grain calls", storage.Get(starterId).Result!.GetResult<string?>(), StringComparison.Ordinal);
        Assert.Equal(0, schedulable.ScheduleAsyncCallCount);
        Assert.Empty(transport.DetachedInvocations);
        Assert.Equal([starterId], storage.Tasks.Select(task => task.Id));
    });
}
