using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 练习任务地址作为存储和发送的值:工作流程从它各自独立的任务和
/// 返回后,一个 grain 接到他们时会打电话和日记,在重新启动 grain、工作流程和工作程序之后
/// 客户端通过它们完成任务。
/// </summary>
public abstract class DurableTaskAddressTests(string journalFormatKey) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task StoredAddressesReachTheirTasksAfterReactivation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IDurableTaskAddressGrain>(Guid.NewGuid());
        var keeper = _fixture.Client.GetGrain<IDurableTaskAddressGrain>(Guid.NewGuid());
        var worker = _fixture.Client.GetGrain<IDurableTaskAddressGrain>(Guid.NewGuid());

        // 工作流程的控点是处理工人的根本任务,其结果包含地址。
        var workflow = await starter.StartDetachedAsync(worker.GetPrimaryKey()).WithId("workflow").ScheduleAsync(cancellationToken);
        var addresses = await workflow.WaitAsync(Timeout, cancellationToken);
        Assert.Equal(
            new TaskAddresses(
                new(worker.GetGrainId(), TaskId.CreateRoot("signaled")),
                new(worker.GetGrainId(), TaskId.CreateRoot("canceled"))),
            addresses);

        // 保管人收到一个电话地址,并日记这些地址,因此下一次启动后收回了这些地址。
        await keeper.StoreAsync(addresses);
        await DeactivateAsync(keeper, cancellationToken);
        Assert.Equal(addresses, await keeper.GetStoredAsync());
        Assert.Equal((DurableTaskStatus.Pending, DurableTaskStatus.Pending), await keeper.PollStoredAsync());

        // 工作流程订阅存储的任务,而保管人则发出一项任务信号并取消另一项任务。
        var awaiting = await keeper.AwaitStoredAsync().WithId("await").ScheduleAsync(cancellationToken);
        await keeper.SignalAndCancelStoredAsync("released");

        Assert.Equal("released", await awaiting.WaitAsync(Timeout, cancellationToken));
        var signaled = await _fixture.Client.GetDurableTask<string>(addresses.Signaled).GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        var canceled = await _fixture.Client.GetDurableTask<string>(addresses.Canceled).GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal("released", signaled.GetResult<string>());
        Assert.Equal(DurableTaskStatus.Canceled, canceled.Status);
    }

    [Fact]
    public async Task ClientHandlesAndReferencesAddressTheirTasks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var worker = _fixture.Client.GetGrain<IDurableTaskAddressGrain>(Guid.NewGuid());

        var handle = await worker.WaitForReleaseAsync().WithId("client-root").ScheduleAsync(cancellationToken);

        Assert.True(handle.TryGetAddress(out var address));
        Assert.Equal(new DurableTaskAddress(worker.GetGrainId(), TaskId.CreateRoot("client-root")), address);
        Assert.Equal(address, worker.GetDurableTask<string>("client-root").Address);
        await worker.SignalDurableTaskAsync(address.Id, DurableTaskAddressGrain.Release, "done", cancellationToken);
        Assert.Equal("done", await _fixture.Client.GetDurableTask<string>(address).WaitAsync(cancellationToken));
    }

    private async Task DeactivateAsync(IDurableTaskAddressGrain grain, CancellationToken cancellationToken)
    {
        var activations = _fixture.Probe.GetActivationCount(grain.GetGrainId());
        await grain.RequestDeactivationAsync();
        await grain.GetStoredAsync();
        await _fixture.Probe.WaitForActivationCountAsync(grain.GetGrainId(), activations + 1, cancellationToken);
    }
}

/// <summary>运行任务地址,其覆盖包含任务和 grain 状态,存储在默认的 JSON 日记格式中。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskAddressTests() : DurableTaskAddressTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行任务时包含任务和 grain 状态中存储于 Orleans 双进制日记格式。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskAddressTests() : DurableTaskAddressTests(DurableTaskRecoveryFixture.OrleansBinary);

[GenerateSerializer]
public sealed record TaskAddresses([property: Id(0)] DurableTaskAddress Signaled, [property: Id(1)] DurableTaskAddress Canceled);

public interface IDurableTaskAddressGrain : IGrainWithGuidKey
{
    DurableTask<string> WaitForReleaseAsync();

    DurableTask<TaskAddresses> StartDetachedAsync(Guid workerKey);

    Task StoreAsync(TaskAddresses addresses);

    Task<TaskAddresses> GetStoredAsync();

    Task<(DurableTaskStatus Signaled, DurableTaskStatus Canceled)> PollStoredAsync();

    DurableTask<string> AwaitStoredAsync();

    Task SignalAndCancelStoredAsync(string value);

    Task RequestDeactivationAsync();
}

[GrainType("durable-task-address-test")]
public sealed class DurableTaskAddressGrain(
    DurableTaskRecoveryProbe probe,
    [FromKeyedServices("signaled")] IDurableValue<DurableTaskAddress> signaled,
    [FromKeyedServices("canceled")] IDurableValue<DurableTaskAddress> canceled)
    : DurableGrain, IDurableTaskAddressGrain
{
    public static readonly DurableSignal<string> Release = new("release");

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        probe.RecordActivation(this.GetGrainId());
        return base.OnActivateAsync(cancellationToken);
    }

    public async DurableTask<string> WaitForReleaseAsync() => await Release.WaitAsync();

    public async DurableTask<TaskAddresses> StartDetachedAsync(Guid workerKey)
    {
        var worker = GrainFactory.GetGrain<IDurableTaskAddressGrain>(workerKey);
        var signaledTask = await worker.WaitForReleaseAsync().Detached("signaled").ScheduleAsync();
        var canceledTask = await worker.WaitForReleaseAsync().Detached("canceled").ScheduleAsync();
        Assert.True(signaledTask.TryGetAddress(out var signaledAddress));
        Assert.True(canceledTask.TryGetAddress(out var canceledAddress));
        return new(signaledAddress, canceledAddress);
    }

    public async Task StoreAsync(TaskAddresses addresses)
    {
        signaled.Value = addresses.Signaled;
        canceled.Value = addresses.Canceled;
        await WriteStateAsync();
    }

    public Task<TaskAddresses> GetStoredAsync() => Task.FromResult(new TaskAddresses(signaled.Value, canceled.Value));

    public async Task<(DurableTaskStatus Signaled, DurableTaskStatus Canceled)> PollStoredAsync() =>
        (await GrainFactory.GetDurableTask<string>(signaled.Value).GetStatusAsync(),
            await GrainFactory.GetDurableTask<string>(canceled.Value).GetStatusAsync());

    public async DurableTask<string> AwaitStoredAsync() => await GrainFactory.GetDurableTask<string>(signaled.Value);

    public async Task SignalAndCancelStoredAsync(string value)
    {
        await GrainFactory.GetGrain(signaled.Value.Target).SignalDurableTaskAsync(signaled.Value.Id, Release, value);
        await GrainFactory.GetDurableTask<string>(canceled.Value).CancelAsync();
    }

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
