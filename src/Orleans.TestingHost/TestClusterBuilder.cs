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
    /// <summary>启动 <see cref="TestCluster"/>的配置构建器。</summary>
    public class TestClusterBuilder
    {
        private readonly List<Action<IConfigurationBuilder>> configureHostConfigActions = new List<Action<IConfigurationBuilder>>();
        private readonly List<Action> configureBuilderActions = new List<Action>();
        private Func<string, IConfiguration, Task<SiloHandle>>? _createSiloAsync;
        private Func<string, IConfiguration, CancellationToken, Task<SiloHandle>>? _createSiloAsyncWithCancellation;
        private TestCluster? _cluster;

        /// <summary>
        /// 使用默认选项初始化一个 <see cref="TestClusterBuilder"/> 新实例。
        /// </summary>
        public TestClusterBuilder()
            : this(2)
        {
        }

        /// <summary>
        /// 初始化一个 <see cref="TestClusterBuilder"/> 新实例,该例子将超越初始 silos 计数。
        /// </summary>
        /// <param name="initialSilosCount">初始数 silos 部署。</param>
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
        /// 获取或设置用于分配连续 silo 端口的端口分隔符。
        /// </summary>
        public ITestClusterPortAllocator PortAllocator { get; set; } = new TestClusterPortAllocator();

        /// <summary>
        /// 将提供给 silos 和由此构建器创建的客户端的配置值。
        /// </summary>
        public Dictionary<string, string?> Properties { get; } = new Dictionary<string, string?>();

        /// <summary>
        /// 获得选项。
        /// </summary>
        /// <value>选项。</value>
        public TestClusterOptions Options { get; }

        /// <summary>
        /// 获取或设置用于创建和启动个人 silo的代表。
        /// </summary>
        public Func<string, IConfiguration, Task<SiloHandle>> CreateSiloAsync
        {
            private get => _createSiloAsync!;
            set
            {
                _createSiloAsync = value;

                // 自定义建造者无法使用内装运输工具。
                Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
            }
        }

        /// <summary>
        /// 获取或设置用于创建和启动单个 silo的取消通知代表。
        /// </summary>
        public Func<string, IConfiguration, CancellationToken, Task<SiloHandle>> CreateSiloAsyncWithCancellation
        {
            private get => _createSiloAsyncWithCancellation!;
            set
            {
                _createSiloAsyncWithCancellation = value;

                // 自定义建造者无法使用内装运输工具。
                Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
            }
        }

        /// <summary>
        /// 向构建器添加配置代表到配置
        /// </summary>        
        /// <param name="configureDelegate">配置代表。</param>
        public TestClusterBuilder ConfigureBuilder(Action configureDelegate)
        {
            this.configureBuilderActions.Add(configureDelegate ?? throw new ArgumentNullException(nameof(configureDelegate)));
            return this;
        }

        /// <summary>
        /// 为构建器本身设置配置。 这将用作初始化每个 silo 主机的基础
        /// 用于构建过程中的后期使用。 这可以称为多次, 结果将是添加 。
        /// </summary>
        /// <param name="configureDelegate">用于配置将使用的 <see cref="IConfigurationBuilder"/>
        /// 要为主机构建 <see cref="IConfiguration"/> 。</param>
        /// <returns>主机构建器的链条设置的相同实例。</returns>
        public TestClusterBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
        {
            this.configureHostConfigActions.Add(configureDelegate ?? throw new ArgumentNullException(nameof(configureDelegate)));
            return this;
        }

        /// <summary>
        /// 添加 <see cref="ISiloConfigurator"/> 或 <see cref="IHostConfigurator"/> 的履行,以配置由测试组创建的 silos 。
        /// </summary>
        /// <typeparam name="T">配置类型。</typeparam>
        /// <returns>建筑工。</returns>
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
        /// 添加 <see cref="IClientBuilderConfigurator"/> 或 <see cref="IHostConfigurator"/> 的执行,以配置为测试组群创建的客户端
        /// </summary>
        /// <typeparam name="T">客户端构建器类型</typeparam>
        /// <returns>建筑工。</returns>
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
        /// <returns>TestCluster.</returns>
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

            // 由于 ClusterId 和 ServiceId 属性被嵌入配置中的`Orleans' 部分,
            // 我们把 ' 绑在一起Orleans将它们与选项捆绑在一起
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
        /// 返回该地址是否属于由此构建器建立的集。
        /// </summary>
        /// <param name="siloAddress"> silo 地址。</param>
        /// 当地址属于本组时,<returns><see langword="true"/> 。</returns>
        public bool ContainsSilo(SiloAddress siloAddress)
        {
            ArgumentNullException.ThrowIfNull(siloAddress);
            return _cluster?.ContainsSilo(siloAddress) == true;
        }

        /// <summary>
        /// 创建集束标识符。
        /// </summary>
        /// <returns>一个新的集束识别码。</returns>
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
            // 如果目前没有设置基准端口, 则设定基端端口 。
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
