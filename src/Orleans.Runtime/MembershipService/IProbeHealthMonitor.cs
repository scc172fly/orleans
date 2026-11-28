#nullable enable

using System;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// 根据探测和 ping 活动提供健康信息，用于监控本地 Silo 的健康状况。
/// </summary>
/// <remarks>
/// <para>
/// 此接口抽象 <see cref="LocalSiloHealthMonitor"/> 使用的探测相关健康检查，
/// 以便根据探测活动确定本地 Silo 是否健康。
/// </para>
/// <para>
/// Orleans 支持两种实现：
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
/// <b>默认探测监控</b>：使用 Orleans 内置 ping 协议跟踪
/// 收到的探测请求及响应，并配合使用标准的
///       <see cref="ClusterHealthMonitor"/>.
///     </description>
///   </item>
///   <item>
///     <description>
/// <b>外部故障检测</b>：使用外部成员资格系统时，若该系统
/// 提供自己的故障检测协议，
/// 应注册空操作实现或自定义实现。外部系统的
/// 探测及心跳活动可通过此接口公开。
///     </description>
///   </item>
/// </list>
/// </remarks>
internal interface IProbeHealthMonitor
{
    /// <summary>
    /// 根据接收的探测请求获取健康退化评分。
    /// </summary>
    /// <param name="now">当前时间。</param>
    /// <param name="activeNodeCount">集群中的活动节点数。</param>
    /// <param name="complaint">若返回非零评分，则为问题描述。</param>
    /// <returns>
    /// 健康时返回 0，否则返回表示退化程度的正整数。
    /// 值越大表示退化越严重。
    /// </returns>
    int CheckReceivedProbeRequests(DateTime now, int activeNodeCount, out string? complaint);

    /// <summary>
    /// 根据接收的探测响应获取健康退化评分。
    /// </summary>
    /// <param name="now">当前时间。</param>
    /// <param name="monitoredNodeCount">此 Silo 正在监控的节点数量。</param>
    /// <param name="complaint">若返回非零评分，则为问题描述。</param>
    /// <returns>
    /// 健康时返回 0，否则返回表示退化程度的正整数。
    /// 值越大表示退化越严重。
    /// </returns>
    int CheckReceivedProbeResponses(DateTime now, int monitoredNodeCount, out string? complaint);
}
