#nullable enable

using System;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// <see cref="IProbeHealthMonitor"/> 的空操作实现，供外部
/// 故障检测系统使用。
/// </summary>
/// <remarks>
/// <para>
/// 使用提供自身故障检测能力的外部成员资格系统时，
/// 例如基于共识的外部检测系统，应注册此实现
/// 以禁用 <see cref="LocalSiloHealthMonitor"/> 中 Orleans 基于探测的健康检查。
/// </para>
/// <para>
/// 此实现始终报告健康（评分为 0），因为外部系统
/// 通过自身协议处理探测及心跳监控。
/// </para>
/// <para>
/// 如需更深入的集成，外部系统可提供自身的
/// <see cref="IProbeHealthMonitor"/> 实现，公开其故障检测器提供的
/// 健康信息。
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
