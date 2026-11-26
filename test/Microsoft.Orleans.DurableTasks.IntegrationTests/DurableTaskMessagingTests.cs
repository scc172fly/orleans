using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Xunit;
using Orleans.Durable.Tasks.Runtime;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 耐久演习 grain 跨区域资源、全区域、全区域、全区域、全区域、全区域、 grains 发件人和发件人之间
/// </summary>
/// <remarks>
/// 一位协调员向一个工人发出几个持久的电话,所以该工人的收件箱和协调员的收件箱
/// 每个接收若干耐久的RPC信息,这些信息可以分一个盒处理,每箱中有一批单批书写。
/// </remarks>
public abstract class DurableTaskMessagingTests(string journalFormatKey) : IAsyncLifetime
{
    private const int FanOut = 8;
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task RemoteCompletionsReturnToCallerThroughInbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = _fixture.Client.GetGrain<IDurableTaskCoordinatorGrain>(Guid.NewGuid());
        var workerKey = Guid.NewGuid();

        var scheduled = await coordinator.SumEchoesAsync(workerKey, FanOut).WithId("sum-echoes").ScheduleAsync(cancellationToken);
        var response = await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal(Enumerable.Range(0, FanOut).Sum(static value => value * 2), response.GetResult<int>());
        Assert.Equal(FanOut, _fixture.MessagingProbe.GetStartedCount(workerKey));
    }

    [Fact]
    public async Task SiloHostedCallerOutsideGrainSchedulesAndCancelsRootTasks()
    {
        // 在 silo主机中运行但位于 grain以外的代码,例如联合主持网站端点、时间表
        // 与外部客户端一样, silo的工厂 grain 。
        var cancellationToken = TestContext.Current.CancellationToken;
        var grainFactory = _fixture.Cluster.GetSiloServiceProvider().GetRequiredService<IGrainFactory>();
        var worker = grainFactory.GetGrain<IDurableTaskWorkerGrain>(Guid.NewGuid());

        var echo = await worker.EchoAsync(21).WithId("silo-hosted-echo").ScheduleAsync(cancellationToken);
        var wait = await worker.WaitForCancellationAsync(1).WithId("silo-hosted-wait").ScheduleAsync(cancellationToken);
        await wait.CancelAsync(cancellationToken);
        var canceled = await wait.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        Assert.Equal(42, await echo.WaitAsync(cancellationToken));
        Assert.Equal(DurableTaskStatus.Canceled, canceled.Status);
    }

    [Fact]
    public async Task PollingInterleavesWithOtherRequestsToTheGrain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workerKey = Guid.NewGuid();
        var worker = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        var started = _fixture.MessagingProbe.WaitForStartedAsync(workerKey, 1);
        var waiting = await worker.WaitForCancellationAsync(1).WithId("long-poll").ScheduleAsync(cancellationToken);
        await started.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        // 民意测验超过测试,所以在调查完成后提出的请求只有在 grain 等待着,
        // 客户端不能拖曳请求和信箱信息完成任务的 grain 。
        var poll = worker.AsReference<IDurableTaskGrainExtension>().SubscribeOrPollAsync(
            waiting.Id,
            new SubscribeOrPollOptions { PollTimeout = TimeSpan.FromMinutes(5) },
            cancellationToken).AsTask();
        var echo = await worker.EchoAsync(21).WithId("echo").ScheduleAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(42, await echo.WaitAsync(cancellationToken));
        Assert.False(poll.IsCompleted);

        await waiting.CancelAsync(cancellationToken);
        var response = await poll.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, response.Status);
    }
    [Fact]
    public async Task CancellationFansOutToRemoteChildrenAndCompletesThroughInbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = _fixture.Client.GetGrain<IDurableTaskCoordinatorGrain>(Guid.NewGuid());
        var workerKey = Guid.NewGuid();
        var allStarted = _fixture.MessagingProbe.WaitForStartedAsync(workerKey, FanOut);
        var allCanceled = _fixture.MessagingProbe.WaitForCanceledAsync(workerKey, FanOut);
        using var events = DurableTaskEventBarriers.Collect();

        var scheduled = await coordinator.WaitForCancellationAsync(workerKey, FanOut).WithId("fan-out").ScheduleAsync(cancellationToken);
        await allStarted.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        await scheduled.CancelAsync(cancellationToken);

        await allCanceled.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        var response = await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, response.Status);
        var workerId = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey).GetGrainId();
        for (var index = 0; index < FanOut; index++)
        {
            // 工人保留每个被取消的孩子的终点状态 在电话范围下,直到打电话者的收件箱
            // 接受其完成,然后删除它。
            var childId = await events.WaitForCallerChildAsync(workerId, coordinator.GetGrainId(), TaskId.CreateRoot("fan-out").Child($"wait-{index}"));
            Assert.Equal(DurableTaskStatus.Canceled, (await events.WaitForCompletedAsync(workerId, childId)).Status);
            await events.WaitForDeletedAsync(workerId, childId);
        }
    }
}

