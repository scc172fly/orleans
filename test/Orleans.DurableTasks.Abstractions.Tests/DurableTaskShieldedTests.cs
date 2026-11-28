using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
[TestCategory("BVT")]
public class DurableTaskShieldedTests
{
    [Fact]
    public async Task ShieldedChildIsShieldedAndItsSiblingsAreNot()
    {
        var log = new ConcurrentDictionary<string, bool>();
        var response = await RunAsync(log, async () =>
        {
            await Step("before");
            await Step("shielded").Shielded();
            await DurableTask.Delay(TimeSpan.Zero).Shielded().WithId("delay");
            await Step("after");
        });

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal(
            new Dictionary<string, bool>
            {
                ["before"] = false,
                ["shielded"] = true,
                ["delay"] = true,
                ["after"] = false,
            },
            log.ToDictionary());
    }

    [Fact]
    public async Task ShieldedCombinatorShieldsEachInput()
    {
        var log = new ConcurrentDictionary<string, bool>();
        var response = await RunAsync(log, async () =>
        {
            await DurableTask.WhenAll([DurableTask.Run(static _ => { }), DurableTask.Run(static _ => { })]).WithId("all").Shielded();
            await Step("after");
        });

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.True(log["0"]);
        Assert.True(log["1"]);
        Assert.False(log["after"]);
    }

    [Fact]
    public async Task ShieldEndsWithTheStartWhichEnteredIt()
    {
        var log = new ConcurrentDictionary<string, bool>();
        var response = await RunAsync(log, async () =>
        {
            await DurableTask.Run(static async _ => await Task.Yield()).Shielded();
            await Task.Yield();
            await Step("after");
        });

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.True(log["$child-1"]);
        Assert.False(log["after"]);
    }

    [Fact]
    public async Task ShieldCoversOnlyChildrenOfTheStartingExecution()
    {
        var log = new ConcurrentDictionary<string, bool>();
        var response = await RunAsync(log, async () =>
        {
            await DurableTask.Run(async _ => await Step("grandchild")).WithId("child").Shielded();
        });

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.True(log["child"]);
        Assert.False(log["grandchild"]);
    }

    [Fact]
    public async Task ShieldedPostAndScheduleAreShielded()
    {
        var log = new ConcurrentDictionary<string, bool>();
        var response = await RunAsync(log, async () =>
        {
            _ = Step("posted").Shielded().Post();
            await await Step("scheduled").Shielded().ScheduleAsync();
            _ = Step("unshielded").Post();
        });

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.True(log["posted"]);
        Assert.True(log["scheduled"]);
        Assert.False(log["unshielded"]);
    }

    [Fact]
    public async Task CanceledExecutionStillObservesItsCancellationAfterAShieldedChild()
    {
        var log = new ConcurrentDictionary<string, bool>();
        var context = new RecordingContext(TaskId.CreateRoot("root"), log);
        context.RequestTaskCancellation("buyer");

        var response = await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async cancellationToken =>
            {
                await Step("release").Shielded();
                var current = DurableTask.Current;
                return (cancellationToken.IsCancellationRequested, current.IsTaskCancellationRequested, current.TaskCancellationReason);
            }),
            context);

        Assert.Equal((true, true, "buyer"), response.GetResult<(bool, bool, string?)>());
        Assert.True(log["release"]);
    }

    [Fact]
    public async Task ShieldedSignalWaitIsOutOfReachOfTheExecutionsCancellation()
    {
        var log = new ConcurrentDictionary<string, bool>();
        var context = new RecordingContext(TaskId.CreateRoot("root"), log);
        context.RequestTaskCancellation("buyer");

        var response = await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async _ =>
            {
                var shielded = await new DurableSignal<string>("shielded").WaitAsync().Shielded();
                var unshielded = await new DurableSignal<string>("unshielded").WaitAsync();
                return $"{shielded}|{unshielded}";
            }),
            context);

        // 作为执行实例一部分运行的屏蔽式等待，使用不会受到该取消请求影响的令牌进行等待。
        Assert.Equal("shielded|unshielded", response.GetResult<string>());
        Assert.False(log["wait:shielded"]);
        Assert.True(log["wait:unshielded"]);
    }

    private static ConfiguredDurableTask Step(string name) => DurableTask.Run(static _ => { }).WithId(name);

    private static async Task<DurableTaskResponse> RunAsync(ConcurrentDictionary<string, bool> log, Func<Task> body)
        => await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async _ => await body()),
            new RecordingContext(TaskId.CreateRoot("root"), log));

    /// <summary>对启动的每个子任务，记录启动代码是否对其应用取消屏蔽。</summary>
    private sealed class RecordingContext(TaskId id, ConcurrentDictionary<string, bool> log) : DurableTaskContext(id)
    {
        public override DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;

        public override Guid Incarnation => Guid.Empty;

        protected override async ValueTask<IScheduledTaskHandle> StartTaskAsync(
            TaskId taskId,
            DurableTask taskDefinition,
            DurableTaskStartOptions options,
            CancellationToken cancellationToken)
        {
            log[taskId.ToString().Split('/')[^1]] = SchedulesShieldedChildren;
            var response = await DurableTaskRuntimeHelper.RunAsync(taskDefinition, new RecordingContext(taskId, log));
            return new CompletedHandle(taskId, response);
        }

        protected override IScheduledTaskHandle PostTask(TaskId taskId, DurableTask taskDefinition, DurableTaskStartOptions options)
        {
            log[taskId.ToString().Split('/')[^1]] = SchedulesShieldedChildren;
            return new CompletedHandle(taskId, DurableTaskResponse.Completed);
        }

        protected override ValueTask<DurableTaskResponse> ScheduleDelayAsync(
            TaskId taskId,
            DateTimeOffset dueTime,
            CancellationToken cancellationToken) => new(DurableTaskResponse.Completed);


        protected override ValueTask<TaskId> SelectCompletionAsync(
            TaskId decisionId,
            IReadOnlyList<TaskId> candidates,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <summary>记录等待操作的令牌是否已取消，并将信号名称作为其值返回。</summary>
        protected override ValueTask<DurableTaskResponse> WaitForSignalAsync(string name, CancellationToken cancellationToken)
        {
            log[$"wait:{name}"] = cancellationToken.IsCancellationRequested;
            return new(DurableTaskResponse.FromResult(name));
        }
    }

    private sealed class CompletedHandle(TaskId taskId, DurableTaskResponse response) : IScheduledTaskHandle
    {
        public TaskId TaskId => taskId;

        public ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken) => new(response);

        public ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken) => new(response);

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
