using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using StreamingEvents = Orleans.Streaming.Diagnostics.StreamingEvents;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans 流流诊断事件并提供
/// 确定等待流事件的方法。
/// </summary>
/// <remarks>
/// 用途 <c>System.Reactive</c> 操作操作符 <c>Replay()</c> ,这样, <c>WaitFor*</c> 方法
/// 与过去和今后的事件相匹配,且无拖延、事件驱动的等待。
/// </remarks>
public sealed class StreamingDiagnosticObserver : IDisposable
{
    private readonly IConnectableObservable<StreamingEvents.StreamingEvent> _events;
    private readonly IDisposable _connection;

    /// <summary>
    /// 创建观察者的新实例,并开始监听流诊断事件。
    /// </summary>
    public static StreamingDiagnosticObserver Create(TestCluster cluster) => new(DiagnosticObserverSiloScope.For(cluster));

    public static StreamingDiagnosticObserver Create(InProcessTestCluster cluster) => new(DiagnosticObserverSiloScope.For(cluster));

    public static StreamingDiagnosticObserver Create(SiloAddress siloAddress) => new(DiagnosticObserverSiloScope.For(siloAddress));

    private StreamingDiagnosticObserver(DiagnosticObserverSiloScope scope)
    {
        _events = StreamingEvents.AllEvents
            .Where(e => e is StreamingEvents.ItemDelivered delivered
                ? scope.Matches(delivered.SiloAddress, delivered.ClusterId)
                : scope.Matches(e.SiloAddress))
            .Replay();
        _connection = _events.Connect();
    }

