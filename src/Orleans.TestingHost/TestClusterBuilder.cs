using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Runtime;

namespace Orleans.TestingHost
{
    /// <summary>用于启动 <see cref="TestCluster"/> 的配置构建器。</summary>
    public class TestClusterBuilder
    {
        private readonly List<Action<IConfigurationBuilder>> configureHostConfigActions = new List<Action<IConfigurationBuilder>>();
        private readonly List<Action> configureBuilderActions = new List<Action>();
        private Func<string, IConfiguration, Task<SiloHandle>>? _createSiloAsync;
        private Func<string, IConfiguration, CancellationToken, Task<SiloHandle>>? _createSiloAsyncWithCancellation;
        private TestCluster? _cluster;

        /// <summary>
        /// 使用默认选项初始化 <see cref="TestClusterBuilder"/> 的新实例。
        /// </summary>
        public TestClusterBuilder()
            : this(2)
        {
        }

        /// <summary>
        /// 使用覆盖后的初始 Silo 数量初始化 <see cref="TestClusterBuilder"/> 的新实例。
        /// </summary>
        /// <param name="initialSilosCount">要部署的初始 Silo 数量。</param>
        public TestClusterBuilder(short initialSilosCount)
        {
            this.Options = new TestClusterOptions
            {
                InitialSilosCount = initialSilosCount,
                ClusterId = CreateClusterId(),
                ServiceId = Guid.NewGuid().ToString("N"),
                UseTestClusterMembership = true,
                InitializeClientOnDeploy = true,
                ConfigureFileLogging = true,
                AssumeHomogenousSilosForTesting = true
            };

            AddSiloBuilderConfigurator<ConfigureDistributedGrainDirectory>();
            this.AddSiloBuilderConfigurator<ConfigureStaticClusterDeploymentOptions>();
            this.ConfigureBuilder(ConfigureDefaultPorts);
        }

        /// <summary>
        /// 获取或设置用于分配连续 Silo 端口的端口分配器。
        /// </summary>
        public ITestClusterPortAllocator PortAllocator { get; set; } = new TestClusterPortAllocator();

        /// <summary>
        /// 提供给此构建器创建的 Silo 及客户端的配置值。
        /// </summary>
        public Dictionary<string, string?> Properties { get; } = new Dictionary<string, string?>();

        /// <summary>
        /// 获取选项。
        /// </summary>
        /// <value>选项。</value>
        public TestClusterOptions Options { get; }

        /// <summary>
        /// 获取或设置用于创建并启动单个 Silo 的委托。
        /// </summary>
        public Func<string, IConfiguration, Task<SiloHandle>> CreateSiloAsync
        {
            private get => _createSiloAsync!;
            set
            {
                _createSiloAsync = value;

                // 自定义构建器无法访问内存传输机制。
                Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
            }
        }

        /// <summary>
        /// 获取或设置用于创建并启动单个 Silo 且支持取消的委托。
        /// </summary>
        public Func<string, IConfiguration, CancellationToken, Task<SiloHandle>> CreateSiloAsyncWithCancellation
        {
            private get => _createSiloAsyncWithCancellation!;
            set
            {
                _createSiloAsyncWithCancellation = value;

                // 自定义构建器无法访问内存传输机制。
                Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
            }
        }

        /// <summary>
        /// 向构建器添加配置委托。
        /// </summary>        
        /// <param name="configureDelegate">配置委托。</param>
        public TestClusterBuilder ConfigureBuilder(Action configureDelegate)
        {
            this.configureBuilderActions.Add(configureDelegate ?? throw new ArgumentNullException(nameof(configureDelegate)));
            return this;
        }

        /// <summary>
        /// 设置构建器自身的配置。此配置将作为初始化各 Silo 宿主的基础，
        /// 供后续构建过程使用。可以多次调用，结果将累积。
        /// </summary>
        /// <param name="configureDelegate">用于配置 <see cref="IConfigurationBuilder"/> 的委托，后者将用于
        /// 构造宿主的 <see cref="IConfiguration"/>。</param>
        /// <returns>用于链式调用的同一宿主构建器实例。</returns>
        public TestClusterBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
        {
            this.configureHostConfigActions.Add(configureDelegate ?? throw new ArgumentNullException(nameof(configureDelegate)));
            return this;
        }

        /// <summary>
        /// 添加 <see cref="ISiloConfigurator"/> 或 <see cref="IHostConfigurator"/> 的实现，用于配置测试集群创建的 Silo。
        /// </summary>
        /// <typeparam name="T">配置器类型。</typeparam>
        /// <returns>构建器。</returns>
        public TestClusterBuilder AddSiloBuilderConfigurator<T>() where T : new()
        {
            if (!typeof(ISiloConfigurator).IsAssignableFrom(typeof(T)) && !typeof(IHostConfigurator).IsAssignableFrom(typeof(T)))
            {
                throw new ArgumentException($"The type {typeof(T)} is not assignable to either {nameof(ISiloConfigurator)} or {nameof(IHostConfigurator)}");
            }

            this.Options.SiloBuilderConfiguratorTypes.Add(typeof(T).AssemblyQualifiedName);
            return this;
        }

