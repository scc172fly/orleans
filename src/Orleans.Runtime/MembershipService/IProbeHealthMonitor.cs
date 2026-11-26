#nullable enable

using System;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// 提供基于地方 silo 健康监测探测/提取活动的卫生信息。
/// </summary>
/// <remarks>
/// <para>
/// 本界面摘要列出 <see cref="LocalSiloHealthMonitor"/>所使用的与探测有关的健康检查
/// 确定本地 silo 是否基于探测活动而健康。
/// </para>
/// <para>
/// Orleans 支持两项执行:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>默认探测器监测</b>:使用 Orleans'内置的平线协议跟踪
///       收到查询请求和答复。
///       <see cref="ClusterHealthMonitor"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>外部故障检测</b>:在使用外部成员资格制度时
///       提供自己的故障检测协议,
///       a 不操作或自定义执行应登记。
///       可以通过该接口显示探测器/心跳活动。
///     </description>
///   </item>
/// </list>
/// </remarks>
internal interface IProbeHealthMonitor
{
    /// <summary>
    /// 根据收到的检测请求 获得健康退化得分
    /// </summary>
    /// <param name="now">当前时间。</param>
    /// <param name="activeNodeCount">组中活动节点的数量。</param>
    /// <param name="complaint">如果返回非零分,请说明问题。</param>
    /// <returns>
    /// 健康得分为0,或表示降解水平的正整数。
    /// 数值较高表明退化程度更严重。
    /// </returns>
    int CheckReceivedProbeRequests(DateTime now, int activeNodeCount, out string? complaint);

    /// <summary>
    /// 根据收到的检测反应 获得健康退化得分
    /// </summary>
    /// <param name="now">当前时间。</param>
    /// <param name="monitoredNodeCount">由此 silo监测的节点数目。</param>
    /// <param name="complaint">如果返回非零分,请说明问题。</param>
    /// <returns>
    /// 健康得分为0,或表示降解水平的正整数。
    /// 数值较高表明退化程度更严重。
    /// </returns>
    int CheckReceivedProbeResponses(DateTime now, int monitoredNodeCount, out string? complaint);
}
