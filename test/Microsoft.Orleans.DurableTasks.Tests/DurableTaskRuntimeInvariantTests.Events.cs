using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Fact]
    public Task CommittedBodyResultLeavesNoPendingWorkWhenTheAttemptCommitsItAgain() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, _, manager, _) = CreateRuntime();
        var workSource = runtime;
        var rootId = TaskId.Parse("root");
        var scheduler = TaskScheduler.Current;
        Task? interleavedWrite = null;

        // 该机构的最终代码将结果和要求另一机构在
        // 尝试再次实施结果。 写作记录了每一个变化, 所以尝试的写作没有包含任何。
        // 整个过程都发生了,所以其他测试任务“root”同时完成;外国完成,排放
        // 绝对不能引发这个测试的写作
        using var subscription = DurableTaskEvents.AllEvents.Subscribe(new EventObserver(evt =>
        {
            if (evt is DurableTaskEvents.TaskCompleted { TaskId: var taskId } completed
                && taskId == rootId
                && ReferenceEquals(completed.GrainContext, runtime.GrainContext)
                && interleavedWrite is null)
            {
                interleavedWrite = Task.Factory.StartNew(
                    async () =>
                    {
                        await manager.WriteStateAsync(cancellationToken);
                        manager.WritesAreEmpty = true;
                    },
                    cancellationToken,
                    TaskCreationOptions.None,
                    scheduler).Unwrap();
            }
        }));
        DurableTaskEvents.EmitTaskCompleted(CreateTestActivation(), rootId, DurableTaskResponse.FromResult(0));
        runtime.ScheduleFromInbox(rootId, CreateRequest(1, () => DurableTask.Run(static async _ =>
        {
            await Task.Yield();
            return 5;
        })));
        await manager.WriteStateAsync(cancellationToken);

        Assert.Equal(5, (await WaitForResponseAsync(runtime, rootId)).GetResult<int>());
        await interleavedWrite!.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        for (var yields = 0; workSource.GetWakeupDelay() is not null; yields++)
        {
            Assert.True(yields < 100, "The committed result stayed pending work.");
            await Task.Yield();
        }

        await runtime.StopAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    });

    [Fact]
    public Task RuntimeReportsATaskTreesLifecycleInOrder() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime(TimeSpan.FromDays(1));
        using var events = new RuntimeEventRecorder(runtime);
        var rootId = TaskId.Parse("root");
        var request = CreateRequest(1, () => DurableTask.Run(async _ => await DurableTask.Run(static _ => 2).WithId("step")));

        runtime.ScheduleFromInbox(rootId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => transport.Completions.Count == 1);
        await WaitUntilIdleAsync(runtime);
        ((IOutboxMessageSource)runtime).OnRetired(Assert.Single(transport.Completions).MessageId, OutboxMessageRetirement.Delivered.Instance);

        // 每场活动都携带运行时间的激活,交付完成后将树删除。
        Assert.Equal(
            [
                "TaskCreated root",
                "ExecutionStarted root",
                "TaskCreated root/step",
                "ExecutionStarted root/step",
                "TaskCompleted root/step",
                "ExecutionStopped root/step",
                "TaskCompleted root",
                "ExecutionStopped root",
                "TaskDeleted root/step",
                "TaskDeleted root",
            ],
            events.Events);
        Assert.False(storage.Contains(rootId));
    });

    [Fact]
    public Task ReplayReportsWhenItRunsLive() => ActivationScheduler.RunAsync(async () =>
    {
        var (runtime, storage, manager, transport) = CreateRuntime();
        var rootId = TaskId.Parse("root");
        var request = CreateRequest(
            1,
            () => DurableTask.Run(async _ =>
            {
                await DurableTask.Run(static _ => 2).WithId("step");
                await DurableTask.Delay(TimeSpan.FromDays(1)).WithId("delay");
            }));

        runtime.ScheduleFromInbox(rootId, request);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        // 延迟恢复的恢复时间已经到了, 所以替换的重弹了一个预定的推迟。
        // 尸体还没有计划要执行, 恢复工作从自己开始。
        await transport.ResumeSent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 替换的重放所记录的步和延迟; 到达延迟, 最后一次记录的操作, 结束
        // 拖延的尝试又恢复了等待时机和结束
        var (replacement, _, _, _) = CreateRuntime(storage, manager, initialize: false);
        replacement.InitializeForActivation(CreateTestActivation());
        using var events = new RuntimeEventRecorder(replacement);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => events.Events.Contains("ExecutionStopped root/delay"));

        Assert.Equal(["ExecutionStarted root", "ReplayEnded root", "ExecutionStarted root/delay", "ExecutionStopped root/delay"], events.Events);
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    /// <summary>记录一次运行时的事件, 以它的启动引发的顺序。</summary>
    private sealed class RuntimeEventRecorder : IDisposable
    {
        private readonly ConcurrentQueue<string> _events = new();
        private readonly IDisposable _subscription;

        public RuntimeEventRecorder(DurableTaskGrainRuntime runtime)
        {
            var context = runtime.GrainContext;
            _subscription = DurableTaskEvents.AllEvents.Subscribe(new EventObserver(evt =>
            {
                if (ReferenceEquals(evt.GrainContext, context))
                {
                    _events.Enqueue($"{evt.GetType().Name} {evt.TaskId}");
                }
            }));
        }

        public IReadOnlyCollection<string> Events => _events;

        public void Dispose() => _subscription.Dispose();
    }
}

/// <summary>运行每次耐久任务运行时事件的一个动作, 同步进行引发该动作的过渡。</summary>
internal sealed class EventObserver(Action<DurableTaskEvents.DurableTaskEvent> onNext) : IObserver<DurableTaskEvents.DurableTaskEvent>
{
    public void OnNext(DurableTaskEvents.DurableTaskEvent value) => onNext(value);

    public void OnCompleted() { }

    public void OnError(Exception error) { }
}
