using System;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

namespace Orleans.TestingHost;

/// <summary>
/// 测试集群的配置选项。
/// </summary>
public sealed class InProcessTestClusterOptions
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
    internal int BaseSiloPort { get; set; }

    /// <summary>
    /// 获取或设置网关起始端口，即首个 Silo 的网关端口。其他 Silo 使用后续端口。
    /// </summary>
    /// <value>基础网关端口。</value>
    internal int BaseGatewayPort { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示是否使用测试集群成员资格机制。
    /// </summary>
    /// <value><see langword="true" /> 表示应使用测试集群成员资格机制；否则为 <see langword="false" />。</value>
    public bool UseTestClusterMembership { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示是否使用测试集群 Grain 目录，仅当 <see cref="UseTestClusterMembership"/> 为 <see langword="true"/> 时适用。
    /// </summary>
    /// <value><see langword="true" /> 表示应使用测试集群 Grain 目录；否则为 <see langword="false" />。</value>
    internal bool UseTestClusterGrainDirectory { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示应用 Silo 配置委托后是否选择分布式 Grain 目录。
    /// </summary>
    /// <value>
    /// <see langword="true"/> 表示选择分布式目录作为默认目录；
    /// <see langword="false"/> 表示使用适用的测试目录替代项或配置的 Silo 目录。
    /// 默认值为 <see langword="false"/>。
    /// </value>
    /// <remarks>
    /// 当测试成员资格和测试 Grain 目录均已启用时，<see langword="false"/> 选择进程内测试目录。
    /// 否则，<see langword="false"/> 保留 Silo 配置所选的目录，包括运行时默认目录。
    /// 若要选择旧版目录，请调用 <see cref="CoreHostingExtensions.UseLegacyGrainDirectory"/>，
    /// 调用位置为 <see cref="InProcessTestClusterBuilder.ConfigureSilo"/>，并将此选项保持为 <see langword="false"/>。
    /// </remarks>
    public bool UseDistributedGrainDirectory { get; set; }

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
    /// 获取 Silo 宿主配置委托。
    /// </summary>
    /// <value>Silo 宿主配置委托。</value>
    public List<Action<InProcessTestSiloSpecificOptions, IHostApplicationBuilder>> SiloHostConfigurationDelegates { get; } = [];

    /// <summary>
    /// 获取客户端宿主配置委托。
    /// </summary>
    /// <value>客户端宿主配置委托。</value>
    public List<Action<IHostApplicationBuilder>> ClientHostConfigurationDelegates { get; } = [];
}
