#nullable enable

using System.Collections.Generic;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// 负责监控集群中各 Silo 的健康状况并检测故障。
/// </summary>
/// <remarks>
/// <para>
/// Orleans 支持两种故障检测模式：
/// </para>
/// <list type="number">
///   <item>
///     <description>
/// <b>内置的基于探测的检测</b>（默认）：Orleans 中的 Silo 主动相互探测，
/// 采用扩展图拓扑。如果某个 Silo 错过太多次探测，则被怀疑发生故障，
/// 并最终被宣告死亡。这由 <see cref="ClusterHealthMonitor"/> 实现。
///     </description>
///   </item>
///   <item>
///     <description>
/// <b>外部故障检测</b>：外部成员资格系统
/// 通过自身协议进行故障检测。此时注册一个空操作实现，
/// 禁用 Orleans 内置探测，同时允许外部系统
/// 通过 <see cref="IMembershipManager"/> 管理成员资格变更。
///     </description>
///   </item>
/// </list>
/// <para>
/// 使用外部故障检测时，存活性检测仍处于启用状态，只不过由
/// 外部系统而非 Orleans 的探测机制处理。这不同于
/// <c>ClusterMembershipOptions.LivenessEnabled = false</c>，后者是一种临时解决方法，
/// 它禁用了 DeclareDead 步骤，却未正确禁用探测基础设施。
/// </para>
/// </remarks>
internal interface IClusterHealthMonitor : ILifecycleParticipant<ISiloLifecycle>, IHealthCheckParticipant
{
    /// <summary>
    /// 获取当前 Silo 正在监控的 Silo 集合。
    /// </summary>
    /// <remarks>
    /// 对于外部故障检测系统，此属性返回空字典，因为
    /// 监控工作由外部系统处理。
    /// </remarks>
    IReadOnlyDictionary<SiloAddress, SiloHealthMonitor> SiloMonitors { get; }
}
