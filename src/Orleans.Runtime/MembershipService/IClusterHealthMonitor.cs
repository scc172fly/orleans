#nullable enable

using System.Collections.Generic;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// 负责监测组群 silos 的健康状况和发现故障。
/// </summary>
/// <remarks>
/// <para>
/// Orleans 支持两种故障检测模型:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       <b>内置探测检测</b> (默认): Orleans silos 积极相互探
///       使用扩展图图形表层的图示。 当 silo 错过太多探测器时, 它会被怀疑
///       最终宣布死亡。这是由 <see cref="ClusterHealthMonitor"/>执行的。
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>外部故障检测</b>:外部成员资格制度
///       使用自己的程序处理失败检测。 在这种情况下,请登记不操作
///       启用以禁用 Orleans'内置测试,同时允许外部系统
///       管理成员变化,通过 <see cref="IMembershipManager"/>。
///     </description>
///   </item>
/// </list>
/// <para>
/// 使用外部故障检测时, 活性仍然被启用 - 它只是由
/// 外部系统而不是 Orleans的探测机制。
/// <c>ClusterMembershipOptions.LivenessEnabled = false</c>,这是一种变通办法
/// 使 DeclareDead 步位无法正常地使测试基础设施失效。
/// </para>
/// </remarks>
internal interface IClusterHealthMonitor : ILifecycleParticipant<ISiloLifecycle>, IHealthCheckParticipant
{
    /// <summary>
    /// 收集 silos 目前正以此监测 silo.
    /// </summary>
    /// <remarks>
    /// 对于外部故障检测系统, 外部故障检测系统返回空字典, 因为
    /// 监测工作由外部处理。
    /// </remarks>
    IReadOnlyDictionary<SiloAddress, SiloHealthMonitor> SiloMonitors { get; }
}
