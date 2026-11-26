using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Runtime;

namespace Orleans.TestingHost;

/// <summary>启动 <see cref="InProcessTestCluster"/>的配置构建器。</summary>
public sealed class InProcessTestClusterBuilder
{
    private InProcessTestCluster? _cluster;

    /// <summary>
    /// 使用默认选项初始化一个 <see cref="InProcessTestClusterBuilder"/> 新实例。
    /// </summary>
    public InProcessTestClusterBuilder()
        : this(2)
    {
    }

    /// <summary>
    /// 初始化一个 <see cref="InProcessTestClusterBuilder"/> 新实例,该例子将超越初始 silos 计数。
    /// </summary>
    /// <param name="initialSilosCount">初始数 silos 部署。</param>
    public InProcessTestClusterBuilder(short initialSilosCount)
    {
        Options = new InProcessTestClusterOptions
        {
            InitialSilosCount = initialSilosCount,
            ClusterId = CreateClusterId(),
            ServiceId = Guid.NewGuid().ToString("N"),
            UseTestClusterMembership = true,
            UseTestClusterGrainDirectory = true,
            InitializeClientOnDeploy = true,
            ConfigureFileLogging = true,
            AssumeHomogenousSilosForTesting = true
        };
    }

    /// <summary>
    /// 获得选项。
    /// </summary>
    /// <value>选项。</value>
    public InProcessTestClusterOptions Options { get; }

    /// <summary>
    /// 港口分配器
    /// </summary>
    public ITestClusterPortAllocator PortAllocator { get; } = new TestClusterPortAllocator();

    /// <summary>
    /// 添加一个用于配置 silo 和客户端主机的代表。
    /// </summary>
    public InProcessTestClusterBuilder ConfigureHost(Action<IHostApplicationBuilder> configureDelegate)
    {
        Options.SiloHostConfigurationDelegates.Add((_, hostBuilder) => configureDelegate(hostBuilder));
        Options.ClientHostConfigurationDelegates.Add(configureDelegate);
        return this;
    }

    /// <summary>
    /// 添加一个代表配置 silos。
    /// </summary>
    /// <returns>建筑工。</returns>
    public InProcessTestClusterBuilder ConfigureSilo(Action<InProcessTestSiloSpecificOptions, ISiloBuilder> configureSiloDelegate)
    {
        Options.SiloHostConfigurationDelegates.Add((options, hostBuilder) => hostBuilder.UseOrleans(siloBuilder => configureSiloDelegate(options, siloBuilder)));
        return this;
    }

    /// <summary>
    /// 添加一个代表来配置 silo 主机。
    /// </summary>
    /// <returns>建筑工。</returns>
    public InProcessTestClusterBuilder ConfigureSiloHost(Action<InProcessTestSiloSpecificOptions, IHostApplicationBuilder> configureSiloHostDelegate)
    {
        Options.SiloHostConfigurationDelegates.Add(configureSiloHostDelegate);
        return this;
    }

    /// <summary>
    /// 添加一个代表配置客户端 。
    /// </summary>
    /// <returns>建筑工。</returns>
    public InProcessTestClusterBuilder ConfigureClient(Action<IClientBuilder> configureClientDelegate)
    {
        Options.ClientHostConfigurationDelegates.Add(hostBuilder => hostBuilder.UseOrleansClient(clientBuilder => configureClientDelegate(clientBuilder)));
        return this;
    }

    /// <summary>
    /// 添加代表来配置客户端主机 。
    /// </summary>
    /// <returns>建筑工。</returns>
    public InProcessTestClusterBuilder ConfigureClientHost(Action<IHostApplicationBuilder> configureHostDelegate)
    {
        Options.ClientHostConfigurationDelegates.Add(hostBuilder => configureHostDelegate(hostBuilder));
        return this;
    }

    /// <summary>
    /// 构建此实例。
    /// </summary>
    /// <returns>InProcessTestCluster.</returns>
    public InProcessTestCluster Build()
    {
        var portAllocator = PortAllocator;

        ConfigureDefaultPorts();

        return _cluster = new InProcessTestCluster(Options, portAllocator);
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
        string DateTimeFormat = @"yyyy-MM-dd\tHH-mm-ss";
        return $"{prefix}{now.ToString(DateTimeFormat, CultureInfo.InvariantCulture)}-{randomSuffix}";
    }

    private void ConfigureDefaultPorts()
    {
        // 如果目前没有设置基准端口, 则设定基端端口 。
        (int baseSiloPort, int baseGatewayPort) = PortAllocator.AllocateConsecutivePortPairs(Options.InitialSilosCount + 3);
        if (Options.BaseSiloPort == 0) Options.BaseSiloPort = baseSiloPort;
        if (Options.BaseGatewayPort == 0) Options.BaseGatewayPort = baseGatewayPort;
    }

    internal class ConfigureStaticClusterDeploymentOptions : IHostConfigurator
    {
        public void Configure(IHostBuilder hostBuilder)
        {
            hostBuilder.ConfigureServices((context, services) =>
            {
                var initialSilos = int.Parse(context.Configuration[nameof(InProcessTestClusterOptions.InitialSilosCount)]!);
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
