using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace DependencyInjection.Tests.DefaultServiceProvider
{
    /// <summary>
    /// 使用默认 Microsoft 识别数据基础设施容器测试依赖性注射功能。
    /// 从 DependencyInjectionGrainTestRunner 中继承所有测试案例,以核实
    /// 默认 ServiceProvider 执行与 Orleans正确有效。
    /// </summary>
    [TestCategory("DI"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("DependencyInjection")]
    public class DependencyInjectionGrainTestsUsingDefaultServiceProvider : DependencyInjectionGrainTestRunner, IClassFixture<DependencyInjectionGrainTestsUsingDefaultServiceProvider.Fixture>
    {
        /// <summary>
        /// 用于配置单 -silo 集的测试固定装置,并配置默认的 DI 容器。
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
