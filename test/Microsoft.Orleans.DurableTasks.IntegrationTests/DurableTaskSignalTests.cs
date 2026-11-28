using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>测试来自客户端、其他 Grain 上的持久化执行实例及 Grain 自身方法的信号投递。</summary>
public abstract class DurableTaskSignalTests(string journalFormatKey) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ClientSignalsWaitingWorkflowAndConflictingSignalsAreRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var waiterKey = Guid.NewGuid();
        var waiter = _fixture.Client.GetGrain<IDurableTaskSignalWaiterGrain>(waiterKey);
        var taskId = TaskId.CreateRoot("approval");
        var started = _fixture.MessagingProbe.WaitForStartedAsync(waiterKey, 1);
        var workflow = await waiter.WaitForApprovalAsync().WithId("approval").ScheduleAsync(cancellationToken);
        await started.WaitAsync(Timeout, cancellationToken);

        await waiter.SignalDurableTaskAsync(taskId, DurableTaskSignalWaiterGrain.Approved, "ada", cancellationToken);

        Assert.Equal("ada", await GetResultAsync(workflow, cancellationToken));
        // 根 ID 重载以 TaskId.CreateRoot(rootId) 为目标地址。
        await waiter.SignalDurableTaskAsync("approval", DurableTaskSignalWaiterGrain.Approved, "ada", cancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => waiter.SignalDurableTaskAsync(taskId, DurableTaskSignalWaiterGrain.Approved, "grace", cancellationToken).AsTask());
        await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            () => waiter.SignalDurableTaskAsync("missing", DurableTaskSignalWaiterGrain.Approved, "ada", cancellationToken).AsTask());

        // 工作流的执行树保存信号记录及其值。
        var tasks = await GetTasksAsync(waiter, cancellationToken);
        Assert.Equal("ada", tasks[taskId.Child("$signal:approved")].Response);
    }

    [Fact]
    public async Task WorkflowOnAnotherGrainSignalsThroughTheOutbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var waiterKey = Guid.NewGuid();
        var waiter = _fixture.Client.GetGrain<IDurableTaskSignalWaiterGrain>(waiterKey);
        var sender = _fixture.Client.GetGrain<IDurableTaskSignalSenderGrain>(Guid.NewGuid());
        var started = _fixture.MessagingProbe.WaitForStartedAsync(waiterKey, 1);
        var workflow = await waiter.WaitForApprovalAsync().WithId("approval").ScheduleAsync(cancellationToken);
        await started.WaitAsync(Timeout, cancellationToken);

        var sending = await sender.SendApprovalAsync(waiterKey, "approval", "grace").WithId("send").ScheduleAsync(cancellationToken);

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, (await sending.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).Status);
        Assert.Equal("grace", await GetResultAsync(workflow, cancellationToken));

        // 发送方在自身工作流的执行树中记录发送操作，因此重放不会再次发送。
        var senderTasks = await GetTasksAsync(sender, cancellationToken);
        Assert.Equal(
            [TaskId.CreateRoot("send"), TaskId.CreateRoot("send").Child($"$send:{waiter.GetGrainId()}:approval:approved")],
            senderTasks.Keys.OrderBy(static id => id.ToString(), StringComparer.Ordinal));
    }

    [Fact]
    public async Task GrainSignalsItsOwnWorkflowAndReplayObservesTheRecordedValue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var waiterKey = Guid.NewGuid();
        var waiter = _fixture.Client.GetGrain<IDurableTaskSignalWaiterGrain>(waiterKey);
        var started = _fixture.MessagingProbe.WaitForStartedAsync(waiterKey, 1);
        var workflow = await waiter.WaitForTwoApprovalsAsync().WithId("approvals").ScheduleAsync(cancellationToken);
        await started.WaitAsync(Timeout, cancellationToken);

        await waiter.ApproveAsync("approvals", "ada");
        await waiter.DeactivateAsync();
        var replayed = _fixture.MessagingProbe.WaitForStartedAsync(waiterKey, 2);
        await waiter.SignalDurableTaskAsync(
            TaskId.CreateRoot("approvals"),
            DurableTaskSignalWaiterGrain.Approved.For("second"),
            "grace",
            cancellationToken);

        Assert.Equal("ada+grace", await GetResultAsync(workflow, cancellationToken));
        await replayed.WaitAsync(Timeout, cancellationToken);
    }

    private static async Task<T> GetResultAsync<T>(ScheduledTask<T> task, CancellationToken cancellationToken) =>
        (await task.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).GetResult<T>();

    private static async Task<Dictionary<TaskId, DurableTaskDiagnosticState>> GetTasksAsync(IAddressable grain, CancellationToken cancellationToken)
    {
        var tasks = new Dictionary<TaskId, DurableTaskDiagnosticState>();
        await foreach (var (taskId, state) in grain.AsReference<IDurableTaskGrainExtension>().GetTasksAsync(cancellationToken))
        {
            tasks.Add(taskId, state);
        }

        return tasks;
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行信号相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskSignalTests() : DurableTaskSignalTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行信号相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskSignalTests() : DurableTaskSignalTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface IDurableTaskSignalWaiterGrain : IGrainWithGuidKey
{
    DurableTask<string> WaitForApprovalAsync();

    DurableTask<string> WaitForTwoApprovalsAsync();

    Task ApproveAsync(string rootId, string value);

    Task DeactivateAsync();
}

public interface IDurableTaskSignalSenderGrain : IGrainWithGuidKey
{
    DurableTask SendApprovalAsync(Guid waiterKey, string rootId, string value);
}

[GrainType("durable-task-signal-waiter-test")]
public sealed class DurableTaskSignalWaiterGrain(IDurableTaskManager tasks, DurableTaskMessagingProbe probe)
    : DurableGrain, IDurableTaskSignalWaiterGrain
{
    public static readonly DurableSignal<string> Approved = new("approved");

    public async DurableTask<string> WaitForApprovalAsync()
    {
        probe.RecordStarted(this.GetPrimaryKey());
        return await Approved.WaitAsync();
    }

    public async DurableTask<string> WaitForTwoApprovalsAsync()
    {
        probe.RecordStarted(this.GetPrimaryKey());
        var first = await Approved.WaitAsync();
        return $"{first}+{await Approved.For("second").WaitAsync()}";
    }

    public async Task ApproveAsync(string rootId, string value) =>
        await tasks.SignalAsync(TaskId.CreateRoot(rootId), Approved, value);

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

[GrainType("durable-task-signal-sender-test")]
public sealed class DurableTaskSignalSenderGrain : DurableGrain, IDurableTaskSignalSenderGrain
{
    public async DurableTask SendApprovalAsync(Guid waiterKey, string rootId, string value) =>
        await GrainFactory.GetGrain<IDurableTaskSignalWaiterGrain>(waiterKey)
            .SignalDurableTaskAsync(TaskId.CreateRoot(rootId), DurableTaskSignalWaiterGrain.Approved, value);
}
