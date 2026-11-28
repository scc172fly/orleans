using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Orleans.BroadcastChannel;
using Orleans.BroadcastChannel.Diagnostics;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans 广播通道诊断事件的测试辅助工具，提供
/// 确定性等待广播通道事件的方法。
/// </summary>
/// <remarks>
/// 使用 <c>System.Reactive</c> 运算符及 <c>Replay()</c>，使 <c>WaitFor*</c> 方法
/// 可匹配过去及未来的事件，采用零延迟的事件驱动等待。
/// </remarks>
public sealed class BroadcastChannelDiagnosticObserver : IDisposable
{
    private readonly IConnectableObservable<BroadcastChannelEvents.BroadcastChannelEvent> _events;
    private readonly IDisposable _connection;

    /// <summary>
    /// 创建观察器的新实例，并开始监听广播通道诊断事件。
    /// </summary>
    public static BroadcastChannelDiagnosticObserver Create(TestCluster cluster) => new(DiagnosticObserverSiloScope.For(cluster));

    public static BroadcastChannelDiagnosticObserver Create(InProcessTestCluster cluster) => new(DiagnosticObserverSiloScope.For(cluster));

    private BroadcastChannelDiagnosticObserver(DiagnosticObserverSiloScope scope)
    {
        _events = BroadcastChannelEvents.AllEvents.Where(e => scope.Matches(e.SiloAddress, e.ClusterId)).Replay();
        _connection = _events.Connect();
    }

    /// <summary>
    /// 等待某个项发布到指定通道。
    /// </summary>
    public async Task<BroadcastChannelEvents.ItemPublished> WaitForItemPublishedAsync(ChannelId channelId, string? providerName, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<BroadcastChannelEvents.ItemPublished>()
            .FirstAsync(e => MatchesChannel(e.ChannelId, e.ProviderName, channelId, providerName))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待某个项投递给指定通道订阅者。
    /// </summary>
    public async Task<BroadcastChannelEvents.ItemDelivered> WaitForItemDeliveredAsync(ChannelId channelId, string? providerName, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<BroadcastChannelEvents.ItemDelivered>()
            .FirstAsync(e => MatchesChannel(e.ChannelId, e.ProviderName, channelId, providerName))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待指定数量的项通过某个通道投递。
    /// </summary>
    public async Task WaitForDeliveryCountAsync(ChannelId channelId, int expectedCount, string? providerName, CancellationToken cancellationToken)
    {
        await _events
            .OfType<BroadcastChannelEvents.ItemDelivered>()
            .Where(e => MatchesChannel(e.ChannelId, e.ProviderName, channelId, providerName))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待指定数量的项发布到某个通道。
    /// </summary>
    public async Task WaitForPublishCountAsync(ChannelId channelId, int expectedCount, string? providerName, CancellationToken cancellationToken)
    {
        await _events
            .OfType<BroadcastChannelEvents.ItemPublished>()
            .Where(e => MatchesChannel(e.ChannelId, e.ProviderName, channelId, providerName))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool MatchesChannel(ChannelId eventChannelId, string eventProviderName, ChannelId channelId, string? providerName)
    {
        return eventChannelId == channelId
            && (providerName is null || eventProviderName == providerName);
    }

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();
}
