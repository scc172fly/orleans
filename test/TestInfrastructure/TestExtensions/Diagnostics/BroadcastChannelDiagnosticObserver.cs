using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Orleans.BroadcastChannel;
using Orleans.BroadcastChannel.Diagnostics;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans 广播频道诊断事件并提供
/// 选择了等待广播频道活动的方法
/// </summary>
/// <remarks>
/// 用途 <c>System.Reactive</c> 操作操作符 <c>Replay()</c> ,这样, <c>WaitFor*</c> 方法
/// 与过去和今后的事件相匹配,且无拖延、事件驱动的等待。
/// </remarks>
public sealed class BroadcastChannelDiagnosticObserver : IDisposable
{
    private readonly IConnectableObservable<BroadcastChannelEvents.BroadcastChannelEvent> _events;
    private readonly IDisposable _connection;

    /// <summary>
    /// 创建了一个新的观察者实例,并开始收听广播频道诊断事件。
    /// </summary>
    public static BroadcastChannelDiagnosticObserver Create(TestCluster cluster) => new(DiagnosticObserverSiloScope.For(cluster));

    public static BroadcastChannelDiagnosticObserver Create(InProcessTestCluster cluster) => new(DiagnosticObserverSiloScope.For(cluster));

    private BroadcastChannelDiagnosticObserver(DiagnosticObserverSiloScope scope)
    {
        _events = BroadcastChannelEvents.AllEvents.Where(e => scope.Matches(e.SiloAddress, e.ClusterId)).Replay();
        _connection = _events.Connect();
    }

    /// <summary>
    /// 等待某个项目发布到特定频道。
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
    /// 等待某项物品交付给特定频道用户。
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
    /// 等待在频道上交付的具体项目数量。
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
    /// 等待向频道发布特定数量的项目。
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
