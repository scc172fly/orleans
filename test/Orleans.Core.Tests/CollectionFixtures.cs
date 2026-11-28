using TestExtensions;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// 定义 NonSilo.Tests 中 DefaultCluster 测试的测试集合。
    /// 此集合确保所有测试共享同一个集群实例，以提高性能。
    /// </summary>
    [CollectionDefinition("DefaultCluster")]
    public class DefaultClusterTestCollection : ICollectionFixture<DefaultClusterFixture> { }

    /// <summary>
    /// 定义 TestEnvironmentFixture 的测试集合。
    /// 此集合为多个测试类提供共享的测试环境设置。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }
}
