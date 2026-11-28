using System;
using Orleans.Runtime.Placement.Repartitioning;
using System.Threading.Tasks;
using Orleans.Placement.Rebalancing;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using Orleans.Runtime.Scheduler;
using Microsoft.Extensions.DependencyInjection;

#nullable enable

namespace Orleans.Runtime.Placement.Rebalancing;

internal sealed partial class ActivationRebalancerMonitor : SystemTarget, IActivationRebalancerMonitor, ILifecycleParticipant<ISiloLifecycle>
{
    private IGrainTimer? _monitorTimer;
    private RebalancingReport _latestReport;
    private long _lastHeartbeatTimestamp;

    private readonly TimeProvider _timeProvider;
    private readonly ActivationDirectory _activationDirectory;
    private readonly IActivationRebalancerWorker _rebalancerGrain;
    private readonly ILogger<ActivationRebalancerMonitor> _logger;
    private readonly List<IActivationRebalancerReportListener> _statusListeners = [];

    // 以工作器报告的周期的两倍作为检查工作器的周期。
    private readonly static TimeSpan TimerPeriod = 2 * IActivationRebalancerMonitor.WorkerReportPeriod;

    public ActivationRebalancerMonitor(
        [FromKeyedServices(TimeProviderNames.ActivationManagement)] TimeProvider timeProvider,
        ActivationDirectory activationDirectory,
        ILoggerFactory loggerFactory,
        IGrainFactory grainFactory,
        SystemTargetShared shared)
        : base(Constants.ActivationRebalancerMonitorType, shared)
    {
        _timeProvider = timeProvider;
        _activationDirectory = activationDirectory;
        _logger = loggerFactory.CreateLogger<ActivationRebalancerMonitor>();
        _rebalancerGrain = grainFactory.GetGrain<IActivationRebalancerWorker>(0);
        _lastHeartbeatTimestamp = _timeProvider.GetTimestamp();

        _latestReport = new()
        {
            ClusterImbalance = 1,
            Host = SiloAddress.Zero,
            Status = RebalancerStatus.Suspended,
            SuspensionDuration = Timeout.InfiniteTimeSpan,
            Statistics = []
        };
        shared.ActivationDirectory.RecordNewTarget(this);
    }

    public void Participate(ISiloLifecycle observer)
    {
        observer.Subscribe(
           nameof(ActivationRepartitioner),
           ServiceLifecycleStage.Active,
           OnStart,
           _ => Task.CompletedTask);

        // 关闭期间，再均衡器必须在 激活实例目录 停用普通 Grain 之前完成迁移。
        observer.Subscribe(
           $"{nameof(ActivationRebalancerMonitor)}.Migration",
           ServiceLifecycleStage.GrainDeactivation + 1,
           _ => Task.CompletedTask,
           MigrateRebalancerOnStop);

        observer.Subscribe(
           nameof(ActivationRepartitioner),
           ServiceLifecycleStage.ApplicationServices,
           _ => Task.CompletedTask,
           OnStop);
    }

    private async Task OnStart(CancellationToken cancellationToken)
    {
        await this.RunOrQueueTask(() =>
        {
            _monitorTimer = RegisterGrainTimer(async ct =>
            {
                var elapsedSinceHeartbeat = _timeProvider.GetElapsedTime(_lastHeartbeatTimestamp);
                var shouldFetchReport = SiloAddress.Zero.Equals(_latestReport.Host) ||
                    elapsedSinceHeartbeat >= IActivationRebalancerMonitor.WorkerReportPeriod;

                if (shouldFetchReport)
                {
                    LogStartingRebalancer(elapsedSinceHeartbeat, IActivationRebalancerMonitor.WorkerReportPeriod);

                    try
                    {
                        _latestReport = await _rebalancerGrain.GetReport(ct);
                    }
                    catch (OperationCanceledException oce) when (oce.CancellationToken == ct) { }
                    catch (Exception ex)
                    {
                        // 这样可避免因成员表在非正常关闭后
                        // 被死亡 Silo 填满等问题导致 Silo 崩溃。
                        LogRebalancerReportFailed(ex);
                    }
                }

            }, TimeSpan.Zero, TimerPeriod);

            return Task.CompletedTask;
        });
    }

    private async Task MigrateRebalancerOnStop(CancellationToken cancellationToken)
    {
        await this.RunOrQueueTask(async () =>
        {
            if (!cancellationToken.IsCancellationRequested &&
                _activationDirectory.FindTarget(_rebalancerGrain.GetGrainId()) is { } activation)
            {
                LogMigratingRebalancer(Silo);
                activation.Migrate(null, cancellationToken); // 将其迁移到其他任意位置。
                await activation.Deactivated.WaitAsync(cancellationToken);
            }
        });
    }

    private Task OnStop(CancellationToken cancellationToken) => this.RunOrQueueTask(() =>
    {
        _monitorTimer?.Dispose();
        return Task.CompletedTask;
    });

    public Task ResumeRebalancing() => _rebalancerGrain.ResumeRebalancing();
    public Task SuspendRebalancing(TimeSpan? duration) => _rebalancerGrain.SuspendRebalancing(duration);

    public async ValueTask<RebalancingReport> GetRebalancingReport(bool force = false)
    {
        if (force)
        {
            try
            {
                _latestReport = await _rebalancerGrain.GetReport();
            }
            catch (Exception ex)
            {
                LogRebalancerReportFailed(ex);
            }
        }

        return _latestReport;
    }

    public Task Report(RebalancingReport report, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _latestReport = report;
        _lastHeartbeatTimestamp = _timeProvider.GetTimestamp();

        foreach (var listener in _statusListeners)
        {
            try
            {
                listener.OnReport(report);
            }
            catch (Exception ex)
            {
                LogErrorWhileNotifyingListener(ex);
            }
        }

        return Task.CompletedTask;
    }

    public void SubscribeToReports(IActivationRebalancerReportListener listener)
    {
        if (!_statusListeners.Contains(listener))
        {
            _statusListeners.Add(listener);
        }
    }

    public void UnsubscribeFromReports(IActivationRebalancerReportListener listener) =>
        _statusListeners.Remove(listener);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "I have not received a report from the activation rebalancer for the last {Duration} which is more than the " +
        "allowed interval {Period}. I will now try to wake it up with the assumption that it has has been stopped ungracefully."
    )]
    private partial void LogStartingRebalancer(TimeSpan duration, TimeSpan period);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "My silo '{Silo}' is stopping now, and I am the host of the activation rebalancer. " +
        "I will attempt to migrate the rebalancer to another silo."
    )]
    private partial void LogMigratingRebalancer(SiloAddress silo);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "An unexpected error occurred while notifying rebalancer listener."
    )]
    private partial void LogErrorWhileNotifyingListener(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "An unexpected error occurred while trying to a grab report from the rebalancer."
    )]
    private partial void LogRebalancerReportFailed(Exception exception);
}
