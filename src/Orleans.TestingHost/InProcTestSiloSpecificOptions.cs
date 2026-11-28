namespace Orleans.TestingHost;

/// <summary>
/// 单个 Silo 的配置覆盖选项。
/// </summary>
public sealed class InProcessTestSiloSpecificOptions
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
    /// 创建 <see cref="TestSiloSpecificOptions"/> 类的实例。
    /// </summary>
    /// <param name="testCluster">测试集群。</param>
    /// <param name="testClusterOptions">测试集群选项。</param>
    /// <param name="instanceNumber">实例编号。</param>
    /// <param name="assignNewPort">设置为 <see langword="true" /> 时，为 Silo 分配新端口。</param>
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
