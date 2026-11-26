using Orleans.Configuration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 尝试在区段运行,每个区段都请求启动: 两个耐久等待之间的代码。
/// </summary>
public sealed partial class DurableTaskRuntimeInvariantTests
{
    [Fact]
    public Task ParkedAttemptResumesOnlyOnceTheRunningSegmentEnds() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, _, manager, _) = CreateRuntime();
        await runtime.RecoverAsync(cancellationToken);
        var parkedId = TaskId.Parse("parked");
        var blockingId = TaskId.Parse("blocking");
        var resumed = false;
        var blockingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await ((IDurableTaskServer)runtime).ScheduleAsync(parkedId, CreateRequest(1, () => DurableTask.Run(async _ =>
        {
            await new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(2)).WithId("call");
            resumed = true;
            return 3;
        })), cancellationToken);
        await WaitUntilAsync(() => runtime.GetWakeupDelay() is null);

        // 停车的尝试部分结束, 所以启动承认了另一个尝试, 其部分持有它。
        await ((IDurableTaskServer)runtime).ScheduleAsync(blockingId, CreateRequest(4, () => DurableTask.Run(async _ =>
        {
            blockingStarted.TrySetResult();
            await release.Task;
            return 5;
        })), cancellationToken);
        await blockingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        // 电话结果在另一段运行时被确认 所以停靠的尝试是可调用的 但不会恢复
        runtime.AcceptResponseFromInbox(parkedId.Child("call"), DurableTaskResponse.FromResult(2), RemoteTarget);
        await manager.WriteStateAsync(cancellationToken);
        for (var yields = 0; yields < 20; yields++)
        {
            await Task.Yield();
        }

        Assert.False(resumed);
        Assert.Equal(new DurableTaskOptions().RunnableExecutionWakeupDelay, runtime.GetWakeupDelay());

        release.SetResult();
        Assert.Equal(5, (await WaitForResponseAsync(runtime, blockingId)).GetResult<int>());
        Assert.Equal(3, (await WaitForResponseAsync(runtime, parkedId)).GetResult<int>());
        Assert.True(resumed);
        await runtime.StopAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    });

    [Fact]
    public Task DeactivationDiscardsAParkedAttemptWithoutResumingIt() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, storage, _, _) = CreateRuntime();
        await runtime.RecoverAsync(cancellationToken);
        var rootId = TaskId.Parse("root");
        var parked = false;
        var resumed = false;

        await ((IDurableTaskServer)runtime).ScheduleAsync(rootId, CreateRequest(1, () => DurableTask.Run(async _ =>
        {
            try
            {
                parked = true;
                await new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(2)).WithId("call");
            }
            finally
            {
                resumed = true;
            }

            return 3;
        })), cancellationToken);
        await WaitUntilAsync(() => parked && runtime.GetWakeupDelay() is null);

        // 停止不等待停靠的尝试, 它没有更多的代码: 下一次激活重播它。
        await runtime.StopAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        for (var yields = 0; yields < 20; yields++)
        {
            await Task.Yield();
        }

        Assert.False(resumed);
        Assert.Null(storage.Get(rootId).Result);
    });

    [Fact]
    public Task WorkSourceReportsRecoveredExecutionsBeforeRecoveryStartsThem() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, storage, manager, _) = CreateRuntime();
        await runtime.RecoverAsync(cancellationToken);
        var rootId = TaskId.Parse("root");
        runtime.ScheduleFromInbox(rootId, CreateRequest(1, () => DurableTask.Run(async _ =>
        {
            await new TestStateManager.TestRemoteDurableTask(CreateRemoteRequest(2)).WithId("call");
            return 3;
        })));
        await manager.WriteStateAsync(cancellationToken);
        await WaitUntilAsync(() => runtime.GetWakeupDelay() is null);

        // 通讯设备在启动时,在运行时间恢复之前,即开始使用时,会咨询工作来源。
        // 启动报告从存储处执行前的操作,并重新武装警报。
        var (replacement, _, _, _) = CreateRuntime(storage, manager);
        Assert.Equal(new DurableTaskOptions().RunnableExecutionWakeupDelay, replacement.GetWakeupDelay());

        await replacement.RecoverAsync(cancellationToken);
        replacement.StartRecoveredExecutions();
        await WaitUntilAsync(() => replacement.GetWakeupDelay() is null);
        await replacement.StopAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    });

    [Fact]
    public Task StoppedRuntimeKeepsTheWakeupOfARunnableExecutionWhichItDiscarded() => ActivationScheduler.RunAsync(async () =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (runtime, _, _, _) = CreateRuntime();
        await runtime.RecoverAsync(cancellationToken);
        var workSource = runtime;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await ((IDurableTaskServer)runtime).ScheduleAsync(TaskId.Parse("root"), CreateRequest(1, () => DurableTask.Run(async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return 1;
        })), cancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        // 停止运动会抛弃运行中的尝试,
        // 之后,必须留下继任者,让 grain 重新恢复处决。
        await runtime.StopAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.Equal(new DurableTaskOptions().RunnableExecutionWakeupDelay, workSource.GetWakeupDelay());
    });
}
