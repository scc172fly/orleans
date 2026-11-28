using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 测试以跨 Grain 调用为输入的超时与竞速：被调用方收到取消原因，
/// 工作流等待被调用方的结果，且超时机制在工作流停用后仍然有效。
/// </summary>
public abstract class DurableTaskRaceTests(string journalFormatKey) : IAsyncLifetime
{
    private const string TimeoutReason = "The operation timed out after 01:00:00.";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task TimeoutCancelsTheRemoteCallWithItsReasonAndReturnsTheCallsResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workerKey = Guid.NewGuid();
        var workflow = _fixture.Client.GetGrain<IDurableTaskRaceWorkflowGrain>(Guid.NewGuid());
        var holding = _fixture.MessagingProbe.WaitForStartedAsync(workerKey, 1);

        var scheduled = await workflow.HoldAsync(workerKey).WithId("hold").ScheduleAsync(cancellationToken);
        await holding.WaitAsync(Timeout, cancellationToken);
        _fixture.JobClock.Advance(TimeSpan.FromHours(2));

        // 被调用方观察到超时原因并返回该原因，因此调用结果即为工作流结果。
        Assert.Equal(TimeoutReason, (await scheduled.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<string>());
    }

    [Fact]
    public async Task TimeoutKeepsItsDeadlineWhenTheWorkflowDeactivatesWhileWaiting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workerKey = Guid.NewGuid();
        var workflow = _fixture.Client.GetGrain<IDurableTaskRaceWorkflowGrain>(Guid.NewGuid());
        var blocking = _fixture.MessagingProbe.WaitForStartedAsync(workerKey, 1);

        var scheduled = await workflow.BlockAsync(workerKey).WithId("block").ScheduleAsync(cancellationToken);
        await blocking.WaitAsync(Timeout, cancellationToken);
        Assert.True(_fixture.Cluster.TryGetGrainContext(workflow.GetGrainId(), out var activation));
        await workflow.DeactivateAsync();
        await activation.Deactivated.WaitAsync(Timeout, cancellationToken);

        // 下一次激活重放工作流，工作流等待第一次激活所记录的延迟。
        await workflow.PingAsync();
        _fixture.JobClock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(TimeoutReason, (await scheduled.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<string>());
        Assert.Equal(1, _fixture.MessagingProbe.GetStartedCount(workerKey));
    }

    [Fact]
    public async Task RaceCancelsTheLosingRemoteCallsAndWaitsForTheirOutcomes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var holdKey = Guid.NewGuid();
        var quoteKey = Guid.NewGuid();
        var workflow = _fixture.Client.GetGrain<IDurableTaskRaceWorkflowGrain>(Guid.NewGuid());

        var scheduled = await workflow.RaceAsync(holdKey, quoteKey).WithId("race").ScheduleAsync(cancellationToken);

        // 每次竞速中落败的调用返回其观察到的原因，针对句柄的竞速会观察该结果。
        Assert.Equal(
            "1:quote; quote; Another task won the race.",
            (await scheduled.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<string>());
        Assert.Equal(2, _fixture.MessagingProbe.GetStartedCount(holdKey));
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行竞速与超时相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskRaceTests() : DurableTaskRaceTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行竞速与超时相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskRaceTests() : DurableTaskRaceTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface IDurableTaskRaceWorkflowGrain : IGrainWithGuidKey
{
    DurableTask<string> HoldAsync(Guid workerKey);

    DurableTask<string> BlockAsync(Guid workerKey);

    DurableTask<string> RaceAsync(Guid holdKey, Guid quoteKey);

    Task DeactivateAsync();

    Task PingAsync();
}

public interface IDurableTaskRaceWorkerGrain : IGrainWithGuidKey
{
    DurableTask<string> HoldAsync();

    DurableTask BlockAsync();

    DurableTask<string> QuoteAfterAsync(Guid holdKey, int started);
}

[GrainType("durable-task-race-workflow-test")]
public sealed class DurableTaskRaceWorkflowGrain : DurableGrain, IDurableTaskRaceWorkflowGrain
{
    public async DurableTask<string> HoldAsync(Guid workerKey) =>
        await Worker(workerKey).HoldAsync().WaitAsync(TimeSpan.FromHours(1)).WithId("hold");

    public async DurableTask<string> BlockAsync(Guid workerKey)
    {
        try
        {
            await Worker(workerKey).BlockAsync().WaitAsync(TimeSpan.FromHours(1)).WithId("block");
            return "completed";
        }
        catch (TimeoutException exception) when (exception.InnerException is OperationCanceledException)
        {
            return exception.Message;
        }
    }

    public async DurableTask<string> RaceAsync(Guid holdKey, Guid quoteKey)
    {
        var (index, quote) = await DurableTask.Race([Worker(holdKey).HoldAsync(), Worker(quoteKey).QuoteAfterAsync(holdKey, 1)])
            .WithId("race");
        var quotes = await DurableTask.ScheduleAll([Worker(holdKey).HoldAsync(), Worker(quoteKey).QuoteAfterAsync(holdKey, 2)])
            .WithId("quotes");
        var winner = await ScheduledTask.Race(quotes);
        return $"{index}:{quote}; {winner.GetResult()}; {quotes[0].GetResult()}";
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task PingAsync() => Task.CompletedTask;

    private IDurableTaskRaceWorkerGrain Worker(Guid key) => GrainFactory.GetGrain<IDurableTaskRaceWorkerGrain>(key);
}

[GrainType("durable-task-race-worker-test")]
public sealed class DurableTaskRaceWorkerGrain(DurableTaskMessagingProbe probe) : DurableGrain, IDurableTaskRaceWorkerGrain
{
    public DurableTask<string> HoldAsync() =>
        DurableTask.Run(async cancellationToken =>
        {
            probe.RecordStarted(this.GetPrimaryKey());
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
                return "completed";
            }
            catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
            {
                return DurableTask.Current.TaskCancellationReason!;
            }
        });

    public DurableTask BlockAsync() =>
        DurableTask.Run(async cancellationToken =>
        {
            probe.RecordStarted(this.GetPrimaryKey());
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
        });

    // 竞争调用启动后才返回，因此该调用在运行期间会感知竞速取消。
    public DurableTask<string> QuoteAfterAsync(Guid holdKey, int started) =>
        DurableTask.Run(async cancellationToken =>
        {
            await probe.WaitForStartedAsync(holdKey, started).WaitAsync(cancellationToken);
            return "quote";
        });
}
