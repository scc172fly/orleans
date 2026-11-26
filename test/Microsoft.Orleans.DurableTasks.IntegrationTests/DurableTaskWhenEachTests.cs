using System.Collections.Concurrent;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>证明工作流程 <c>WhenEach</c> 在每个激活中都产生相同的完成顺序。</summary>
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

        // 输入 2 先完成, 然后启动被解除 。
        await SignalAsync(grain, taskId, "2", cancellationToken);
        await _fixture.MessagingProbe.WaitForStartedAsync(yieldKey, 1).WaitAsync(Timeout, cancellationToken);
        await grain.DeactivateAsync();

        // 下一次激活重播输入 2的完成, 然后按此顺序观察输入 0和1 完成 。
        await SignalAsync(grain, taskId, "0", cancellationToken);
        await _fixture.MessagingProbe.WaitForStartedAsync(yieldKey, 3).WaitAsync(Timeout, cancellationToken);
        await SignalAsync(grain, taskId, "1", cancellationToken);

        // 查点结束后,企图记录其命令
        await _fixture.MessagingProbe.WaitForStartedAsync(yieldKey, 5).WaitAsync(Timeout, cancellationToken);
        await grain.DeactivateAsync();

        // 上次激活时, 以每个输入完整内容重放点数 。
        await SignalAsync(grain, taskId, "done", cancellationToken);

        var response = await workflow.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal("2,0,1", response.GetResult<string>());
        Assert.Equal(["2,0,1", "2,0,1"], DurableTaskWhenEachGrain.Orders[key]);
        Assert.Equal(3, _fixture.MessagingProbe.GetStartedCount(key));
    }

    private static ValueTask SignalAsync(IDurableTaskWhenEachGrain grain, TaskId taskId, string item, CancellationToken cancellationToken) =>
        grain.SignalDurableTaskAsync(taskId, DurableTaskWhenEachGrain.Item.For(item), item, cancellationToken);
}

/// <summary>运行运行 <c>WhenEach</c> 默认存储任务状态的覆盖 JSON 日记格式。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskWhenEachTests() : DurableTaskWhenEachTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行运行 <c>WhenEach</c> 任务状态包含在 Orleans 双进制日记格式。</summary>
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

    /// <summary>获得每次完成查点的尝试的顺序,按 grain 键进行。</summary>
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
