using TestExtensions;
using Xunit;

namespace Tester.ZooKeeperUtils
{
    // 测试集合必须在每个程序集中仅定义一次。

    /// <summary>
    /// 定义一个供需要共享测试环境配置的测试使用的测试集合。
    /// 提供 ZooKeeper 专用的测试环境设置和资源。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }
}
