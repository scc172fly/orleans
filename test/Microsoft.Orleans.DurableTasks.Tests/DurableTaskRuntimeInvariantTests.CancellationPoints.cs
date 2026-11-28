using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Tasks.Storage;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 取消点：执行实例记录其主体首次观察到任务取消的位置，该位置按主体启动持久化操作的次数计数；
/// 重放会在该位置传递取消。
/// </summary>
public sealed partial class DurableTaskRuntimeInvariantTests
{
    private static readonly DurableSignal<string> NeverSent = new("never-sent");
    private static readonly TimeSpan CancellationPointTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [MemberData(nameof(DurableTaskJournalHarness.Formats), MemberType = typeof(DurableTaskJournalHarness))]
    public Task ReplayObservesTaskCancellationAtTheRecordedPointAndRecordsTheSameCompensation(string journalFormatKey) => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflowId = TaskId.CreateRoot($"canceled-{Guid.NewGuid():N}");
        var probe = new CancellationProbe();
        await using var harness = new DurableTaskReplayHarness(grain => ObserveAtGateAsync(grain, probe, holdBeforeCompensation: false), journalFormatKey);
        string[] live;
        TaskId[] liveRecords;
        await using (var activation = await harness.ActivateAsync(cancellationToken))
        {
            await activation.ScheduleAsync(workflowId, cancellationToken);
            await activation.Grain.AtGate.WaitAsync(CancellationPointTimeout, cancellationToken);

            // 主体在第二个操作启动后的门控处观察到取消，即取消点 2。
            await ((IDurableTaskManager)activation.Runtime).CancelAsync(workflowId, "stop", cancellationToken);
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            await activation.Grain.UnrelatedWrites;
            live = probe.NextActivation();
            liveRecords = GetRecordIds(activation.Storage, workflowId);
            Assert.Equal(2, GetCancellationPoint(activation.Storage, workflowId));
        }

        Assert.Equal(["first:False", "second:False", "canceled:True"], live);
        Assert.Equal(1, harness.EffectRuns);

        // 已完成补偿的主体保持等待时，激活实例结束。每种日志格式都恢复该记录，重放
        // 在到达已记录的取消点之前使持久化取消令牌保持未取消状态，因此会沿相同路径执行。
        await using var replay = await harness.ActivateAsync(cancellationToken);
        Assert.True(replay.Storage.TryGetTask(workflowId.Child(DurableTaskGrainRuntime.CancellationPointSegment), out var record));
        Assert.Equal(DurableTaskRecordKind.CancellationObserved, record.Kind);
        Assert.Equal(2, record.Result!.GetResult<int>());
        await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
        probe.Release();

