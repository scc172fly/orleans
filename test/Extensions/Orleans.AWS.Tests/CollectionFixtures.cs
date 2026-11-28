using TestExtensions;
using Xunit;

namespace AWSUtils.Tests
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
    /// 提供 AWS 专用的测试环境配置及资源。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }
}
