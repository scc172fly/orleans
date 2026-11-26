using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Runtime;
using Orleans.TestingHost.Tests.Grains;
using TestExtensions;
using Xunit;

namespace Orleans.TestingHost.Tests
{
    /// <summary>
    /// Orleans TestCluster 功能测试。
    /// 
    /// TestCluster 是 Orleans' 初级测试基础设施组件,提供模拟
    /// 集成测试的组群环境。 它允许开发者:
    /// - 在单一个程序中组合多个 silos
    /// - 测试 grain 相互作用和集群行为
    /// - 核查分布式系统假设情况,不依赖外部
    /// - 配置测试环境并定制
    /// 
    /// 这些测试核实了 TestCluster 初始化、配置和生命周期管理。
    /// 
    /// 每项T0-T9级测试的分类集启动,单独进行,以确保
    /// 试验之间没有静态状态干扰。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T0
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
            var primary = Assert.IsType<InProcessSiloHandle>(testCluster.Primary);
            Assert.Equal("TestClusterFatalErrorHandler", primary.ServiceProvider.GetRequiredService<IFatalErrorHandler>().GetType().Name);
        }

        [Fact, TestCategory("Functional")]
        public async Task FatalErrorStopsSiloWithoutTerminatingTestHost()
        {
            var builder = new TestClusterBuilder(1);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();
            await testCluster.DeployAsync(TestContext.Current.CancellationToken);

            var primary = Assert.IsType<InProcessSiloHandle>(testCluster.Primary);
            var applicationLifetime = primary.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = applicationLifetime.ApplicationStopped.Register(stopped.SetResult);

            primary.ServiceProvider.GetRequiredService<IFatalErrorHandler>().OnFatalException(context: "Test");

            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T1
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T2
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T3
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
            await testCluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T4
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
            await testCluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T5
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
            await testCluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T6
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
            await testCluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T7
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
            await testCluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T8
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
            await testCluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T9
    {
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            await using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
            await testCluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// TestCluster 配置功能测试。
    /// 证明不同类型的配置人员(主机、客户、 ClientHost)
    /// 在集束装置初始化过程中适当援引。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class T10
    {
        private static bool _hostWasInvoked;
        private static bool _clientWasInvoked;

        [Fact, TestCategory("Functional")]
        public async Task ClientBuilder_HostConfigurator() => await Test<HostConfigurator>(true, false);

        [Fact, TestCategory("Functional")]
        public async Task ClientBuilder_ClientHostConfigurator() => await Test<ClientHostConfigurator>(true, true);

        [Fact, TestCategory("Functional")]
        public async Task ClientBuilder_ClientConfigurator() => await Test<ClientConfigurator>(false, true);

        private static async Task Test<TConfigurator>(bool hostInvoked, bool clientInvoked)
            where TConfigurator : new()
        {
            _hostWasInvoked = false;
            _clientWasInvoked = false;

            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.AddClientBuilderConfigurator<TConfigurator>();
            using var testCluster = builder.Build();

            await testCluster.DeployAsync(TestContext.Current.CancellationToken);
            await testCluster.StopAllSilosAsync(TestContext.Current.CancellationToken);

            _hostWasInvoked.Should().Be(hostInvoked);
            _clientWasInvoked.Should().Be(clientInvoked);
        }

        /// <summary>
        /// 只配置主机的测试配置器 。
        /// 用于校验只为主机配置的设置方案 。
        /// </summary>
        private class HostConfigurator : IHostConfigurator
        {
            public void Configure(IHostBuilder hostBuilder) => _hostWasInvoked = true;
        }

        /// <summary>
        /// 测试配置主机和客户端的配置器。
        /// 演示单个配置器如何处理这两个方面
        /// 测试组群设置。
        /// </summary>
        private class ClientHostConfigurator : IHostConfigurator, IClientBuilderConfigurator
        {

            public void Configure(IHostBuilder hostBuilder) => _hostWasInvoked = true;
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => _clientWasInvoked = true;
        }

        /// <summary>
        /// 仅配置客户端的测试配置器 。
        /// 用于校验客户专用配置方案 。
        /// </summary>
        private class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => _clientWasInvoked = true;
        }
    }

    /// <summary>
    /// TestCluster 功能的主测试类。
    /// 实施 IAsyncLifetime ,以妥善管理集群生命周期
    /// 并确保在测试完成后进行清理。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class TestClusterTests : IAsyncLifetime
    {
        private TestCluster? _testCluster;

        /// <summary>
        /// 测试基本 TestCluster 初始化和 grain 通信。
        /// 证明:
        /// - 能够创建多 silos 集集
        /// - Grains 可以被激活和调用
        /// - 国家可以设置,从 grains中检索
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task CanInitialize()
        {
            var builder = new TestClusterBuilder(2);
            builder.Options.ServiceId = Guid.NewGuid().ToString();
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            _testCluster = builder.Build();

            await _testCluster.DeployAsync(TestContext.Current.CancellationToken);

            var grain = _testCluster.Client.GetGrain<ISimpleGrain>(1);

            await grain.SetA(2);
            Assert.Equal(2, await grain.GetA());
        }

        /// <summary>
        /// 使用遗留配置方法测试 TestCluster 初始化。
        /// 演示如何使用 ISiloConfigurator 来反向兼容
        /// 具有旧的测试配置模式。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task CanInitializeWithLegacyConfiguration()
        {
            var builder = new TestClusterBuilder(2);
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            _testCluster = builder.Build();

            await _testCluster.DeployAsync(TestContext.Current.CancellationToken);

            var grain = _testCluster.Client.GetGrain<ISimpleGrain>(1);

            await grain.SetA(2);
            Assert.Equal(2, await grain.GetA());
        }

        /// <summary>
        /// 示例 silo 添加内存的配置器 grain 储存。
        /// 演示如何在测试中自定义 silo 配置
        /// 使用 ISiloConfigurator 接口。
        /// </summary>
        public class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.AddMemoryGrainStorageAsDefault();
            }
        }

        public ValueTask InitializeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (_testCluster is not null)
            {
                await _testCluster.DisposeAsync();
            }
        }
    }
}
