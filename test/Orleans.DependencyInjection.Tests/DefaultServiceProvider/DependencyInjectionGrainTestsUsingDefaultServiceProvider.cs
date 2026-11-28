using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace DependencyInjection.Tests.DefaultServiceProvider
{
    /// <summary>
    /// 测试使用默认 Microsoft DI 容器时的依赖注入功能。
    /// 继承 DependencyInjectionGrainTestRunner 的所有测试用例，以验证
    /// 默认 ServiceProvider 实现是否与 Orleans 正常协作。
    /// </summary>
    [TestCategory("DI"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("DependencyInjection")]
    public class DependencyInjectionGrainTestsUsingDefaultServiceProvider : DependencyInjectionGrainTestRunner, IClassFixture<DependencyInjectionGrainTestsUsingDefaultServiceProvider.Fixture>
    {
        /// <summary>
        /// 使用默认 DI 容器配置单 Silo 集群的测试夹具。
        /// </summary>
        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 1;
                builder.AddSiloBuilderConfigurator<TestSiloBuilderConfigurator>();
            }
        }

        public DependencyInjectionGrainTestsUsingDefaultServiceProvider(Fixture fixture)
            : base(fixture)
        {
        }
    }
}
