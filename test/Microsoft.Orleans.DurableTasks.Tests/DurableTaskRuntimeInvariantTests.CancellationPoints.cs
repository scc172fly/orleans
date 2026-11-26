using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Tasks.Storage;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 注销点:执行时记录在身体持久运行开始时计算到的点,其
/// 机体首先观察任务取消的情况,重播在那个时候显示取消。
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

            // 尸体在第二次手术开始后,在大门观察吊销:第2点。
            await ((IDurableTaskManager)activation.Runtime).CancelAsync(workflowId, "stop", cancellationToken);
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            await activation.Grain.UnrelatedWrites;
            live = probe.NextActivation();
            liveRecords = GetRecordIds(activation.Storage, workflowId);
            Assert.Equal(2, GetCancellationPoint(activation.Storage, workflowId));
        }

        Assert.Equal(["first:False", "second:False", "canceled:True"], live);
        Assert.Equal(1, harness.EffectRuns);

        // 激活结束, 补偿机构保持时停止。 每份日记格式都恢复记录和重放
        // 保持耐久的牌子未加排整,直到它到达记录点,所以它走的是相同的路径。
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

            // 尝试停在信号等待处,其起点是第三点。
            await ((IDurableTaskManager)activation.Runtime).CancelAsync(workflowId, "stop", cancellationToken);
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            await activation.Grain.UnrelatedWrites;
            live = probe.NextActivation();
            Assert.Equal(3, GetCancellationPoint(activation.Storage, workflowId));
        }

        Assert.Equal(["first:False", "second:False", "wait:False", "canceled:True"], live);

        // 重播在启动信号等待时发送取消, 然后和第一次运行一样结束 。
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

            // 请求提出后,机构予以遵守并保存在以后的任何写作前,即记录点。
            await ((IDurableTaskManager)activation.Runtime).CancelAsync(workflowId, "stop", cancellationToken);
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            Assert.Equal([2], recordedPoints);
        }

        Assert.Equal(["first:False", "second:False", "canceled:True"], probe.NextActivation());

        // 重放恢复请求时没有点。 它用未排出牌子重播其记录操作,
        // 观察注销到达最后一位时的值,并记录该点。
        await using (var replay = await harness.ActivateAsync(cancellationToken))
        {
            await probe.Held.WaitAsync(CancellationPointTimeout, cancellationToken);
            Assert.Equal([2, 2], recordedPoints);
            probe.Release();
            Assert.Equal("compensated", (await replay.GetResponseAsync(workflowId, cancellationToken)).GetResult<string>());
        }

        Assert.Equal(["first:False", "second:False", "canceled:True"], probe.NextActivation());
        Assert.Equal(1, harness.EffectRuns);

        // 写作结果的文字也使重播点被赋予了。
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

        // 重复使用同一代码 就能到达每一个记录的行动 结束它的重播 并且不会出现差异
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

        // 后一版本在第一次操作后返回, 因此重播从未达到点2。
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

        // 到达点的代码重播执行。
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
    /// 运行两个效果, 然后在 grain大门等待耐久牌, 所以它看到一个取消, 并到达
    /// 在2点; 补偿效果, 并坚持到试验释放它。
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

    /// <summary>运行两个效果, 然后在无人发送的信号等待处停靠, 第三个点, 然后补偿。</summary>
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

    /// <summary>记录补偿效果,要求书面作出赔偿,并可选择保留。</summary>
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
    /// 记录一个引爆装置在每一步前 观察到的持久标志 并保持这一尝试
    /// 它可以补偿,直到试验释放它或启动结束。
    /// </summary>
    private sealed class CancellationProbe
    {
        private readonly List<string> _observed = [];
        private TaskCompletionSource _atWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>一旦尝试即将完成 等待信号。</summary>
        public Task AtWait => _atWait.Task;

        /// <summary>一旦尝试暂停, 完成 。</summary>
        public Task Held => _held.Task;

        /// <summary>获取是否在尝试保持时仍然重播。</summary>
        public bool HeldWhileReplaying { get; private set; }

        public void Observe(string step)
        {
            _observed.Add($"{step}:{DurableTask.Current.CancellationToken.IsCancellationRequested}");
            if (step == "wait")
            {
                _atWait.TrySetResult();
            }
        }

        /// <summary>将尝试维持到 <see cref="Release"/>或取消尝试。</summary>
        public Task HoldAsync()
        {
            HeldWhileReplaying = DurableTask.Current.IsReplaying;
            _held.TrySetResult();
            return _release.Task.WaitAsync(DurableTask.Current.AttemptCancellationToken);
        }

        public void Release() => _release.TrySetResult();

        /// <summary>返回结束激活的尝试所观察到的内容,并重新设置下一次激活的探测器。</summary>
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
