using TestExtensions;
using Xunit;

namespace Tester
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
    /// 提供 Orleans 核心测试环境的设置及资源。
    /// </summary>
    [CollectionDefinition(TestEnvironmentFixture.DefaultCollection)]
    public class TestEnvironmentFixtureCollection : ICollectionFixture<TestEnvironmentFixture> { }

    /// <summary>
    /// 基于 Azure 的集成测试所用的测试集群夹具基类。
    /// 在运行测试前确保 Azure Storage 连接可用。
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
    /// 使用 <see cref="InProcessTestCluster"/> 的 Azure 集成测试集群夹具基类。
    /// 在运行测试前确保 Azure Storage 连接可用。
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
