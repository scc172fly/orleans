#nullable enable

using System;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// 执行不执行 <see cref="IProbeHealthMonitor"/> 与外部使用
/// 故障检测系统。
/// </summary>
/// <remarks>
/// <para>
/// 使用外部成员资格制度,提供自己的故障检测
/// 登记这一实施情况(如外部协商一致检测系统)
/// 禁用 Orleans检查健康检查 <see cref="LocalSiloHealthMonitor"/>.
/// </para>
/// <para>
/// 自外部系统以来,这一执行总是报告健康(0个核心)
/// 通过其自己的程序处理探测/心脏跳动监测。
/// </para>
/// <para>
/// 为了更复杂的一体化,外部系统可以提供自己的
/// 向健康信息表面显示的<see cref="IProbeHealthMonitor"/> 执行
/// 无法检测出故障
/// </para>
/// </remarks>
internal sealed class NoOpProbeHealthMonitor : IProbeHealthMonitor
{
    /// <inheritdoc/>
    public int CheckReceivedProbeRequests(DateTime now, int activeNodeCount, out string? complaint)
    {
        complaint = null;
        return 0;
    }

    /// <inheritdoc/>
    public int CheckReceivedProbeResponses(DateTime now, int monitoredNodeCount, out string? complaint)
    {
        complaint = null;
        return 0;
    }
}
