using TestExtensions;
using Xunit;

namespace Tester
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
    /// 提供核心 Orleans 测试环境设置和资源。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }

    /// <summary>
    /// 用于 Azure- 基于集成测试的基准测试组群固定装置。
    /// 确保 Azure 在进行测试之前能够实现储存连接。
    /// </summary>
    public abstract class BaseAzureTestClusterFixture : BaseTestClusterFixture
    {
        protected override void CheckPreconditionsOrThrow()
        {
            base.CheckPreconditionsOrThrow();
            TestUtils.CheckForAzureStorage();
        }
    }

    /// <summary>
    /// Azure- 使用 <see cref="InProcessTestCluster"/>进行集成测试的基准试验组群固定装置。
    /// 确保 Azure 在进行测试之前能够实现储存连接。
    /// </summary>
    public abstract class BaseInProcessAzureTestClusterFixture : BaseInProcessTestClusterFixture
    {
        protected override void CheckPreconditionsOrThrow()
        {
            base.CheckPreconditionsOrThrow();
            TestUtils.CheckForAzureStorage();
        }
    }
}
