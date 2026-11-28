using Orleans.Configuration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 执行尝试分段运行，每个执行段都是激活实例上的一个本地请求，即两次持久化等待之间的代码。
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

        // 已挂起执行尝试的执行段结束，因此激活实例接纳另一个执行尝试，后者的执行段占用该激活实例。
        await ((IDurableTaskServer)runtime).ScheduleAsync(blockingId, CreateRequest(4, () => DurableTask.Run(async _ =>
        {
            blockingStarted.TrySetResult();
            await release.Task;
            return 5;
        })), cancellationToken);
        await blockingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        // 调用结果在另一个执行段运行期间提交，因此已挂起的执行尝试可以运行，但尚未恢复执行。
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

        // 停止过程不会等待已挂起的执行尝试，后者不再运行任何代码；下一个激活实例将重放它。
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

        // Messaging 在激活实例启动时、运行时恢复之前查询其工作来源，因此替代
        // 激活实例会从存储报告待完成的执行实例，并重新设置唤醒操作。
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

        // 停止过程丢弃正在运行的执行尝试，而 Messaging 会持续查询其工作来源，直至消息泵停止：
        // 当时运行的唤醒操作必须保留后续唤醒，重新激活 Grain 以恢复执行实例。
        await runtime.StopAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.Equal(new DurableTaskOptions().RunnableExecutionWakeupDelay, workSource.GetWakeupDelay());
    });
}