    /// <summary>
    /// 等待对提供商更改队列所有权 。
    /// </summary>
    public async Task<StreamingEvents.BalancerChanged> WaitForBalancerChangedAsync(string? streamProvider, SiloAddress? siloAddress, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.BalancerChanged>()
            .FirstAsync(e => MatchesProviderAndSilo(e.StreamProvider, e.SiloAddress, streamProvider, siloAddress))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待一个拉动代理开始队列。
    /// </summary>
    public async Task<StreamingEvents.PullingAgentStarted> WaitForPullingAgentStartedAsync(string? streamProvider, SiloAddress? siloAddress, QueueId? queueId, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.PullingAgentStarted>()
            .FirstAsync(e => MatchesProviderSiloAndQueue(e.StreamProvider, e.SiloAddress, e.QueueId, streamProvider, siloAddress, queueId))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待一个供应商开始的具体数量的拉动剂。
    /// </summary>
    public async Task WaitForPullingAgentStartedCountAsync(string? streamProvider, int expectedCount, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.PullingAgentStarted>()
            .Where(e => MatchesProvider(e.StreamProvider, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待一个拉动代理停下来排队
    /// </summary>
    public async Task<StreamingEvents.PullingAgentStopped> WaitForPullingAgentStoppedAsync(string? streamProvider, SiloAddress? siloAddress, QueueId? queueId, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.PullingAgentStopped>()
            .FirstAsync(e => MatchesProviderSiloAndQueue(e.StreamProvider, e.SiloAddress, e.QueueId, streamProvider, siloAddress, queueId))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待队列接收器初始化 。
    /// </summary>
    public async Task<StreamingEvents.QueueReceiverInitialized> WaitForQueueReceiverInitializedAsync(string? streamProvider, SiloAddress? siloAddress, QueueId? queueId, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.QueueReceiverInitialized>()
            .FirstAsync(e => MatchesProviderSiloAndQueue(e.StreamProvider, e.SiloAddress, e.QueueId, streamProvider, siloAddress, queueId))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待启动一个提供者的具体数量队列接收器。
    /// </summary>
    public async Task WaitForQueueReceiverInitializedCountAsync(string? streamProvider, int expectedCount, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.QueueReceiverInitialized>()
            .Where(e => MatchesProvider(e.StreamProvider, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待队列接收器初始化失败 。
    /// </summary>
    public async Task<StreamingEvents.QueueReceiverInitializationFailed> WaitForQueueReceiverInitializationFailedAsync(string? streamProvider, SiloAddress? siloAddress, QueueId? queueId, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.QueueReceiverInitializationFailed>()
            .FirstAsync(e => MatchesProviderSiloAndQueue(e.StreamProvider, e.SiloAddress, e.QueueId, streamProvider, siloAddress, queueId))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待一个拉动代理注册本地流条目
    /// </summary>
    public async Task<StreamingEvents.PullingAgentStreamRegistered> WaitForPullingAgentStreamRegisteredAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.PullingAgentStreamRegistered>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待着一个拉动剂流注册失败。
    /// </summary>
    public async Task<StreamingEvents.PullingAgentStreamRegistrationFailed> WaitForPullingAgentStreamRegistrationFailedAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.PullingAgentStreamRegistrationFailed>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待信息传递到特定流上。
    /// </summary>
    public async Task<StreamingEvents.MessageDelivered> WaitForMessageDeliveredAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.MessageDelivered>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待在流上发送特定数量的信息。
    /// </summary>
    public async Task WaitForDeliveryCountAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.MessageDelivered>()
            .Where(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待在一条流上交付的具体数量的具体个别物品。
    /// </summary>
    /// <remarks>
    /// 与批量交付的 <see cref="WaitForDeliveryCountAsync"/> 不同,
    /// 这种方法在批次中计数单个项目。
    /// </remarks>
    public async Task WaitForItemDeliveryCountAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        var actualCount = 0;
        try
        {
            await _events
                .OfType<StreamingEvents.ItemDelivered>()
                .Where(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
                .Do(_ => Interlocked.Increment(ref actualCount))
                .Take(expectedCount)
                .LastOrDefaultAsync()
                .ToTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"Canceled while waiting for {expectedCount} item deliveries on stream {streamId} from provider '{streamProvider ?? "<any>"}'; observed {Volatile.Read(ref actualCount)}.",
                exception,
                cancellationToken);
        }
    }

    /// <summary>
    /// 等待向某一订阅单位交付特定数量的个别物品。
    /// </summary>
    public async Task WaitForItemDeliveryCountAsync(StreamId streamId, Guid subscriptionId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        var actualCount = 0;
        try
        {
            await _events
                .OfType<StreamingEvents.ItemDelivered>()
                .Where(e => MatchesSubscription(e.StreamId, e.SubscriptionId, e.StreamProvider, streamId, subscriptionId, streamProvider))
                .Do(_ => Interlocked.Increment(ref actualCount))
                .Take(expectedCount)
                .LastOrDefaultAsync()
                .ToTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"Canceled while waiting for {expectedCount} item deliveries on stream {streamId}, subscription {subscriptionId}, from provider '{streamProvider ?? "<any>"}'; observed {Volatile.Read(ref actualCount)}.",
                exception,
                cancellationToken);
        }
    }

    /// <summary>
    /// 等待含有特定数量项目的信息在流上发送,然后
    /// 以让该流报告光标排空后的过渡。
    /// </summary>
    public async Task WaitForItemDeliveryAndCursorDrainAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .Where(e => e switch
            {
                StreamingEvents.MessageDelivered message => MatchesStream(message.StreamId, message.StreamProvider, streamId, streamProvider),
                StreamingEvents.ConsumerCursorDrained drained => MatchesStream(drained.StreamId, drained.StreamProvider, streamId, streamProvider),
                _ => false,
            })
            .Scan((DeliveredCount: 0, Drained: false), (state, evt) => evt switch
            {
                StreamingEvents.MessageDelivered message => (state.DeliveredCount + message.Batch.GetEvents<object>().Count(), state.Drained),
                StreamingEvents.ConsumerCursorDrained when state.DeliveredCount >= expectedCount => (state.DeliveredCount, true),
                _ => state,
            })
            .FirstAsync(state => state.Drained)
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待含有特定订阅对象所要发送的具体项目数量的信件,然后
    /// 订阅用户的光标排空过渡需要报告。
    /// </summary>
    public async Task WaitForItemDeliveryAndCursorDrainAsync(StreamId streamId, Guid subscriptionId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .Where(e => e switch
            {
                StreamingEvents.MessageDelivered message => MatchesSubscription(message.StreamId, message.SubscriptionId, message.StreamProvider, streamId, subscriptionId, streamProvider),
                StreamingEvents.ConsumerCursorDrained drained => MatchesSubscription(drained.StreamId, drained.SubscriptionId, drained.StreamProvider, streamId, subscriptionId, streamProvider),
                _ => false,
            })
            .Scan((DeliveredCount: 0, Drained: false), (state, evt) => evt switch
            {
                StreamingEvents.MessageDelivered message => (state.DeliveredCount + message.Batch.GetEvents<object>().Count(), state.Drained),
                StreamingEvents.ConsumerCursorDrained when state.DeliveredCount >= expectedCount => (state.DeliveredCount, true),
                _ => state,
            })
            .FirstAsync(state => state.Drained)
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待流成为不活动 。
    /// </summary>
    public async Task<StreamingEvents.StreamInactive> WaitForStreamInactiveAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.StreamInactive>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待在特定流中添加订阅。
    /// </summary>
    public async Task<StreamingEvents.SubscriptionAdded> WaitForSubscriptionAddedAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.SubscriptionAdded>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待在流上添加特定数量的订阅数。
    /// </summary>
    public async Task WaitForSubscriptionAddedCountAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.SubscriptionAdded>()
            .Where(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待订阅在特定流上永久注册。
    /// </summary>
    public async Task<StreamingEvents.SubscriptionRegistered> WaitForSubscriptionRegisteredAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.SubscriptionRegistered>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待特定数目的订阅在一条流上永久登记。
    /// </summary>
    public async Task WaitForSubscriptionRegisteredCountAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.SubscriptionRegistered>()
            .Where(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待特定流的订阅。
    /// </summary>
    public async Task<StreamingEvents.SubscriptionAttached> WaitForSubscriptionAttachedAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.SubscriptionAttached>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待在一条流上附加一定数量的订阅。
    /// </summary>
    public async Task WaitForSubscriptionAttachedCountAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.SubscriptionAttached>()
            .Where(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待在特定流中取消订阅。
    /// </summary>
    public async Task<StreamingEvents.SubscriptionRemoved> WaitForSubscriptionRemovedAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.SubscriptionRemoved>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待从特定流中永久删除订阅。
    /// </summary>
    public async Task<StreamingEvents.SubscriptionUnregistered> WaitForSubscriptionUnregisteredAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.SubscriptionUnregistered>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待特定订阅从特定流中永久删除。
    /// </summary>
    public async Task<StreamingEvents.SubscriptionUnregistered> WaitForSubscriptionUnregisteredAsync(StreamId streamId, Guid subscriptionId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.SubscriptionUnregistered>()
            .FirstAsync(e => MatchesSubscription(e.StreamId, e.SubscriptionId, e.StreamProvider, streamId, subscriptionId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待从流中永久删除特定数量的订阅。
    /// </summary>
    public async Task WaitForSubscriptionUnregisteredCountAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.SubscriptionUnregistered>()
            .Where(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待订阅从特定流中分离出来。
    /// </summary>
    public async Task<StreamingEvents.SubscriptionDetached> WaitForSubscriptionDetachedAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.SubscriptionDetached>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待从流中分离出特定数量的订阅数。
    /// </summary>
    public async Task WaitForSubscriptionDetachedCountAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.SubscriptionDetached>()
            .Where(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待消费光标排入特定的流。
    /// </summary>
    public async Task<StreamingEvents.ConsumerCursorDrained> WaitForConsumerCursorDrainedAsync(StreamId streamId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.ConsumerCursorDrained>()
            .FirstAsync(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待消费光标排干,以获取特定订阅。
    /// </summary>
    public async Task<StreamingEvents.ConsumerCursorDrained> WaitForConsumerCursorDrainedAsync(StreamId streamId, Guid subscriptionId, string? streamProvider, CancellationToken cancellationToken)
    {
        return await _events
            .OfType<StreamingEvents.ConsumerCursorDrained>()
            .FirstAsync(e => MatchesSubscription(e.StreamId, e.SubscriptionId, e.StreamProvider, streamId, subscriptionId, streamProvider))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待特定数量的消费光标排干流中的事件。
    /// </summary>
    public async Task WaitForConsumerCursorDrainedCountAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.ConsumerCursorDrained>()
            .Where(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待特定数量的消费光标耗尽的事件,以订阅具体用户。
    /// </summary>
    public async Task WaitForConsumerCursorDrainedCountAsync(StreamId streamId, Guid subscriptionId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.ConsumerCursorDrained>()
            .Where(e => MatchesSubscription(e.StreamId, e.SubscriptionId, e.StreamProvider, streamId, subscriptionId, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 等待在流上删除特定数量的订阅。
    /// </summary>
    public async Task WaitForSubscriptionRemovedCountAsync(StreamId streamId, int expectedCount, string? streamProvider, CancellationToken cancellationToken)
    {
        await _events
            .OfType<StreamingEvents.SubscriptionRemoved>()
            .Where(e => MatchesStream(e.StreamId, e.StreamProvider, streamId, streamProvider))
            .Take(expectedCount)
            .LastOrDefaultAsync()
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool MatchesStream(StreamId eventStreamId, string eventStreamProvider, StreamId streamId, string? streamProvider)
    {
        return eventStreamId == streamId
            && MatchesProvider(eventStreamProvider, streamProvider);
    }

    private static bool MatchesSubscription(StreamId eventStreamId, Guid eventSubscriptionId, string eventStreamProvider, StreamId streamId, Guid subscriptionId, string? streamProvider)
    {
        return eventStreamId == streamId
            && eventSubscriptionId == subscriptionId
            && MatchesProvider(eventStreamProvider, streamProvider);
    }

    private static bool MatchesProviderSiloAndQueue(string eventStreamProvider, SiloAddress? eventSiloAddress, QueueId eventQueueId, string? streamProvider, SiloAddress? siloAddress, QueueId? queueId)
    {
        return MatchesProviderAndSilo(eventStreamProvider, eventSiloAddress, streamProvider, siloAddress)
            && (queueId is null || eventQueueId.Equals(queueId.Value));
    }

    private static bool MatchesProviderAndSilo(string eventStreamProvider, SiloAddress? eventSiloAddress, string? streamProvider, SiloAddress? siloAddress)
    {
        return MatchesProvider(eventStreamProvider, streamProvider)
            && (siloAddress is null || eventSiloAddress?.Equals(siloAddress) == true);
    }

    private static bool MatchesProvider(string eventStreamProvider, string? streamProvider)
    {
        return streamProvider is null || eventStreamProvider == streamProvider;
    }

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();
}

/// <summary>
/// 与流诊断观察员合作的推广方法。
/// </summary>
public static class StreamingDiagnosticExtensions
{
}
