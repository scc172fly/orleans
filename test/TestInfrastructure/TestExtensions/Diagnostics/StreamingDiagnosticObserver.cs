using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using StreamingEvents = Orleans.Streaming.Diagnostics.StreamingEvents;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans 流处理诊断事件的测试辅助工具，提供
/// 确定性等待流处理事件的方法。
/// </summary>
/// <remarks>
/// 使用 <c>System.Reactive</c> 运算符及 <c>Replay()</c>，使 <c>WaitFor*</c> 方法
/// 可匹配过去及未来的事件，采用零延迟的事件驱动等待。
/// </remarks>
public sealed class StreamingDiagnosticObserver : IDisposable
{
    private readonly IConnectableObservable<StreamingEvents.StreamingEvent> _events;
    private readonly IDisposable _connection;

    /// <summary>
    /// 创建观察器的新实例，并开始监听流处理诊断事件。
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
    /// 等待指定提供程序的队列所有权发生变化。
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
    /// 等待指定队列的拉取代理启动。
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
    /// 等待指定提供程序出现指定次数的拉取代理启动。
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
    /// 等待指定队列的拉取代理停止。
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
    /// 等待队列接收器初始化完成。
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
    /// 等待指定提供程序的指定数量的队列接收器初始化完成。
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
    /// 等待队列接收器初始化失败。
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
    /// 等待拉取代理注册本地流条目。
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
    /// 等待拉取代理的流注册失败。
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
    /// 等待消息通过指定流投递。
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
    /// 等待指定数量的消息通过某个流投递。
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
    /// 等待指定数量的单个项通过某个流投递。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="WaitForDeliveryCountAsync"/>（统计批次投递次数）不同，
    /// 此方法统计各批次中的单个项。
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
    /// 等待指定数量的单个项投递给特定订阅。
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
    /// 等待包含指定数量项的消息通过某个流投递，并随后
    /// 等待该流报告游标耗尽状态转换。
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
    /// 等待包含指定数量项的消息投递给特定订阅，并随后
    /// 等待报告该订阅的游标耗尽状态转换。
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
    /// 等待流进入非活动状态。
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
    /// 等待在指定流上添加订阅。
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
    /// 等待在某个流上添加指定数量的订阅。
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
    /// 等待在指定流上持久化注册订阅。
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
    /// 等待在某个流上持久化注册指定数量的订阅。
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
    /// 等待订阅附加到指定流。
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
    /// 等待指定数量的订阅附加到某个流。
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
    /// 等待从指定流移除订阅。
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
    /// 等待从指定流持久化移除订阅。
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
    /// 等待从指定流持久化移除特定订阅。
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
    /// 等待从某个流持久化移除指定数量的订阅。
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
    /// 等待订阅从指定流分离。
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
    /// 等待指定数量的订阅从某个流分离。
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
    /// 等待指定流上的消费者游标耗尽。
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
    /// 等待指定订阅的消费者游标耗尽。
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
    /// 等待某个流上指定数量的消费者游标耗尽事件。
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
    /// 等待指定订阅的指定数量的消费者游标耗尽事件。
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
    /// 等待从某个流移除指定数量的订阅。
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
/// 用于流处理诊断观察器的扩展方法。
/// </summary>
public static class StreamingDiagnosticExtensions
{
}
