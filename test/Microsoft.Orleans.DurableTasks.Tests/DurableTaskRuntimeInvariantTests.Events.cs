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

        // 主体的最终代码暂存其结果，另一操作请求的写入在执行尝试再次提交结果之前
        // 提交了该结果。该写入已捕获所有变更，因此执行尝试的写入不携带任何变更。
        // 事件在整个进程范围内生效，因此其他测试中名为 'root' 的任务也可能并发完成；此处以确定性方式
        // 发出的外部完成事件不得触发本测试的写入。
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

        // 每个事件都携带运行时的激活实例，已投递的完成消息会删除执行树。
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
        // 延迟操作的恢复与其到期时间一同暂存，因此替代激活实例会重放已调度的延迟操作。尚未由主体
        // 调度延迟操作的延迟子任务属于待处理执行实例，恢复过程会独立启动它。
        await transport.ResumeSent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await runtime.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 替代激活实例重放已记录的步骤和延迟操作；到达最后一个已记录操作（延迟操作）时，
        // 重放结束而不暂存任何内容。延迟操作的执行尝试恢复等待其到期时间，然后结束。
        var (replacement, _, _, _) = CreateRuntime(storage, manager, initialize: false);
        replacement.InitializeForActivation(CreateTestActivation());
        using var events = new RuntimeEventRecorder(replacement);
        await replacement.ResumePendingTasksAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => events.Events.Contains("ExecutionStopped root/delay"));

        Assert.Equal(["ExecutionStarted root", "ReplayEnded root", "ExecutionStarted root/delay", "ExecutionStopped root/delay"], events.Events);
        await replacement.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    });

    /// <summary>按某个运行时的激活实例引发事件的顺序记录该运行时的事件。</summary>
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

/// <summary>针对每个持久化任务运行时事件，在引发事件的状态转换处同步执行操作。</summary>
internal sealed class EventObserver(Action<DurableTaskEvents.DurableTaskEvent> onNext) : IObserver<DurableTaskEvents.DurableTaskEvent>
{
    public void OnNext(DurableTaskEvents.DurableTaskEvent value) => onNext(value);

    public void OnCompleted() { }

    public void OnError(Exception error) { }
}
