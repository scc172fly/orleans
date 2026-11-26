using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DependencyInjection.Tests.Autofac
{
    // 自动软件不支持键服务
#if false
    /// <summary>
    /// 使用Autofac作为 Orleans grains的识别数据交换容器测试依赖注射功能。
    /// </summary>
    [TestCategory("DI"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("DependencyInjection")]
    public class DependencyInjectionGrainTestsUsingAutofac : DependencyInjectionGrainTestRunner, IClassFixture<DependencyInjectionGrainTestsUsingAutofac.Fixture>
    {
        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 1;
                builder.AddSiloBuilderConfigurator<TestSiloBuilderConfigurator>();
                builder.AddSiloBuilderConfigurator<SiloBuilderConfiguratorConfiguringAutofac>();
            }
            //配置后将自动应用器作为 DI 容器
            private class SiloBuilderConfiguratorConfiguringAutofac : IHostConfigurator
            {
                public void Configure(IHostBuilder hostBuilder)
                {
                    hostBuilder.UseServiceProviderFactory(new AutofacServiceProviderFactory());
                }
            }
        }

        public DependencyInjectionGrainTestsUsingAutofac(Fixture fixture)
            : base(fixture)
        {
        }
    }

    /// <summary>
    /// 测试 Orleans silos 在配置使用自动法作为数据交换容器时能够成功启动。
    /// </summary>
    [TestCategory("DI"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("DependencyInjection")]
    public class DependencyInjectionSiloStartsUsingAutofac : IClassFixture<DependencyInjectionSiloStartsUsingAutofac.Fixture>
    {
        private readonly BaseTestClusterFixture fixture;

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.GatewayPerSilo = false;
                builder.Options.InitialSilosCount = 2;
                builder.AddSiloBuilderConfigurator<HostBuilderConfigurator>();
            }

            //配置后将自动应用器作为 DI 容器
            private class HostBuilderConfigurator : IHostConfigurator
            {
                public void Configure(IHostBuilder hostBuilder)
                {
                    hostBuilder.UseServiceProviderFactory(new AutofacServiceProviderFactory());
                }
            }

            private class SiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder siloBuilder)
                {
                }
            }
        }

        public DependencyInjectionSiloStartsUsingAutofac(Fixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public async Task ClusterStart()
        {
            var grain = this.fixture.Client.GetGrain<ISimpleGrain>(0);
            await grain.IncrementA();
        }
    }
#endif
}
