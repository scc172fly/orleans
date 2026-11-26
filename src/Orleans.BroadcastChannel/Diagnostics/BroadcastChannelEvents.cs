using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.BroadcastChannel.Diagnostics;

/// <summary>
/// 为 Orleans 广播频道活动提供诊断听众和事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断等先进假设情景。
/// </remarks>
public static class BroadcastChannelEvents
{
    /// <summary>
    /// 广播频道活动诊断听众的姓名。
    /// </summary>
    public const string ListenerName = "Orleans.BroadcastChannel";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获得所有广播频道活动的可观测序列。
    /// </summary>
    public static IObservable<BroadcastChannelEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 用于广播频道诊断活动的基础班。
    /// </summary>
    /// <param name="providerName">广播频道提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo 如果与事件有关,请予以说明。</param>
    /// <param name="clusterId">与事件相关的组群识别符号。</param>
    public abstract class BroadcastChannelEvent(string providerName, SiloAddress? siloAddress, string clusterId)
    {
        /// <summary>
        /// 初始化一个没有群集识别的事件 。
        /// </summary>
        /// <param name="providerName">广播频道提供者的名称。</param>
        protected BroadcastChannelEvent(string providerName)
            : this(providerName, siloAddress: null, clusterId: string.Empty)
        {
        }

        /// <summary>
        /// 广播频道供应商的名字。
        /// </summary>
        public readonly string ProviderName = providerName;

        /// <summary>
        /// 与事件相关的 silo 地址(如果有的话)。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;

        /// <summary>
        /// 与事件相关的组别标识符 。
        /// </summary>
        public readonly string ClusterId = clusterId;
    }

    /// <summary>
    /// 当某物品发布到广播频道时,该事件的有效有效载荷。
    /// </summary>
    /// <param name="providerName">广播频道提供者的名称。</param>
    /// <param name="channelId">频道 ID。</param>
    /// <param name="subscriberCount">将接收此项目的用户数。</param>
    /// <param name="siloAddress">大会主席的地址 silo 如果有的话,公布该项目。</param>
    /// <param name="clusterId">发布该集组的标识符 。</param>
    public sealed class ItemPublished(
        string providerName,
        ChannelId channelId,
        int subscriberCount,
        SiloAddress? siloAddress,
        string clusterId) : BroadcastChannelEvent(providerName, siloAddress, clusterId)
    {
        /// <summary>
        /// 初始化一个没有群集识别的事件 。
        /// </summary>
        public ItemPublished(string providerName, ChannelId channelId, int subscriberCount)
            : this(providerName, channelId, subscriberCount, siloAddress: null, clusterId: string.Empty)
        {
        }

        /// <summary>
        /// 频道的身份证
        /// </summary>
        public readonly ChannelId ChannelId = channelId;

        /// <summary>
        /// 将接收此项目的用户数。
        /// </summary>
        public readonly int SubscriberCount = subscriberCount;
    }

    /// <summary>
    /// 当物品交付给广播频道用户时,该物体的有效有效载荷。
    /// </summary>
    /// <param name="providerName">广播频道提供者的名称。</param>
    /// <param name="channelId">频道 ID。</param>
    /// <param name="consumerGrainId">下 grain 消费者的身份。</param>
    /// <param name="siloAddress">大会主席的地址 silo 发送项目。</param>
    /// <param name="clusterId">交付项目的组的识别码 。</param>
    public sealed class ItemDelivered(
        string providerName,
        ChannelId channelId,
        GrainId consumerGrainId,
        SiloAddress? siloAddress,
        string clusterId) : BroadcastChannelEvent(providerName, siloAddress, clusterId)
    {
        /// <summary>
        /// 初始化一个没有群集识别的事件 。
        /// </summary>
        public ItemDelivered(string providerName, ChannelId channelId, GrainId consumerGrainId)
            : this(providerName, channelId, consumerGrainId, siloAddress: null, clusterId: string.Empty)
        {
        }

        /// <summary>
        /// 频道的身份证
        /// </summary>
        public readonly ChannelId ChannelId = channelId;

        /// <summary>
        /// 消费者的 grain 身份。
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
