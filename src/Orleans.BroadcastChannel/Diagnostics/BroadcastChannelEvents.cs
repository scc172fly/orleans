using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.BroadcastChannel.Diagnostics;

/// <summary>
/// 提供 Orleans 广播通道事件的诊断侦听器及事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公共类型，但在次要版本之间可能发生变化。它们适用于
/// 模拟测试和诊断等高级场景。
/// </remarks>
public static class BroadcastChannelEvents
{
    /// <summary>
    /// 广播通道事件的诊断侦听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.BroadcastChannel";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获取所有广播通道事件的可观察序列。
    /// </summary>
    public static IObservable<BroadcastChannelEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 广播通道诊断事件使用的基类。
    /// </summary>
    /// <param name="providerName">广播通道提供程序的名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果存在）。</param>
    /// <param name="clusterId">与事件关联的集群标识符。</param>
    public abstract class BroadcastChannelEvent(string providerName, SiloAddress? siloAddress, string clusterId)
    {
        /// <summary>
        /// 初始化一个不含集群标识的事件。
        /// </summary>
        /// <param name="providerName">广播通道提供程序的名称。</param>
        protected BroadcastChannelEvent(string providerName)
            : this(providerName, siloAddress: null, clusterId: string.Empty)
        {
        }

        /// <summary>
        /// 广播通道提供程序的名称。
        /// </summary>
        public readonly string ProviderName = providerName;

        /// <summary>
        /// 与事件关联的 Silo 地址（如果存在）。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;

        /// <summary>
        /// 与事件关联的集群标识符。
        /// </summary>
        public readonly string ClusterId = clusterId;
    }

    /// <summary>
    /// 向广播通道发布元素时的事件有效载荷。
    /// </summary>
    /// <param name="providerName">广播通道提供程序的名称。</param>
    /// <param name="channelId">通道 ID。</param>
    /// <param name="subscriberCount">将接收此元素的订阅者数量。</param>
    /// <param name="siloAddress">发布该元素的 Silo 的地址（如果存在）。</param>
    /// <param name="clusterId">发布该元素的集群的标识符。</param>
    public sealed class ItemPublished(
        string providerName,
        ChannelId channelId,
        int subscriberCount,
        SiloAddress? siloAddress,
        string clusterId) : BroadcastChannelEvent(providerName, siloAddress, clusterId)
    {
        /// <summary>
        /// 初始化一个不含集群标识的事件。
        /// </summary>
        public ItemPublished(string providerName, ChannelId channelId, int subscriberCount)
            : this(providerName, channelId, subscriberCount, siloAddress: null, clusterId: string.Empty)
        {
        }

        /// <summary>
        /// 通道 ID。
        /// </summary>
        public readonly ChannelId ChannelId = channelId;

        /// <summary>
        /// 将接收此元素的订阅者数量。
        /// </summary>
        public readonly int SubscriberCount = subscriberCount;
    }

    /// <summary>
    /// 向广播通道订阅者投递元素时的事件有效载荷。
    /// </summary>
    /// <param name="providerName">广播通道提供程序的名称。</param>
    /// <param name="channelId">通道 ID。</param>
    /// <param name="consumerGrainId">消费者的 Grain ID。</param>
    /// <param name="siloAddress">投递该元素的 Silo 的地址。</param>
    /// <param name="clusterId">投递该元素的集群的标识符。</param>
    public sealed class ItemDelivered(
        string providerName,
        ChannelId channelId,
        GrainId consumerGrainId,
        SiloAddress? siloAddress,
        string clusterId) : BroadcastChannelEvent(providerName, siloAddress, clusterId)
    {
        /// <summary>
        /// 初始化一个不含集群标识的事件。
        /// </summary>
        public ItemDelivered(string providerName, ChannelId channelId, GrainId consumerGrainId)
            : this(providerName, channelId, consumerGrainId, siloAddress: null, clusterId: string.Empty)
        {
        }

        /// <summary>
        /// 通道 ID。
        /// </summary>
        public readonly ChannelId ChannelId = channelId;

        /// <summary>
        /// 消费者的 Grain ID。
        /// </summary>
        public readonly GrainId ConsumerGrainId = consumerGrainId;
    }

    internal static void EmitItemPublished(string providerName, ChannelId channelId, int subscriberCount, SiloAddress? siloAddress, string clusterId)
    {
        if (!Listener.IsEnabled(nameof(ItemPublished)))
        {
            return;
        }

        Emit(providerName, channelId, subscriberCount, siloAddress, clusterId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string providerName, ChannelId channelId, int subscriberCount, SiloAddress? siloAddress, string clusterId)
        {
            Listener.Write(nameof(ItemPublished), new ItemPublished(
                providerName,
                channelId,
                subscriberCount,
                siloAddress,
                clusterId));
        }
    }

    internal static void EmitItemDelivered(string providerName, ChannelId channelId, GrainId consumerGrainId, SiloAddress siloAddress, string clusterId)
    {
        if (!Listener.IsEnabled(nameof(ItemDelivered)))
        {
            return;
        }

        Emit(providerName, channelId, consumerGrainId, siloAddress, clusterId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string providerName, ChannelId channelId, GrainId consumerGrainId, SiloAddress siloAddress, string clusterId)
        {
            Listener.Write(nameof(ItemDelivered), new ItemDelivered(
                providerName,
                channelId,
                consumerGrainId,
                siloAddress,
                clusterId));
        }
    }

    private sealed class Observable : IObservable<BroadcastChannelEvent>
    {
        public IDisposable Subscribe(IObserver<BroadcastChannelEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<BroadcastChannelEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();
            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is BroadcastChannelEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
