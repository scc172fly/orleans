using System.Collections.Concurrent;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 使用 Orleans 放置诊断事件并提供
/// 等待统计的方法 确定传播。
/// </summary>
/// <remarks>
/// 这使通过允许等待测试,能够对安置策略进行确定性测试
/// 供统计传播在作出断言之前,要全部完成 silos 。
/// </remarks>
public sealed class PlacementDiagnosticObserver : IDisposable, IObserver<DeploymentLoadPublisherEvents.DeploymentLoadPublisherEvent>
{
    private readonly ConcurrentBag<DeploymentLoadPublisherEvents.Published> _publishedEvents = new();
    private readonly ConcurrentBag<DeploymentLoadPublisherEvents.Received> _receivedEvents = new();
    private readonly ConcurrentBag<DeploymentLoadPublisherEvents.ClusterRefreshed> _clusterRefreshedEvents = new();
    private readonly ConcurrentBag<DeploymentLoadPublisherEvents.Removed> _removedEvents = new();
    private readonly ILogger? _logger;
    private IDisposable? _subscription;
    private int _placementListenerSubscriptionCount;

    /// <summary>
    /// 获得定位听众订阅的数。 可用于调试 。
    /// </summary>
    public int PlacementListenerSubscriptionCount => _placementListenerSubscriptionCount;

    /// <summary>
    /// 收集所有被捕获的统计数据 发布事件。
    /// </summary>
    public IReadOnlyCollection<DeploymentLoadPublisherEvents.Published> PublishedEvents => _publishedEvents.ToArray();

    /// <summary>
    /// 收集所有被捕获的统计数据 收到事件。
    /// </summary>
    public IReadOnlyCollection<DeploymentLoadPublisherEvents.Received> ReceivedEvents => _receivedEvents.ToArray();

    /// <summary>
    /// 获得所有被俘获的群组统计更新活动。
    /// </summary>
    public IReadOnlyCollection<DeploymentLoadPublisherEvents.ClusterRefreshed> ClusterRefreshedEvents => _clusterRefreshedEvents.ToArray();

    /// <summary>
    /// 收集所有被捕获的统计数据,消除事件。
    /// </summary>
    public IReadOnlyCollection<DeploymentLoadPublisherEvents.Removed> RemovedEvents => _removedEvents.ToArray();

    /// <summary>
    /// 创建了一个新的观察者实例,并开始收听安置诊断事件。
    /// </summary>
    /// <param name="logger">调试输出的可选日志。</param>
    public static PlacementDiagnosticObserver Create(TestCluster cluster, ILogger? logger = null) =>
        Create(DiagnosticObserverSiloScope.For(cluster), logger);

    public static PlacementDiagnosticObserver Create(InProcessTestCluster cluster, ILogger? logger = null) =>
        Create(DiagnosticObserverSiloScope.For(cluster), logger);

    internal static PlacementDiagnosticObserver Create(SiloAddress siloAddress, ILogger? logger = null) =>
        Create(DiagnosticObserverSiloScope.For(siloAddress), logger);

    private static PlacementDiagnosticObserver Create(DiagnosticObserverSiloScope scope, ILogger? logger)
    {
        var observer = new PlacementDiagnosticObserver(logger);
        observer._subscription = DeploymentLoadPublisherEvents.AllEvents
            .Where(e => scope.Matches(GetObserverSilo(e)))
            .Subscribe(observer);
        observer._placementListenerSubscriptionCount = 1;
        return observer;
    }

    private PlacementDiagnosticObserver(ILogger? logger = null)
    {
        _logger = logger;
    }

