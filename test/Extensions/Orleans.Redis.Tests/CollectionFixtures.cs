using TestExtensions;
using Xunit;

namespace Tester.Redis
{
    // 必须在每组集会中确定一次大会收藏的收藏

    /// <summary>
    /// 定义需要共享测试环境配置的测试集。
    /// 提供 Redis- 使用共同固定物设置特定测试环境。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<CommonFixture> { }
}