/// <summary>运行跨grain 信息覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskMessagingTests() : DurableTaskMessagingTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行跨grain 信息覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskMessagingTests() : DurableTaskMessagingTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface IDurableTaskCoordinatorGrain : IGrainWithGuidKey
{
    DurableTask<int> SumEchoesAsync(Guid workerKey, int count);
    DurableTask<int> WaitForCancellationAsync(Guid workerKey, int count);
}

public interface IDurableTaskWorkerGrain : IGrainWithGuidKey
{
    DurableTask<int> EchoAsync(int value);
    DurableTask<int> WaitForCancellationAsync(int value);

    /// <summary>弄清楚工人信箱里写了什么死信的原因</summary>
    Task<string[]> GetInboxDeadLetterReasonsAsync();
}

public sealed class DurableTaskMessagingProbe
{
    private readonly ConcurrentDictionary<Guid, Counter> _started = [];
    private readonly ConcurrentDictionary<Guid, Counter> _canceled = [];

    public void RecordStarted(Guid workerKey) => _started.GetOrAdd(workerKey, static _ => new()).Increment();

    public void RecordCanceled(Guid workerKey) => _canceled.GetOrAdd(workerKey, static _ => new()).Increment();

    public int GetStartedCount(Guid workerKey) => _started.GetOrAdd(workerKey, static _ => new()).Count;

    public Task WaitForStartedAsync(Guid workerKey, int count) => _started.GetOrAdd(workerKey, static _ => new()).WaitForAsync(count);

    public Task WaitForCanceledAsync(Guid workerKey, int count) => _canceled.GetOrAdd(workerKey, static _ => new()).WaitForAsync(count);

    private sealed class Counter
    {
        private readonly List<(int Count, TaskCompletionSource Completion)> _waiters = [];
        private int _count;

        public int Count
        {
            get
            {
                lock (_waiters)
                {
                    return _count;
                }
            }
        }

        public void Increment()
        {
            lock (_waiters)
            {
                _count++;
                foreach (var waiter in _waiters.Where(waiter => waiter.Count <= _count).ToList())
                {
                    _waiters.Remove(waiter);
                    waiter.Completion.TrySetResult();
                }
            }
        }

        public Task WaitForAsync(int count)
        {
            lock (_waiters)
            {
                if (_count >= count)
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, completion));
                return completion.Task;
            }
        }
    }
}

[GrainType("durable-task-coordinator-test")]
public sealed class DurableTaskCoordinatorGrain : DurableGrain, IDurableTaskCoordinatorGrain
{
    public async DurableTask<int> SumEchoesAsync(Guid workerKey, int count)
    {
        var worker = GrainFactory.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        var scheduled = new List<ScheduledTask<int>>(count);
        for (var index = 0; index < count; index++)
        {
            scheduled.Add(await worker.EchoAsync(index).WithId($"echo-{index}").ScheduleAsync());
        }

        var sum = 0;
        foreach (var task in scheduled)
        {
            sum += await task.WaitAsync();
        }

        return sum;
    }

    public async DurableTask<int> WaitForCancellationAsync(Guid workerKey, int count)
    {
        var worker = GrainFactory.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        var scheduled = new List<ScheduledTask<int>>(count);
        for (var index = 0; index < count; index++)
        {
            scheduled.Add(await worker.WaitForCancellationAsync(index).WithId($"wait-{index}").ScheduleAsync());
        }

        var sum = 0;
        foreach (var task in scheduled)
        {
            sum += await task.WaitAsync();
        }

        return sum;
    }
}

[GrainType("durable-task-worker-test")]
public sealed class DurableTaskWorkerGrain(DurableTaskMessagingProbe probe, IMessagingDiagnostics diagnostics)
    : DurableGrain, IDurableTaskWorkerGrain
{
    public Task<string[]> GetInboxDeadLetterReasonsAsync() =>
        Task.FromResult(diagnostics.InboxDeadLetters.Select(deadLetter => deadLetter.Reason).ToArray());

    public DurableTask<int> EchoAsync(int value)
    {
        probe.RecordStarted(this.GetPrimaryKey());
        return DurableTask.FromResult(value * 2);
    }

    private static readonly DurableSignal<int> Never = new("never");

    // 空闲的公园在信号上,但一直没有到来,所以它 grain 承认了其他请求,直到任务取消结束等待。
    public async DurableTask<int> WaitForCancellationAsync(int value)
    {
        probe.RecordStarted(this.GetPrimaryKey());
        try
        {
            await Never.WaitAsync();
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            probe.RecordCanceled(this.GetPrimaryKey());
            throw;
        }

        return value;
    }
}
