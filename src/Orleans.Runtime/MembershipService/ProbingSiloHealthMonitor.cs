#nullable enable

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime.Messaging;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// <see cref="IProbeHealthMonitor"/> 的默认实现，使用 Orleans 内置的
/// ping 协议监控探测健康状况。
/// </summary>
/// <remarks>
/// 此实现跟踪：
/// <list type="bullet">
/// <item><description>通过 <see cref="ProbeRequestMonitor"/> 接收的探测请求</description></item>
/// <item><description>通过 <see cref="IClusterHealthMonitor.SiloMonitors"/> 接收的探测响应</description></item>
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

        // 仅在存在多个其他节点时，才考虑最近一次收到探测请求的时间。
        // 否则，在只有一个或两个节点的集群中，它可能无法投票将另一个节点判定为死亡。
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

        // 确定最近一次成功收到 ping 响应距今有多长时间。
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

        // 仅在当前节点监控多个其他节点时，才考虑最近一次成功 ping 的时间。
        // 否则，在只有一个或两个节点的集群中，它可能无法投票将另一个节点判定为死亡。
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
