using System.Collections.Concurrent;
using System.Globalization;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 证明: <c>WhenEach</c> 和 <c>WhenAny</c> 选择通过耐久完成序列完成的完成, 因此重放
/// 替换启动程序观察现场尝试的命令,没有记录决定。
/// </summary>
public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public Task WhenEachReplaysTheLiveCompletionOrderWithoutDecisionRecords(int stagedFirst, int stagedSecond) => ActivationScheduler.RunAsync(async () =>
    {
        var manager = new TestStateManager();
        var storage = new TestStorage(manager);
        var (runtime, _, _, _) = CreateRuntime(storage, manager, reentrant: true);
        var rootId = TaskId.CreateRoot("root");
        var operationId = rootId.Child("$when-each-1");
        var releases = CreateReleases(3);
        var yields = CreateSignals(3);
        var attempts = new ConcurrentQueue<int[]>();
        var firstAttemptYieldedAll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(1, () => DurableTask.Run(async cancellationToken =>
        {
            var order = new List<int>();
            await foreach (var task in DurableTask.WhenEach(Inputs(releases)))
            {
                Assert.Equal(10 + InputIndex(task), task.GetResult());
                order.Add(InputIndex(task));
                yields[order.Count - 1].TrySetResult();
            }

            attempts.Enqueue([.. order]);
            if (attempts.Count == 1)
            {
                // 第一次尝试停在这里,中止了 所以下一次激活会重播
                firstAttemptYieldedAll.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return order.Count;
        }));

        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, request, TestContext.Current.CancellationToken);

        // 输入 2 以自己的文字完成。
        releases[2].SetResult(12);
        await yields[0].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 其它投入的结果在日记上时,由作者撰写,因此下一篇作品既承诺也承诺
        // 控件齐全,按其排列顺序决定。
        await CommitTogetherAsync(manager, storage, releases, [operationId.Child($"{stagedFirst}"), operationId.Child($"{stagedSecond}")], [stagedFirst, stagedSecond]);
        await yields[2].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await firstAttemptYieldedAll.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(
            storage.Get(operationId.Child($"{stagedFirst}")).CompletionSequence
                < storage.Get(operationId.Child($"{stagedSecond}")).CompletionSequence);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 当替换激活重放点数时, 每一个输入都是完整的 。
        var (replacement, _, _, _) = CreateRuntime(storage, manager, reentrant: true);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        var response = await replacement.GetScheduledTaskHandle(rootId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(3, response.GetResult<int>());
        int[] expected = [2, stagedFirst, stagedSecond];
        Assert.Equal([expected, expected], attempts);
        Assert.DoesNotContain(storage.Tasks, task => task.Id.ToString().Contains("$next-", StringComparison.Ordinal));
    });

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public Task WhenAnyReplaysTheWinnerOfASharedWriteWithoutADecisionRecord(int stagedFirst, int stagedSecond) => ActivationScheduler.RunAsync(async () =>
    {
        var manager = new TestStateManager();
        var storage = new TestStorage(manager);
        var (runtime, _, _, _) = CreateRuntime(storage, manager, reentrant: true);
        var rootId = TaskId.CreateRoot("root");
        var operationId = rootId.Child("race");
        var releases = CreateReleases(2);
        var winners = new ConcurrentQueue<(int Index, int Result)>();
        var firstAttemptSelected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(1, () => DurableTask.Run(async cancellationToken =>
        {
            var winner = await DurableTask.WhenAny(Inputs(releases)).WithId("race");
            winners.Enqueue(winner);
            if (winners.Count == 1)
            {
                firstAttemptSelected.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return winner.Index;
        }));

        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, request, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => storage.Contains(operationId.Child("1")));
        await CommitTogetherAsync(manager, storage, releases, [operationId.Child($"{stagedFirst}"), operationId.Child($"{stagedSecond}")], [stagedFirst, stagedSecond]);
        await firstAttemptSelected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var (replacement, _, _, _) = CreateRuntime(storage, manager, reentrant: true);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        var response = await replacement.GetScheduledTaskHandle(rootId)
            .WaitAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(stagedFirst, response.GetResult<int>());
        Assert.Equal([(stagedFirst, 10 + stagedFirst), (stagedFirst, 10 + stagedFirst)], winners);
        Assert.False(storage.Contains(operationId.Child("$winner")));
    });

    [Fact]
    public Task CompletionSelectionPrefersARecordedDecisionOverTheCompletionSequence() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, _, _) = CreateRuntime();
        var first = TaskId.Parse("root/op/0");
        var second = TaskId.Parse("root/op/1");
        var decisionId = TaskId.Parse("root/op/$winner");
        _ = storage.GetOrCreate(first);

        // 记录没有完成序列的结果,例如在排序盖章之前储存的顺序。
        storage.GetOrCreate(second).Result = DurableTaskResponse.FromResult(2);
        Assert.Equal(second, await runtime.SelectCompletionAsync(decisionId, [first, second], TestContext.Current.CancellationToken));
        Assert.True(storage.Contains(decisionId));

        storage.SetResponse(first, storage.Get(first), DurableTaskResponse.FromResult(1));
        storage.SetResponse(second, storage.Get(second), DurableTaskResponse.FromResult(2));
        Assert.True(storage.Get(first).CompletionSequence < storage.Get(second).CompletionSequence);

        // 被记录的决定保留了胜者
        Assert.Equal(second, await runtime.SelectCompletionAsync(decisionId, [first, second], TestContext.Current.CancellationToken));

        // 没有记录,最低完成顺序 赢得任何候选人的顺序, 并且没有决定存储。
        var nextId = TaskId.Parse("root/op/$next-0");
        Assert.Equal(first, await runtime.SelectCompletionAsync(nextId, [second, first], TestContext.Current.CancellationToken));
        Assert.False(storage.Contains(nextId));
    });

    /// <summary>
    /// 以写作维持日记,在 <paramref name="indexes"/> 等待著,而他们依其排列而排列,
    /// 以此顺序来计算结果,然后发布日记,这样一书写的结果都包含两个结果。
    /// </summary>
    private static async Task CommitTogetherAsync(
        TestStateManager manager,
        TestStorage storage,
        TaskCompletionSource<int>[] releases,
        TaskId[] ids,
        int[] indexes)
    {
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AfterWriteStarted = async () =>
        {
            manager.AfterWriteStarted = null;
            holding.TrySetResult();
            await release.Task;
        };
        var heldWrite = manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask();
        await holding.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var writes = manager.WriteCount;
        for (var step = 0; step < ids.Length; step++)
        {
            var id = ids[step];
            releases[indexes[step]].SetResult(10 + indexes[step]);
            await WaitUntilAsync(() => storage.Contains(id) && storage.Get(id).CompletedAt is not null);
        }

        Assert.Equal(writes, manager.WriteCount);
        release.SetResult();
        await heldWrite.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static TaskCompletionSource<int>[] CreateReleases(int count) =>
        Enumerable.Range(0, count)
            .Select(static _ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();

    private static TaskCompletionSource[] CreateSignals(int count) =>
        Enumerable.Range(0, count)
            .Select(static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();

    private static DurableTask<int>[] Inputs(TaskCompletionSource<int>[] releases) =>
        releases.Select(static release => DurableTask.Run(async _ => await release.Task)).ToArray();

    private static int InputIndex(ScheduledTask task)
    {
        var id = task.Id.ToString();
        return int.Parse(id[(id.LastIndexOf('/') + 1)..], CultureInfo.InvariantCulture);
    }
}
