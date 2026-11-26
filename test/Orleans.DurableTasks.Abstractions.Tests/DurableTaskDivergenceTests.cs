using System.Threading.Tasks.Sources;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

/// <summary>
/// 证明,重播其记录与纪录不符的一幕使它的努力停滞不前
/// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>以及抽象主义拥有的尝试规则
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
                    // 分裂不是身体能处理的失败
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
        // 东道方可以返回一个 ValueTask ,并由一个集合源支持,该来源报告另一业务的结果或抛出;
        // 当它被读取两次时。
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

        // 启动的每个输入都是启动失败前的终端, 第一个输入顺序中的第一个, 无法完成组合。
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
        // 抽象将取消尝试与每次等待的手柄联系起来, 所以宿主不需要将其本身连接起来。
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
        // 抽象的将取消尝试的企图与每个孩子开始的时间联系起来,所以一个等待期儿童开始
        // 等候时, 主机会像等待一样,尝试搁置待决的结束。
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
        // 启动报告在尝试继续时也具有象征意义。
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

    /// <summary>宿主的孩子们,选择和结果 每个测试脚本。</summary>
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

        /// <summary>一直保持一个开始, 直到抽象给主机的符号被取消。</summary>
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

    /// <summary>完整的源,就像集合的源一样,可以读一次。</summary>
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
