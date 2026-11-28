using System.Collections.Concurrent;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans 放置诊断事件的测试辅助工具，提供
/// 确定性等待统计信息传播的方法。
/// </summary>
/// <remarks>
/// 这使放置策略的测试具有确定性：测试可以在执行断言之前等待
/// 统计信息在所有 Silo 上传播完成。
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
    /// 获取放置侦听器的订阅数量，可用于调试。
    /// </summary>
    public int PlacementListenerSubscriptionCount => _placementListenerSubscriptionCount;

    /// <summary>
    /// 获取所有捕获的统计信息发布事件。
    /// </summary>
    public IReadOnlyCollection<DeploymentLoadPublisherEvents.Published> PublishedEvents => _publishedEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的统计信息接收事件。
    /// </summary>
    public IReadOnlyCollection<DeploymentLoadPublisherEvents.Received> ReceivedEvents => _receivedEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的集群统计信息刷新事件。
    /// </summary>
    public IReadOnlyCollection<DeploymentLoadPublisherEvents.ClusterRefreshed> ClusterRefreshedEvents => _clusterRefreshedEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的统计信息移除事件。
    /// </summary>
    public IReadOnlyCollection<DeploymentLoadPublisherEvents.Removed> RemovedEvents => _removedEvents.ToArray();

    /// <summary>
    /// 创建观察器的新实例，并开始监听放置诊断事件。
    /// </summary>
    /// <param name="logger">用于调试输出的可选日志记录器。</param>
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
    /// 等待指定 Silo 发布统计信息。
    /// </summary>
    /// <param name="siloAddress">要等待的 Silo 地址。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>发布事件的有效载荷。</returns>
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
    /// 等待指定 Silo 完成集群统计信息刷新。
    /// </summary>
    /// <param name="siloAddress">应完成刷新的 Silo 地址。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>集群统计信息刷新事件的有效载荷。</returns>
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
    /// 等待集群内所有 Silo 刷新集群统计信息。
    /// 这可用于确保在进行放置断言之前，统计信息已传播完成。
    /// </summary>
    /// <param name="siloAddresses">应进行刷新的所有 Silo 的地址。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>所有 Silo 刷新统计信息后完成的任务。</returns>
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
    /// 等待另一个 Silo 收到指定 Silo 发出的统计信息。
    /// </summary>
    /// <param name="fromSilo">发送统计信息的 Silo。</param>
    /// <param name="receiverSilo">应接收统计信息的 Silo。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>接收事件的有效载荷。</returns>
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
    /// 等待指定数量的统计信息发布事件。
    /// </summary>
    /// <param name="expectedCount">等待的最少事件数量。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
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
    /// 等待指定数量的集群统计信息刷新事件。
    /// </summary>
    /// <param name="expectedCount">等待的最少事件数量。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
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
    /// 获取指定 Silo 的集群统计信息刷新事件数量。
    /// </summary>
    /// <param name="siloAddress">用于筛选的 Silo 地址。</param>
    /// <returns>刷新事件的数量。</returns>
    public int GetClusterRefreshCount(SiloAddress siloAddress)
    {
        return _clusterRefreshedEvents.Count(e => e.SiloAddress.Equals(siloAddress));
    }

    /// <summary>
    /// 清除所有捕获的事件，可用于在测试阶段之间重置状态。
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
