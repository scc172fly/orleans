using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.TestingHost;
using UnitTests.GrainInterfaces;
using UnitTests.TestHelper;
using Xunit;

namespace UnitTests.MembershipTests
{
    /// <summary>
    /// 客户目录分区重建在 silo 失败后测试。
    /// </summary>
    public class ClientIdPartitionDataRebuildTests : IDisposable
    {
        internal class Observer : ISimpleGrainObserver
        {
            private readonly SemaphoreSlim semaphore = new SemaphoreSlim(0);
            private int lastA;
            private int lastB;

            public void StateChanged(int a, int b)
            {
                this.lastA = a;
                this.lastB = b;
                this.semaphore.Release();
            }

            public async Task WaitForNotification(int expectedA, int expectedB, TimeSpan timeout, CancellationToken cancellationToken)
            {
                Assert.True(await this.semaphore.WaitAsync(timeout, cancellationToken), "No notification received");
                Assert.Equal(expectedA, this.lastA);
                Assert.Equal(expectedB, this.lastB);
            }
        }

        private readonly ITestOutputHelper output;

        private TestCluster hostedCluster = null!;

        public ClientIdPartitionDataRebuildTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact(Skip = "Not reliable in PR build, skipping for now")]
        //[形式(SiloUnavailableException), TestCategory(“功能”)
        public async Task ReconstructClientIdPartitionTest_Observer()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // 确保客户端在 SIL2 分区上的用户条目并获得 grain 生活在西里岛3号
            var grain = await SetupTestAndPickGrain<ISimpleObserverableGrain>(g => g.GetRuntimeInstanceId(), cancellationToken);
            var observer = new Observer();
            var reference = this.hostedCluster.GrainFactory!.CreateObjectReference<ISimpleGrainObserver>(observer);

            await grain.Subscribe(reference);

            // 试验第一次通知
            await grain.SetA(10);
            await observer.WaitForNotification(10, 0, TimeSpan.FromSeconds(10), cancellationToken);

            // 杀死持有目录客户端条目的 silo
            await this.hostedCluster.SecondarySilos[0].StopSiloAsync(stopGracefully: false, cancellationToken);
            await Task.Delay(5000, cancellationToken);

            // 第二次通知应有效,因为目录在下列时间“重建”了:
            // silos 组群中检测到已死亡的
            await grain.SetB(20);
            await observer.WaitForNotification(10, 20, TimeSpan.FromSeconds(10), cancellationToken);
        }

        [Fact(Skip = "Not reliable in PR build, skipping for now")]
        //[形式(SiloUnavailableException), TestCategory(“功能”)
        public async Task ReconstructClientIdPartitionTest_Request()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // 确保客户端在 SIL2 分区上的用户条目并获得 grain 生活在西里瓦2号上
            var grain = await SetupTestAndPickGrain<ITestGrain>(g => g.GetRuntimeInstanceId(), cancellationToken);

            // 启动长任务并杀死持有目录客户端条目的 silo
            var promise = grain.DoLongAction(TimeSpan.FromSeconds(10), "LongAction");
            await this.hostedCluster.SecondarySilos[0].StopSiloAsync(stopGracefully: false, cancellationToken);

            // 因为它应该工作,因为目录是"重建"
            // silos 组群中检测到已死亡的
            await promise.WaitAsync(cancellationToken);
        }

        private async Task<T> SetupTestAndPickGrain<T>(
            Func<T, Task<string>> getRuntimeInstanceId,
            CancellationToken cancellationToken) where T : class, IGrainWithIntegerKey
        {
            // 确保客户端在 SILO2 分区上的条目
            GrainId clientId = default;
            CreateAndDeployTestCluster();
            for (var i = 0; i < 100; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (this.hostedCluster.Client! == null)
                {
                    await this.hostedCluster.InitializeClientAsync(cancellationToken);
                }

                var client = this.hostedCluster.ServiceProvider.GetRequiredService<OutsideRuntimeClient>();
                clientId = client.CurrentActivationAddress.GrainId;
                var report = await TestUtils.GetDetailedGrainReport(this.hostedCluster.InternalGrainFactory!, clientId, hostedCluster.Primary!);
                if (this.hostedCluster.SecondarySilos[0].SiloAddress.Equals(report.PrimaryForGrain))
                {
                    break;
                }
                clientId = default;
                await this.hostedCluster.KillClientAsync();
            }
            Assert.False(clientId.IsDefault);

            // 确保在硅3上启动 grain
            T? grain = null;
            for (var i = 0; i < 100; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                grain = this.hostedCluster.GrainFactory!.GetGrain<T>(i);
                var instanceId = await getRuntimeInstanceId(grain).WaitAsync(cancellationToken);
                if (instanceId.Contains(hostedCluster.SecondarySilos[1].SiloAddress.Endpoint.ToString()))
                {
                    break;
                }
                grain = null;
            }
            Assert.NotNull(grain);

            return grain;
        }

        private void CreateAndDeployTestCluster()
        {
            var builder = new TestClusterBuilder(3);

            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            builder.AddClientBuilderConfigurator<ClientConfigurator>();
            this.hostedCluster = builder.Build();
            this.hostedCluster.Deploy();
        }

        public class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.Configure<ClusterMembershipOptions>(options =>
                {
                    options.NumMissedProbesLimit = 1;
                    options.ProbeTimeout = TimeSpan.FromMilliseconds(500);
                    options.MaxProbeTimeout = TimeSpan.FromMilliseconds(500);
                    options.NumVotesForDeathDeclaration = 1;
                });

                hostBuilder.Configure<GrainDirectoryOptions>(options => options.CacheSize = 0);
            }
        }

        public class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder.Configure<GatewayOptions>(options => options.PreferredGatewayIndex = 0);
            }
        }

        public void Dispose()
        {
            try
            {
                hostedCluster?.StopAllSilos();
            }
            finally
            {
                hostedCluster?.Dispose();
                hostedCluster = null!;
            }
        }
    }
}
