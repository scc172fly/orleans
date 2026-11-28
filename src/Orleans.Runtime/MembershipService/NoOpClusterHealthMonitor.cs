#nullable enable

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// <see cref="IClusterHealthMonitor"/> 的空操作实现，供外部
/// 故障检测系统使用。
/// </summary>
/// <remarks>
/// <para>
/// 使用提供自身故障检测能力的外部成员资格系统时，
/// 例如基于共识的外部检测系统，应注册此实现
/// 以禁用 Orleans 内置的基于探测的监控。
/// </para>
/// <para>
/// 对于外部故障检测，正确的做法是由外部系统
/// 检测故障并通过 <see cref="IMembershipManager"/> 报告成员资格变更。
/// Orleans 的探测机制不再需要，继续使用会造成浪费。
/// </para>
/// </remarks>
internal sealed partial class NoOpClusterHealthMonitor : IClusterHealthMonitor
{
    private readonly ILogger<NoOpClusterHealthMonitor> _logger;

    public NoOpClusterHealthMonitor(ILogger<NoOpClusterHealthMonitor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<SiloAddress, SiloHealthMonitor> SiloMonitors
        => ImmutableDictionary<SiloAddress, SiloHealthMonitor>.Empty;

    /// <inheritdoc/>
    void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(
            nameof(NoOpClusterHealthMonitor),
            ServiceLifecycleStage.Active,
            _ =>
            {
                LogExternalFailureDetectionActive(_logger);
                return System.Threading.Tasks.Task.CompletedTask;
            },
            _ => System.Threading.Tasks.Task.CompletedTask);
    }

    /// <inheritdoc/>
    bool IHealthCheckable.CheckHealth(DateTime lastCheckTime, [MaybeNullWhen(true)] out string reason)
    {
        // 从 Orleans 的角度来看，外部故障检测始终被视为健康。
        // 外部系统负责健康监控。
        reason = null;
        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "External failure detection is active. Orleans probe-based health monitoring is disabled."
    )]
    private static partial void LogExternalFailureDetectionActive(ILogger logger);
}
