using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 验证删除已完成任务的执行树会取消该任务启动但从未观察的工作：
/// 一个运行中的本地子任务，以及一个对其他 Grain 的待完成调用。
/// </summary>
public abstract class DurableTaskTreeDeletionTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly TreeDeletionProbe _probe = new();
    private readonly DurableTaskRecoveryFixture _fixture;

    protected DurableTaskTreeDeletionTests(string journalFormatKey)
    {
        _fixture = new(journalFormatKey, silo => silo.Services.AddSingleton(_probe));
    }

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task DeletingACompletedTasksTreeCancelsTheWorkWhichItLeftRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<ITreeDeletionStarterGrain>(Guid.NewGuid());
        var owner = _fixture.Client.GetGrain<ITreeDeletionOwnerGrain>(Guid.NewGuid());
        var worker = _fixture.Client.GetGrain<ITreeDeletionWorkerGrain>(Guid.NewGuid());
        var ownerId = owner.GetGrainId();
        var workerId = worker.GetGrainId();
        var startId = TaskId.CreateRoot("start");
        using var events = DurableTaskEventBarriers.Collect();

        // 所有者的任务启动本地子任务及对工作 Grain 的调用，未观察两者便完成。
        var start = await starter.StartAsync(owner.GetPrimaryKey(), worker.GetPrimaryKey()).WithId("start").ScheduleAsync(cancellationToken);
        Assert.Equal(TreeDeletionOwnerGrain.Returned, (await start.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<string>());
        var ownedId = await events.WaitForCallerChildAsync(ownerId, starter.GetGrainId(), startId.Child(TreeDeletionStarterGrain.CallId));
        var localId = ownedId.Child(TreeDeletionOwnerGrain.LocalId);
        var remoteId = ownedId.Child(TreeDeletionOwnerGrain.RemoteId);
        var workerTaskId = await events.WaitForCallerChildAsync(workerId, ownerId, remoteId);

        // 启动方接受所有者的完成消息，此操作删除所有者的执行树以及仍在运行的工作。
        await events.WaitForDeletedAsync(ownerId, ownedId);
        await events.WaitForDeletedAsync(ownerId, localId);
        await events.WaitForDeletedAsync(ownerId, remoteId);

        // 本地子任务的执行尝试被取消，但任务本身未被取消，其清理操作会提交。
        var local = await _probe.LocalUnwound.WaitAsync(Timeout, cancellationToken);
        Assert.False(local.TaskCancellationRequested);
        Assert.True(local.AttemptCanceled);
        Assert.Null(local.CleanupFailure);

        // 工作 Grain 的任务通过所有者发件箱收到以持久化取消形式传递的删除通知。
        Assert.True(await _probe.RemoteCanceled.WaitAsync(Timeout, cancellationToken));
        Assert.Equal(DurableTaskStatus.Canceled, (await events.WaitForCompletedAsync(workerId, workerTaskId)).Status);

        // 所有者接受被取消任务的完成消息，而不重新创建该任务，因此工作 Grain 删除自己的任务。
        await events.WaitForDeletedAsync(workerId, workerTaskId);
        Assert.Empty(await owner.GetInboxDeadLetterReasonsAsync());
        Assert.Empty(await GetTasksAsync(owner));
        Assert.Equal(TreeDeletionOwnerGrain.CleanedUp, await owner.GetCleanupAsync());

        // 已提交的日志包含被取消尝试的清理结果。
        Assert.True(_fixture.Cluster.TryGetGrainContext(ownerId, out var activation));
        await owner.RequestDeactivationAsync();
        await activation.Deactivated.WaitAsync(Timeout, cancellationToken);
        Assert.Equal(TreeDeletionOwnerGrain.CleanedUp, await owner.GetCleanupAsync());
        Assert.Empty(await GetTasksAsync(owner));
    }

    private static async Task<List<TaskId>> GetTasksAsync(IAddressable grain)
    {
        var tasks = new List<TaskId>();
        await foreach (var (taskId, _) in grain.AsReference<IDurableTaskGrainExtension>().GetTasksAsync(TestContext.Current.CancellationToken))
        {
            tasks.Add(taskId);
        }

        return tasks;
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行执行树删除相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskTreeDeletionTests() : DurableTaskTreeDeletionTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行执行树删除相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskTreeDeletionTests() : DurableTaskTreeDeletionTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>本地子任务的执行尝试在退出时观察到的内容。</summary>
public sealed record LocalUnwinding(bool TaskCancellationRequested, bool AttemptCanceled, Exception? CleanupFailure);

/// <summary>记录删除所有者执行树时，所有者尚未观察的工作所观察到的内容。</summary>
public sealed class TreeDeletionProbe
{
    private readonly TaskCompletionSource<LocalUnwinding> _localUnwound = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _remoteCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>本地子任务的执行尝试退出后即完成。</summary>
    public Task<LocalUnwinding> LocalUnwound => _localUnwound.Task;

    /// <summary>工作 Grain 的任务观察到取消后即完成，返回是否为任务取消。</summary>
    public Task<bool> RemoteCanceled => _remoteCanceled.Task;

    public void RecordLocalUnwound(LocalUnwinding unwinding) => _localUnwound.TrySetResult(unwinding);

    public void RecordRemoteCanceled(bool taskCancellationRequested) => _remoteCanceled.TrySetResult(taskCancellationRequested);
}

public interface ITreeDeletionStarterGrain : IGrainWithGuidKey
{
    DurableTask<string> StartAsync(Guid ownerKey, Guid workerKey);
}

public interface ITreeDeletionOwnerGrain : IGrainWithGuidKey
{
    /// <summary>启动本地子任务和对工作 Grain 的调用，未观察两者便返回。</summary>
    DurableTask<string> StartWorkAndReturnAsync(Guid workerKey);

    Task<string?> GetCleanupAsync();

    Task<string[]> GetInboxDeadLetterReasonsAsync();

    Task RequestDeactivationAsync();
}

public interface ITreeDeletionWorkerGrain : IGrainWithGuidKey
{
    /// <summary>等待其任务被取消。</summary>
    DurableTask<string> HoldAsync();
}

[GrainType("durable-task-tree-deletion-starter-test")]
public sealed class TreeDeletionStarterGrain : DurableGrain, ITreeDeletionStarterGrain
{
    public const string CallId = "own";

    public async DurableTask<string> StartAsync(Guid ownerKey, Guid workerKey) =>
        await GrainFactory.GetGrain<ITreeDeletionOwnerGrain>(ownerKey).StartWorkAndReturnAsync(workerKey).WithId(CallId);
}

[GrainType("durable-task-tree-deletion-owner-test")]
public sealed class TreeDeletionOwnerGrain(
    TreeDeletionProbe probe,
    IMessagingDiagnostics diagnostics,
    [FromKeyedServices("cleanup")] IDurableValue<string> cleanup)
    : DurableGrain, ITreeDeletionOwnerGrain
{
    public const string LocalId = "local";
    public const string RemoteId = "remote";
    public const string Returned = "returned";
    public const string CleanedUp = "cleaned up";

    public async DurableTask<string> StartWorkAndReturnAsync(Guid workerKey)
    {
        // 任务在其子任务仍运行时完成，因此删除其执行树会取消子任务。
#pragma warning disable ORLEANS0219
        _ = await DurableTask.Run(RunLocalWorkAsync).WithId(LocalId).ScheduleAsync();
        _ = await GrainFactory.GetGrain<ITreeDeletionWorkerGrain>(workerKey).HoldAsync().WithId(RemoteId).ScheduleAsync();
#pragma warning restore ORLEANS0219
        return Returned;
    }

    public Task<string?> GetCleanupAsync() => Task.FromResult(cleanup.Value);

    public Task<string[]> GetInboxDeadLetterReasonsAsync() =>
        Task.FromResult(diagnostics.InboxDeadLetters.Select(deadLetter => deadLetter.Reason).ToArray());

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private async Task RunLocalWorkAsync(CancellationToken cancellationToken)
    {
        var context = DurableTask.Current;
        try
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var failure = await global::Xunit.Record.ExceptionAsync(async () =>
            {
                cleanup.Value = CleanedUp;
                await WriteStateAsync();
            });
            probe.RecordLocalUnwound(new(context.IsTaskCancellationRequested, context.AttemptCancellationToken.IsCancellationRequested, failure));
            throw;
        }
    }
}

[GrainType("durable-task-tree-deletion-worker-test")]
public sealed class TreeDeletionWorkerGrain(TreeDeletionProbe probe) : DurableGrain, ITreeDeletionWorkerGrain
{
    public DurableTask<string> HoldAsync() =>
        DurableTask.Run<string>(async cancellationToken =>
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                probe.RecordRemoteCanceled(DurableTask.Current.IsTaskCancellationRequested);
                throw;
            }

            return "released";
        });
}
