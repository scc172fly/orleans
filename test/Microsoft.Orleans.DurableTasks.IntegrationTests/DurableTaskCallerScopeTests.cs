using Orleans.Concurrency;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 验证具有相同工作流根 ID 和子任务名称的两个调用方，其远程子执行实例在
/// 共享的被调用方上保持独立。
/// </summary>
public abstract class DurableTaskCallerScopeTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly CallerScopeProbe _probe = new();
    private readonly DurableTaskRecoveryFixture _fixture;

    protected DurableTaskCallerScopeTests(string journalFormatKey)
    {
        _fixture = new(journalFormatKey, silo => silo.Services.AddSingleton(_probe));
    }

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task CallersWithTheSameRootIdKeepSeparateChildrenOnASharedCallee()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var callee = _fixture.Client.GetGrain<ISharedCalleeGrain>(Guid.NewGuid());
        var first = _fixture.Client.GetGrain<ISharedRootCallerGrain>(Guid.NewGuid());
        var second = _fixture.Client.GetGrain<ISharedRootCallerGrain>(Guid.NewGuid());
        var calleeId = callee.GetGrainId();
        var childId = TaskId.CreateRoot("place-order").Child(SharedRootCallerGrain.CallId);
        using var events = DurableTaskEventBarriers.Collect();

        // 每个调用方的工作流都是其自身 Grain 上的根任务 'place-order'，并以 'reserve' 名称调用被调用方。
        var firstOrder = await first.PlaceOrderAsync(callee.GetPrimaryKey(), 1).WithId("place-order").ScheduleAsync(cancellationToken);
        var secondOrder = await second.PlaceOrderAsync(callee.GetPrimaryKey(), 2).WithId("place-order").ScheduleAsync(cancellationToken);
        await Task.WhenAll(_probe.Started(1), _probe.Started(2)).WaitAsync(Timeout, cancellationToken);
        var firstChildId = await events.WaitForCallerChildAsync(calleeId, first.GetGrainId(), childId);
        var secondChildId = await events.WaitForCallerChildAsync(calleeId, second.GetGrainId(), childId);
        Assert.NotEqual(firstChildId, secondChildId);

        var running = new List<TaskId>();
        await foreach (var taskId in callee.AsReference<IDurableTaskGrainExtension>().GetRunningTasksAsync(cancellationToken))
        {
            running.Add(taskId);
        }

        Assert.Equal(2, running.Count);
        Assert.Contains(firstChildId, running);
        Assert.Contains(secondChildId, running);

        // 取消第一个工作流，仅取消它自己在被调用方上的子任务。
        await firstOrder.CancelAsync(cancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, (await firstOrder.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).Status);
        Assert.Equal(DurableTaskStatus.Canceled, (await events.WaitForCompletedAsync(calleeId, firstChildId)).Status);
        Assert.True(_probe.Canceled(1).IsCompleted);
        Assert.False(_probe.Canceled(2).IsCompleted);

        // 第二个工作流的子任务以自身结果完成。
        _probe.Release(2);
        Assert.Equal(2, (await secondOrder.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<int>());
        Assert.Equal(2, (await events.WaitForCompletedAsync(calleeId, secondChildId)).GetResult<int>());

        // 各子任务自己的调用方接受其完成消息后，被调用方删除该子任务。
        await events.WaitForDeletedAsync(calleeId, firstChildId);
        await events.WaitForDeletedAsync(calleeId, secondChildId);
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行调用方作用域相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCallerScopeTests() : DurableTaskCallerScopeTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行调用方作用域相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskCallerScopeTests() : DurableTaskCallerScopeTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>按任务的值为键保留被调用方的任务，直至测试释放或取消这些任务。</summary>
public sealed class CallerScopeProbe
{
    private readonly ConcurrentDictionary<(string Kind, int Value), TaskCompletionSource> _signals = new();

    public Task Started(int value) => Get("started", value).Task;
    public Task Canceled(int value) => Get("canceled", value).Task;
    public void Release(int value) => Get("released", value).TrySetResult();

    public async Task HoldAsync(int value, CancellationToken cancellationToken)
    {
        Get("started", value).TrySetResult();
        try
        {
            await Get("released", value).Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Get("canceled", value).TrySetResult();
            throw;
        }
    }

    private TaskCompletionSource Get(string kind, int value) =>
        _signals.GetOrAdd((kind, value), static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
}

public interface ISharedCalleeGrain : IGrainWithGuidKey
{
    DurableTask<int> HoldAsync(int value);
}

public interface ISharedRootCallerGrain : IGrainWithGuidKey
{
    DurableTask<int> PlaceOrderAsync(Guid calleeKey, int value);
}

// 每个调用方的子任务直接等待释放而不进行持久化等待，因此被调用方同时运行两个执行段。
[Reentrant]
[GrainType("durable-task-shared-callee-test")]
public sealed class SharedCalleeGrain(CallerScopeProbe probe) : DurableGrain, ISharedCalleeGrain
{
    public DurableTask<int> HoldAsync(int value) =>
        DurableTask.Run(async cancellationToken =>
        {
            await probe.HoldAsync(value, cancellationToken);
            return value;
        });
}

[GrainType("durable-task-shared-root-caller-test")]
public sealed class SharedRootCallerGrain : DurableGrain, ISharedRootCallerGrain
{
    public const string CallId = "reserve";

    public async DurableTask<int> PlaceOrderAsync(Guid calleeKey, int value) =>
        await GrainFactory.GetGrain<ISharedCalleeGrain>(calleeKey).HoldAsync(value).WithId(CallId);
}
