using TestExtensions;
using Xunit;

namespace UnitTests
{
    // 程序集集合必须在每个程序集中定义一次。

    /// <summary>
    /// 定义一个供需要默认 Orleans 集群配置的测试使用的测试集合。
    /// 此集合中的测试共享一个集群实例，以提高性能。
    /// </summary>
    [CollectionDefinition("DefaultCluster")]
    public class DefaultClusterTestCollection : ICollectionFixture<DefaultClusterFixture> { }

    /// <summary>
    /// 定义一个供需要共享测试环境配置的测试使用的测试集合。
    /// 为单元测试和集成测试提供 Orleans 内部测试环境的设置。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }
}