    private static SiloAddress GetObserverSilo(DeploymentLoadPublisherEvents.DeploymentLoadPublisherEvent value) =>
        value switch
        {
            DeploymentLoadPublisherEvents.Published published => published.SiloAddress,
            DeploymentLoadPublisherEvents.Received received => received.ReceiverSilo,
            DeploymentLoadPublisherEvents.ClusterRefreshed refreshed => refreshed.SiloAddress,
            DeploymentLoadPublisherEvents.Removed removed => removed.ObserverSilo,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    /// <summary>
    /// 等待某一具体 silo公布统计数据。
    /// </summary>
    /// <param name="siloAddress">等待的 silo 地址。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>所公布的事件有效载荷。</returns>
    public async Task<DeploymentLoadPublisherEvents.Published> WaitForStatisticsPublishedAsync(SiloAddress siloAddress, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        var existingMatch = _publishedEvents.FirstOrDefault(e => e.SiloAddress.Equals(siloAddress));
        if (existingMatch is not null)
        {
            return existingMatch;
        }

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var match = _publishedEvents.FirstOrDefault(e => e.SiloAddress.Equals(siloAddress));
            if (match is not null)
            {
                return match;
            }
        }

        throw new TimeoutException($"Timed out waiting for statistics published from silo {siloAddress} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待群集统计更新完成特定 silo。
    /// </summary>
    /// <param name="siloAddress">填充刷新内容的 silo 地址。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>集束更新事件有效载荷。</returns>
    public async Task<DeploymentLoadPublisherEvents.ClusterRefreshed> WaitForClusterStatisticsRefreshedAsync(SiloAddress siloAddress, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        var existingMatch = _clusterRefreshedEvents.FirstOrDefault(e => e.SiloAddress.Equals(siloAddress));
        if (existingMatch is not null)
        {
            return existingMatch;
        }

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var match = _clusterRefreshedEvents.FirstOrDefault(e => e.SiloAddress.Equals(siloAddress));
            if (match is not null)
            {
                return match;
            }
        }

        throw new TimeoutException($"Timed out waiting for cluster statistics refresh on silo {siloAddress} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待分组中所有 silos 的群集统计更新。
    /// 这有助于确保在作出安置指控之前,已传播统计数据。
    /// </summary>
    /// <param name="siloAddresses">所有人的讲话 silos 应该是新鲜的。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>一项在所有 silos 更新其统计数据后完成的任务。</returns>
    public async Task WaitForAllSilosRefreshedAsync(IEnumerable<SiloAddress> siloAddresses, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var siloSet = siloAddresses.ToHashSet();
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var refreshedSilos = _clusterRefreshedEvents.Select(e => e.SiloAddress).ToHashSet();
            if (siloSet.All(s => refreshedSilos.Contains(s)))
            {
                return;
            }

            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var refreshedSilosFinal = _clusterRefreshedEvents.Select(e => e.SiloAddress).ToHashSet();
        var missingSilos = siloSet.Where(s => !refreshedSilosFinal.Contains(s)).ToList();
        throw new TimeoutException($"Timed out waiting for cluster statistics refresh on silos: {string.Join(", ", missingSilos)} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待从具体数据中收到统计数据 silo 相对 silo.
    /// </summary>
    /// <param name="fromSilo">下 silo 发送统计的那个人。</param>
    /// <param name="receiverSilo">下 silo 统计应收到。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>收到事件有效载荷。</returns>
    public async Task<DeploymentLoadPublisherEvents.Received> WaitForStatisticsReceivedAsync(SiloAddress fromSilo, SiloAddress receiverSilo, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        var existingMatch = _receivedEvents.FirstOrDefault(e => e.FromSilo.Equals(fromSilo) && e.ReceiverSilo.Equals(receiverSilo));
        if (existingMatch is not null)
        {
            return existingMatch;
        }

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var match = _receivedEvents.FirstOrDefault(e => e.FromSilo.Equals(fromSilo) && e.ReceiverSilo.Equals(receiverSilo));
            if (match is not null)
            {
                return match;
            }
        }

        throw new TimeoutException($"Timed out waiting for statistics from {fromSilo} to be received by {receiverSilo} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待特定数量的统计数据公布活动。
    /// </summary>
    /// <param name="expectedCount">等待的最小事件数量 。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    public async Task WaitForPublishedCountAsync(int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            if (_publishedEvents.Count >= expectedCount)
            {
                return;
            }

            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (_publishedEvents.Count >= expectedCount)
        {
            return;
        }

        throw new TimeoutException($"Timed out waiting for {expectedCount} statistics published events. Current count: {_publishedEvents.Count} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待特定数量的群组统计更新活动。
    /// </summary>
    /// <param name="expectedCount">等待的最小事件数量 。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    public async Task WaitForClusterRefreshedCountAsync(int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            if (_clusterRefreshedEvents.Count >= expectedCount)
            {
                return;
            }

            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (_clusterRefreshedEvents.Count >= expectedCount)
        {
            return;
        }

        throw new TimeoutException($"Timed out waiting for {expectedCount} cluster statistics refreshed events. Current count: {_clusterRefreshedEvents.Count} after {effectiveTimeout}");
    }

    /// <summary>
    /// 获得特定 silo的组群更新事件数 。
    /// </summary>
    /// <param name="siloAddress">用于过滤的 silo 地址。</param>
    /// <returns>刷新事件计数。</returns>
    public int GetClusterRefreshCount(SiloAddress siloAddress)
    {
        return _clusterRefreshedEvents.Count(e => e.SiloAddress.Equals(siloAddress));
    }

    /// <summary>
    /// 清除所有记录的事件。 可用于在测试阶段之间重建状态 。
    /// </summary>
    public void Clear()
    {
        _publishedEvents.Clear();
        _receivedEvents.Clear();
        _clusterRefreshedEvents.Clear();
        _removedEvents.Clear();
    }

    void IObserver<DeploymentLoadPublisherEvents.DeploymentLoadPublisherEvent>.OnNext(DeploymentLoadPublisherEvents.DeploymentLoadPublisherEvent value)
    {
        switch (value)
        {
            case DeploymentLoadPublisherEvents.Published published:
                _logger?.LogDebug("PlacementDiagnosticObserver: StatisticsPublished from {SiloAddress}", published.SiloAddress);
                _publishedEvents.Add(published);
                break;
            case DeploymentLoadPublisherEvents.Received received:
                _logger?.LogDebug("PlacementDiagnosticObserver: StatisticsReceived from {FromSilo} by {ReceiverSilo}", received.FromSilo, received.ReceiverSilo);
                _receivedEvents.Add(received);
                break;
            case DeploymentLoadPublisherEvents.ClusterRefreshed clusterRefreshed:
                _logger?.LogDebug("PlacementDiagnosticObserver: ClusterStatisticsRefreshed on {SiloAddress}, SiloCount={SiloCount}", clusterRefreshed.SiloAddress, clusterRefreshed.Statistics.Count);
                _clusterRefreshedEvents.Add(clusterRefreshed);
                break;
            case DeploymentLoadPublisherEvents.Removed removed:
                _logger?.LogDebug("PlacementDiagnosticObserver: StatisticsRemoved for {RemovedSilo} on {ObserverSilo}", removed.RemovedSilo, removed.ObserverSilo);
                _removedEvents.Add(removed);
                break;
        }
    }

    void IObserver<DeploymentLoadPublisherEvents.DeploymentLoadPublisherEvent>.OnError(Exception error)
    {
    }

    void IObserver<DeploymentLoadPublisherEvents.DeploymentLoadPublisherEvent>.OnCompleted()
    {
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
