using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Orleans.Runtime;
using Tester;
using TestExtensions;
using Xunit;

namespace ServiceBus.Tests
{
    // 必须在每组集会中确定一次大会收藏的收藏

    /// <summary>
    /// 定义需要默认 Orleans 组合集设置的测试收藏。
    /// 这份收藏中的测试有一个单一组别实例,可以用来改进业绩。
    /// </summary>
    [CollectionDefinition("DefaultCluster")]
    public class DefaultClusterTestCollection : ICollectionFixture<DefaultClusterFixture> { }


    /// <summary>
    /// 定义需要共享测试环境配置的测试集。
    /// 提供 Azure 服务公交车和事件枢纽特定环境测试设置。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }

    /// <summary>
    /// 事件枢纽集成测试基准测试组群固定装置。
    /// 确保事件枢纽连接,等待流队列初始化。
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
                // 等待10个队列的排列,会在整个组群中读取。
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
