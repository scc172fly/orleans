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
/// LeaseBasedQueueBalancer。此平衡器支持分组自动规模假设情景中的队列平衡,
/// 并试图尽可能支持理想分布。
/// </summary>
/// <remarks>
/// 初始化 <see cref="LeaseBasedQueueBalancer"/> 类的新实例。
/// </remarks>
/// <param name="name">名称。</param>
/// <param name="options">选项。</param>
/// <param name="leaseProvider">租赁提供人。</param>
/// <param name="services">服务。</param>
/// <param name="loggerFactory">伐木厂</param>
/// <param name="timeProvider">供应商用于安排租赁保养和购置的时间。</param>
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
    private RoundRobinSelector<QueueId> _queueSelector = null!; // 初始化时开始。
    private int _allQueuesCount;
    private int _responsibility;
    private int _leaseOrder;
    private int _activeSiloCount;

    /// <summary>
    /// 创建新的 <see cref="LeaseBasedQueueBalancer"/> 实例。
    /// </summary>
    /// <param name="services">服务。</param>
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

        // 选择器默认现在可以绕行 Robin 选择器, 但我们还可以做进一步的修改, 以便在必要时使选择器配置可操作。 选择算法可以使用 。
        // 影响分组启动和自动缩放中平衡稳定时间的队列
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

        // 停止处理会员更新事宜。
        await base.Shutdown();

        try
        {
            // 停止获得和续租。
            await Task.WhenAll(_leaseMaintenanceTimerTask, _leaseAcquisitionTimerTask);

            // 排队排出排水系统并解除所有自有租赁。
            await _executor.AddNext(async () =>
            {
                _responsibility = 0;
                await ReleaseLeasesToMeetResponsibility(isShuttingDown: true);
            });
        }
        finally
        {
            // 确保始终处理计时器,即使排队排水或解除租约失败。
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

                // 如果我们在 leaseAcquisitionTimer 停止续展期间损失了一些租赁,则重新开始。
                if (!allLeasesRenewed)
                {
                    // 立即让收购定时器开火
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
            // 设定下一轮的周期。
            // 它可以因另一种方法而变异,但不能同时改变。
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
                // 停止获取计时器 。
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

        // 先删除最老的已获得队列, 这样可以给正在移动的队列提供最大恢复时间 。
        AcquiredLease[] queuesToGiveUp = _myQueues
            .OrderBy(queue => queue.LeaseOrder)
            .Take(queueCountToRelease)
            .Select(queue => queue.AcquiredLease)
            .ToArray();

        // 即使在释放失败的情况下也从列表中删除队列, 因为可以让租赁到期 。
        // TODO: 标记清除, 这样我们就不会续延, 只取消未到期的租赁。
        for (int index = _myQueues.Count - 1; index >= 0; index--)
        {
            if (queuesToGiveUp.Contains(_myQueues[index].AcquiredLease))
            {
                _myQueues.RemoveAt(index);
            }
        }

        await _leaseProvider.Release(_options.LeaseCategory, queuesToGiveUp);

        // 删除删除 queuesToGiveUp 调自 myQueue 平衡商放行了租赁合同之后的列表。
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

        // 跟踪有多少其余的租赁可能存在。
        var possibleLeaseCount = _queueSelector.Count - _myQueues.Count;
        LogDebugHoldingLeased(Logger, _myQueues.Count, leasesToAcquire, expectedTotalLeaseCount, possibleLeaseCount);

        // 试图获得租赁,直到我们没有更多可获取或无法获取
        var sw = ValueStopwatch.StartNew();
        while (!Cancellation.IsCancellationRequested && leasesToAcquire > 0 && possibleLeaseCount > 0)
        {
            // 选择要获取的新队列
            List<QueueId> expectedQueues = _queueSelector.NextSelection(leasesToAcquire, _myQueues.Select(queue => queue.QueueId).ToList());

            // 每一个队列的建设租赁请求
            LeaseRequest[] leaseRequests = expectedQueues
                .Select(queue => new LeaseRequest(queue.ToString(), _options.LeaseLength))
                .ToArray();

            // 成功将已成功获得的队列添加到 myQueues 列表
            AcquireLeaseResult[] results = await _leaseProvider.Acquire(_options.LeaseCategory, leaseRequests);
            for (var i = 0; i < results.Length; i++)
            {
                AcquireLeaseResult result = results[i];
                // 平衡者要求取得每一项结果都具有租赁身份,并获得成功后获得租赁。
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
                    // 预计大部分时间都是如此。
                    case ResponseCode.LeaseNotAvailable:
                        {
                            LogDebugFailedToAcquireLeaseNotAvailable(Logger, result.FailureException, result.AcquiredLease!.ResourceKey, result.StatusCode);
                            break;
                        }
                    // 获取呼叫不应返回此代码, 以日志错误
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
                // 时间被我们分配的时间吹散了,下次再试一次
                break;
            }
        }

        LogDebugHoldingLeases(Logger, _myQueues.Count, _responsibility);
    }

    /// <summary>
    /// 续租租赁
    /// </summary>
    /// <returns>布尔 - 如果我们不续签所有租赁</returns>, 则假的
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

        // 更新 myQueues 清单,成功续租。
        for (var i = results.Length - 1; i >= 0; i--)
        {
            AcquireLeaseResult result = results[i];
            // 平衡者要求取得每一项结果都具有租赁身份,并获得成功后获得租赁。
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
                // 如果租约到期和/或有人拿走,则可以采取这些措施。
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

        // 如果队列改变,通知听众。
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
    /// 检查是否要查看这个平衡器是否贪婪, 这意味着它试图抓住一个
    ///   队列比非贪婪平衡器多。
    /// </summary>
    /// <param name="overflow">免费排队数量,假设所有平衡者都履行最低责任</param>
    /// <param name="activeSilos">数 silos 主机队列</param>
    /// <returns>布尔 - 真实表示平衡器应尝试获取
    ///   队列多于非greedy 平衡器</returns>
    private bool ShouldBeGreedy(int overflow, HashSet<SiloAddress> activeSilos)
    {
        // 如果使用多个流流提供者, 这将选择相同的 silos 贪婪的人
        //   增加供应者数量,使不平衡加剧。
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
            // 确保获取计时器运行中 。
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
