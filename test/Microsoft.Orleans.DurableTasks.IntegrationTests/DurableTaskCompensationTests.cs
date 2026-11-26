using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 通过单独的任务和整个 grains的行政中止,合作取消合同,并给予补偿。
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

        // 赔偿是清单 grain的一个分离根,在注销后将完成。
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

        // 中止运行没有工作流程代码, 所以没有开始补偿 。
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

/// <summary>运行补偿和中止覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCompensationTests() : DurableTaskCompensationTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行补偿和中止覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
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
            // 一旦清单持有发布版本, 将返回的排队时间安排后, 因此工作流程的结果显示, 调用者可以
            // 看着它的根身份显示释放
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
