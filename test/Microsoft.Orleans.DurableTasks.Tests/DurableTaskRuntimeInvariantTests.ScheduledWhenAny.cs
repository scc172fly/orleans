using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 证明: <c>ScheduledTask.WhenAny</c> 以完成序列选择,因此在
/// 替换激活返回现场尝试的赢家,没有记录的决定。
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
                // 第一次尝试停在这里,中止了 所以下一次激活会重播
                firstAttemptSelected.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return winner.GetResult();
        }));

        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, request, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Contains(batchId.Child("1")));

        // 两种结果都以一个写作方式承诺;它们被排队的顺序选择获胜者。
        await CommitTogetherAsync(manager, storage, releases, [batchId.Child($"{stagedFirst}"), batchId.Child($"{stagedSecond}")], [stagedFirst, stagedSecond]);
        await firstAttemptSelected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 替换启动后,两名候选人在重新显示甄选时都已完成。
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
