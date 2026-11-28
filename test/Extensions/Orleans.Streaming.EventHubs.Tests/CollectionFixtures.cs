using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Orleans.Runtime;
using Tester;
using TestExtensions;
using Xunit;

namespace ServiceBus.Tests
{
    // 测试集合必须在每个程序集中仅定义一次。

    /// <summary>
    /// 定义一个供需要默认 Orleans 集群配置的测试使用的测试集合。
    /// 此集合中的测试共享一个集群实例，以提高性能。
    /// </summary>
    [CollectionDefinition("DefaultCluster")]
    public class DefaultClusterTestCollection : ICollectionFixture<DefaultClusterFixture> { }


    /// <summary>
    /// 定义一个供需要共享测试环境配置的测试使用的测试集合。
    /// 提供针对 Azure Service Bus 和 Event Hub 的测试环境设置。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }

    /// <summary>
    /// Event Hub 集成测试的基础测试集群夹具。
    /// 确保 Event Hub 连通，并等待流队列初始化。
    /// </summary>
    public abstract class BaseEventHubTestClusterFixture : BaseTestClusterFixture
    {
        protected override void CheckPreconditionsOrThrow()
        {
            base.CheckPreconditionsOrThrow();
            TestUtils.CheckForEventHub();
        }

        public override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();
            if (!PreconditionsMet)
            {
                return;
            }
            var collectors = HostedCluster.Silos
                .Select(silo => HostedCluster.GetSiloServiceProvider(silo.SiloAddress).GetRequiredService<IMeterFactory>())
                .Select(meterFactory => new MetricCollector<long>(meterFactory, "Microsoft.Orleans", "orleans-streams-queue-read-duration"))
                .ToArray();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                // 等待整个集群累计完成 10 次队列读取。
                while (collectors.Sum(collector => collector.GetMeasurementSnapshot().Count) < 10)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);
                }
            }
            finally
            {
                foreach (var collector in collectors)
                {
                    collector.Dispose();
                }
            }
        }
    }
}
