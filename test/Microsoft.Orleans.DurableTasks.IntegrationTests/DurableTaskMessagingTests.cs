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
/// 测试通过消息传递收件箱和发件箱实现的跨 Grain 持久化 RPC。
/// </summary>
/// <remarks>
/// 协调者向同一工作 Grain 扇出多次持久化调用，因此工作 Grain 的收件箱和协调者的收件箱
/// 各自收到多条持久化 RPC 消息，可在一个收件箱批次内通过一次批量写入完成处理。
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
        // 在 Silo 宿主中但在 Grain 之外运行的代码（例如共同托管的 Web 终结点）通过
        // Silo 的 Grain 工厂调度任务，方式与外部客户端相同。
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

        // 轮询持续时间超过测试时长，因此在发起轮询之后发送的请求只有在 Grain 允许两者交错运行时才能完成。正在等待的
        // 客户端不得阻塞负责通过请求与收件箱消息完成该任务的 Grain。
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
            // 工作 Grain 在调用方作用域下保留各被取消子任务的终态，直到调用方收件箱
            // 接受其完成消息，然后删除该子任务。
            var childId = await events.WaitForCallerChildAsync(workerId, coordinator.GetGrainId(), TaskId.CreateRoot("fan-out").Child($"wait-{index}"));
            Assert.Equal(DurableTaskStatus.Canceled, (await events.WaitForCompletedAsync(workerId, childId)).Status);
            await events.WaitForDeletedAsync(workerId, childId);
        }
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行跨 Grain 消息传递相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskMessagingTests() : DurableTaskMessagingTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行跨 Grain 消息传递相关测试。</summary>
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

    /// <summary>获取工作 Grain 收件箱死信的原因。</summary>
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

    // 挂起以等待永远不会到达的信号，因此在任务取消结束等待前，所属 Grain 可接纳其他请求。
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
