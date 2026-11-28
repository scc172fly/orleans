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
/// 验证客户端等待任务时，即使因 Grain 所在 Silo 崩溃或 Grain 停用等原因
/// 暂时无法访问任务所属的 Grain，也会继续等待，并在 Grain 恢复后收到任务结果。
/// </summary>
public abstract class DurableTaskClientWaitTests(string journalFormatKey) : IAsyncLifetime
{
    private const int Value = 5;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    // 持久化作业在周期性分片检查时接管崩溃 Silo 的分片，其中包含崩溃激活的唤醒作业。
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

        // 受门控的步骤的执行段占用不可重入的伙伴 Grain，因此停用从该激活自身发起，
        // 并在该执行段结束后完成，停止操作会丢弃该执行段的结果。
        activation.Deactivate(new(DeactivationReasonCode.ApplicationRequested, "The test deactivates the partner."), CancellationToken);
        _harness.Gates.Open(key);
        await activation.Deactivated.WaitAsync(WaitTimeout, CancellationToken);

        Assert.Equal(Value + 1, (await waiting.WaitAsync(WaitTimeout, CancellationToken)).GetResult<int>());
    }

    /// <summary>启动伙伴 Grain 受门控的步骤以及客户端对它的等待，步骤开始运行后返回该等待任务。</summary>
    private async Task<Task<DurableTaskResponse>> StartWaitAsync(Guid key, IFaultPartnerGrain partner)
    {
        await partner.GatedStepAsync(Value).WithId("gated").ScheduleAsync(CancellationToken);
        await _harness.Fixture.MessagingProbe.WaitForStartedAsync(key, 1).WaitAsync(WaitTimeout, CancellationToken);
        var waiting = partner.GetDurableTask<int>("gated").GetResponseAsync(CancellationToken);
        Assert.False(waiting.IsCompleted);
        return waiting;
    }

    /// <summary>使用放置提示在 <paramref name="silo"/> 上激活伙伴 Grain。</summary>
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

/// <summary>使用默认 JSON 日志格式存储任务状态，运行客户端等待相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskClientWaitTests() : DurableTaskClientWaitTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行客户端等待相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskClientWaitTests() : DurableTaskClientWaitTests(DurableTaskRecoveryFixture.OrleansBinary);
