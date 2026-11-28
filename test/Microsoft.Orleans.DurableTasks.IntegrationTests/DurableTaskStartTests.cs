using Orleans.Concurrency;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Journaling;
using Orleans.Serialization;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 测试客户端、普通 Grain 方法和持久化执行实例如何启动任务，以及通过以下方式进行 Grain 内任务管理：
/// <see cref="IDurableTaskManager"/>.
/// </summary>
public abstract class DurableTaskStartTests(string journalFormatKey) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ClientStartsRootsUnderTheirNamesOrUnderNewRandomIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var worker = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(Guid.NewGuid());

        var named = await worker.EchoAsync(4).WithId("client-scheduled").ScheduleAsync(cancellationToken);
        var unnamed = await worker.EchoAsync(5).ScheduleAsync(cancellationToken);

        Assert.Equal(TaskId.CreateRoot("client-scheduled"), named.Id);
        Assert.True(Guid.TryParseExact(unnamed.Id.ToString(), "N", out _));
        Assert.Equal(8, await named.WaitAsync(Timeout, cancellationToken));
        Assert.Equal(10, await unnamed.WaitAsync(Timeout, cancellationToken));
        Assert.Equal(8, (await worker.GetDurableTask<int>("client-scheduled").GetResponseAsync(cancellationToken)).GetResult<int>());
        Assert.Equal(14, await worker.EchoAsync(7));
    }

    [Fact]
    public void ClientCannotPost()
    {
        var worker = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(Guid.NewGuid());

        var exception = Assert.Throws<InvalidOperationException>(() => worker.EchoAsync(1).WithId("posted").Post());

        Assert.Contains("durable outbox", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrdinaryGrainMethodPostsRootWithItsWriteAndTheRootIsPolledById()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IDurableTaskStartingGrain>(Guid.NewGuid());
        var workerKey = Guid.NewGuid();
        var started = _fixture.MessagingProbe.WaitForStartedAsync(workerKey, 1);

        await starter.PostEchoAsync(workerKey, 21, "posted-echo");
        await started.WaitAsync(Timeout, cancellationToken);

        var posted = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey).GetDurableTask<int>("posted-echo");
        var response = await posted.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal(42, response.GetResult<int>());

        // 启动方不为已投递的调用保留任务。
        Assert.Empty(await GetTaskIdsAsync(starter, cancellationToken));
    }

    [Fact]
    public async Task OrdinaryGrainMethodSchedulesOnItsOwnGrainAndOnAnother()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IDurableTaskStartingGrain>(Guid.NewGuid());

        Assert.Equal((6, 22), await starter.ScheduleAndAwaitAsync(Guid.NewGuid(), 11).WaitAsync(Timeout, cancellationToken));
    }

    [Fact]
    public async Task WorkflowPostsDetachedRootAndRetainsOnlyItsRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IDurableTaskStartingGrain>(Guid.NewGuid());
        var workerKey = Guid.NewGuid();
        var started = _fixture.MessagingProbe.WaitForStartedAsync(workerKey, 1);

        var workflow = await starter.PostEchoInWorkflowAsync(workerKey, 5, "workflow-echo").WithId("workflow").ScheduleAsync(cancellationToken);
        await workflow.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        await started.WaitAsync(Timeout, cancellationToken);

        var detached = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey).GetDurableTask<int>("workflow-echo");
        var response = await detached.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal(10, response.GetResult<int>());

        // 启动方保留客户端调度的工作流及其执行树中的记录，但不为分离调用保留任务。
        var starterTasks = await GetTaskIdsAsync(starter, cancellationToken);
        Assert.Equal(2, starterTasks.Count);
        Assert.Contains(TaskId.CreateRoot("workflow"), starterTasks);
        var workerId = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey).GetGrainId();
        Assert.Contains(TaskId.CreateRoot("workflow").Child($"$detach:{workerId}:workflow-echo"), starterTasks);
    }

    [Fact]
    public async Task WorkflowObservesDetachedTasksThroughTheirStarts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IDurableTaskStartingGrain>(Guid.NewGuid());
        var workerKey = Guid.NewGuid();

        var workflow = await starter.AwaitDetachedEchoesAsync(workerKey, 3).WithId("workflow").ScheduleAsync(cancellationToken);

        // 被等待的分离任务根据启动 Grain 和工作流派生其根 ID，而被调度的任务使用
        // 其名称。
        Assert.Equal(6 + 8, await workflow.WaitAsync(Timeout, cancellationToken));
        var worker = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        Assert.Equal(6, (await worker.GetDurableTask<int>(GetDetachedRootId(starter, "workflow", "$child-1")).GetResponseAsync(cancellationToken)).GetResult<int>());
        Assert.Equal(8, (await worker.GetDurableTask<int>("scheduled-echo").GetResponseAsync(cancellationToken)).GetResult<int>());

        // 在持久化执行之外，等待引用即等待任务的结果；无需知道结果类型，引用即可读取其任务
        // 及 Grain。
        var scheduled = worker.GetDurableTask<int>("scheduled-echo");
        Assert.Equal(8, await scheduled);
        IDurableTaskReference untyped = scheduled;
        Assert.Equal((worker.GetGrainId(), TaskId.CreateRoot("scheduled-echo")), (untyped.Target, untyped.Id));
    }

    [Fact]
    public async Task HandlesNameTheTasksWhichOtherPartiesAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IDurableTaskStartingGrain>(Guid.NewGuid());
        var workerKey = Guid.NewGuid();
        var workflowId = TaskId.CreateRoot("workflow");

        var workflow = await starter.ReportTargetTaskIdsAsync(workerKey).WithId("workflow").ScheduleAsync(cancellationToken);
        var ids = await workflow.WaitAsync(Timeout, cancellationToken);

        // 分离启动的句柄指向工作 Grain 的根任务，工作流通过该句柄观察任务，
        // 客户端通过该句柄读取任务。调用句柄指向工作 Grain 在启动方作用域下存储的任务。
        var detachedRootId = GetDetachedRootId(starter, "workflow", "$child-1");
        Assert.Equal(detachedRootId, ids.DetachedTarget);
        Assert.NotEqual(ids.DetachedTarget, ids.Detached);
        Assert.Equal((workflowId.Child("$child-2"), detachedRootId), (ids.Observed, ids.ObservedTarget));
        Assert.Equal(workflowId.Child("$child-3"), ids.Call);
        Assert.True(DurableTaskEventBarriers.IsCallerChild(starter.GetGrainId(), ids.Call, ids.CallTarget));
        var worker = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        Assert.Equal(8, (await worker.GetDurableTask<int>(ids.DetachedTarget).GetResponseAsync(cancellationToken)).GetResult<int>());
    }

    [Fact]
    public async Task WorkflowScheduleFailsWhenItsHostRejectsTheStart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IDurableTaskStartingGrain>(Guid.NewGuid());
        var workerKey = Guid.NewGuid();
        var worker = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        await worker.EchoAsync(1).WithId("taken").ScheduleAsync(cancellationToken);

        var workflow = await starter.ScheduleConflictingStartAsync(workerKey, "taken").WithId("workflow").ScheduleAsync(cancellationToken);

        Assert.Contains("different request", await workflow.WaitAsync(Timeout, cancellationToken), StringComparison.Ordinal);

        // 工作 Grain 使用冲突代码将冲突调用记录为已处理，启动方发件箱将其报告为
        // DurableTaskConflictException，且不产生任何死信。
        Assert.Empty(await worker.GetInboxDeadLetterReasonsAsync());
    }

    [Fact]
    public async Task WorkflowStartOfTheSameMethodWithOtherArgumentsReachesTheExistingTask()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IDurableTaskStartingGrain>(Guid.NewGuid());
        var workerKey = Guid.NewGuid();
        var worker = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        await worker.EchoAsync(1).WithId("taken").ScheduleAsync(cancellationToken);

        // 任务的标识取决于方法而非参数，因此启动请求访问已存储的任务。
        var workflow = await starter.ScheduleEchoAsync(workerKey, "taken", 2).WithId("workflow").ScheduleAsync(cancellationToken);

        // 回显方法将参数乘以二，因此结果来自已存储的、回显参数 1 的任务。
        Assert.Equal("echoed 2", await workflow.WaitAsync(Timeout, cancellationToken));
        Assert.Equal(2, (await worker.GetDurableTask<int>("taken").GetResponseAsync(cancellationToken)).GetResult<int>());
        Assert.Empty(await worker.GetInboxDeadLetterReasonsAsync());
    }

    [Fact]
    public async Task GrainCancelsItsOwnTaskThroughTheTaskManager()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starterKey = Guid.NewGuid();
        var starter = _fixture.Client.GetGrain<IDurableTaskStartingGrain>(starterKey);
        var started = _fixture.MessagingProbe.WaitForStartedAsync(starterKey, 1);
        var waiting = await starter.WaitForCancellationAsync().WithId("wait").ScheduleAsync(cancellationToken);
        await started.WaitAsync(Timeout, cancellationToken);

        var status = await starter.CancelThroughManagerAsync("wait").WaitAsync(Timeout, cancellationToken);

        Assert.Equal(DurableTaskStatus.Canceled, status);
        var response = await waiting.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal(DurableTaskStatus.Canceled, response.Status);
    }

    [Fact]
    public async Task TaskWhoseOutcomeDoesNotSerializeFailsAndItsGrainKeepsCommitting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IDurableTaskStartingGrain>(Guid.NewGuid());

        // 无法存储的执行结果会使任务以序列化失败进入故障状态，该故障结果与其他结果一样提交。
        var result = await starter.ReturnUnserializableAsync().WithId("unserializable-result").ScheduleAsync(cancellationToken);
        var resultResponse = await result.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal(DurableTaskStatus.Failed, resultResponse.Status);
        Assert.Contains(typeof(UnserializableValue).FullName!, Assert.IsType<SerializerException>(resultResponse.Exception).Message, StringComparison.Ordinal);

        var exception = await starter.ThrowUnserializableAsync().WithId("unserializable-exception").ScheduleAsync(cancellationToken);
        var exceptionResponse = await exception.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal(DurableTaskStatus.Failed, exceptionResponse.Status);
        Assert.Contains(typeof(InvalidOperationException).FullName!, Assert.IsType<SerializerException>(exceptionResponse.Exception).Message, StringComparison.Ordinal);

        // Grain 继续提交其余工作。
        var later = await starter.TripleAsync(2).WithId("later").ScheduleAsync(cancellationToken);
        Assert.Equal(6, await later.WaitAsync(Timeout, cancellationToken));
    }

    /// <summary>
    /// 派生 <paramref name="host"/> 的执行 <paramref name="executionId"/> 启动的分离任务的根 ID，
    /// 该任务对应操作 <paramref name="segment"/>：一个包含 <c>{HostId}/{Id}/{segment}</c> 的段，各部分均经转义。
    /// </summary>
    private static TaskId GetDetachedRootId(IAddressable host, string executionId, string segment) =>
        TaskId.CreateRoot($"{TaskId.CreateRoot(host.GetGrainId().ToString())}/{TaskId.CreateRoot(executionId).Child(segment)}");

    private static async Task<List<TaskId>> GetTaskIdsAsync(IAddressable grain, CancellationToken cancellationToken)
    {
        var taskIds = new List<TaskId>();
        await foreach (var (taskId, _) in grain.AsReference<IDurableTaskGrainExtension>().GetTasksAsync(cancellationToken))
        {
            taskIds.Add(taskId);
        }

        return taskIds;
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行任务启动相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskStartTests() : DurableTaskStartTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行任务启动相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskStartTests() : DurableTaskStartTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface IDurableTaskStartingGrain : IGrainWithGuidKey
{
    Task PostEchoAsync(Guid workerKey, int value, string rootId);

    // 等待自身 Grain 上任务的请求采用交错执行，因为任务的执行段也是发给该 Grain 的请求。
    [AlwaysInterleave]
    Task<(int Own, int Other)> ScheduleAndAwaitAsync(Guid workerKey, int value);

    DurableTask<int> TripleAsync(int value);

    /// <summary>以没有编解码器的类型的值完成。</summary>
    DurableTask<object> ReturnUnserializableAsync();

    /// <summary>以异常失败，该异常的数据包含没有编解码器的类型的值。</summary>
    DurableTask<int> ThrowUnserializableAsync();

    DurableTask PostEchoInWorkflowAsync(Guid workerKey, int value, string rootId);

    DurableTask<int> AwaitDetachedEchoesAsync(Guid workerKey, int value);

    /// <summary>启动工作 Grain 的 <c>WaitForCancellationAsync</c>，使用 <paramref name="rootId"/> 为根 ID，测试会先用另一个方法占用该根 ID。</summary>
    DurableTask<string> ScheduleConflictingStartAsync(Guid workerKey, string rootId);

    /// <summary>启动工作 Grain 对 <paramref name="value"/> 的回显，使用 <paramref name="rootId"/> 为根 ID，并等待其完成。</summary>
    DurableTask<string> ScheduleEchoAsync(Guid workerKey, string rootId, int value);

    DurableTask<TargetTaskIds> ReportTargetTaskIdsAsync(Guid workerKey);

    DurableTask<int> WaitForCancellationAsync();

    [AlwaysInterleave]
    Task<DurableTaskStatus> CancelThroughManagerAsync(string rootId);
}

[GenerateSerializer]
public sealed record TargetTaskIds(
    [property: Id(0)] TaskId Detached,
    [property: Id(1)] TaskId DetachedTarget,
    [property: Id(2)] TaskId Observed,
    [property: Id(3)] TaskId ObservedTarget,
    [property: Id(4)] TaskId Call,
    [property: Id(5)] TaskId CallTarget);

/// <summary>没有编解码器的类型的值，因此无法序列化。</summary>
public sealed class UnserializableValue;

[GrainType("durable-task-starting-test")]
public sealed class DurableTaskStartingGrain(IDurableTaskManager tasks, DurableTaskMessagingProbe probe)
    : DurableGrain, IDurableTaskStartingGrain
{
    public async Task PostEchoAsync(Guid workerKey, int value, string rootId)
    {
        _ = GrainFactory.GetGrain<IDurableTaskWorkerGrain>(workerKey).EchoAsync(value).WithId(rootId).Post();
        await WriteStateAsync();
    }

    public async Task<(int Own, int Other)> ScheduleAndAwaitAsync(Guid workerKey, int value)
    {
        // Grain 通过自身运行时调度自己的任务，因此调用不会等待 Grain 自身。
        var own = await this.AsReference<IDurableTaskStartingGrain>().TripleAsync(2).ScheduleAsync();
        var other = await GrainFactory.GetGrain<IDurableTaskWorkerGrain>(workerKey).EchoAsync(value).ScheduleAsync();
        return (await own, await other);
    }

    public DurableTask<int> TripleAsync(int value) => DurableTask.FromResult(value * 3);

    public DurableTask<object> ReturnUnserializableAsync() => DurableTask.FromResult<object>(new UnserializableValue());

    public DurableTask<int> ThrowUnserializableAsync() => DurableTask.Run(static _ => ThrowUnserializable());

    private static int ThrowUnserializable() =>
        throw new InvalidOperationException("The order was rejected.") { Data = { ["order"] = new UnserializableValue() } };

    public DurableTask PostEchoInWorkflowAsync(Guid workerKey, int value, string rootId) =>
        DurableTask.Run(cancellationToken =>
        {
            _ = GrainFactory.GetGrain<IDurableTaskWorkerGrain>(workerKey).EchoAsync(value).Detached(rootId).Post();
        });

    public async DurableTask<int> AwaitDetachedEchoesAsync(Guid workerKey, int value)
    {
        var worker = GrainFactory.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        var awaited = await worker.EchoAsync(value).Detached();
        var scheduled = await worker.EchoAsync(value + 1).Detached("scheduled-echo").ScheduleAsync();
        return awaited + await scheduled;
    }

    public async DurableTask<string> ScheduleConflictingStartAsync(Guid workerKey, string rootId)
    {
        try
        {
            await GrainFactory.GetGrain<IDurableTaskWorkerGrain>(workerKey).WaitForCancellationAsync(2).Detached(rootId).ScheduleAsync();
            return "accepted";
        }
        catch (DurableTaskConflictException conflict)
        {
            return conflict.Message;
        }
    }

    public async DurableTask<string> ScheduleEchoAsync(Guid workerKey, string rootId, int value)
    {
        var echo = await GrainFactory.GetGrain<IDurableTaskWorkerGrain>(workerKey).EchoAsync(value).Detached(rootId).ScheduleAsync();
        return $"echoed {await echo}";
    }

    public async DurableTask<TargetTaskIds> ReportTargetTaskIdsAsync(Guid workerKey)
    {
        var worker = GrainFactory.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        var detached = await worker.EchoAsync(4).Detached().ScheduleAsync();
        var observed = await worker.GetDurableTask<int>(detached.TargetTaskId).ScheduleAsync();
        var call = await worker.EchoAsync(5).ScheduleAsync();
        await detached;
        await observed;
        await call;
        return new(detached.Id, detached.TargetTaskId, observed.Id, observed.TargetTaskId, call.Id, call.TargetTaskId);
    }

    public DurableTask<int> WaitForCancellationAsync() =>
        DurableTask.Run(async cancellationToken =>
        {
            probe.RecordStarted(this.GetPrimaryKey());
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        });

    public async Task<DurableTaskStatus> CancelThroughManagerAsync(string rootId)
    {
        var taskId = TaskId.CreateRoot(rootId);
        await tasks.CancelAsync(taskId);
        return (await tasks.GetTask<int>(taskId).GetResponseAsync()).Status;
    }
}
