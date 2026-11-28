using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Messaging;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 验证 <see cref="DurableTask.GetVersion(string, int, int)"/> 对不同时间启动的执行实例返回哪个变更版本，
/// 这些执行实例分别在部署之前、期间及之后启动；同时验证使用不受支持的版本的执行实例如何停滞。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class DurableTaskVersionTests
{
    private static CancellationToken TestCancellation => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public Task FirstRunRecordsTheNewestVersionAndReplayReturnsIt() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-first-run");
        await using var harness = new DurableTaskReplayHarness(grain => Workflow(grain, maxSupported: 2));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(workflowId, TestCancellation);
            await CommitAtGateAsync(activation);

            var record = Assert.Single(activation.Storage.Tasks, task => task.Id == workflowId.Child("$version:change"));
            Assert.Equal(2, record.State.Result!.GetResult<int>());
        }

        // 后续部署添加版本 3，执行实例保留它此前记录的版本。
        harness.Workflow = grain => Workflow(grain, maxSupported: 3);
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(Timeout, TestCancellation);
        replay.Grain.OpenGate();

        Assert.Equal(2, (await replay.GetResponseAsync(workflowId, TestCancellation)).GetResult<int>());

        static async DurableTask<int> Workflow(ReplayGrain grain, int maxSupported)
        {
            var version = DurableTask.GetVersion("change", 0, maxSupported);
            await DurableTask.Run(static _ => { }).WithId("first");
            await grain.WaitAtGateAsync();
            return version;
        }
    });

    [Fact]
    public Task ExecutionWhichEarlierCodeRanPastTheCheckFollowsVersionZero() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-old-execution");
        await using var harness = new DurableTaskReplayHarness(static grain => Before(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(workflowId, TestCancellation);
            await CommitAtGateAsync(activation);
        }

        harness.Workflow = static grain => After(grain);
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(Timeout, TestCancellation);
        replay.Grain.OpenGate();

        // 此前运行在当前版本检查所在位置之后记录了 "second"，因此执行实例遵循
        // 变更前的代码，并记录该版本选择决定。
        Assert.Equal("0:second", (await replay.GetResponseAsync(workflowId, TestCancellation)).GetResult<string>());
        var record = Assert.Single(replay.Storage.Tasks, task => task.Id == workflowId.Child("$version:change"));
        Assert.Equal(0, record.State.Result!.GetResult<int>());

        static async DurableTask<string> Before(ReplayGrain grain)
        {
            await DurableTask.Run(static _ => { }).WithId("first");
            await DurableTask.Run(static _ => { }).WithId("second");
            await grain.WaitAtGateAsync();
            return "unchanged";
        }

        static async DurableTask<string> After(ReplayGrain grain)
        {
            await DurableTask.Run(static _ => { }).WithId("first");
            var version = DurableTask.GetVersion("change", 0, 1);
            var step = version >= 1 ? "replacement" : "second";
            await DurableTask.Run(static _ => { }).WithId(step);
            await grain.WaitAtGateAsync();
            return $"{version}:{step}";
        }
    });

    [Fact]
    public Task ExecutionWhichEarlierCodeDidNotRunPastTheCheckFollowsTheNewestVersion() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-not-past");
        await using var harness = new DurableTaskReplayHarness(static grain => Before(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(workflowId, TestCancellation);
            await CommitAtGateAsync(activation);
        }

        harness.Workflow = static grain => After(grain);
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(Timeout, TestCancellation);
        replay.Grain.OpenGate();

        // 先前运行在当前版本检查位置之前等待同步关卡打开。
        Assert.Equal("1:replacement", (await replay.GetResponseAsync(workflowId, TestCancellation)).GetResult<string>());

        static async DurableTask<string> Before(ReplayGrain grain)
        {
            await DurableTask.Run(static _ => { }).WithId("first");
            await grain.WaitAtGateAsync();
            await DurableTask.Run(static _ => { }).WithId("second");
            return "unchanged";
        }

        static async DurableTask<string> After(ReplayGrain grain)
        {
            await DurableTask.Run(static _ => { }).WithId("first");
            await grain.WaitAtGateAsync();
            var version = DurableTask.GetVersion("change", 0, 1);
            var step = version >= 1 ? "replacement" : "second";
            await DurableTask.Run(static _ => { }).WithId(step);
            return $"{version}:{step}";
        }
    });

    [Fact]
    public Task OperationInsertedBeforeTheCheckKeepsTheExecutionOnVersionZero() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-inserted");
        bool? replayingAtCheck = null;
        await using var harness = new DurableTaskReplayHarness(static grain => Before(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(workflowId, TestCancellation);
            await CommitAtGateAsync(activation);
        }

        harness.Workflow = grain => After(grain, replaying => replayingAtCheck = replaying);
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(Timeout, TestCancellation);
        replay.Grain.OpenGate();

        // 插入的步骤使重放结束，而 "second" 仍表明先前运行已越过版本检查的位置。
        Assert.Equal("0:second", (await replay.GetResponseAsync(workflowId, TestCancellation)).GetResult<string>());
        Assert.False(replayingAtCheck);

        static async DurableTask<string> Before(ReplayGrain grain)
        {
            await DurableTask.Run(static _ => { }).WithId("first");
            await DurableTask.Run(static _ => { }).WithId("second");
            await grain.WaitAtGateAsync();
            return "unchanged";
        }

        static async DurableTask<string> After(ReplayGrain grain, Action<bool> observe)
        {
            await DurableTask.Run(static _ => { }).WithId("first");
            await DurableTask.Run(static _ => { }).WithId("inserted");
            observe(DurableTask.Current.IsReplaying);
            var version = DurableTask.GetVersion("change", 0, 1);
            var step = version >= 1 ? "replacement" : "second";
            await DurableTask.Run(static _ => { }).WithId(step);
            await grain.WaitAtGateAsync();
            return $"{version}:{step}";
        }
    });

    [Fact]
    public Task EveryCheckOfAChangeInAnExecutionReturnsTheFirstDecision() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-loop");
        var versions = new ConcurrentQueue<int>();
        await using var harness = new DurableTaskReplayHarness(grain => Loop(grain, versions, check: false));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(workflowId, TestCancellation);
            await CommitAtGateAsync(activation);
        }

        // 先前运行在同步关卡之前完成了两次迭代。重放之后运行的后续迭代仍然
        // 遵循第一次迭代所确定的版本。
        harness.Workflow = grain => Loop(grain, versions, check: true);
        await using (var replay = await harness.ActivateAsync(TestCancellation))
        {
            await CommitAtGateAsync(replay);
        }

        Assert.Equal([0, 0, 0, 0], versions);

        // 再次重放时读取已记录的决定。
        versions.Clear();
        await using var recovered = await harness.ActivateAsync(TestCancellation);
        await recovered.Grain.AtGate.WaitAsync(Timeout, TestCancellation);
        recovered.Grain.OpenGate();
        Assert.Equal(4, (await recovered.GetResponseAsync(workflowId, TestCancellation)).GetResult<int>());
        Assert.Equal([0, 0, 0, 0], versions);

        static async DurableTask<int> Loop(ReplayGrain grain, ConcurrentQueue<int> versions, bool check)
        {
            var iterations = check ? 4 : 2;
            for (var i = 0; i < iterations; i++)
            {
                if (check)
                {
                    versions.Enqueue(DurableTask.GetVersion("loop", 0, 1));
                }

                await DurableTask.Run(static _ => { }).WithId("iteration");
            }

            await grain.WaitAtGateAsync();
            return iterations;
        }
    });

    [Fact]
    public Task RemovedCheckLeavesNoOperationForReplaysToReach() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-removed");
        var observations = new ConcurrentQueue<(bool Replaying, int Version)>();
        await using var harness = new DurableTaskReplayHarness(static grain => Before(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(workflowId, TestCancellation);
            await CommitAtGateAsync(activation);
        }

        // 某次部署移除了对 "retired" 的检查，其记录仍保留在执行实例的任务树中。
        harness.Workflow = grain => After(grain, observations);
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(Timeout, TestCancellation);
        replay.Grain.OpenGate();
        await replay.GetResponseAsync(workflowId, TestCancellation);

        var (replaying, version) = Assert.Single(observations);
        Assert.False(replaying);
        Assert.Equal(1, version);

        static async DurableTask Before(ReplayGrain grain)
        {
            DurableTask.GetVersion("retired", 0, 1);
            await DurableTask.Run(static _ => { }).WithId("first");
            await grain.WaitAtGateAsync();
        }

        static async DurableTask After(ReplayGrain grain, ConcurrentQueue<(bool, int)> observations)
        {
            await DurableTask.Run(static _ => { }).WithId("first");
            observations.Enqueue((DurableTask.Current.IsReplaying, DurableTask.GetVersion("added", 0, 1)));
            await grain.WaitAtGateAsync();
        }
    });

    [Fact]
    public Task StepInheritsTheVersionWhichItsParentRecorded() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-step");
        await using var harness = new DurableTaskReplayHarness(static _ => Workflow());
        await using var activation = await harness.ActivateAsync(TestCancellation);
        await activation.ScheduleAsync(workflowId, TestCancellation);

        Assert.Equal((1, 1, 2), (await activation.GetResponseAsync(workflowId, TestCancellation)).GetResult<(int, int, int)>());

        // 继承父任务决定的步骤不记录自己的决定；未找到父任务决定的步骤会记录自己的决定。
        var records = activation.Storage.Tasks
            .Select(static task => task.Id.ToString())
            .Where(static id => id.Contains("/$version:", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        Assert.Equal(["version-step/$version:change", "version-step/other/$version:other"], records);

        static async DurableTask<(int, int, int)> Workflow()
        {
            var parent = DurableTask.GetVersion("change", 0, 1);
            var inherited = await DurableTask.Run(static _ => DurableTask.GetVersion("change", 0, 2)).WithId("inherits");
            var own = await DurableTask.Run(static _ => DurableTask.GetVersion("other", 0, 2)).WithId("other");
            return (parent, inherited, own);
        }
    });

    [Fact]
    public Task UnsupportedVersionStallsTheExecutionUntilCodeWhichSupportsItReplaysIt() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-stall");
        var stalled = new TaskCompletionSource<DurableTaskVersionException>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = DurableTaskEvents.AllEvents.Subscribe(new EventObserver(evt =>
        {
            if (evt is DurableTaskEvents.ExecutionStalled { Reason: DurableTaskAttemptCancellationReason.UnsupportedVersion, Exception: DurableTaskVersionException exception } && evt.TaskId == workflowId)
            {
                stalled.TrySetResult(exception);
            }
        }));

        await using var harness = new DurableTaskReplayHarness(grain => Workflow(grain, maxSupported: 2));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(workflowId, TestCancellation);
            await CommitAtGateAsync(activation);
        }

        // 仅支持版本 1 的代码（例如滚动部署期间的较早构建版本）重放任务。
        harness.Workflow = grain => Workflow(grain, maxSupported: 1);
        await using (var older = await harness.ActivateAsync(TestCancellation))
        {
            var exception = await stalled.Task.WaitAsync(Timeout, TestCancellation);
            Assert.Equal(("change", 2, 0, 1), (exception.Name, exception.Version, exception.MinSupported, exception.MaxSupported));

            // 任务保持待完成状态，本次执行尝试已结束，Grain 保留低频唤醒操作，以供后续激活实例恢复执行。
            Assert.True(older.Storage.TryGetTask(workflowId, out var state));
            Assert.False(state.Result is { IsCompleted: true });
            await WaitUntilAsync(() => older.Runtime.GetWakeupDelay() == TimeSpan.FromMinutes(1));
        }

        harness.Workflow = grain => Workflow(grain, maxSupported: 2);
        await using var newer = await harness.ActivateAsync(TestCancellation);
        await newer.Grain.AtGate.WaitAsync(Timeout, TestCancellation);
        newer.Grain.OpenGate();
        Assert.Equal(2, (await newer.GetResponseAsync(workflowId, TestCancellation)).GetResult<int>());

        static async DurableTask<int> Workflow(ReplayGrain grain, int maxSupported)
        {
            await DurableTask.Run(static _ => { }).WithId("first");
            var version = DurableTask.GetVersion("change", 0, maxSupported);
            await grain.WaitAtGateAsync();
            return version;
        }
    });

    [Fact]
    public Task StalledAttemptWhichHandlesItsCancellationKeepsItsTaskPending() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-stall-handled");
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new ConcurrentQueue<Exception>();
        using var subscription = DurableTaskEvents.AllEvents.Subscribe(new EventObserver(evt =>
        {
            if (evt is DurableTaskEvents.ExecutionStalled && evt.TaskId == workflowId)
            {
                stalled.TrySetResult();
            }
        }));

        await using var harness = new DurableTaskReplayHarness(static grain => Recorded(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(workflowId, TestCancellation);
            await CommitAtGateAsync(activation);
        }

        // 提高最低支持版本的代码重放已记录版本 0 的执行实例。版本检查以及
        // 任务体捕获取消异常后启动的步骤，都会报告执行尝试因
        // 不支持该版本而被取消，且任务体的状态更改成功。
        harness.Workflow = grain => Handling(grain, failures.Enqueue);
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await stalled.Task.WaitAsync(Timeout, TestCancellation);

        Assert.Equal(2, failures.Count);
        Assert.All(failures, static failure =>
        {
            var canceled = Assert.IsType<DurableTaskAttemptCanceledException>(failure);
            Assert.Equal(DurableTaskAttemptCancellationReason.UnsupportedVersion, canceled.Reason);
            var unsupported = Assert.IsType<DurableTaskVersionException>(canceled.InnerException);
            Assert.Equal(("change", 0, 1, 1), (unsupported.Name, unsupported.Version, unsupported.MinSupported, unsupported.MaxSupported));
        });
        Assert.Equal(1, replay.Grain.Counter.Value);
        Assert.True(replay.Storage.TryGetTask(workflowId, out var state));
        Assert.False(state.Result is { IsCompleted: true });
        Assert.DoesNotContain(replay.Storage.Tasks, task => task.Id == workflowId.Child("fallback"));

        static async DurableTask<string> Recorded(ReplayGrain grain)
        {
            DurableTask.GetVersion("change", 0, 0);
            await DurableTask.Run(static _ => { }).WithId("first");
            await grain.WaitAtGateAsync();
            return "recorded";
        }

        static async DurableTask<string> Handling(ReplayGrain grain, Action<Exception> observe)
        {
            try
            {
                DurableTask.GetVersion("change", 1, 1);
            }
            catch (DurableTaskAttemptCanceledException exception)
            {
                observe(exception);
                try
                {
                    await DurableTask.Run(static _ => { }).WithId("fallback");
                }
                catch (Exception scheduleFailure)
                {
                    observe(scheduleFailure);
                }

                grain.Counter.Value++;
            }

            return "handled";
        }
    });

    [Fact]
    public Task DescriptorFollowsTheVersionWhichTheExecutionRecorded() => ActivationScheduler.RunAsync(async () =>
    {
        var workflowId = TaskId.CreateRoot("version-descriptor");
        await using var harness = new DurableTaskReplayHarness(static grain => Workflow(grain, Original));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(workflowId, TestCancellation);
            await CommitAtGateAsync(activation);
        }

        // 下一次部署添加版本 2；待完成的执行实例继续使用版本 1。
        harness.Workflow = static grain => Workflow(grain, Extended);
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(Timeout, TestCancellation);
        replay.Grain.OpenGate();

        Assert.Equal("1:True:False", (await replay.GetResponseAsync(workflowId, TestCancellation)).GetResult<string>());

        static async DurableTask<string> Workflow(ReplayGrain grain, DurableVersion change)
        {
            var version = change.Get();
            await grain.WaitAtGateAsync();
            return $"{version}:{change.IsAtLeast(1)}:{change.IsAtLeast(2)}";
        }
    });

    private static readonly DurableVersion Original = new("descriptor", minSupported: 0, maxSupported: 1);

    private static readonly DurableVersion Extended = new("descriptor", minSupported: 0, maxSupported: 2);

    /// <summary>等待工作流在同步关卡处进入等待，再通过一次无关的写入提交工作流已暂存的更改。</summary>
    private static async Task CommitAtGateAsync(ReplayActivation activation)
    {
        await activation.Grain.AtGate.WaitAsync(Timeout, TestCancellation);
        activation.Grain.RequestUnrelatedWrite();
        await activation.Grain.UnrelatedWrites;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition wasn't met in time.");
            await Task.Delay(10, TestCancellation);
        }
    }
}
