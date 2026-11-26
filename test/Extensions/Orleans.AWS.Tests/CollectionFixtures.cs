using TestExtensions;
using Xunit;

namespace AWSUtils.Tests
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
    /// 提供针对澳大利亚和威尔士妇女协会的测试环境设置和资源。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }
}
