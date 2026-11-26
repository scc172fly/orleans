using System;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

namespace Orleans.TestingHost;

/// <summary>
/// 测试组群配置选项 。
/// </summary>
public sealed class InProcessTestClusterOptions
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
    internal int BaseSiloPort { get; set; }

    /// <summary>
    /// 获取或设置基端网关,这是第一个 silo的网关。其他 silos 将使用随后的港口。
    /// </summary>
    /// <value>基端网关端口。</value>
    internal int BaseGatewayPort { get; set; }

    /// <summary>
    /// 获取或设定一个数值,显示是否使用测试组群成员资格。
    /// </summary>
    /// <value><see langword="true" /> 如果采用测试组群成员制;否则, <see langword="false" />.</value>
    public bool UseTestClusterMembership { get; set; }

    /// <summary>
    /// 获取或设定一个数值,以显示是否使用测试组 grain 目录,该数值只有在 <see cref="UseTestClusterMembership"/> 是 <see langword="true"/>时才适用。
    /// </summary>
    /// <value><see langword="true" /> 如果使用测试组 grain 目录;否则, <see langword="false" />。</value>
    internal bool UseTestClusterGrainDirectory { get; set; }

    /// <summary>
    /// 获取或设置一个值,以显示是否选择分布的 grain 应用后目录 silo 组合代表。
    /// </summary>
    /// <value>
    /// <see langword="true"/> 选择分布式目录为默认值;
    /// <see langword="false"/> 使用适用的测试指令替换或配置 silo 目录。
    /// 默认为 <see langword="false"/>。
    /// </value>
    /// <remarks>
    /// 随着测试会籍和测试 grain 目录启用, <see langword="false"/> 选择了在程序中的测试目录。
    /// 否则, <see langword="false"/> 将保存由 silo 配置选定的目录,包括运行时默认。
    /// 要选择遗留目录,请调用 <see cref="CoreHostingExtensions.UseLegacyGrainDirectory"/>
    /// 从 <see cref="InProcessTestClusterBuilder.ConfigureSilo"/> 中退出, 并离开此选项 <see langword="false"/>。
    /// </remarks>
    public bool UseDistributedGrainDirectory { get; set; }

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
    /// 得到 silo 东道国配置代表。
    /// </summary>
    /// <value>下 silo 东道国配置代表。</value>
    public List<Action<InProcessTestSiloSpecificOptions, IHostApplicationBuilder>> SiloHostConfigurationDelegates { get; } = [];

    /// <summary>
    /// 获取客户主机配置代表。
    /// </summary>
    /// <value>客户主机配置代表。</value>
    public List<Action<IHostApplicationBuilder>> ClientHostConfigurationDelegates { get; } = [];
}
