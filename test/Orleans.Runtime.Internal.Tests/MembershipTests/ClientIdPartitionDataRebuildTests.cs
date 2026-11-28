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
    /// 测试 Silo 故障后的客户端目录分区重建。
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
        //[Fact(typeof(SiloUnavailableException)), TestCategory("Functional")]
        public async Task ReconstructClientIdPartitionTest_Observer()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // 确保客户端条目位于 Silo2 的分区上，并获取位于 Silo3 的 Grain。
            var grain = await SetupTestAndPickGrain<ISimpleObserverableGrain>(g => g.GetRuntimeInstanceId(), cancellationToken);
            var observer = new Observer();
            var reference = this.hostedCluster.GrainFactory!.CreateObjectReference<ISimpleGrainObserver>(observer);

            await grain.Subscribe(reference);

            // 测试第一次通知。
            await grain.SetA(10);
            await observer.WaitForNotification(10, 0, TimeSpan.FromSeconds(10), cancellationToken);

            // 终止持有客户端目录条目的 Silo。
            await this.hostedCluster.SecondarySilos[0].StopSiloAsync(stopGracefully: false, cancellationToken);
            await Task.Delay(5000, cancellationToken);

            // 第二次通知应正常工作，因为目录已在
            // 集群中的 Silo 检测到死亡 Silo 时重建。
            await grain.SetB(20);
            await observer.WaitForNotification(10, 20, TimeSpan.FromSeconds(10), cancellationToken);
        }

        [Fact(Skip = "Not reliable in PR build, skipping for now")]
        //[Fact(typeof(SiloUnavailableException)), TestCategory("Functional")]
        public async Task ReconstructClientIdPartitionTest_Request()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // 确保客户端条目位于 Silo2 的分区上，并获取位于 Silo2 的 Grain。
            var grain = await SetupTestAndPickGrain<ITestGrain>(g => g.GetRuntimeInstanceId(), cancellationToken);

            // 启动长时间运行的任务，并终止持有客户端目录条目的 Silo。
            var promise = grain.DoLongAction(TimeSpan.FromSeconds(10), "LongAction");
            await this.hostedCluster.SecondarySilos[0].StopSiloAsync(stopGracefully: false, cancellationToken);

            // 此操作应正常工作，因为目录已在
            // 集群中的 Silo 检测到死亡 Silo 时重建。
            await promise.WaitAsync(cancellationToken);
        }

        private async Task<T> SetupTestAndPickGrain<T>(
            Func<T, Task<string>> getRuntimeInstanceId,
            CancellationToken cancellationToken) where T : class, IGrainWithIntegerKey
        {
            // 确保客户端条目位于 Silo2 的分区上。
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

            // 确保 Grain 在 Silo3 上激活。
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
