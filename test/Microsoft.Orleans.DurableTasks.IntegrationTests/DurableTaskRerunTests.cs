using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Durable.Tasks;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 证明根任务ID再次运行, 其 grain 删除了它先前的运行, 在Message 的重复范围内
/// 窗口中。
/// </summary>
/// <remarks>
/// 结果保留期短于重复窗口。 根的每运行一次都有其自身的化身,
/// 通讯传递了较后运行的 请求而不是拆卸它
/// 前面的跑步, 和被调用者储存 后一个呼叫在另一ID下。
/// </remarks>
public abstract class DurableTaskRerunTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(1);
    private readonly DurableTaskRecoveryFixture _fixture;

    protected DurableTaskRerunTests(string journalFormatKey)
    {
        _fixture = new(
            journalFormatKey,
            silo => silo.Services.Configure<DurableTaskOptions>(options => options.ResultRetentionPeriod = Retention));
    }

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task RootWithARemoteChildRunsAgainAfterItsEarlierRunWasDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var caller = _fixture.Client.GetGrain<IRerunCallerGrain>(Guid.NewGuid());
        var callee = _fixture.Client.GetGrain<IRerunCalleeGrain>(Guid.NewGuid());
        var callerId = caller.GetGrainId();
        var calleeId = callee.GetGrainId();
        var rootId = TaskId.CreateRoot("rerun");
        var callId = rootId.Child(RerunCallerGrain.CallId);
        using var events = DurableTaskEventBarriers.Collect();

        // 第一个运行点呼叫被调用者, 即一旦被调用者接受完成后删除任务 。
        var first = await caller.RunAsync(callee.GetPrimaryKey(), 7).WithId("rerun").ScheduleAsync(cancellationToken);
        Assert.Equal(7, (await first.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<int>());
        var firstChildId = await events.WaitForCallerChildAsync(calleeId, callerId, callId);
        await events.WaitForDeletedAsync(calleeId, firstChildId);

        // 第一转的留存结束, 完全在重复窗口内, 所以下一个相同的根 ID 时间表
        // 以相同的请求删除并再次运行根。
        _fixture.JobClock.Advance(Retention + TimeSpan.FromMinutes(1));
        var second = await caller.RunAsync(callee.GetPrimaryKey(), 7).WithId("rerun").ScheduleAsync(cancellationToken);
        await events.WaitForDeletedAsync(callerId, rootId);

        // 第二波的呼叫到达被召唤者 作为一项新任务,运行完成
        Assert.Equal(7, (await second.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<int>());
        var secondCreated = await events.WaitForEventAsync(
            nameof(global::Orleans.Durable.Tasks.Diagnostics.DurableTaskEvents.TaskCreated),
            evt => evt.Payload is global::Orleans.Durable.Tasks.Diagnostics.DurableTaskEvents.TaskCreated payload
                && payload.GrainContext.GrainId == calleeId
                && payload.TaskId != firstChildId
                && DurableTaskEventBarriers.IsCallerChild(callerId, callId, payload.TaskId),
            Timeout,
            cancellationToken);
        Assert.NotNull(secondCreated.Payload);
    }
}

/// <summary>运行根重新运行覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskRerunTests() : DurableTaskRerunTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行根重新运行覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskRerunTests() : DurableTaskRerunTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface IRerunCallerGrain : IGrainWithGuidKey
{
    DurableTask<int> RunAsync(Guid calleeKey, int value);
}

public interface IRerunCalleeGrain : IGrainWithGuidKey
{
    DurableTask<int> EchoAsync(int value);
}

[GrainType("durable-task-rerun-caller-test")]
public sealed class RerunCallerGrain : DurableGrain, IRerunCallerGrain
{
    public const string CallId = "echo";

    public async DurableTask<int> RunAsync(Guid calleeKey, int value) =>
        await GrainFactory.GetGrain<IRerunCalleeGrain>(calleeKey).EchoAsync(value).WithId(CallId);
}

[GrainType("durable-task-rerun-callee-test")]
public sealed class RerunCalleeGrain : DurableGrain, IRerunCalleeGrain
{
    public DurableTask<int> EchoAsync(int value) => DurableTask.FromResult(value);
}
