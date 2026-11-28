using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 测试将任务地址作为存储和传输值使用：工作流从分离任务的句柄取得地址并
/// 返回；Grain 在调用中接收地址并记入日志；重新激活后，Grain、工作流及
/// 客户端均可通过这些地址访问任务。
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

        // 工作流的分离任务句柄指向工作 Grain 的根任务，其结果携带这些地址。
        var workflow = await starter.StartDetachedAsync(worker.GetPrimaryKey()).WithId("workflow").ScheduleAsync(cancellationToken);
        var addresses = await workflow.WaitAsync(Timeout, cancellationToken);
        Assert.Equal(
            new TaskAddresses(
                new(worker.GetGrainId(), TaskId.CreateRoot("signaled")),
                new(worker.GetGrainId(), TaskId.CreateRoot("canceled"))),
            addresses);

        // 保管 Grain 在调用中接收地址并记入日志，因此下一次激活能够恢复地址。
        await keeper.StoreAsync(addresses);
        await DeactivateAsync(keeper, cancellationToken);
        Assert.Equal(addresses, await keeper.GetStoredAsync());
        Assert.Equal((DurableTaskStatus.Pending, DurableTaskStatus.Pending), await keeper.PollStoredAsync());

        // 工作流订阅已存储的任务，保管 Grain 向一个任务发送信号并取消另一个任务。
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

/// <summary>使用默认 JSON 日志格式存储任务和 Grain 状态，运行任务地址相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskAddressTests() : DurableTaskAddressTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务和 Grain 状态，运行任务地址相关测试。</summary>
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
