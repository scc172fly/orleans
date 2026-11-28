using System.Collections.Generic;
using System.Net;
using Orleans.Configuration;
using Orleans.Runtime;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 测试集群的配置选项。
    /// </summary>
    public class TestClusterOptions
    {
        /// <summary>
        /// 获取或设置集群标识符。
        /// </summary>
        /// <seealso cref="ClusterOptions.ClusterId"/>
        /// <value>集群标识符。</value>
        public string ClusterId { get; set; } = null!;

        /// <summary>
        /// 获取或设置服务标识符。
        /// </summary>
        /// <seealso cref="ClusterOptions.ServiceId"/>
        /// <value>服务标识符。</value>
        public string ServiceId { get; set; } = null!;

        /// <summary>
        /// 获取或设置基础 Silo 端口，即第一个 Silo 使用的端口。其他 Silo 将使用后续端口。
        /// </summary>
        /// <value>基础 Silo 端口。</value>
        public int BaseSiloPort { get; set; }

        /// <summary>
        /// 获取或设置网关起始端口，即首个 Silo 的网关端口。其他 Silo 使用后续端口。
        /// </summary>
        /// <value>基础网关端口。</value>
        public int BaseGatewayPort { get; set; }

        /// <summary>
        /// 获取或设置一个值，指示是否使用测试集群成员资格机制。
        /// </summary>
        /// <value><see langword="true" /> 表示应使用测试集群成员资格机制；否则为 <see langword="false" />。</value>
        public bool UseTestClusterMembership { get; set; }

        /// <summary>
        /// 获取或设置一个值，指示是否使用真实环境统计信息。
        /// </summary>
        public bool UseRealEnvironmentStatistics { get; set; }

        /// <summary>
        /// 获取或设置一个值，指示部署时是否立即初始化客户端。
        /// </summary>
        /// <value><see langword="true" /> 表示部署时应立即初始化客户端；否则为 <see langword="false" />。</value>
        public bool InitializeClientOnDeploy { get; set; }

        /// <summary>
        /// 获取或设置初始 Silo 数量。
        /// </summary>
        /// <value>初始 Silo 数量。</value>
        public short InitialSilosCount { get; set; }

        /// <summary>
        /// 获取或设置应用程序基础目录。
        /// </summary>
        /// <value>应用程序基础目录。</value>
        public string? ApplicationBaseDirectory { get; set; }

        /// <summary>
        /// 获取或设置一个值，指示是否配置文件日志记录。
        /// </summary>
        /// <value><see langword="true" /> 表示应配置文件日志记录；否则为 <see langword="false" />。</value>
        public bool ConfigureFileLogging { get; set; } = true;

        /// <summary>
        /// 获取或设置一个值，指示测试时是否假定所有 Silo 同构。
        /// </summary>
        /// <value><see langword="true" /> 表示集群应假定 Silo 同构；否则为 <see langword="false" />。</value>
        public bool AssumeHomogenousSilosForTesting { get; set; }

        /// <summary>
        /// 获取或设置一个值，指示是否每个 Silo 都应承载网关。
        /// </summary>
        /// <value><see langword="true" /> 表示每个 Silo 均应承载网关；否则为 <see langword="false" />。</value>
        public bool GatewayPerSilo { get; set; } = true;

        /// <summary>
        /// 获取 Silo 构建器配置器类型。
        /// </summary>
        /// <value>Silo 构建器配置器类型。</value>
        public List<string?> SiloBuilderConfiguratorTypes { get; } = new List<string?>();

        /// <summary>
        /// 获取客户端构建器配置器类型。
        /// </summary>
        /// <value>客户端构建器配置器类型。</value>
        public List<string?> ClientBuilderConfiguratorTypes { get; } = new List<string?>();

        /// <summary>
        /// 获取或设置用于连接 Silo 与客户端的传输机制。
        /// </summary>
        /// <remarks>
        /// 默认为内存传输。
        /// </remarks>
        public ConnectionTransportType ConnectionTransport { get; set; } = ConnectionTransportType.InMemory;

        /// <summary>
        /// 将这些选项转换为字典。
        /// </summary>
        /// <returns>选项字典。</returns>
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
    /// 单个 Silo 的配置覆盖选项。
    /// </summary>
    public class TestSiloSpecificOptions
    {
        /// <summary>
        /// 获取或设置 Silo 端口。
        /// </summary>
        /// <value>Silo 端口。</value>
        public int SiloPort { get; set; }

        /// <summary>
        /// 获取或设置网关端口。
        /// </summary>
        /// <value>网关端口。</value>
        public int GatewayPort { get; set; }

        /// <summary>
        /// 获取或设置 Silo 名称。
        /// </summary>
        /// <value>Silo 名称。</value>
        public string SiloName { get; set; } = null!;

        /// <summary>
        /// 获取或设置主 Silo 端口。
        /// </summary>
        /// <value>主 Silo 端口。</value>
        public IPEndPoint? PrimarySiloEndPoint { get; set; }

        /// <summary>
        /// 创建 <see cref="TestSiloSpecificOptions"/> 类的实例。
        /// </summary>
        /// <param name="testCluster">测试集群。</param>
        /// <param name="testClusterOptions">测试集群选项。</param>
        /// <param name="instanceNumber">实例编号。</param>
        /// <param name="assignNewPort">设置为 <see langword="true" /> 时，为 Silo 分配新端口。</param>
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
        /// 将这些选项转换为字典。
        /// </summary>
        /// <returns>选项字典。</returns>
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
    /// 描述一种传输方式。
    /// </summary>
    public enum ConnectionTransportType
    {
        /// <summary>
        /// 使用真实的 TCP 套接字。
        /// </summary>
        TcpSocket = 0,

        /// <summary>
        /// 使用内存套接字。
        /// </summary>
        InMemory = 1,

        /// <summary>
        /// 使用 Unix 套接字。
        /// </summary>
        UnixSocket = 2,
    }
}