        Assert.Equal("compensated", (await replay.GetResponseAsync(workflowId, cancellationToken)).GetResult<string>());
        Assert.Equal(live, probe.NextActivation());
        Assert.Equal(1, harness.EffectRuns);
        Assert.Equal(1, replay.Grain.Counter.Value);
        Assert.Equal(liveRecords, GetRecordIds(replay.Storage, workflowId));
        Assert.Equal(2, GetCancellationPoint(replay.Storage, workflowId));
    });

    [Fact]
    public Task CancellationOfAParkedAttemptRecordsThePointOfTheParkedWait() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflowId = TaskId.CreateRoot($"canceled-{Guid.NewGuid():N}");
        var probe = new CancellationProbe();
        await using var harness = new DurableTaskReplayHarness(grain => ObserveAtSignalWaitAsync(grain, probe));
        string[] live;
        await using (var activation = await harness.ActivateAsync(cancellationToken))
        {
            await activation.ScheduleAsync(workflowId, cancellationToken);
            await probe.AtWait.WaitAsync(CancellationPointTimeout, cancellationToken);

            // 执行尝试停驻在信号等待处，该等待的启动是其第三个计数点。
            await ((IDurableTaskManager)activation.Runtime).CancelAsync(workflowId, "stop", cancellationToken);
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            await activation.Grain.UnrelatedWrites;
            live = probe.NextActivation();
            Assert.Equal(3, GetCancellationPoint(activation.Storage, workflowId));
        }

        Assert.Equal(["first:False", "second:False", "wait:False", "canceled:True"], live);

        // 重放在启动信号等待时传递取消，随后该等待以与首次运行相同的方式结束。
        await using var replay = await harness.ActivateAsync(cancellationToken);
        await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
        probe.Release();

        Assert.Equal("compensated", (await replay.GetResponseAsync(workflowId, cancellationToken)).GetResult<string>());
        Assert.Equal(live, probe.NextActivation());
        Assert.Equal(1, harness.EffectRuns);
        Assert.Equal(3, GetCancellationPoint(replay.Storage, workflowId));
    });

    [Fact]
    public Task ReplayWhosePointDidNotCommitObservesTheCancellationWhereTheReplayEndsAndRecordsIt() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflowId = TaskId.CreateRoot($"canceled-{Guid.NewGuid():N}");
        var pointId = workflowId.Child(DurableTaskGrainRuntime.CancellationPointSegment);
        var recordedPoints = new ConcurrentQueue<int>();
        using var subscription = DurableTaskEvents.AllEvents.Subscribe(new EventObserver(evt =>
        {
            if (evt is DurableTaskEvents.TaskCompleted { TaskId: var taskId } completed && taskId == pointId)
            {
                recordedPoints.Enqueue(completed.Response.GetResult<int>());
            }
        }));
        var probe = new CancellationProbe();
        await using var harness = new DurableTaskReplayHarness(grain => ObserveAtGateAsync(grain, probe, holdBeforeCompensation: true));
        await using (var activation = await harness.ActivateAsync(cancellationToken))
        {
            await activation.ScheduleAsync(workflowId, cancellationToken);
            await activation.Grain.AtGate.WaitAsync(CancellationPointTimeout, cancellationToken);

            // 请求提交，主体观察到请求并保持等待，此时尚未有后续写入提交取消点的记录。
            await ((IDurableTaskManager)activation.Runtime).CancelAsync(workflowId, "stop", cancellationToken);
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            Assert.Equal([2], recordedPoints);
        }

        Assert.Equal(["first:False", "second:False", "canceled:True"], probe.NextActivation());

        // 重放恢复没有取消点的请求。它使用未取消的令牌重放已记录的操作，
        // 到达最后一个已记录操作时观察到取消，并记录该取消点。
        await using (var replay = await harness.ActivateAsync(cancellationToken))
        {
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            Assert.Equal([2, 2], recordedPoints);
            probe.Release();
            Assert.Equal("compensated", (await replay.GetResponseAsync(workflowId, cancellationToken)).GetResult<string>());
        }

        Assert.Equal(["first:False", "second:False", "canceled:True"], probe.NextActivation());
        Assert.Equal(1, harness.EffectRuns);

        // 提交结果的写入也提交了重放期间记录的取消点。
        await using var recovered = await harness.ActivateAsync(cancellationToken);
        Assert.Equal(2, GetCancellationPoint(recovered.Storage, workflowId));
    });

    [Fact]
    public Task CancellationPointIsNotARecordedOperationOfItsExecution() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflowId = TaskId.CreateRoot($"canceled-{Guid.NewGuid():N}");
        var probe = new CancellationProbe();
        await using var harness = new DurableTaskReplayHarness(grain => ObserveAtGateAsync(grain, probe, holdBeforeCompensation: false));
        await using (var activation = await harness.ActivateAsync(cancellationToken))
        {
            await activation.ScheduleAsync(workflowId, cancellationToken);
            await activation.Grain.AtGate.WaitAsync(CancellationPointTimeout, cancellationToken);
            await ((IDurableTaskManager)activation.Runtime).CancelAsync(workflowId, "stop", cancellationToken);
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            await activation.Grain.UnrelatedWrites;
            Assert.Equal(
                [workflowId.Child("compensate"), workflowId.Child("first"), workflowId.Child("second")],
                activation.Runtime.GetRecordedOperations(workflowId)!.OrderBy(static id => id.ToString(), StringComparer.Ordinal));
            _ = probe.NextActivation();
        }

        // 相同代码的重放到达每个已记录操作，由此结束重放，不会发生偏离。
        using var stalls = new StallRecorder(workflowId);
        await using var replay = await harness.ActivateAsync(cancellationToken);
        await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
        Assert.False(probe.HeldWhileReplaying);
        probe.Release();

        Assert.Equal("compensated", (await replay.GetResponseAsync(workflowId, cancellationToken)).GetResult<string>());
        Assert.False(stalls.Stalled.IsCompleted);
    });

    [Fact]
    public Task ReplayWhichEndsBeforeTheRecordedPointStallsAsDiverged() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflowId = TaskId.CreateRoot($"canceled-{Guid.NewGuid():N}");
        var probe = new CancellationProbe();
        await using var harness = new DurableTaskReplayHarness(grain => ObserveAtGateAsync(grain, probe, holdBeforeCompensation: false));
        await using (var activation = await harness.ActivateAsync(cancellationToken))
        {
            await activation.ScheduleAsync(workflowId, cancellationToken);
            await activation.Grain.AtGate.WaitAsync(CancellationPointTimeout, cancellationToken);
            await ((IDurableTaskManager)activation.Runtime).CancelAsync(workflowId, "stop", cancellationToken);
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            await activation.Grain.UnrelatedWrites;
            _ = probe.NextActivation();
        }

        // 较新版本在第一个操作后就返回，因此其重放永远不会到达取消点 2。
        var original = harness.Workflow;
        harness.Workflow = static _ => Changed();
        using (var stalls = new StallRecorder(workflowId))
        {
            await using var changed = await harness.ActivateAsync(cancellationToken);
            var stalled = await stalls.Stalled.WaitAsync(CancellationPointTimeout, cancellationToken);
            Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
            Assert.Contains("ended at cancellation point 1, before point 2", stalled.Exception.Message, StringComparison.Ordinal);
            Assert.True(changed.Storage.TryGetTask(workflowId, out var state));
            Assert.Null(state.CompletedAt);
        }

        // 能够到达该取消点的代码重放执行实例。
        harness.Workflow = original;
        await using var replay = await harness.ActivateAsync(cancellationToken);
        await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
        probe.Release();
        Assert.Equal("compensated", (await replay.GetResponseAsync(workflowId, cancellationToken)).GetResult<string>());
        Assert.Equal(1, harness.EffectRuns);

        static async DurableTask<string> Changed()
        {
            DurableTask.RunInline("first", static () => 1);
            await Task.Yield();
            return "changed";
        }
    });

    /// <summary>
    /// 运行两个效果，随后使用持久化取消令牌在 Grain 的门控处等待，因此在该处到达的取消
    /// 会在取消点 2 被观察到；使用效果执行补偿，并保持等待直至测试将其释放。
    /// </summary>
    private static async DurableTask<string> ObserveAtGateAsync(ReplayGrain grain, CancellationProbe probe, bool holdBeforeCompensation)
    {
        try
        {
            probe.Observe("first");
            DurableTask.RunInline("first", static () => 1);
            probe.Observe("second");
            DurableTask.RunInline("second", static () => 2);
            await grain.WaitAtGateAsync();
            return "completed";
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            probe.Observe("canceled");
            if (holdBeforeCompensation)
            {
                await probe.HoldAsync();
            }

            await CompensateAsync(grain, probe, hold: !holdBeforeCompensation);
            return "compensated";
        }
    }

    /// <summary>运行两个效果，然后停驻在无人发送信号的信号等待处（第三个计数点）；随后执行补偿。</summary>
    private static async DurableTask<string> ObserveAtSignalWaitAsync(ReplayGrain grain, CancellationProbe probe)
    {
        try
        {
            probe.Observe("first");
            DurableTask.RunInline("first", static () => 1);
            probe.Observe("second");
            DurableTask.RunInline("second", static () => 2);
            probe.Observe("wait");
            return await NeverSent.WaitAsync();
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            probe.Observe("canceled");
            await CompensateAsync(grain, probe, hold: true);
            return "compensated";
        }
    }

    /// <summary>记录补偿效果，请求一次提交该效果的写入，并可选择保持等待。</summary>
    private static Task CompensateAsync(ReplayGrain grain, CancellationProbe probe, bool hold)
    {
        DurableTask.RunInline("compensate", () =>
        {
            grain.Harness.CountEffectRun();
            return ++grain.Counter.Value;
        });
        grain.RequestUnrelatedWrite();
        return hold ? probe.HoldAsync() : Task.CompletedTask;
    }

    private static int? GetCancellationPoint(DurableTaskGrainStorage storage, TaskId executionId) =>
        storage.TryGetTask(executionId.Child(DurableTaskGrainRuntime.CancellationPointSegment), out var record)
            ? record.Result!.GetResult<int>()
            : null;

    private static TaskId[] GetRecordIds(DurableTaskGrainStorage storage, TaskId executionId) =>
        [.. storage.GetChildren(executionId).Select(static child => child.Id).OrderBy(static id => id.ToString(), StringComparer.Ordinal)];

    /// <summary>
    /// 记录一个激活实例的执行尝试在每个步骤之前观察到的持久化取消令牌状态，并在执行尝试完成补偿后
    /// 使其保持等待，直至测试释放它或激活实例结束。
    /// </summary>
    private sealed class CancellationProbe
    {
        private readonly List<string> _observed = [];
        private TaskCompletionSource _atWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>执行尝试即将等待其信号时完成。</summary>
        public Task AtWait => _atWait.Task;

        /// <summary>执行尝试进入保持等待状态时完成。</summary>
        public Task Held => _held.Task;

        /// <summary>获取执行尝试进入保持等待状态时是否仍在重放。</summary>
        public bool HeldWhileReplaying { get; private set; }

        public void Observe(string step)
        {
            _observed.Add($"{step}:{DurableTask.Current.CancellationToken.IsCancellationRequested}");
            if (step == "wait")
            {
                _atWait.TrySetResult();
            }
        }

        /// <summary>使执行尝试保持等待，直至 <see cref="Release"/>，或该执行尝试被取消。</summary>
        public Task HoldAsync()
        {
            HeldWhileReplaying = DurableTask.Current.IsReplaying;
            _held.TrySetResult();
            return _release.Task.WaitAsync(DurableTask.Current.AttemptCancellationToken);
        }

        public void Release() => _release.TrySetResult();

        /// <summary>返回已结束的激活实例的执行尝试所观察到的内容，并重置探针以供下一个激活实例使用。</summary>
        public string[] NextActivation()
        {
            string[] observed = [.. _observed];
            _observed.Clear();
            _atWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return observed;
        }
    }
}
