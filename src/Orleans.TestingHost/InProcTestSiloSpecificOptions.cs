namespace Orleans.TestingHost;

/// <summary>
/// 单个配置的替换 silos。
/// </summary>
public sealed class InProcessTestSiloSpecificOptions
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
    /// 创建 <see cref="TestSiloSpecificOptions"/> 类实例。
    /// </summary>
    /// <param name="testCluster">测试组。</param>
    /// <param name="testClusterOptions">测试组群选项。</param>
    /// <param name="instanceNumber">实例编号。</param>
    /// <param name="assignNewPort">设定为 <see langword="true" />,为 silo.</param>
    /// <returns>选项。</returns>
    public static InProcessTestSiloSpecificOptions Create(InProcessTestCluster testCluster, InProcessTestClusterOptions testClusterOptions, int instanceNumber, bool assignNewPort = false)
    {
        var result = new InProcessTestSiloSpecificOptions
        {
            SiloName = $"Silo_{instanceNumber}",
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
}
