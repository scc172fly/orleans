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
/// 证明删除已完成任务的执行树会取消任务开始和从未观察到的工作:
/// 正在运行的本地儿童, 以及尚未通知另一个孩子 grain。
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

        // 业主的任务就是让当地孩子和工人打电话,完成后也没有人观察。
        var start = await starter.StartAsync(owner.GetPrimaryKey(), worker.GetPrimaryKey()).WithId("start").ScheduleAsync(cancellationToken);
        Assert.Equal(TreeDeletionOwnerGrain.Returned, (await start.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<string>());
        var ownedId = await events.WaitForCallerChildAsync(ownerId, starter.GetGrainId(), startId.Child(TreeDeletionStarterGrain.CallId));
        var localId = ownedId.Child(TreeDeletionOwnerGrain.LocalId);
        var remoteId = ownedId.Child(TreeDeletionOwnerGrain.RemoteId);
        var workerTaskId = await events.WaitForCallerChildAsync(workerId, ownerId, remoteId);

        // 启动器接受所有者的完成, 从而删除所有者树上的仍在运行的工程 。
        await events.WaitForDeletedAsync(ownerId, ownedId);
        await events.WaitForDeletedAsync(ownerId, localId);
        await events.WaitForDeletedAsync(ownerId, remoteId);

        // 当地儿童的尝试取消,没有取消任务,而且清理工作也承诺。
        var local = await _probe.LocalUnwound.WaitAsync(Timeout, cancellationToken);
        Assert.False(local.TaskCancellationRequested);
        Assert.True(local.AttemptCanceled);
        Assert.Null(local.CleanupFailure);

        // 工人的任务通过业主的发包作为持久取消而被删除。
        Assert.True(await _probe.RemoteCanceled.WaitAsync(Timeout, cancellationToken));
        Assert.Equal(DurableTaskStatus.Canceled, (await events.WaitForCompletedAsync(workerId, workerTaskId)).Status);

        // 所有人接受取消的任务完成后, 不重建它, 所以工人删除了任务 。
        await events.WaitForDeletedAsync(workerId, workerTaskId);
        Assert.Empty(await owner.GetInboxDeadLetterReasonsAsync());
        Assert.Empty(await GetTasksAsync(owner));
        Assert.Equal(TreeDeletionOwnerGrain.CleanedUp, await owner.GetCleanupAsync());

        // 承诺日记保存取消的尝试清理。
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

/// <summary>运行树形删除覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskTreeDeletionTests() : DurableTaskTreeDeletionTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行以 Orleans 二进制日记格式存储的任务状态树删除覆盖。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskTreeDeletionTests() : DurableTaskTreeDeletionTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>当地孩子的尝试所观察到的 是不健康的</summary>
public sealed record LocalUnwinding(bool TaskCancellationRequested, bool AttemptCanceled, Exception? CleanupFailure);

/// <summary>记录当业主的树被删除时,主人未观察的工作会看到什么。</summary>
public sealed class TreeDeletionProbe
{
    private readonly TaskCompletionSource<LocalUnwinding> _localUnwound = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _remoteCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>一旦当地孩子的尝试解除了 就会完全结束</summary>
    public Task<LocalUnwinding> LocalUnwound => _localUnwound.Task;

    /// <summary>一旦工人的任务被观察到取消,就完成,是否是取消任务。</summary>
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
    /// <summary>开始一个本地孩子, 和给工人的电话, 返回时也没有观察。</summary>
    DurableTask<string> StartWorkAndReturnAsync(Guid workerKey);

    Task<string?> GetCleanupAsync();

    Task<string[]> GetInboxDeadLetterReasonsAsync();

    Task RequestDeactivationAsync();
}

public interface ITreeDeletionWorkerGrain : IGrainWithGuidKey
{
    /// <summary>等待任务取消。</summary>
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
        // 任务完成时,孩子在运行, 所以删除树上的树就取消它们。
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
