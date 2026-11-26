using Orleans.Concurrency;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Journaling;
using Orleans.Serialization;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 如何开始使用客户、普通 grain 方法和持久处决方法,以及如何在-grain 任务管理中通过
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

        // 启动器对已张贴的呼叫没有保留任务 。
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

        // 启动程序保留客户端计划流程, 记录在其树上, 但没有任务用于独立调用 。
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

        // 等待的分离任务从起始 grain 和工作流程以及预定用途中产生根代号
        // 其名称。
        Assert.Equal(6 + 8, await workflow.WaitAsync(Timeout, cancellationToken));
        var worker = _fixture.Client.GetGrain<IDurableTaskWorkerGrain>(workerKey);
        Assert.Equal(6, (await worker.GetDurableTask<int>(GetDetachedRootId(starter, "workflow", "$child-1")).GetResponseAsync(cancellationToken)).GetResult<int>());
        Assert.Equal(8, (await worker.GetDurableTask<int>("scheduled-echo").GetResponseAsync(cancellationToken)).GetResult<int>());

        // 在一个持久性执行之外,等待参考结果的持久执行,等待任务结果,该参考内容为任务
        // 和无结果型号的 grain 。
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

        // 独立启动程序处理 指定工人的根任务, 工作流程通过它观察,
        // 用户通过它读取。 电话的手柄指定了工人在初始范围下储存的工作。
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

        // 工人记录了冲突代码处理的 矛盾性职业
        // 以 DurableTaskConflictException报告,无任何死字。
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

        // 任务的身份是它的方法,而不是其论点,所以开始就达到了存储的任务。
        var workflow = await starter.ScheduleEchoAsync(workerKey, "taken", 2).WithId("workflow").ScheduleAsync(cancellationToken);

        // 反响是其论点的两倍,因此结果就是存储的任务,结果又回响了1。
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

        // 无法存储的结果 与序列化失败的任务有误 这造成了任何结果
        var result = await starter.ReturnUnserializableAsync().WithId("unserializable-result").ScheduleAsync(cancellationToken);
        var resultResponse = await result.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal(DurableTaskStatus.Failed, resultResponse.Status);
        Assert.Contains(typeof(UnserializableValue).FullName!, Assert.IsType<SerializerException>(resultResponse.Exception).Message, StringComparison.Ordinal);

        var exception = await starter.ThrowUnserializableAsync().WithId("unserializable-exception").ScheduleAsync(cancellationToken);
        var exceptionResponse = await exception.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal(DurableTaskStatus.Failed, exceptionResponse.Status);
        Assert.Contains(typeof(InvalidOperationException).FullName!, Assert.IsType<SerializerException>(exceptionResponse.Exception).Message, StringComparison.Ordinal);

        // 下 grain 继续从事其他工作。
        var later = await starter.TripleAsync(2).WithId("later").ScheduleAsync(cancellationToken);
        Assert.Equal(6, await later.WaitAsync(Timeout, cancellationToken));
    }

    /// <summary>
    /// 生成一个分离任务( 包括 <paramref name="host"/>执行 <paramref name="executionId"/> 开始
    /// 其业务的运作情况 <paramref name="segment"/>:分部分举行 <c>{HostId}/{Id}/{segment}</c>每一部分都逃脱了
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

/// <summary>运行任务启动覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskStartTests() : DurableTaskStartTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行任务启动覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskStartTests() : DurableTaskStartTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface IDurableTaskStartingGrain : IGrainWithGuidKey
{
    Task PostEchoAsync(Guid workerKey, int value, string rootId);

    // 等待自身 grain 间断任务的请求,因为任务各部分是向 grain提出的请求。
    [AlwaysInterleave]
    Task<(int Own, int Other)> ScheduleAndAwaitAsync(Guid workerKey, int value);

    DurableTask<int> TripleAsync(int value);

    /// <summary>完成时,其值为没有编码器的类型。</summary>
    DurableTask<object> ReturnUnserializableAsync();

    /// <summary>失当,但数据具有无编码器类型值的除外。</summary>
    DurableTask<int> ThrowUnserializableAsync();

    DurableTask PostEchoInWorkflowAsync(Guid workerKey, int value, string rootId);

    DurableTask<int> AwaitDetachedEchoesAsync(Guid workerKey, int value);

    /// <summary>工人开始 <c>WaitForCancellationAsync</c> 下 <paramref name="rootId"/>,测试要用另一种方法进行。</summary>
    DurableTask<string> ScheduleConflictingStartAsync(Guid workerKey, string rootId);

    /// <summary>工人开始回响 <paramref name="value"/> 下 <paramref name="rootId"/>等待着它</summary>
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

/// <summary>一种没有编码的值,所以不序列。</summary>
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
        // 下 grain 任务在运行时安排,所以电话不会等待 grain 本身。
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
