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

        // 抽象解脱任何可排出的任务, 而此主机只开始 grain 请求, 其序列化请求
        // 目标恢复为根任务, 所以在执行记录或发送任何信息之前, 每个目标开始失败 。
        Assert.Contains("durable grain calls", storage.Get(starterId).Result!.GetResult<string?>(), StringComparison.Ordinal);
        Assert.Equal(0, schedulable.ScheduleAsyncCallCount);
        Assert.Empty(transport.DetachedInvocations);
        Assert.Equal([starterId], storage.Tasks.Select(task => task.Id));
    });
}
