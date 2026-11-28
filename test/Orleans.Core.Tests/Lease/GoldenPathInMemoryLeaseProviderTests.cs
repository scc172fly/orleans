using Xunit;
using Orleans.Runtime.Development;
using TestExtensions;
using TestExtensions.Runners;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// 按照黄金路径测试模式测试内存租约提供程序的实现。
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
