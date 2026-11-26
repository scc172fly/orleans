#nullable enable

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// 执行不执行 <see cref="IClusterHealthMonitor"/> 与外部使用
/// 故障检测系统。
/// </summary>
/// <remarks>
/// <para>
/// 使用外部成员资格制度,提供自己的故障检测
/// 登记这一实施情况(如外部协商一致检测系统)
/// 禁用 Orleans'内置探测监测。
/// </para>
/// <para>
/// 这是发现外部故障的正确方法----外部系统
/// 处理检测失败情况,并报告通过 <see cref="IMembershipManager"/>改变成员资格。
/// 不需要 "Orleans" 探测机,这将会是浪费的。
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
        // 从 Orleans的角度而言,发现外部故障总是被认为是健康的。
        // 外部系统负责健康监测。
        reason = null;
        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "External failure detection is active. Orleans probe-based health monitoring is disabled."
    )]
    private static partial void LogExternalFailureDetectionActive(ILogger logger);
}
