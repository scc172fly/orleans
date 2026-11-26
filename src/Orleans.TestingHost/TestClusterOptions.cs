using System.Collections.Generic;
using System.Net;
using Orleans.Configuration;
using Orleans.Runtime;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 测试组群配置选项 。
    /// </summary>
    public class TestClusterOptions
    {
        /// <summary>
        /// 获取或设置集束识别码。
        /// </summary>
        /// <seealso cref="ClusterOptions.ClusterId"/>
        /// <value>集束识别符号。</value>
        public string ClusterId { get; set; } = null!;

        /// <summary>
        /// 获取或设置服务标识符。
        /// </summary>
        /// <seealso cref="ClusterOptions.ServiceId"/>
        /// <value>服务标识符。</value>
        public string ServiceId { get; set; } = null!;

        /// <summary>
        /// 获取或设置基 silo 端口,这是第一个 silo的端口。其他 silos 将使用随后的端口。
        /// </summary>
        /// <value>基地 silo 端口。</value>
        public int BaseSiloPort { get; set; }

        /// <summary>
        /// 获取或设置基端网关,这是第一个 silo的网关。其他 silos 将使用随后的港口。
        /// </summary>
        /// <value>基端网关端口。</value>
        public int BaseGatewayPort { get; set; }

        /// <summary>
        /// 获取或设定一个数值,显示是否使用测试组群成员资格。
        /// </summary>
        /// <value><see langword="true" /> 如果采用测试组群成员制;否则, <see langword="false" />.</value>
        public bool UseTestClusterMembership { get; set; }

        /// <summary>
        /// 获取或设定一个数值,以显示是否使用实际环境统计数据。
        /// </summary>
        public bool UseRealEnvironmentStatistics { get; set; }

        /// <summary>
        /// 获取或设定一个值,以显示是否在部署后立即初始化客户。
        /// </summary>
        /// <value><see langword="true" /> ,如果客户部署后应立即启动;否则, <see langword="false" />。</value>
        public bool InitializeClientOnDeploy { get; set; }

        /// <summary>
        /// 获取或设置初始 silos 数。
        /// </summary>
        /// <value>初始 silos 计数。</value>
        public short InitialSilosCount { get; set; }

        /// <summary>
        /// 获取或设置应用程序基准目录。
        /// </summary>
        /// <value>应用程序基准目录。</value>
        public string? ApplicationBaseDirectory { get; set; }

        /// <summary>
        /// 获取或设置一个值,以显示是否配置文件记录。
        /// </summary>
        /// <value><see langword="true" /> 如果文件记录应该配置; 否则, <see langword="false" />。</value>
        public bool ConfigureFileLogging { get; set; } = true;

        /// <summary>
        /// 获取或设定一个数值,表明是否为测试目的假设同质 silos 。
        /// </summary>
        /// <value><see langword="true" /> 如果组群应假定同质 silos;否则, <see langword="false" />。</value>
        public bool AssumeHomogenousSilosForTesting { get; set; }

        /// <summary>
        /// 获取或设置一个值,以显示每个 silo 是否应该主持网关。
        /// </summary>
        /// <value><see langword="true" /> ,如果每个 silo 都设有网关;否则, <see langword="false" />。</value>
        public bool GatewayPerSilo { get; set; } = true;

        /// <summary>
        /// 获取 silo 构建器配置类型。
        /// </summary>
        /// <value>下 silo 建筑配置类型。</value>
        public List<string?> SiloBuilderConfiguratorTypes { get; } = new List<string?>();

        /// <summary>
        /// 获取客户端构建器配置类型 。
        /// </summary>
        /// <value>客户端构建器配置类型。</value>
        public List<string?> ClientBuilderConfiguratorTypes { get; } = new List<string?>();

        /// <summary>
        /// 获取或设定一个值,以显示连接 silos 和客户的运输方式。
        /// </summary>
        /// <remarks>
        /// 默认为在模拟传输中。
        /// </remarks>
        public ConnectionTransportType ConnectionTransport { get; set; } = ConnectionTransportType.InMemory;

        /// <summary>
        /// 将这些选项转换成字典。
        /// </summary>
        /// <returns>选项词典。</returns>
        public Dictionary<string, string?> ToDictionary()
        {
            var result = new Dictionary<string, string?>
            {
                [$"Orleans:{nameof(ClusterId)}"] = this.ClusterId,
                [$"Orleans:{nameof(ServiceId)}"] = this.ServiceId,
                [nameof(BaseSiloPort)] = this.BaseSiloPort.ToString(),
                [nameof(BaseGatewayPort)] = this.BaseGatewayPort.ToString(),
                [nameof(UseTestClusterMembership)] = this.UseTestClusterMembership.ToString(),
                [nameof(UseRealEnvironmentStatistics)] = this.UseRealEnvironmentStatistics.ToString(),
                [nameof(InitializeClientOnDeploy)] = this.InitializeClientOnDeploy.ToString(),
                [nameof(InitialSilosCount)] = this.InitialSilosCount.ToString(),
                [nameof(ApplicationBaseDirectory)] = this.ApplicationBaseDirectory,
                [nameof(ConfigureFileLogging)] = this.ConfigureFileLogging.ToString(),
                [nameof(AssumeHomogenousSilosForTesting)] = this.AssumeHomogenousSilosForTesting.ToString(),
                [nameof(GatewayPerSilo)] = this.GatewayPerSilo.ToString(),
                [nameof(ConnectionTransport)] = this.ConnectionTransport.ToString(),
            };

            if (UseTestClusterMembership)
            {
                result["Orleans:Clustering:ProviderType"] = "Development";
            }

            result["UseRealEnvironmentStatistics"] = UseRealEnvironmentStatistics ? "True" : "False";

            if (this.SiloBuilderConfiguratorTypes != null)
            {
                for (int i = 0; i < this.SiloBuilderConfiguratorTypes.Count; i++)
                {
                    result[$"{nameof(SiloBuilderConfiguratorTypes)}:{i}"] = this.SiloBuilderConfiguratorTypes[i];
                }
            }

            if (this.ClientBuilderConfiguratorTypes != null)
            {
                for (int i = 0; i < this.ClientBuilderConfiguratorTypes.Count; i++)
                {
                    result[$"{nameof(ClientBuilderConfiguratorTypes)}:{i}"] = this.ClientBuilderConfiguratorTypes[i];
                }
            }

            return result;
        }
    }

    /// <summary>
    /// 单个配置的替换 silos。
    /// </summary>
    public class TestSiloSpecificOptions
    {
        /// <summary>
        /// 获取或设置 silo 端口。
        /// </summary>
        /// <value> silo 端口。</value>
        public int SiloPort { get; set; }

        /// <summary>
        /// 获取或设置网关端口。
        /// </summary>
        /// <value>网关端口。</value>
        public int GatewayPort { get; set; }

        /// <summary>
        /// 获取或设置 silo的名称。
        /// </summary>
        /// <value> silo的名称。</value>
        public string SiloName { get; set; } = null!;

        /// <summary>
        /// 获取或设置主 silo 端口。
        /// </summary>
        /// <value>主 silo 端口。</value>
        public IPEndPoint? PrimarySiloEndPoint { get; set; }

        /// <summary>
        /// 创建 <see cref="TestSiloSpecificOptions"/> 类实例。
        /// </summary>
        /// <param name="testCluster">测试组。</param>
        /// <param name="testClusterOptions">测试组群选项。</param>
        /// <param name="instanceNumber">实例编号。</param>
        /// <param name="assignNewPort">设定为 <see langword="true" />,为 silo.</param>
        /// <returns>选项。</returns>
        public static TestSiloSpecificOptions Create(TestCluster testCluster, TestClusterOptions testClusterOptions, int instanceNumber, bool assignNewPort = false)
        {
            var result = new TestSiloSpecificOptions
            {
                SiloName = testClusterOptions.UseTestClusterMembership && instanceNumber == 0 ? Silo.PrimarySiloName : $"Secondary_{instanceNumber}",
                PrimarySiloEndPoint = testClusterOptions.UseTestClusterMembership ? new IPEndPoint(IPAddress.Loopback, testClusterOptions.BaseSiloPort) : null,
            };

            if (assignNewPort)
            {
                var (siloPort, gatewayPort) = testCluster.PortAllocator.AllocateConsecutivePortPairs(1);
                result.SiloPort = siloPort;
                result.GatewayPort = (instanceNumber == 0 || testClusterOptions.GatewayPerSilo) ? gatewayPort : 0;
            }
            else
            {
                result.SiloPort = testClusterOptions.BaseSiloPort + instanceNumber;
                result.GatewayPort = (instanceNumber == 0 || testClusterOptions.GatewayPerSilo) ? testClusterOptions.BaseGatewayPort + instanceNumber : 0;
            }

            return result;
        }

        /// <summary>
        /// 将这些选项转换成字典。
        /// </summary>
        /// <returns>选项词典。</returns>
        public Dictionary<string, string?> ToDictionary()
        {
            var result = new Dictionary<string, string?>
            {
                [$"Orleans:Endpoints:AdvertisedIPAddress"] = IPAddress.Loopback.ToString(),
                [$"Orleans:Endpoints:{nameof(SiloPort)}"] = this.SiloPort.ToString(),
                [$"Orleans:EndPoints:{nameof(GatewayPort)}"] = this.GatewayPort.ToString(),
                ["Orleans:Name"] = this.SiloName,
            };

            if (PrimarySiloEndPoint != null)
            {
                result[$"Orleans:Clustering:{nameof(PrimarySiloEndPoint)}"] = this.PrimarySiloEndPoint.ToString();
            }

            return result;
        }
    }

    /// <summary>
    /// 描述运输方法
    /// </summary>
    public enum ConnectionTransportType
    {
        /// <summary>
        /// 使用真实的 TCP 套接字 。
        /// </summary>
        TcpSocket = 0,

        /// <summary>
        /// 内存套接字中的用途 。
        /// </summary>
        InMemory = 1,

        /// <summary>
        /// Unix套接字中的用途 。
        /// </summary>
        UnixSocket = 2,
    }
}
