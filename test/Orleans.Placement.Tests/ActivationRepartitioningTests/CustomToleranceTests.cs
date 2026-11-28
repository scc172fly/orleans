using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Placement;
using Orleans.Placement.Repartitioning;
using Orleans.Runtime.Placement;
using Orleans.Runtime.Placement.Repartitioning;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace UnitTests.ActivationRepartitioningTests;

// 场景示意图见：https://github.com/dotnet/orleans/pull/8877
[TestSuite("Functional")]
[TestProvider("None")]
[TestArea("Placement")]
[TestCategory("Functional"), TestCategory("ActivationRepartitioning"), Category("BVT")]
public class CustomToleranceTests(CustomToleranceTests.Fixture fixture, ITestOutputHelper output) : RepartitioningTestBase<CustomToleranceTests.Fixture>(fixture), IClassFixture<CustomToleranceTests.Fixture>
{
    [Fact]
    public async Task Should_ConvertAllRemoteCalls_ToLocalCalls_WhileRespectingTolerance()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(1));
        var cancellationToken = cts.Token;
        await AdjustActivationCountOffsets(cancellationToken);

        var e1 = GrainFactory.GetGrain<IE>(1);
        var e2 = GrainFactory.GetGrain<IE>(2);
        var e3 = GrainFactory.GetGrain<IE>(3);

        RequestContext.Set(IPlacementDirector.PlacementHintKey, Silo1);

        await e1.FirstPing(Silo2);
        await e2.FirstPing(Silo2);
        await e3.FirstPing(Silo2);

        RequestContext.Set(IPlacementDirector.PlacementHintKey, Silo2);
        var x = GrainFactory.GetGrain<IX>(0);
        await x.Ping();

        var i = 0;
        while (i < 3)
        {
            await e1.Ping();
            await e2.Ping();
            await e3.Ping();
            await x.Ping();
            i++;
        }

        var f1 = GrainFactory.GetGrain<IF>(1);
        var f2 = GrainFactory.GetGrain<IF>(2);
        var f3 = GrainFactory.GetGrain<IF>(3);

        var e1_host = await e1.GetAddress();
        var e2_host = await e2.GetAddress();
        var e3_host = await e3.GetAddress();

        var f1_host = await f1.GetAddress();
        var f2_host = await f2.GetAddress();
        var f3_host = await f3.GetAddress();

        Assert.Equal(Silo1, e1_host);
        Assert.Equal(Silo1, e2_host);
        Assert.Equal(Silo1, e3_host);

        Assert.Equal(Silo2, f1_host);
        Assert.Equal(Silo2, f2_host);
        Assert.Equal(Silo2, f3_host);

        Assert.Equal(Silo2, await x.GetAddress()); // X 仍位于 Silo 2。

        await TriggerExchangeRequestAfterFlushingBuffers(Silo1Repartitioner, cancellationToken);

        // 此时的分布如下：

        // S1：E1-F1、E3-F3、sys.svc.clustering.dev、其余激活实例（两个 Silo 均有的默认激活实例）。
        // S2：E2-F2、X、其余激活实例（两个 Silo 均有的默认激活实例）。

        // 容差 <= 2；若忽略一份默认激活实例、sys.svc.clustering.dev 及用于抵消该实例的 X，
        // 则 Silo 1 共计 4 个激活实例，Silo 2 共计 2 个，符合容差要求，且所有远程调用
        // 均已转换为本地调用。

        await Test().WaitAsync(cancellationToken);

        // 为确认结果，再次触发 's1_rebalancer'，此时应不再发生迁移。
        i = 0;
        while (i < 3)
        {
            await e1.Ping();
            await e2.Ping();
            await e3.Ping();
            await x.Ping();
            i++;
        }

        await LogEdgesAsync(Silo1Repartitioner, cancellationToken);
        await LogEdgesAsync(Silo2Repartitioner, cancellationToken);
        await TriggerExchangeRequestAfterFlushingBuffers(Silo1Repartitioner, cancellationToken);
        await Test().WaitAsync(cancellationToken);

        // 为进一步确认，现在触发 's2_rebalancer'，同样应不再发生迁移。
        i = 0;
        while (i < 3)
        {
            await e1.Ping();
            await e2.Ping();
            await e3.Ping();
            await x.Ping();
            i++;
        }

        await TriggerExchangeRequestAfterFlushingBuffers(Silo2Repartitioner, cancellationToken);

        await Test().WaitAsync(cancellationToken);

        async Task Test()
        {
            e1_host = await e1.GetAddress();
            e2_host = await e2.GetAddress();
            e3_host = await e3.GetAddress();

            f1_host = await f1.GetAddress();
            f2_host = await f2.GetAddress();
            f3_host = await f3.GetAddress();

            // 检查每个 Grain 是否与其配对 Grain 位于同一 Silo。
            Assert.Equal(f1_host, e1_host);
            Assert.Equal(f2_host, e2_host);
            Assert.Equal(f3_host, e3_host);

            var locations = new SiloAddress[] { e1_host, e2_host, e3_host };
            Assert.False(locations.All(h => h.Equals(Silo1)), "Grains should not all be located on silo 1.");
            Assert.False(locations.All(h => h.Equals(Silo2)), "Grains should not all be located on silo 2.");

            Assert.Equal(Silo2, await x.GetAddress()); // X 仍位于 Silo 2。
        }
    }

    private async Task LogEdgesAsync(
        IActivationRepartitionerSystemTarget repartitioner,
        CancellationToken cancellationToken)
    {
        var edgeCounts = await repartitioner.GetGrainCallFrequencies(cancellationToken);
        output.WriteLine($"{repartitioner.GetGrainId()} call frequencies:");
        foreach (var (edge, freq) in edgeCounts)
        {
            output.WriteLine($"\t{edge} x {freq}");
        }
    }

    public interface IE : IGrainWithIntegerKey
    {
        Task FirstPing(SiloAddress silo2);
        Task Ping();
        Task<SiloAddress> GetAddress();
    }

    public interface IF : IGrainWithIntegerKey
    {
        Task Ping();
        Task<SiloAddress> GetAddress();
    }

    public interface IX : IGrainWithIntegerKey
    {
        Task Ping();
        Task<SiloAddress> GetAddress();
    }

    public class E : Grain, IE
    {
        public async Task FirstPing(SiloAddress silo2)
        {
            RequestContext.Set(IPlacementDirector.PlacementHintKey, silo2);
            await GrainFactory.GetGrain<IF>(this.GetPrimaryKeyLong()).Ping();
        }

        public Task Ping() => GrainFactory.GetGrain<IF>(this.GetPrimaryKeyLong()).Ping();
        public Task<SiloAddress> GetAddress() => Task.FromResult(GrainContext.Address.SiloAddress!);
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            ServiceProvider.GetRequiredService<ILogger<E>>().LogInformation("Activating {GrainId} on silo {SiloAddress}", this.GrainId, this.Runtime.SiloAddress);
            return base.OnActivateAsync(cancellationToken);
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            ServiceProvider.GetRequiredService<ILogger<E>>().LogInformation("Deactivating {GrainId} on silo {SiloAddress}. Reason: {Reason}", this.GrainId, this.Runtime.SiloAddress, reason);
            return base.OnDeactivateAsync(reason, cancellationToken);
        }
    }

    public class F : Grain, IF
    {
        public Task Ping() => Task.CompletedTask;

        public Task<SiloAddress> GetAddress() => Task.FromResult(GrainContext.Address.SiloAddress!);

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            ServiceProvider.GetRequiredService<ILogger<F>>().LogInformation("Activating {GrainId} on silo {SiloAddress}", this.GrainId, this.Runtime.SiloAddress);
            return base.OnActivateAsync(cancellationToken);
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            ServiceProvider.GetRequiredService<ILogger<F>>().LogInformation("Deactivating {GrainId} on silo {SiloAddress}. Reason: {Reason}", this.GrainId, this.Runtime.SiloAddress, reason);
            return base.OnDeactivateAsync(reason, cancellationToken);
        }
    }

    /// <summary>
    /// 此举仅用于使两个 Silo 的初始负载均衡，因为默认情况下主 Silo
    /// 比辅助 Silo 多一个激活实例，即 'sys.svc.clustering.dev'。
    /// </summary>
    [Immovable(ImmovableKind.Repartitioner)]
    public class X : Grain, IX
    {
        public Task Ping() => Task.CompletedTask;
        public Task<SiloAddress> GetAddress() => Task.FromResult(GrainContext.Address.SiloAddress!);

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            ServiceProvider.GetRequiredService<ILogger<X>>().LogInformation("Activating {GrainId} on silo {SiloAddress}", this.GrainId, this.Runtime.SiloAddress);
            return base.OnActivateAsync(cancellationToken);
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            ServiceProvider.GetRequiredService<ILogger<X>>().LogInformation("Deactivating {GrainId} on silo {SiloAddress}. Reason: {Reason}", this.GrainId, this.Runtime.SiloAddress, reason);
            return base.OnDeactivateAsync(reason, cancellationToken);
        }
    }

    public class Fixture : BaseTestClusterFixture
    {
        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.InitialSilosCount = 2;
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            builder.AddClientBuilderConfigurator<ClientConfigurator>();
        }

        private class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
                => hostBuilder
                    .Configure<SiloMessagingOptions>(o =>
                    {
                        o.AssumeHomogenousSilosForTesting = true;
                        o.ClientGatewayShutdownNotificationTimeout = default;
                    })
                    .Configure<ActivationRepartitionerOptions>(o =>
                    {
                        // 设置为让定时器实际上几乎不会触发！测试将手动调用协议。
                        o.MinRoundPeriod = TimeSpan.FromSeconds(299);
                        o.MaxRoundPeriod = TimeSpan.FromSeconds(300);
                        // 将此值设为零，以便测试中无需延迟即可多次调用协议。
                        o.RecoveryPeriod = TimeSpan.Zero;

                        // 排除测试中概率过滤可能导致误报的微小概率。
                        o.AnchoringFilterEnabled = false;
                    })
                    .AddActivationRepartitioner<HardLimitRule>()
                    .ConfigureLogging(logging => logging.AddFilter("Orleans.Runtime.Placement.Repartitioning", LogLevel.Trace))
                    .ConfigureServices(service => service.AddSingleton<IRepartitionerMessageFilter, TestMessageFilter>());
        }

        private class HardLimitRule : IImbalanceToleranceRule
        {
            public bool IsSatisfiedBy(uint imbalance) => imbalance <= 2;
        }

        private class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder.Configure<GatewayOptions>(o => o.PreferredGatewayIndex = 0);
            }
        }
    }
}
