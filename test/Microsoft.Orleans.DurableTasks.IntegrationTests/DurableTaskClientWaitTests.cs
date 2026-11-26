using Microsoft.Extensions.DependencyInjection;
using Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;
using Orleans.Durable.Tasks;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Runtime.Placement;
using Orleans.TestingHost;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 证明等待任务客户端的客户端一直等待无法到达任务 grain, 如崩溃
/// 在 grain中 silo 或 grain的解除,并在 grain 恢复后收到任务结果。
/// </summary>
public abstract class DurableTaskClientWaitTests(string journalFormatKey) : IAsyncLifetime
{
    private const int Value = 5;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    // 永久工作会采用崩溃的 silo碎片, 以定期的硬检查来保持崩溃激活的警钟的醒悟。
    private readonly DurableTaskFaultHarness _harness = new(
        journalFormatKey,
        silo => silo.Services.Configure<DurableJobsOptions>(options => options.ShardCheckInterval = TimeSpan.FromSeconds(1)));

    public ValueTask InitializeAsync() => _harness.InitializeAsync();
    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ClientWaitCompletesAfterTheGrainsSiloCrashes()
    {
        var silo = _harness.Fixture.Cluster.Silos[^1];
        var (key, partner) = await ActivatePartnerOnAsync(silo);
        var waiting = await StartWaitAsync(key, partner);

        await _harness.Fixture.Cluster.KillSiloAsync(silo, CancellationToken);
        await _harness.Fixture.Cluster.WaitForLivenessToStabilizeAsync(didKill: true);
        _harness.Gates.Open(key);

        Assert.Equal(Value + 1, (await waiting.WaitAsync(WaitTimeout, CancellationToken)).GetResult<int>());
    }

    [Fact]
    public async Task ClientWaitCompletesAfterTheGrainDeactivates()
    {
        var key = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(key);
        var waiting = await StartWaitAsync(key, partner);
        Assert.True(_harness.Fixture.Cluster.TryGetGrainContext(partner.GetGrainId(), out var activation));

        // 门阶的段段 持有非复发伙伴 所以解除作用开始于激活本身
        // 一旦完成部分,其结果就是停止弃鱼的结束。
        activation.Deactivate(new(DeactivationReasonCode.ApplicationRequested, "The test deactivates the partner."), CancellationToken);
        _harness.Gates.Open(key);
        await activation.Deactivated.WaitAsync(WaitTimeout, CancellationToken);

        Assert.Equal(Value + 1, (await waiting.WaitAsync(WaitTimeout, CancellationToken)).GetResult<int>());
    }

    /// <summary>启动伴侣的大门 和客户等待它, 并返回等待 一旦步骤运行。</summary>
    private async Task<Task<DurableTaskResponse>> StartWaitAsync(Guid key, IFaultPartnerGrain partner)
    {
        await partner.GatedStepAsync(Value).WithId("gated").ScheduleAsync(CancellationToken);
        await _harness.Fixture.MessagingProbe.WaitForStartedAsync(key, 1).WaitAsync(WaitTimeout, CancellationToken);
        var waiting = partner.GetDurableTask<int>("gated").GetResponseAsync(CancellationToken);
        Assert.False(waiting.IsCompleted);
        return waiting;
    }

    /// <summary>使用其定位提示在 <paramref name="silo"/> 上激活伙伴。</summary>
    private async Task<(Guid Key, IFaultPartnerGrain Grain)> ActivatePartnerOnAsync(InProcessSiloHandle silo)
    {
        await _harness.Fixture.Cluster.WaitForLivenessToStabilizeAsync();
        var key = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(key);
        RequestContext.Set(IPlacementDirector.PlacementHintKey, silo.SiloAddress);
        try
        {
            await partner.GetStateAsync();
        }
        finally
        {
            RequestContext.Remove(IPlacementDirector.PlacementHintKey);
        }

        Assert.True(_harness.Fixture.Cluster.TryGetGrainContext(partner.GetGrainId(), out var context));
        Assert.Equal(silo.SiloAddress, context.Address.SiloAddress);
        return (key, partner);
    }
}

/// <summary>运行客户端等待覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskClientWaitTests() : DurableTaskClientWaitTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行客户端等待覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskClientWaitTests() : DurableTaskClientWaitTests(DurableTaskRecoveryFixture.OrleansBinary);
