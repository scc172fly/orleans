using Xunit;
using Orleans.Runtime.Development;
using TestExtensions;
using TestExtensions.Runners;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// 采用金路测试模式进行租赁供应商实施模拟测试。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Lease")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GoldenPathInMemoryLeaseProviderTests : GoldenPathLeaseProviderTestRunner, IClassFixture<GoldenPathInMemoryLeaseProviderTests.Fixture>
    {
        public GoldenPathInMemoryLeaseProviderTests(Fixture fixture, ITestOutputHelper output)
            : base(new InMemoryLeaseProvider(fixture.GrainFactory), output)
        {
        }

        public class Fixture : BaseTestClusterFixture
        {
        }
    }
}
