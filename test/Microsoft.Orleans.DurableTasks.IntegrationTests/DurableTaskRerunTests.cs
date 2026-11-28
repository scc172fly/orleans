using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Durable.Tasks;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 验证 Grain 删除根任务 ID 的先前运行后，即使仍处于消息传递的去重
/// 窗口内，该根任务 ID 也会再次运行。
/// </summary>
/// <remarks>
/// 结果保留期短于去重窗口。根任务的每次运行都有自己的运行世代标识，
/// 该值包含在其调用的传输标识符中，因此消息传递会传递后一次运行的调用，而不会将其与
/// 先前运行的调用去重；被调用方使用另一个 ID 存储后一次调用。
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

        // 第一次运行调用被调用方，调用方接受完成消息后，被调用方删除自己的任务。
        var first = await caller.RunAsync(callee.GetPrimaryKey(), 7).WithId("rerun").ScheduleAsync(cancellationToken);
        Assert.Equal(7, (await first.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<int>());
        var firstChildId = await events.WaitForCallerChildAsync(calleeId, callerId, callId);
        await events.WaitForDeletedAsync(calleeId, firstChildId);

        // 第一次运行的保留期在去重窗口结束之前就已届满，因此再次调度同一根 ID 时，
        // 会删除旧任务，并使用相同请求重新运行根任务。
        _fixture.JobClock.Advance(Retention + TimeSpan.FromMinutes(1));
        var second = await caller.RunAsync(callee.GetPrimaryKey(), 7).WithId("rerun").ScheduleAsync(cancellationToken);
        await events.WaitForDeletedAsync(callerId, rootId);

        // 第二次运行的调用作为新任务抵达被调用方，并且此次运行完成。
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

/// <summary>使用默认 JSON 日志格式存储任务状态，运行根任务再次运行相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskRerunTests() : DurableTaskRerunTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行根任务再次运行相关测试。</summary>
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
