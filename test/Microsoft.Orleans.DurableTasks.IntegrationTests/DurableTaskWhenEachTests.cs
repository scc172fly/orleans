using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>验证工作流的 <c>WhenEach</c> 在每次激活中均产生相同的完成顺序。</summary>
public abstract class DurableTaskWhenEachTests(string journalFormatKey) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task WhenEachYieldsTheSameOrderAcrossDeactivations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid();
        var yieldKey = Guid.NewGuid();
        var grain = _fixture.Client.GetGrain<IDurableTaskWhenEachGrain>(key);
        var taskId = TaskId.CreateRoot("each");
        var started = _fixture.MessagingProbe.WaitForStartedAsync(key, 1);
        var workflow = await grain.CollectInCompletionOrderAsync(yieldKey).WithId("each").ScheduleAsync(cancellationToken);
        await started.WaitAsync(Timeout, cancellationToken);

        // 输入 2 首先完成，然后该激活被停用。
        await SignalAsync(grain, taskId, "2", cancellationToken);
        await _fixture.MessagingProbe.WaitForStartedAsync(yieldKey, 1).WaitAsync(Timeout, cancellationToken);
        await grain.DeactivateAsync();

        // 下一次激活重放输入 2 的完成，然后依次观察到输入 0 和输入 1 完成。
        await SignalAsync(grain, taskId, "0", cancellationToken);
        await _fixture.MessagingProbe.WaitForStartedAsync(yieldKey, 3).WaitAsync(Timeout, cancellationToken);
        await SignalAsync(grain, taskId, "1", cancellationToken);

        // 枚举结束后，执行尝试记录其观察到的顺序。
        await _fixture.MessagingProbe.WaitForStartedAsync(yieldKey, 5).WaitAsync(Timeout, cancellationToken);
        await grain.DeactivateAsync();

        // 最后一次激活在所有输入均已完成的情况下重放枚举。
        await SignalAsync(grain, taskId, "done", cancellationToken);

        var response = await workflow.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal("2,0,1", response.GetResult<string>());
        Assert.Equal(["2,0,1", "2,0,1"], DurableTaskWhenEachGrain.Orders[key]);
        Assert.Equal(3, _fixture.MessagingProbe.GetStartedCount(key));
    }

    private static ValueTask SignalAsync(IDurableTaskWhenEachGrain grain, TaskId taskId, string item, CancellationToken cancellationToken) =>
        grain.SignalDurableTaskAsync(taskId, DurableTaskWhenEachGrain.Item.For(item), item, cancellationToken);
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行 <c>WhenEach</c> 相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskWhenEachTests() : DurableTaskWhenEachTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行 <c>WhenEach</c> 相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskWhenEachTests() : DurableTaskWhenEachTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface IDurableTaskWhenEachGrain : IGrainWithGuidKey
{
    DurableTask<string> CollectInCompletionOrderAsync(Guid yieldKey);

    Task DeactivateAsync();
}

[GrainType("durable-task-when-each-test")]
public sealed class DurableTaskWhenEachGrain(DurableTaskMessagingProbe probe) : DurableGrain, IDurableTaskWhenEachGrain
{
    public static readonly DurableSignal<string> Item = new("item");

    /// <summary>按 Grain 键获取每次完成枚举的执行尝试所观察到的顺序。</summary>
    public static ConcurrentDictionary<Guid, ConcurrentQueue<string>> Orders { get; } = [];

    public async DurableTask<string> CollectInCompletionOrderAsync(Guid yieldKey)
    {
        probe.RecordStarted(this.GetPrimaryKey());
        var order = new List<string>();
        await foreach (var item in DurableTask.WhenEach([Item.For("0").WaitAsync(), Item.For("1").WaitAsync(), Item.For("2").WaitAsync()]))
        {
            order.Add(item.GetResult());
            probe.RecordStarted(yieldKey);
        }

        var joined = string.Join(",", order);
        Orders.GetOrAdd(this.GetPrimaryKey(), static _ => []).Enqueue(joined);
        probe.RecordStarted(yieldKey);
        await Item.For("done").WaitAsync();
        return joined;
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
