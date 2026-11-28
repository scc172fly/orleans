using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.LeaseProviders;
using Orleans.Runtime;
using Orleans.Runtime.Internal;
using StreamingEvents = Orleans.Streaming.Diagnostics.StreamingEvents;

namespace Orleans.Streams;

/// <summary>
/// LeaseBasedQueueBalancer。此均衡器支持集群自动缩放、
/// 服务器意外故障等场景中的队列均衡，并尽可能维持理想分布。
/// </summary>
/// <remarks>
/// 初始化 <see cref="LeaseBasedQueueBalancer"/> 类的新实例。
/// </remarks>
/// <param name="name">名称。</param>
/// <param name="options">选项。</param>
/// <param name="leaseProvider">租约提供程序。</param>
/// <param name="services">服务提供程序。</param>
/// <param name="loggerFactory">日志记录器工厂。</param>
/// <param name="timeProvider">用于安排租约维护和获取操作的时间提供程序。</param>
public partial class LeaseBasedQueueBalancer(
    string name,
    LeaseBasedQueueBalancerOptions options,
    ILeaseProvider leaseProvider,
    IServiceProvider services,
    ILoggerFactory loggerFactory,
    [FromKeyedServices(StreamingTimeProviderNames.Streaming)] TimeProvider timeProvider) : QueueBalancerBase(services, loggerFactory.CreateLogger($"{typeof(LeaseBasedQueueBalancer).FullName}.{name}")), IStreamQueueBalancer
{
    private sealed class AcquiredQueue(int order, QueueId queueId, AcquiredLease lease)
    {
        public int LeaseOrder { get; set; } = order;
        public QueueId QueueId { get; set; } = queueId;
        public AcquiredLease AcquiredLease { get; set; } = lease;
    }

    private readonly string _name = name;
    private readonly LeaseBasedQueueBalancerOptions _options = options;
    private readonly ILeaseProvider _leaseProvider = leaseProvider;
    private readonly AsyncSerialExecutor _executor = new();
    private readonly List<AcquiredQueue> _myQueues = [];
    private readonly PeriodicTimer _leaseMaintenanceTimer = new(Timeout.InfiniteTimeSpan, timeProvider);
    private readonly PeriodicTimer _leaseAcquisitionTimer = new(Timeout.InfiniteTimeSpan, timeProvider);
    private Task _leaseMaintenanceTimerTask = Task.CompletedTask;
    private Task _leaseAcquisitionTimerTask = Task.CompletedTask;
    private RoundRobinSelector<QueueId> _queueSelector = null!; // 在 Initialize 中初始化。
    private int _allQueuesCount;
    private int _responsibility;
    private int _leaseOrder;
    private int _activeSiloCount;

    /// <summary>
    /// 创建新的 <see cref="LeaseBasedQueueBalancer"/> 实例。
    /// </summary>
    /// <param name="services">服务提供程序。</param>
    /// <param name="name">名称。</param>
    /// <returns>新的 <see cref="LeaseBasedQueueBalancer"/> 实例。</returns>
    public static IStreamQueueBalancer Create(IServiceProvider services, string name)
    {
        var options = services.GetOptionsByName<LeaseBasedQueueBalancerOptions>(name);
        var leaseProvider = services.GetKeyedService<ILeaseProvider>(name)
            ?? services.GetService<ILeaseProvider>()
            ?? throw new InvalidOperationException($"No lease provider found for queue balancer '{name}'. Register an implementation of {nameof(ILeaseProvider)}.");
        return ActivatorUtilities.CreateInstance<LeaseBasedQueueBalancer>(services, name, options, leaseProvider);
    }

    /// <inheritdoc/>
    public override async Task Initialize(IStreamQueueMapper queueMapper)
    {
        if (Cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException("Cannot initialize a terminated balancer.");
        }

        ArgumentNullException.ThrowIfNull(queueMapper);
        var allQueues = queueMapper.GetAllQueues().ToList();
        _allQueuesCount = allQueues.Count;

        // 目前默认使用轮询选择器；必要时可进一步将选择器改为可配置。选择器算法可能
        // 影响集群初始化和自动缩放时队列均衡达到稳定所需的时间。
        _queueSelector = new RoundRobinSelector<QueueId>(allQueues);
        await base.Initialize(queueMapper);
        StartMaintenanceTasks();

        void StartMaintenanceTasks()
        {
            using var _ = new ExecutionContextSuppressor();
            _leaseAcquisitionTimerTask = PeriodicallyAcquireLeasesToMeetResponsibility();
            _leaseMaintenanceTimerTask = PeriodicallyMaintainLeases();
        }
    }

    /// <inheritdoc/>
    public override async Task Shutdown()
    {
        if (Cancellation.IsCancellationRequested) return;

        // 停止处理成员资格更新。
        await base.Shutdown();

        try
        {
            // 停止获取和续订租约。
            await Task.WhenAll(_leaseMaintenanceTimerTask, _leaseAcquisitionTimerTask);

            // 等待已排队操作处理完毕，并释放所有持有的租约。
            await _executor.AddNext(async () =>
            {
                _responsibility = 0;
                await ReleaseLeasesToMeetResponsibility(isShuttingDown: true);
            });
        }
        finally
        {
            // 确保始终释放定时器，即使处理已排队操作或释放租约失败也是如此。
            _leaseMaintenanceTimer.Dispose();
            _leaseAcquisitionTimer.Dispose();
        }
    }

    /// <inheritdoc/>
    public override IEnumerable<QueueId> GetMyQueues()
    {
        if (Cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException("Cannot acquire queues from a terminated balancer.");
        }

        return _myQueues.Select(queue => queue.QueueId);
    }

    private async Task PeriodicallyMaintainLeases()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        while (await WaitForNextTick(_leaseMaintenanceTimer))
        {
            try
            {
                await _executor.AddNext(MaintainLeases);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error maintaining leases.");
            }
        }

        async Task MaintainLeases()
        {
            if (Cancellation.IsCancellationRequested) return;
            var oldQueues = new HashSet<QueueId>(_myQueues.Select(queue => queue.QueueId));
            try
            {
                bool allLeasesRenewed = await RenewLeases();

                // 如果 leaseAcquisitionTimer 停止后，在续订过程中丢失了部分租约，则重新启动它。
                if (!allLeasesRenewed)
                {
                    // 使获取租约的定时器立即触发。
                    _leaseAcquisitionTimer.Period = TimeSpan.FromMilliseconds(1);
                }
            }
            finally
            {
                await NotifyOnChange(oldQueues);
            }
        }
    }

    private async Task PeriodicallyAcquireLeasesToMeetResponsibility()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        while (await WaitForNextTick(_leaseAcquisitionTimer))
        {
            // 设置下一轮的周期。
            // 它可能由其他方法修改，但不会并发修改。
            _leaseAcquisitionTimer.Period = _options.LeaseAcquisitionPeriod;

            try
            {
                await _executor.AddNext(AcquireLeasesToMeetResponsibility);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error acquiring leases.");
            }
        }
    }

    private async Task AcquireLeasesToMeetResponsibility()
    {
        if (Cancellation.IsCancellationRequested) return;
        var oldQueues = new HashSet<QueueId>(_myQueues.Select(queue => queue.QueueId));
        try
        {
            if (_myQueues.Count < _responsibility)
            {
                await AcquireLeasesToMeetExpectation(_responsibility, _options.LeaseLength.Divide(10));
            }
            else if (_myQueues.Count > _responsibility)
            {
                await ReleaseLeasesToMeetResponsibility();
            }
        }
        finally
        {
            await NotifyOnChange(oldQueues);
            if (_myQueues.Count == _responsibility)
            {
                // 停止获取租约的定时器。
                _leaseAcquisitionTimer.Period = Timeout.InfiniteTimeSpan;
            }
        }
    }

    private async Task ReleaseLeasesToMeetResponsibility(bool isShuttingDown = false)
    {
        if (Cancellation.IsCancellationRequested && !isShuttingDown) return;
        LogTraceReleaseLeasesToMeetResponsibility(Logger, _myQueues.Count, _responsibility);

        var queueCountToRelease = _myQueues.Count - _responsibility;
        if (queueCountToRelease <= 0)
        {
            return;
        }

        // 优先移除最早获取的队列，为被迁移的队列提供尽可能长的恢复时间。
        AcquiredLease[] queuesToGiveUp = _myQueues
            .OrderBy(queue => queue.LeaseOrder)
            .Take(queueCountToRelease)
            .Select(queue => queue.AcquiredLease)
            .ToArray();

        // 即使释放失败，也从列表中移除队列，因为可以等待租约到期。
        // TODO：改为标记为待移除，从而不再续订，并且仅移除尚未到期的租约。- jbragg
        for (int index = _myQueues.Count - 1; index >= 0; index--)
        {
            if (queuesToGiveUp.Contains(_myQueues[index].AcquiredLease))
            {
                _myQueues.RemoveAt(index);
            }
        }

        await _leaseProvider.Release(_options.LeaseCategory, queuesToGiveUp);

        // 均衡器释放 queuesToGiveUp 中队列的租约后，从 myQueue 列表中移除这些队列。（译注：当前实现先从列表移除队列，再调用 Release；此处仅记录日志。）
        LogDebugReleasedLeases(Logger, queueCountToRelease, _myQueues.Count, _responsibility);
    }

    private async Task AcquireLeasesToMeetExpectation(int expectedTotalLeaseCount, TimeSpan timeout)
    {
        if (Cancellation.IsCancellationRequested) return;
        LogTraceAcquireLeasesToMeetExpectation(Logger, _myQueues.Count, expectedTotalLeaseCount);

        var leasesToAcquire = expectedTotalLeaseCount - _myQueues.Count;
        if (leasesToAcquire <= 0)
        {
            return;
        }

        // 跟踪剩余可能获取的租约数量。
        var possibleLeaseCount = _queueSelector.Count - _myQueues.Count;
        LogDebugHoldingLeased(Logger, _myQueues.Count, leasesToAcquire, expectedTotalLeaseCount, possibleLeaseCount);

        // 尝试获取租约，直到没有更多需要获取的租约，或者没有更多可能获取的租约。
        var sw = ValueStopwatch.StartNew();
        while (!Cancellation.IsCancellationRequested && leasesToAcquire > 0 && possibleLeaseCount > 0)
        {
            // 选择要获取的新队列。
            List<QueueId> expectedQueues = _queueSelector.NextSelection(leasesToAcquire, _myQueues.Select(queue => queue.QueueId).ToList());

            // 根据各队列构建租约请求。
            LeaseRequest[] leaseRequests = expectedQueues
                .Select(queue => new LeaseRequest(queue.ToString(), _options.LeaseLength))
                .ToArray();

            // 将成功获取的队列添加到 myQueues 列表。
            AcquireLeaseResult[] results = await _leaseProvider.Acquire(_options.LeaseCategory, leaseRequests);
            for (var i = 0; i < results.Length; i++)
            {
                AcquireLeaseResult result = results[i];
                // 均衡器要求每个结果都包含租约标识，并且成功时包含已获取的租约。
                switch (result.StatusCode)
                {
                    case ResponseCode.OK:
                        {
                            _myQueues.Add(new AcquiredQueue(_leaseOrder++, expectedQueues[i], result.AcquiredLease!));
                            break;
                        }
                    case ResponseCode.TransientFailure:
                        {
                            LogWarningFailedToAcquireLeaseTransient(Logger, result.FailureException, result.AcquiredLease!.ResourceKey);
                            break;
                        }
                    // 这在很多情况下都是预期行为。
                    case ResponseCode.LeaseNotAvailable:
                        {
                            LogDebugFailedToAcquireLeaseNotAvailable(Logger, result.FailureException, result.AcquiredLease!.ResourceKey, result.StatusCode);
                            break;
                        }
                    // 获取租约的调用不应返回此代码，因此将其记录为错误。
                    case ResponseCode.InvalidToken:
                        {
                            LogErrorFailedToAcquireLeaseInvalidToken(Logger, result.FailureException, result.AcquiredLease!.ResourceKey);
                            break;
                        }
                    default:
                        {
                            LogErrorUnexpectedAcquireLease(Logger, result.FailureException, result.AcquiredLease!.ResourceKey, result.StatusCode);
                            break;
                        }
                }
            }

            possibleLeaseCount -= expectedQueues.Count;
            leasesToAcquire = expectedTotalLeaseCount - _myQueues.Count;
            LogDebugHoldingLeased(Logger, _myQueues.Count, leasesToAcquire, expectedTotalLeaseCount, possibleLeaseCount);

            if (sw.Elapsed > timeout)
            {
                // 已用完分配的时间，在下一个周期重试。
                break;
            }
        }

        LogDebugHoldingLeases(Logger, _myQueues.Count, _responsibility);
    }

    /// <summary>
    /// 续订租约。
    /// </summary>
    /// <returns>bool：如果未能成功续订所有租约，则为 false。</returns>
    private async Task<bool> RenewLeases()
    {
        bool allRenewed = true;
        if (Cancellation.IsCancellationRequested) return false;
        LogTraceRenewLeases(Logger, _myQueues.Count);

        if (_myQueues.Count <= 0)
        {
            return allRenewed;
        }

        var results = await _leaseProvider.Renew(_options.LeaseCategory, _myQueues.Select(queue => queue.AcquiredLease).ToArray());

        // 使用成功续订的租约更新 myQueues 列表。
        for (var i = results.Length - 1; i >= 0; i--)
        {
            AcquireLeaseResult result = results[i];
            // 均衡器要求每个结果都包含租约标识，并且成功时包含已获取的租约。
            switch (result.StatusCode)
            {
                case ResponseCode.OK:
                    {
                        _myQueues[i].AcquiredLease = result.AcquiredLease!;
                        break;
                    }
                case ResponseCode.TransientFailure:
                    {
                        _myQueues.RemoveAt(i);
                        allRenewed = false;
                        LogWarningFailedToRenewLeaseTransient(Logger, result.FailureException, result.AcquiredLease!.ResourceKey);
                        break;
                    }
                // 当租约已到期和/或被其他持有者获取时，可能发生这些情况。
                case ResponseCode.InvalidToken:
                case ResponseCode.LeaseNotAvailable:
                    {
                        _myQueues.RemoveAt(i);
                        allRenewed = false;
                        LogWarningFailedToRenewLeaseReason(Logger, result.FailureException, result.AcquiredLease!.ResourceKey, result.StatusCode);
                        break;
                    }
                default:
                    {
                        _myQueues.RemoveAt(i);
                        allRenewed = false;
                        LogErrorUnexpectedRenewLease(Logger, result.FailureException, result.AcquiredLease!.ResourceKey, result.StatusCode);
                        break;
                    }
            }
        }

        LogDebugRenewedLeases(Logger, _myQueues.Count);

        return allRenewed;
    }

    private Task NotifyOnChange(HashSet<QueueId> oldQueues)
    {
        if (Cancellation.IsCancellationRequested) return Task.CompletedTask;
        var newQueues = new HashSet<QueueId>(_myQueues.Select(queue => queue.QueueId));

        // 如果队列发生变化，则通知侦听器。
        if (!oldQueues.SetEquals(newQueues))
        {
            StreamingEvents.EmitQueueChange(_name, SiloAddress, [.. oldQueues], [.. newQueues], this);
            return NotifyListeners();
        }

        return Task.CompletedTask;
    }

    private async ValueTask<bool> WaitForNextTick(PeriodicTimer timer)
    {
        try
        {
            return await timer.WaitForNextTickAsync(Cancellation);
        }
        catch (OperationCanceledException) when (Cancellation.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    protected override void OnClusterMembershipChange(HashSet<SiloAddress> activeSilos)
    {
        if (Cancellation.IsCancellationRequested) return;
        ScheduleUpdateResponsibilities(activeSilos).Ignore();
    }

    private async Task ScheduleUpdateResponsibilities(HashSet<SiloAddress> activeSilos)
    {
        if (Cancellation.IsCancellationRequested) return;

        try
        {
            await _executor.AddNext(() => UpdateResponsibilities(activeSilos));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating lease responsibilities.");
        }
    }

    /// <summary>
    /// 检查此均衡器是否应采用贪婪策略，即尝试比
    /// 非贪婪均衡器多获取一个队列。
    /// </summary>
    /// <param name="overflow">假定所有均衡器均满足最低分配要求时的空闲队列数量。</param>
    /// <param name="activeSilos">托管队列的活动 Silo 数量。</param>
    /// <returns>bool：true 表示该均衡器应尝试比
    /// 非贪婪均衡器多获取一个队列。</returns>
    private bool ShouldBeGreedy(int overflow, HashSet<SiloAddress> activeSilos)
    {
        // 如果使用多个流提供程序，这将为所有提供程序选择同一组 Silo
        // 采用贪婪策略，随着流提供程序数量增加，会加剧不均衡。
        return activeSilos.OrderBy(silo => silo)
                          .Take(overflow)
                          .Contains(SiloAddress);
    }

    private async Task UpdateResponsibilities(HashSet<SiloAddress> activeSilos)
    {
        if (Cancellation.IsCancellationRequested) return;
        _activeSiloCount = Math.Max(1, activeSilos.Count);
        _responsibility = _allQueuesCount / _activeSiloCount;
        var overflow = _allQueuesCount % _activeSiloCount;
        if (overflow != 0 && ShouldBeGreedy(overflow, activeSilos))
        {
            _responsibility++;
        }

        LogDebugUpdatingResponsibilities(Logger, _allQueuesCount, _activeSiloCount, _responsibility, _myQueues.Count);

        if (_myQueues.Count < _responsibility && _leaseAcquisitionTimer.Period == Timeout.InfiniteTimeSpan)
        {
            // 确保获取租约的定时器正在运行。
            _leaseAcquisitionTimer.Period = _options.LeaseAcquisitionPeriod;
        }

        _leaseMaintenanceTimer.Period = _options.LeaseRenewPeriod;
        await AcquireLeasesToMeetResponsibility();
    }

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "ReleaseLeasesToMeetResponsibility. QueueCount: {QueueCount}, Responsibility: {Responsibility}"
    )]
    private static partial void LogTraceReleaseLeasesToMeetResponsibility(ILogger logger, int queueCount, int responsibility);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Released leases for {QueueCount} queues. Holding leases for {HoldingQueueCount} of an expected {MinQueueCount} queues."
    )]
    private static partial void LogDebugReleasedLeases(ILogger logger, int queueCount, int holdingQueueCount, int minQueueCount);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "AcquireLeasesToMeetExpectation. QueueCount: {QueueCount}, ExpectedTotalLeaseCount: {ExpectedTotalLeaseCount}"
    )]
    private static partial void LogTraceAcquireLeasesToMeetExpectation(ILogger logger, int queueCount, int expectedTotalLeaseCount);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Holding leased for {QueueCount} queues. Trying to acquire {acquireQueueCount} queues to reach {TargetQueueCount} of a possible {PossibleLeaseCount}"
    )]
    private static partial void LogDebugHoldingLeased(ILogger logger, int queueCount, int acquireQueueCount, int targetQueueCount, int possibleLeaseCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to acquire lease {LeaseKey} due to transient error."
    )]
    private static partial void LogWarningFailedToAcquireLeaseTransient(ILogger logger, Exception? exception, string leaseKey);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Failed to acquire lease {LeaseKey} due to {Reason}."
    )]
    private static partial void LogDebugFailedToAcquireLeaseNotAvailable(ILogger logger, Exception? exception, string leaseKey, ResponseCode reason);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to acquire acquire {LeaseKey} unexpected invalid token."
    )]
    private static partial void LogErrorFailedToAcquireLeaseInvalidToken(ILogger logger, Exception? exception, string leaseKey);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unexpected response to acquire request of lease {LeaseKey}. StatusCode {StatusCode}."
    )]
    private static partial void LogErrorUnexpectedAcquireLease(ILogger logger, Exception? exception, string leaseKey, ResponseCode statusCode);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Holding leased for {QueueCount} queues. Trying to acquire {acquireQueueCount} queues to reach {TargetQueueCount} of a possible {PossibleLeaseCount} lease"
    )]
    private static partial void LogDebugHoldingLeasedAgain(ILogger logger, int queueCount, int acquireQueueCount, int targetQueueCount, int possibleLeaseCount);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Holding leases for {QueueCount} of an expected {MinQueueCount} queues."
    )]
    private static partial void LogDebugHoldingLeases(ILogger logger, int queueCount, int minQueueCount);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "RenewLeases. QueueCount: {QueueCount}"
    )]
    private static partial void LogTraceRenewLeases(ILogger logger, int queueCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to renew lease {LeaseKey} due to transient error."
    )]
    private static partial void LogWarningFailedToRenewLeaseTransient(ILogger logger, Exception? exception, string leaseKey);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to renew lease {LeaseKey} due to {Reason}."
    )]
    private static partial void LogWarningFailedToRenewLeaseReason(ILogger logger, Exception? exception, string leaseKey, ResponseCode reason);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unexpected response to renew of lease {LeaseKey}. StatusCode {StatusCode}."
    )]
    private static partial void LogErrorUnexpectedRenewLease(ILogger logger, Exception? exception, string leaseKey, ResponseCode statusCode);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Renewed leases for {QueueCount} queues."
    )]
    private static partial void LogDebugRenewedLeases(ILogger logger, int queueCount);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Updating Responsibilities for {QueueCount} queue over {SiloCount} silos. Need {MinQueueCount} queues, have {MyQueueCount}"
    )]
    private static partial void LogDebugUpdatingResponsibilities(ILogger logger, int queueCount, int siloCount, int minQueueCount, int myQueueCount);
}
