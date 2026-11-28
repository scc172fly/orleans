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
    /// Orleans TestCluster 功能的测试。
    /// 
    /// TestCluster 是 Orleans 的主要测试基础设施组件，提供用于集成测试的内存中
    /// 集群环境。开发者可以通过它：
    /// - 在单个进程内启动多个 Silo
    /// - 测试 Grain 交互与集群行为
    /// - 不依赖外部组件验证分布式系统场景
    /// - 配置和自定义测试环境
    /// 
    /// 这些测试验证 TestCluster 的初始化、配置和生命周期管理。
    /// 
    /// T0–T9 各类分别独立测试集群初始化，以确保
    /// 测试之间不存在静态状态干扰。
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
    /// TestCluster 配置器功能的测试。
    /// 验证不同类型的配置器（Host、Client、ClientHost）
    /// 是否在集群初始化时正确调用。
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
        /// 仅配置宿主的测试配置器。
        /// 用于验证仅配置宿主的场景。
        /// </summary>
        private class HostConfigurator : IHostConfigurator
        {
            public void Configure(IHostBuilder hostBuilder) => _hostWasInvoked = true;
        }

        /// <summary>
        /// 同时配置宿主和客户端的测试配置器。
        /// 演示如何使用单个配置器处理
        /// 测试集群设置的这两个方面。
        /// </summary>
        private class ClientHostConfigurator : IHostConfigurator, IClientBuilderConfigurator
        {

            public void Configure(IHostBuilder hostBuilder) => _hostWasInvoked = true;
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => _clientWasInvoked = true;
        }

        /// <summary>
        /// 仅配置客户端的测试配置器。
        /// 用于验证仅配置客户端的场景。
        /// </summary>
        private class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => _clientWasInvoked = true;
        }
    }

    /// <summary>
    /// TestCluster 功能的主要测试类。
    /// 实现 IAsyncLifetime，以正确管理集群生命周期，
    /// 并确保测试完成后进行清理。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("TestingHost")]
    public class TestClusterTests : IAsyncLifetime
    {
        private TestCluster? _testCluster;

        /// <summary>
        /// 测试 TestCluster 的基本初始化与 Grain 通信。
        /// 验证以下情况：
        /// - 可以创建包含多个 Silo 的集群
        /// - 可以激活和调用 Grain
        /// - 可以在 Grain 中设置和读取状态
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
        /// 使用旧版配置方式测试 TestCluster 初始化。
        /// 演示如何使用 ISiloConfigurator，以向后兼容
        /// 旧的测试配置模式。
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
        /// 添加内存 Grain 存储的 Silo 配置器示例。
        /// 演示如何在测试中通过 ISiloConfigurator 接口
        /// 自定义 Silo 配置。
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
