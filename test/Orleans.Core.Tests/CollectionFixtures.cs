using TestExtensions;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// 定义用于 DefaultCluster 测试中 NonSilo.Tests.
    /// 这套集确保所有测试都采用同一组,以便提高性能。
    /// </summary>
    [CollectionDefinition("DefaultCluster")]
    public class DefaultClusterTestCollection : ICollectionFixture<DefaultClusterFixture> { }

    /// <summary>
    /// 定义 TestEnvironmentFixture的测试收藏。
    /// 此收藏提供多个测试类的共享测试环境设置 。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }
}