        /// <summary>
        /// 添加 <see cref="IClientBuilderConfigurator"/> 或 <see cref="IHostConfigurator"/> 的实现，用于配置为测试集群创建的客户端。
        /// </summary>
        /// <typeparam name="T">客户端构建器类型。</typeparam>
        /// <returns>构建器。</returns>
        public TestClusterBuilder AddClientBuilderConfigurator<T>() where T : new()
        {
            if (!typeof(IClientBuilderConfigurator).IsAssignableFrom(typeof(T)) && !typeof(IHostConfigurator).IsAssignableFrom(typeof(T)))
            {
                throw new ArgumentException($"The type {typeof(T)} is not assignable to either {nameof(IClientBuilderConfigurator)} or {nameof(IHostConfigurator)}");
            }

            this.Options.ClientBuilderConfiguratorTypes.Add(typeof(T).AssemblyQualifiedName);
            return this;
        }

        /// <summary>
        /// 构建此实例。
        /// </summary>
        /// <returns>TestCluster。</returns>
        public TestCluster Build()
        {
            var portAllocator = this.PortAllocator;
            var configBuilder = new ConfigurationBuilder();

            foreach (var action in configureBuilderActions)
            {
                action();
            }

            configBuilder.AddInMemoryCollection(this.Properties);
            configBuilder.AddInMemoryCollection(this.Options.ToDictionary());
            foreach (var buildAction in this.configureHostConfigActions)
            {
                buildAction(configBuilder);
            }

            var configuration = configBuilder.Build();
            var finalOptions = new TestClusterOptions();
            configuration.Bind(finalOptions);

            // 由于 ClusterId 和 ServiceId 属性嵌套在配置中的 'Orleans' 节内，
            // 因此此处绑定 'Orleans' 节，以将它们绑定到选项。
            configuration.GetSection("Orleans").Bind(finalOptions);

            var configSources = new ReadOnlyCollection<IConfigurationSource>(configBuilder.Sources);
            var testCluster = _cluster = new TestCluster(finalOptions, configSources, portAllocator);
            if (_createSiloAsyncWithCancellation is not null)
            {
                testCluster.CreateSiloAsyncWithCancellation = CreateSiloAsyncWithCancellation;
            }
            else if (_createSiloAsync is not null)
            {
                testCluster.CreateSiloAsync = CreateSiloAsync;
            }

            return testCluster;
        }

        /// <summary>
        /// 返回地址是否属于此构建器构建的集群。
        /// </summary>
        /// <param name="siloAddress">Silo 地址。</param>
        /// <returns>当地址属于此集群时为 <see langword="true"/>。</returns>
        public bool ContainsSilo(SiloAddress siloAddress)
        {
            ArgumentNullException.ThrowIfNull(siloAddress);
            return _cluster?.ContainsSilo(siloAddress) == true;
        }

        /// <summary>
        /// 创建集群标识符。
        /// </summary>
        /// <returns>新的集群标识符。</returns>
        public static string CreateClusterId()
        {
            string prefix = "testcluster-";
            int randomSuffix = Random.Shared.Next(1000);
            DateTime now = DateTime.UtcNow;
            string DateTimeFormat = @"yyyy-MM-dd-HH-mm-ss";
            return $"{prefix}{now.ToString(DateTimeFormat, CultureInfo.InvariantCulture)}-{randomSuffix}";
        }

        private void ConfigureDefaultPorts()
        {
            // 如果当前未设置基础端口，则设置基础端口。
            (int baseSiloPort, int baseGatewayPort) = this.PortAllocator.AllocateConsecutivePortPairs(this.Options.InitialSilosCount + 3);
            if (this.Options.BaseSiloPort == 0) this.Options.BaseSiloPort = baseSiloPort;
            if (this.Options.BaseGatewayPort == 0) this.Options.BaseGatewayPort = baseGatewayPort;
        }

        internal class ConfigureStaticClusterDeploymentOptions : IHostConfigurator
        {
            public void Configure(IHostBuilder hostBuilder)
            {
                hostBuilder.ConfigureServices((context, services) =>
                {
                    var initialSilos = int.Parse(context.Configuration[nameof(TestClusterOptions.InitialSilosCount)]!);
                    var siloNames = Enumerable.Range(0, initialSilos).Select(GetSiloName).ToList();
                    services.Configure<StaticClusterDeploymentOptions>(options => options.SiloNames = siloNames);
                });
            }

            private static string GetSiloName(int instanceNumber)
            {
                return instanceNumber == 0 ? Silo.PrimarySiloName : $"Secondary_{instanceNumber}";
            }
        }
    }
}
