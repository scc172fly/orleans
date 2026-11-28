using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 测试跨 Grain 的协作式取消（通过分离任务执行补偿）及管理性中止。
/// </summary>
public abstract class DurableTaskCompensationTests(string journalFormatKey) : IAsyncLifetime
{
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task CanceledWorkflowCompensatesThroughDetachedTaskAndReturnsItsOwnResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflowKey = Guid.NewGuid();
        var inventoryKey = Guid.NewGuid();
        var workflow = _fixture.Client.GetGrain<IDurableTaskCompensatingWorkflowGrain>(workflowKey);
        var holding = _fixture.MessagingProbe.WaitForStartedAsync(inventoryKey, 1);
        var holdCanceled = _fixture.MessagingProbe.WaitForCanceledAsync(inventoryKey, 1);

        var scheduled = await workflow.ReserveAsync(inventoryKey, 3).WithId("reserve").ScheduleAsync(cancellationToken);
        await holding.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await scheduled.CancelAsync(cancellationToken);

        var response = await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal("compensated", response.GetResult<string>());
        await holdCanceled.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        // 补偿是库存 Grain 上的分离根任务，在取消之后一直运行至完成。
        var release = _fixture.Client.GetGrain<IDurableTaskInventoryTestGrain>(inventoryKey)
            .GetDurableTask<int>(DurableTaskCompensatingWorkflowGrain.GetCompensationRootId(workflowKey));
        var released = await release.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(3, released.GetResult<int>());
    }

    [Fact]
    public async Task AbortThroughGrainExtensionFaultsWorkflowAndAbortsRemoteChildWithoutCompensation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflowKey = Guid.NewGuid();
        var inventoryKey = Guid.NewGuid();
        var workflow = _fixture.Client.GetGrain<IDurableTaskCompensatingWorkflowGrain>(workflowKey);
        var holding = _fixture.MessagingProbe.WaitForStartedAsync(inventoryKey, 1);
        var holdStopped = _fixture.MessagingProbe.WaitForCanceledAsync(inventoryKey, 1);
        var scheduled = await workflow.ReserveAsync(inventoryKey, 2).WithId("abort-me").ScheduleAsync(cancellationToken);
        await holding.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        await workflow.AsReference<IDurableTaskGrainExtension>().AbortAsync(TaskId.CreateRoot("abort-me"), "operator", cancellationToken);

        var response = await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(DurableTaskStatus.Failed, response.Status);
        Assert.Equal("operator", Assert.IsType<DurableTaskAbortedException>(response.Exception).Message);
        await holdStopped.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        var diagnostics = await GetDiagnosticsAsync(workflow, TaskId.CreateRoot("abort-me"), cancellationToken);
        Assert.Equal("Faulted", diagnostics.Status);
        Assert.Equal("DurableTaskAbortedException: operator", diagnostics.Response);

        // 中止不运行任何工作流代码，因此未启动补偿。
        var release = _fixture.Client.GetGrain<IDurableTaskInventoryTestGrain>(inventoryKey)
            .GetDurableTask<int>(DurableTaskCompensatingWorkflowGrain.GetCompensationRootId(workflowKey));
        await Assert.ThrowsAsync<DurableTaskNotFoundException>(
            () => release.GetResponseAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, cancellationToken));
    }

    private static async Task<DurableTaskDiagnosticState> GetDiagnosticsAsync(
        IAddressable grain,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        await foreach (var (id, state) in grain.AsReference<IDurableTaskGrainExtension>().GetTasksAsync(cancellationToken))
        {
            if (id == taskId)
            {
                return state;
            }
        }

        throw new Xunit.Sdk.XunitException($"Task '{taskId}' has no diagnostics.");
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行补偿与中止相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCompensationTests() : DurableTaskCompensationTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行补偿与中止相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskCompensationTests() : DurableTaskCompensationTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface IDurableTaskCompensatingWorkflowGrain : IGrainWithGuidKey
{
    DurableTask<string> ReserveAsync(Guid inventoryKey, int count);
}

public interface IDurableTaskInventoryTestGrain : IGrainWithGuidKey
{
    DurableTask<int> ReserveAsync(int item);

    DurableTask<int> HoldAsync();

    DurableTask<int> ReleaseAsync(int count);
}

[GrainType("durable-task-compensating-workflow-test")]
public sealed class DurableTaskCompensatingWorkflowGrain : DurableGrain, IDurableTaskCompensatingWorkflowGrain
{
    public static string GetCompensationRootId(Guid workflowKey) => $"compensate-{workflowKey:N}";

    public async DurableTask<string> ReserveAsync(Guid inventoryKey, int count)
    {
        var inventory = GrainFactory.GetGrain<IDurableTaskInventoryTestGrain>(inventoryKey);
        var reserved = 0;
        try
        {
            for (var item = 0; item < count; item++)
            {
                await inventory.ReserveAsync(item).WithId($"reserve-{item}");
                reserved++;
            }

            await inventory.HoldAsync().WithId("hold");
            return "completed";
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // 库存 Grain 持有释放任务后调度操作才返回，因此工作流结果表明调用方可以
            // 通过根 ID 查找该释放任务。
            await inventory.ReleaseAsync(reserved).Detached(GetCompensationRootId(this.GetPrimaryKey())).ScheduleAsync();
            return "compensated";
        }
    }
}

[GrainType("durable-task-inventory-test")]
public sealed class DurableTaskInventoryTestGrain(DurableTaskMessagingProbe probe) : DurableGrain, IDurableTaskInventoryTestGrain
{
    public DurableTask<int> ReserveAsync(int item) => DurableTask.FromResult(item);

    public DurableTask<int> HoldAsync() =>
        DurableTask.Run(async cancellationToken =>
        {
            probe.RecordStarted(this.GetPrimaryKey());
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                probe.RecordCanceled(this.GetPrimaryKey());
                throw;
            }

            return 0;
        });

    public DurableTask<int> ReleaseAsync(int count) => DurableTask.FromResult(count);
}
