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
/// 证明两个电话用户的边远子女,其工作流程共用根身份识别码和孩子姓名的偏远子女在
/// 他们同族的同族,
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

        // 每个呼叫者的工作流程都是自己 grain的根任务“位置顺序”,并将被调用者称为“保留区”。
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

        // 取消第一个工作流程只取消被调用时的自家孩子。
        await firstOrder.CancelAsync(cancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, (await firstOrder.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).Status);
        Assert.Equal(DurableTaskStatus.Canceled, (await events.WaitForCompletedAsync(calleeId, firstChildId)).Status);
        Assert.True(_probe.Canceled(1).IsCompleted);
        Assert.False(_probe.Canceled(2).IsCompleted);

        // 第二个工作流程的孩子在完成时有自己的结果。
        _probe.Release(2);
        Assert.Equal(2, (await secondOrder.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<int>());
        Assert.Equal(2, (await events.WaitForCompletedAsync(calleeId, secondChildId)).GetResult<int>());

        // 被调用者在自己的调用者接受完成后,将每个孩子删除。
        await events.WaitForDeletedAsync(calleeId, firstChildId);
        await events.WaitForDeletedAsync(calleeId, secondChildId);
    }
}

/// <summary>运行调用范围,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCallerScopeTests() : DurableTaskCallerScopeTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行以 Orleans 二进制日记格式存储的任务状态的调用员范围覆盖。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskCallerScopeTests() : DurableTaskCallerScopeTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>持有被调用人的任务, 以其值为键, 直到测试释放他们或取消它们。</summary>
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

// 每个打电话的孩子等待释放,没有持久的等待,所以被召唤者同时运行两个部分。
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
