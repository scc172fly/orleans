using System.Threading.Tasks.Sources;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

/// <summary>
/// 验证偏离记录的重放会使其执行尝试因
/// <see cref="DurableTaskAttemptCancellationReason.Diverged"/> 而停滞，并验证由抽象层负责的执行尝试规则。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
[TestCategory("BVT")]
public class DurableTaskDivergenceTests
{
    private static readonly TaskId RootId = TaskId.CreateRoot("root");

    [Fact]
    public async Task RecordedSelectionWhichIsNoLongerACandidateDivergesTheAttempt()
    {
        var context = new ScriptedContext { Selection = TaskId.Parse("root/elsewhere") };

        var response = await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async _ =>
            {
                var handles = await DurableTask.ScheduleAll([DurableTask.Run(static _ => 1), DurableTask.Run(static _ => 2)]);
                try
                {
                    await ScheduledTask.WhenAny(handles, CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    // 分歧并非任务体可以处理的普通失败。
                }
            }),
            context);

        AssertDiverged<InvalidOperationException>(context, response);
    }

    [Fact]
    public async Task RecordedResultOfAnotherTypeDivergesTheAttempt()
    {
        var context = new ScriptedContext { Result = DurableTaskResponse.FromResult("one") };

        var response = await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async _ =>
            {
                try
                {
                    return await DurableTask.Run(static _ => 1);
                }
                catch (InvalidCastException)
                {
                    return -1;
                }
            }),
            context);

        AssertDiverged<InvalidCastException>(context, response);
    }

    [Fact]
    public void RecordedResultOfAnotherTypeOutsideAnExecutionThrowsTheMismatch()
    {
        var exception = Assert.Throws<InvalidCastException>(() => DurableTaskResponse.FromResult("one").GetResult<int>());

        Assert.Equal("The durable task result is 'System.String', not 'System.Int32'.", exception.Message);
    }

    [Fact]
    public async Task CompletedWaitIsReadOnce()
    {
        // 宿主可能返回由池化源支持的 ValueTask；重复读取时，该源可能返回另一操作的结果，或
        // 抛出异常。
        var context = new ScriptedContext { SingleUseResult = DurableTaskResponse.FromResult(7) };

        var response = await DurableTaskRuntimeHelper.RunAsync(DurableTask.Run(async _ => await DurableTask.Run(static _ => 0)), context);

        Assert.Equal(7, response.GetResult<int>());
    }

    [Fact]
    public async Task StartFailureSettlesTheInputsWhichStartedBeforeTheCombinatorFails()
    {
        var context = new ScriptedContext { FailingStart = "1" };

        var response = await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.WhenAll([DurableTask.Run(static _ => 1), DurableTask.Run(static _ => 2), DurableTask.Run(static _ => 3)]),
            context);

        // 在启动失败（按输入顺序的第一个失败）使组合器失败前，每个已启动的输入都已达到终态。
        Assert.Equal("start 1", Assert.IsType<InvalidOperationException>(response.Exception).Message);
        Assert.Equal(["root/$when-all-1/0", "root/$when-all-1/2"], context.Canceled.Select(static id => id.ToString()).Order(StringComparer.Ordinal));
        Assert.All(context.Handles.Values, static handle => Assert.True(handle.IsTerminal));
    }

    [Fact]
    public async Task VersionCheckOfACanceledAttemptThrowsWithoutAskingTheHost()
    {
        var context = new ScriptedContext();
        context.CancelAttempt(DurableTaskAttemptCancellationReason.ActivationStopping);

        var response = await DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(_ => DurableTask.GetVersion("change", 0, 1) + new DurableVersion("other", 0, 1).Get()),
            context);

        var canceled = Assert.IsType<DurableTaskAttemptCanceledException>(response.Exception);
        Assert.Equal(DurableTaskAttemptCancellationReason.ActivationStopping, canceled.Reason);
        Assert.Equal(0, context.VersionChecks);
    }

    [Fact]
    public async Task HandleWaitEndsWhenTheAttemptIsCanceled()
    {
        // 抽象层将执行尝试的取消关联到每个句柄等待操作，因此宿主无需自行关联。
        var context = new ScriptedContext { PendingStarts = true };

        var running = DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async _ =>
            {
                var handles = await DurableTask.ScheduleAll([DurableTask.Run(static _ => 1)]);
                await ScheduledTask.WhenAll(handles, CancellationToken.None);
            }),
            context).AsTask();
        Assert.False(running.IsCompleted);
        context.CancelAttempt(DurableTaskAttemptCancellationReason.ActivationStopping);
        var response = await running.WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);

        Assert.IsType<DurableTaskAttemptCanceledException>(response.Exception);
    }

    [Fact]
    public async Task ChildStartEndsWhenTheAttemptIsCanceled()
    {
        // 抽象层将执行尝试的取消关联到每个子任务的启动，因此即使宿主将某个被等待子任务的启动操作保持为待完成状态，
        // 该子任务也会随执行尝试结束，正如其等待操作一样。
        var context = new ScriptedContext { HeldStarts = true };

        var running = DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async _ => await DurableTask.Run(static _ => 1).WithId("child")),
            context).AsTask();
        Assert.False(running.IsCompleted);
        context.CancelAttempt(DurableTaskAttemptCancellationReason.ActivationStopping);
        var response = await running.WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);

        var canceled = Assert.IsType<DurableTaskAttemptCanceledException>(response.Exception);
        Assert.Equal(DurableTaskAttemptCancellationReason.ActivationStopping, canceled.Reason);
    }

    [Fact]
    public async Task ChildStartReportsTheCallersCancellation()
    {
        // 调用者的令牌使待处理的启动被放弃；启动操作报告该令牌，而执行尝试继续运行。
        var context = new ScriptedContext { HeldStarts = true };
        using var caller = new CancellationTokenSource();
        OperationCanceledException? observed = null;

        var running = DurableTaskRuntimeHelper.RunAsync(
            DurableTask.Run(async _ =>
            {
                observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => DurableTask.Run(static _ => 1).WithId("child").ScheduleAsync(caller.Token));
            }),
            context).AsTask();
        Assert.False(running.IsCompleted);
        await caller.CancelAsync();
        var response = await running.WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);

        Assert.Null(response.Exception);
        Assert.IsNotType<DurableTaskAttemptCanceledException>(observed);
        Assert.Equal(caller.Token, observed!.CancellationToken);
        Assert.False(context.AttemptCancellationToken.IsCancellationRequested);
    }

    private static void AssertDiverged<TCause>(ScriptedContext context, DurableTaskResponse response)
        where TCause : Exception
    {
        var canceled = Assert.IsType<DurableTaskAttemptCanceledException>(response.Exception);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, canceled.Reason);
        Assert.IsType<TCause>(canceled.InnerException);
        Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, context.AttemptCancellationReason());
        Assert.True(context.AttemptCancellationToken.IsCancellationRequested);
    }

    /// <summary>其子任务、选择和结果由各个测试预设的宿主。</summary>
    private sealed class ScriptedContext() : DurableTaskContext(RootId)
    {
        public TaskId? Selection { get; init; }

        public DurableTaskResponse? Result { get; init; }

        public DurableTaskResponse? SingleUseResult { get; init; }

        public string? FailingStart { get; init; }

        public bool PendingStarts { get; init; }

        public bool HeldStarts { get; init; }

        public int VersionChecks { get; private set; }

        public Dictionary<TaskId, ScriptedHandle> Handles { get; } = [];

        public List<TaskId> Canceled { get; } = [];

        public override DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;

        public override Guid Incarnation => Guid.Empty;

        protected override ValueTask<IScheduledTaskHandle> StartTaskAsync(
            TaskId taskId,
            DurableTask taskDefinition,
            DurableTaskStartOptions options,
            CancellationToken cancellationToken)
        {
            if (taskId.ToString().Split('/')[^1] == FailingStart)
            {
                return ValueTask.FromException<IScheduledTaskHandle>(new InvalidOperationException($"start {FailingStart}"));
            }

            if (HeldStarts)
            {
                return HoldAsync(cancellationToken);
            }

            var handle = new ScriptedHandle(this, taskId);
            Handles.Add(taskId, handle);
            if (!PendingStarts && FailingStart is null)
            {
                handle.Complete(Result ?? DurableTaskResponse.FromResult(0));
            }

            return new(handle);
        }

        protected override ValueTask<DurableTaskResponse> ScheduleDelayAsync(TaskId taskId, DateTimeOffset dueTime, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        protected override ValueTask<TaskId> SelectCompletionAsync(TaskId decisionId, IReadOnlyList<TaskId> candidates, CancellationToken cancellationToken)
            => new(Selection ?? candidates[0]);

        protected override int GetVersion(string name, int minSupported, int maxSupported)
        {
            VersionChecks++;
            return maxSupported;
        }

        public void OnCanceled(TaskId taskId) => Canceled.Add(taskId);

        /// <summary>挂起启动操作，直到抽象层交给宿主的令牌被取消。</summary>
        private static async ValueTask<IScheduledTaskHandle> HoldAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new System.Diagnostics.UnreachableException();
        }
    }

    private sealed class ScriptedHandle(ScriptedContext context, TaskId taskId) : IScheduledTaskHandle
    {
        private readonly TaskCompletionSource<DurableTaskResponse> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskId TaskId => taskId;

        public bool IsTerminal => _response.Task.IsCompleted;

        public void Complete(DurableTaskResponse response) => _response.TrySetResult(response);

        public ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken)
        {
            if (context.SingleUseResult is { } result)
            {
                return new(new SingleUseSource(result), token: 0);
            }

            return new(_response.Task.WaitAsync(cancellationToken));
        }

        public ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken)
        {
            context.OnCanceled(taskId);
            Complete(DurableTaskResponse.Canceled);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>已完成的源，与池化源一样，只能读取一次。</summary>
    private sealed class SingleUseSource(DurableTaskResponse response) : IValueTaskSource<DurableTaskResponse>
    {
        private int _reads;

        public DurableTaskResponse GetResult(short token)
            => ++_reads == 1 ? response : throw new InvalidOperationException("The source was read twice.");

        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => continuation(state);
    }
}
