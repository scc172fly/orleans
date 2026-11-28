using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 验证持久化执行实例中的 <c>ScheduledTask.WhenAny</c> 按完成序号进行选择，因此在
/// 替代激活实例中重放时，无须记录选择决定即可返回实际执行尝试中的胜出任务。
/// </summary>
public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public Task ScheduledTaskWhenAnyReplaysTheLiveWinnerWithoutADecisionRecord(int stagedFirst, int stagedSecond) => ActivationScheduler.RunAsync(async () =>
    {
        var manager = new TestStateManager();
        var storage = new TestStorage(manager);
        var (runtime, _, _, _) = CreateRuntime(storage, manager, reentrant: true);
        var rootId = TaskId.CreateRoot("root");
        var batchId = rootId.Child("batch");
        var releases = CreateReleases(2);
        var winners = new ConcurrentQueue<TaskId>();
        var firstAttemptSelected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(1, () => DurableTask.Run(async cancellationToken =>
        {
            var handles = await DurableTask.ScheduleAll(Inputs(releases)).WithId("batch");
            var winner = await ScheduledTask.WhenAny(handles, TestContext.Current.CancellationToken);
            winners.Enqueue(winner.Id);
            if (winners.Count == 1)
            {
                // 第一次尝试在此处停止并被中止，因此下一个激活实例会重放它。
                firstAttemptSelected.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return winner.GetResult();
        }));

        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, request, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Contains(batchId.Child("1")));

        // 两个结果在一次写入中提交；暂存结果的先后顺序决定哪个任务胜出。
        await CommitTogetherAsync(manager, storage, releases, [batchId.Child($"{stagedFirst}"), batchId.Child($"{stagedSecond}")], [stagedFirst, stagedSecond]);
        await firstAttemptSelected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 替代激活实例重放选择操作时，两个候选任务均已完成。
        var (replacement, _, _, _) = CreateRuntime(storage, manager, reentrant: true);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        var response = await replacement.GetScheduledTaskHandle(rootId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(10 + stagedFirst, response.GetResult<int>());
        var expected = batchId.Child($"{stagedFirst}");
        Assert.Equal([expected, expected], winners);
        Assert.False(storage.Contains(rootId.Child("$when-any-1").Child("$winner")));
    });
}
