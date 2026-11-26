using TestExtensions;
using Xunit;

namespace Consul.Tests
{
    // 必须在每组集会中确定一次大会收藏的收藏

    /// <summary>
    /// 定义需要共享测试环境配置的测试集。
    /// 提供具体针对领事的测试环境设置和资源。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }
}
