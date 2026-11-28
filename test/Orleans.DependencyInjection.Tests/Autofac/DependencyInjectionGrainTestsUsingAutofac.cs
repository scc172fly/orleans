using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DependencyInjection.Tests.Autofac
{
    // Autofac 尚不支持键控服务
#if false
    /// <summary>
    /// 测试使用 Autofac 作为 Orleans Grain 的 DI 容器时的依赖注入功能。
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
            // 配置为使用 Autofac 作为 DI 容器
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
    /// 测试配置为使用 Autofac 作为 DI 容器时，Orleans Silo 能否成功启动。
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

            // 配置为使用 Autofac 作为 DI 容器
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
