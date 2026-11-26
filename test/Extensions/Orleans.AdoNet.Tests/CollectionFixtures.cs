using TestExtensions;
using Xunit;

namespace Tester.SQLUtils
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
    /// 提供 ADO.NET 和 SQL 数据库中具体环境测试设置。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }
}
