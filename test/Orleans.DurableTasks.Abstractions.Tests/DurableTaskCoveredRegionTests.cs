using System.Reflection;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

/// <summary>
/// 验证已覆盖区域，这是将记录已覆盖的代码排除在执行实例历史之外的唯一机制：
/// 同步区域（例如效果及宿主的状态操作）和异步覆盖流程（例如
/// 事务体），宿主通过内部成员进入这些区域。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
[TestCategory("BVT")]
public class DurableTaskCoveredRegionTests
{
    private static CancellationToken TestCancellation => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task CoveredFlowCoversItsAwaitsAndTakesNoIdentifiers()
    {
        var context = new RecordingContext();
        var failures = new List<InvalidOperationException>();

        var response = await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async _ =>
            {
                var nested = await RunInCoveredFlowAsync(DurableTask.Current, async () =>
                {
                    await Task.Yield();

                    // await 之后仍处于覆盖区域中：效果作为该流程的一部分运行，持久化操作会抛出异常。
                    var value = DurableTask.RunInline("inner", static () => 1) + DurableTask.RunInline(static () => 1);
                    failures.Add(Assert.Throws<InvalidOperationException>(static () => DurableTask.Run(static _ => 0).Post()));
                    failures.Add(Assert.Throws<InvalidOperationException>(static () => DurableTask.GetVersion("change", 0, 1)));
                    failures.Add(await Assert.ThrowsAsync<InvalidOperationException>(async () => await DurableTask.Delay(TimeSpan.FromMinutes(1))));
                    return value;
                });

                // 区域已经结束，因此后续操作会被记录；此前的流程未占用任何标识符。
                return nested + DurableTask.RunInline(static () => 10) + DurableTask.RunInline("inner", static () => 100);
            }),
            context);

        Assert.Equal(112, response.GetResult<int>());
        Assert.Equal(3, failures.Count);
        Assert.All(failures, static failure => Assert.Contains("ORLEANS0227", failure.Message, StringComparison.Ordinal));
        Assert.Equal(["root/$child-1", "root/inner"], context.Effects);
        Assert.Empty(context.Posts);
    }

    [Fact]
    public async Task CoveredFlowLeavesTheExecutionsOtherFlowsUncovered()
    {
        var context = new RecordingContext();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var response = await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async _ =>
            {
                // 未由覆盖区域任务体启动的流程，在覆盖流程运行期间仍记录其效果。
                var other = RecordLaterAsync(release.Task);
                var covered = await RunInCoveredFlowAsync(DurableTask.Current, async () =>
                {
                    release.SetResult();
                    var recorded = await other;
                    return recorded + DurableTask.RunInline("covered", static () => 1);
                });
                return covered;
            }),
            context);

        Assert.Equal(2, response.GetResult<int>());
        Assert.Equal(["root/other"], context.Effects);

        static async Task<int> RecordLaterAsync(Task gate)
        {
            await gate;
            return DurableTask.RunInline("other", static () => 1);
        }
    }

    [Fact]
    public async Task SynchronousRegionRunsEffectsDirectlyAndRejectsOperationsUntilDisposed()
    {
        var context = new RecordingContext();

        var response = await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(cancellationToken =>
            {
                int covered;
                using (EnterCoveredRegion(DurableTask.Current))
                {
                    covered = DurableTask.RunInline("covered", static () => 1);
                    Assert.Throws<InvalidOperationException>(static () => DurableTask.Run(static _ => 0).WithId("child").Post());
                }

                _ = DurableTask.Run(static _ => 0).WithId("child").Post();
                return covered + DurableTask.RunInline("covered", static () => 1);
            }),
            context);

        // 被拒绝的启动原本会占用的具名出现位置仍然可用。
        Assert.Equal(2, response.GetResult<int>());
        Assert.Equal(["root/covered"], context.Effects);
        Assert.Equal(["root/child"], context.Posts);
    }

    private static Task<int> RunInCoveredFlowAsync(DurableTaskContext context, Func<Task<int>> body)
        => (Task<int>)typeof(DurableTaskContext)
            .GetMethod("RunInCoveredFlowAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(int))
            .Invoke(context, [body])!;

    private static IDisposable EnterCoveredRegion(DurableTaskContext context)
        => (IDisposable)typeof(DurableTaskContext)
            .GetMethod("EnterCoveredRegion", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, null)!;

    /// <summary>记录效果和投递操作、并立即完成其他所有操作的宿主。</summary>
    private sealed class RecordingContext() : DurableTaskContext(TaskId.CreateRoot("root"))
    {
        public List<string> Effects { get; } = [];

        public List<string> Posts { get; } = [];

        public override DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;

        public override Guid Incarnation => Guid.Empty;

        protected override DurableTaskResponse RunEffect<TState>(TaskId effectId, TState state, Func<TState, DurableTaskResponse> effect)
        {
            Effects.Add(effectId.ToString());
            return effect(state);
        }

        protected override IScheduledTaskHandle PostTask(TaskId taskId, DurableTask taskDefinition, DurableTaskStartOptions options)
        {
            Posts.Add(taskId.ToString());
            return new CompletedHandle(taskId);
        }

        protected override int GetVersion(string name, int minSupported, int maxSupported) => maxSupported;

        protected override ValueTask<IScheduledTaskHandle> StartTaskAsync(
            TaskId taskId,
            DurableTask taskDefinition,
            DurableTaskStartOptions options,
            CancellationToken cancellationToken) => new(new CompletedHandle(taskId));

        protected override ValueTask<DurableTaskResponse> ScheduleDelayAsync(TaskId taskId, DateTimeOffset dueTime, CancellationToken cancellationToken)
            => new(DurableTaskResponse.Completed);

        protected override ValueTask<TaskId> SelectCompletionAsync(TaskId decisionId, IReadOnlyList<TaskId> candidates, CancellationToken cancellationToken)
            => new(candidates[0]);
    }

    private sealed class CompletedHandle(TaskId taskId) : IScheduledTaskHandle
    {
        public TaskId TaskId => taskId;

        public ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken) => new(DurableTaskResponse.Completed);

        public ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken) => new(DurableTaskResponse.Completed);

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
