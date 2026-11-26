#nullable enable

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime.Messaging;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// 默认执行 <see cref="IProbeHealthMonitor"/> 用途 Orleans内置
/// 监测健康检测的 ping 协议
/// </summary>
/// <remarks>
/// 执行轨道 :
/// <list type="bullet">
///   <item><description>收到查询请求 <see cref="ProbeRequestMonitor"/></description></item>
///   <item><description>收到通过 <see cref="IClusterHealthMonitor.SiloMonitors"/></description></item>
/// </list>
/// </remarks>
internal sealed partial class ProbingSiloHealthMonitor : IProbeHealthMonitor
{
    private readonly ProbeRequestMonitor _probeRequestMonitor;
    private readonly IClusterHealthMonitor _clusterHealthMonitor;
    private readonly ClusterMembershipOptions _clusterMembershipOptions;
    private readonly ILogger<ProbingSiloHealthMonitor> _logger;

    public ProbingSiloHealthMonitor(
        ProbeRequestMonitor probeRequestMonitor,
        IClusterHealthMonitor clusterHealthMonitor,
        IOptions<ClusterMembershipOptions> clusterMembershipOptions,
        ILogger<ProbingSiloHealthMonitor> logger)
    {
        _probeRequestMonitor = probeRequestMonitor;
        _clusterHealthMonitor = clusterHealthMonitor;
        _clusterMembershipOptions = clusterMembershipOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public int CheckReceivedProbeRequests(DateTime now, int activeNodeCount, out string? complaint)
    {
        complaint = null;

        // 只有在有不止一个其他节点时,才考虑最后收到的探测请求是否属常态。
        // 否则,它可能无法对一两个节点组中的另一个节点投下表决票。
        if (activeNodeCount <= 2)
        {
            return 0;
        }

        var sinceLastProbeRequest = _probeRequestMonitor.ElapsedSinceLastProbeRequest;
        var recencyWindow = _clusterMembershipOptions.GetFailureDetectionTimeout();

        if (!sinceLastProbeRequest.HasValue)
        {
            LogNoProbeRequests();
            complaint = "This silo has not received any probe requests";
            return 1;
        }

        if (sinceLastProbeRequest.Value > recencyWindow)
        {
            var lastRequestTime = now - sinceLastProbeRequest.Value;
            LogNoRecentProbeRequest(lastRequestTime);
            complaint = $"This silo has not received a probe request since {lastRequestTime}";
            return 1;
        }

        return 0;
    }

    /// <inheritdoc/>
    public int CheckReceivedProbeResponses(DateTime now, int monitoredNodeCount, out string? complaint)
    {
        complaint = null;

        // 确定最近收到的最新成功电话回音回复。
        var siloMonitors = _clusterHealthMonitor.SiloMonitors;
        var elapsedSinceLastResponse = default(TimeSpan?);

        foreach (var monitor in siloMonitors.Values)
        {
            var current = monitor.ElapsedSinceLastResponse;
            if (current.HasValue && (!elapsedSinceLastResponse.HasValue || current.Value < elapsedSinceLastResponse.Value))
            {
                elapsedSinceLastResponse = current.Value;
            }
        }

        // 只有在最后一个成功点对另一个节点进行不止一个监测时,才考虑最后成功点的静态性。
        // 否则,它可能无法对一两个节点组中的另一个节点投下表决票。
        if (siloMonitors.Count <= 1)
        {
            return 0;
        }

        var recencyWindow = _clusterMembershipOptions.GetFailureDetectionTimeout();

        if (!elapsedSinceLastResponse.HasValue)
        {
            LogNoProbeResponses();
            complaint = "This silo has not received any successful probe responses";
            return 1;
        }

        if (elapsedSinceLastResponse.Value > recencyWindow)
        {
            LogNoRecentProbeResponse(elapsedSinceLastResponse.Value);
            complaint = $"This silo has not received a successful probe response since {elapsedSinceLastResponse.Value}";
            return 1;
        }

        return 0;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This silo has not received any probe requests"
    )]
    private partial void LogNoProbeRequests();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This silo has not received a probe request since {LastProbeRequest}"
    )]
    private partial void LogNoRecentProbeRequest(DateTime lastProbeRequest);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This silo has not received any successful probe responses"
    )]
    private partial void LogNoProbeResponses();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This silo has not received a successful probe response since {LastSuccessfulResponse}"
    )]
    private partial void LogNoRecentProbeResponse(TimeSpan lastSuccessfulResponse);
}
