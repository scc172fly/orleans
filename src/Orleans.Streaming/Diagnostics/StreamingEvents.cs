using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Streaming.Diagnostics;

/// <summary>
/// 提供 Orleans 流动事件的诊断听众和事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断等先进假设情景。
/// </remarks>
public static class StreamingEvents
{
    /// <summary>
    /// 流出事件诊断听众的名字 。
    /// </summary>
    public const string ListenerName = "Orleans.Streaming";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获得所有流动事件的可观测序列。
    /// </summary>
    public static IObservable<StreamingEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 用于流动诊断事件的基准类。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo 如果与事件有关,请予以说明。</param>
    public abstract class StreamingEvent(
        string streamProvider,
        SiloAddress? siloAddress)
    {
        /// <summary>
        /// 流源提供者的名称 。
        /// </summary>
        public readonly string StreamProvider = streamProvider;

        /// <summary>
        /// 与事件相关的 silo 地址(如果有的话)。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;
    }

    /// <summary>
    /// 当流信息被发送到消费者时,事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="subscriptionId">消费者的订阅身份证</param>
    /// <param name="siloAddress">大会主席的地址 silo 处理此发送。</param>
    /// <param name="consumer">消费者端点。</param>
    /// <param name="batch">交付的批次。</param>
    public sealed class MessageDelivered(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress,
        IAddressable consumer,
        IBatchContainer batch) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 消费者的订阅身份证
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 消费者终点
        /// </summary>
        public readonly IAddressable Consumer = consumer;

        /// <summary>
        /// 交付的批次。
        /// </summary>
        public readonly IBatchContainer Batch = batch;
    }

    /// <summary>
    /// 当溪流因无活动而失去作用时,事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="inactivityPeriod">配置的无活动期。</param>
    /// <param name="siloAddress">大会主席的地址 silo 发生这种情况的地方。</param>
    public sealed class StreamInactive(
        string streamProvider,
        StreamId streamId,
        TimeSpan inactivityPeriod,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 配置的无活动期 。
        /// </summary>
        public readonly TimeSpan InactivityPeriod = inactivityPeriod;
    }

    /// <summary>
    /// 添加串流订阅时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="subscriptionId">订阅编号。</param>
    /// <param name="consumerGrainId">下 grain 消费者的身份。</param>
    /// <param name="siloAddress">处理此订阅事宜的 silo 地址。</param>
    public sealed class SubscriptionAdded(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        GrainId consumerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅身份证
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 消费者的 grain 身份。
        /// </summary>
        public readonly GrainId ConsumerGrainId = consumerGrainId;
    }

    /// <summary>
    /// 当一条溪流的订阅在普鲁次次州注册时,该事件的有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="subscriptionId">订阅编号。</param>
    /// <param name="consumerGrainId">下 grain 消费者的身份。</param>
    /// <param name="siloAddress">大会主席的地址 silo 处理此注册事宜 。</param>
    public sealed class SubscriptionRegistered(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        GrainId consumerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅身份证
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 消费者的 grain 身份。
        /// </summary>
        public readonly GrainId ConsumerGrainId = consumerGrainId;
    }

    /// <summary>
    /// 当连接着一个拉动剂并随时可以接收数据时,用户会使用流的有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="subscriptionId">订阅编号。</param>
    /// <param name="consumerGrainId">下 grain 消费者的身份。</param>
    /// <param name="siloAddress">处理此附件的 silo 地址。</param>
    public sealed class SubscriptionAttached(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        GrainId consumerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅身份证
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 消费者的 grain 身份。
        /// </summary>
        public readonly GrainId ConsumerGrainId = consumerGrainId;
    }

    /// <summary>
    /// 取消串流订阅时的事件有效载荷 。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="subscriptionId">订阅编号。</param>
    /// <param name="siloAddress">处理此订阅事宜的 silo 地址。</param>
    public sealed class SubscriptionRemoved(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅身份证
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;
    }

    /// <summary>
    /// 当流订阅从阴极状态中永久去除时, 事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="subscriptionId">订阅编号。</param>
    /// <param name="siloAddress">处理此清除的 silo 地址。</param>
    public sealed class SubscriptionUnregistered(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅身份证
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;
    }

    /// <summary>
    /// 当连接流时从拖动剂中分离出来, 事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="subscriptionId">订阅编号。</param>
    /// <param name="siloAddress">大会主席的地址 silo 处理这个分遣队。</param>
    public sealed class SubscriptionDetached(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅身份证
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;
    }

    /// <summary>
    /// 当生产商在阴道状态为流体进行可久性注册时,该物质的有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="producerGrainId">生产者 grain ID。</param>
    /// <param name="siloAddress">大会主席的地址 silo 处理此注册事宜 。</param>
    public sealed class ProducerRegistered(
        string streamProvider,
        StreamId streamId,
        GrainId producerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 制片人 grain ID。
        /// </summary>
        public readonly GrainId ProducerGrainId = producerGrainId;
    }

    /// <summary>
    /// 当生产商从阴道状态中为溪流永久清除时, 事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="producerGrainId">生产者 grain ID。</param>
    /// <param name="siloAddress">处理此清除的 silo 地址。</param>
    public sealed class ProducerUnregistered(
        string streamProvider,
        StreamId streamId,
        GrainId producerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 制片人 grain ID。
        /// </summary>
        public readonly GrainId ProducerGrainId = producerGrainId;
    }

    /// <summary>
    /// 当消费光标被排干,不再有现有工作时,活动有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="subscriptionId">订阅编号。</param>
    /// <param name="siloAddress">处理此光标的 silo 地址。</param>
    public sealed class ConsumerCursorDrained(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅身份证
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;
    }

    /// <summary>
    /// 当从流批次中将个别物品交付消费者时,事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="subscriptionId">消费者的订阅身份证</param>
    /// <param name="siloAddress">大会主席的地址 silo 处理此发送。</param>
    /// <param name="clusterId">处理此交付的组群识别符号。</param>
    /// <param name="sequenceToken">交付项目的序列符号。</param>
    public sealed class ItemDelivered(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress,
        string clusterId,
        StreamSequenceToken? sequenceToken) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 初始化一个没有群集识别的事件 。
        /// </summary>
        public ItemDelivered(
            string streamProvider,
            StreamId streamId,
            Guid subscriptionId,
            SiloAddress? siloAddress,
            StreamSequenceToken? sequenceToken)
            : this(streamProvider, streamId, subscriptionId, siloAddress, clusterId: string.Empty, sequenceToken)
        {
        }

        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 消费者的订阅身份证
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 与交付有关的组别识别符号。
        /// </summary>
        public readonly string ClusterId = clusterId;

        /// <summary>
        /// 交付物品的顺序符号 。
        /// </summary>
        public readonly StreamSequenceToken? SequenceToken = sequenceToken;
    }

    /// <summary>
    /// 重新平衡结束后,当队列所有权发生变化时,事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo.</param>
    /// <param name="previousQueues">更改前的队列所有。</param>
    /// <param name="currentQueues">更改后所有队列。</param>
    /// <param name="queueBalancer">队列平衡器实例。</param>
    public sealed class BalancerChanged(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId[] previousQueues,
        QueueId[] currentQueues,
        IStreamQueueBalancer queueBalancer) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 更改前的队列为所有 。
        /// </summary>
        public readonly QueueId[] PreviousQueues = previousQueues;

        /// <summary>
        /// 换了之后的队列都归他们所有
        /// </summary>
        public readonly QueueId[] CurrentQueues = currentQueues;

        /// <summary>
        /// 排队平衡器
        /// </summary>
        public readonly IStreamQueueBalancer QueueBalancer = queueBalancer;
    }

    /// <summary>
    /// 当拉动代理经理报告其当前队列任务时, 事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo.</param>
    /// <param name="currentQueues">管理器目前拥有的队列。</param>
    /// <param name="runningAgents">运行的拖动代理器数。</param>
    public sealed class PullingAgentManagerState(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId[] currentQueues,
        int runningAgents) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 管理员目前拥有的队列 。
        /// </summary>
        public readonly QueueId[] CurrentQueues = currentQueues;

        /// <summary>
        /// 运行中的拖车代理商数量 。
        /// </summary>
        public readonly int RunningAgents = runningAgents;
    }

    /// <summary>
    /// 以部署为基础的队列平衡器完成 silo 成熟期过渡时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">运行队列平衡器的 silo 地址。</param>
    /// <param name="maturedSiloAddress">下 silo 其到期期已满。</param>
    /// <param name="isLocalSilo">到期的 silo 是否是本地的 silo。</param>
    /// <param name="queueBalancer">队列平衡器实例。</param>
    public sealed class QueueBalancerMaturityCompleted(
        string streamProvider,
        SiloAddress? siloAddress,
        SiloAddress maturedSiloAddress,
        bool isLocalSilo,
        IStreamQueueBalancer queueBalancer) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 到期期已满的 silo 。
        /// </summary>
        public readonly SiloAddress MaturedSiloAddress = maturedSiloAddress;

        /// <summary>
        /// 成熟的 silo 是本地的 silo。
        /// </summary>
        public readonly bool IsLocalSilo = isLocalSilo;

        /// <summary>
        /// 排队平衡器
        /// </summary>
        public readonly IStreamQueueBalancer QueueBalancer = queueBalancer;
    }

    /// <summary>
    /// 当持续流拉动剂开始排队时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo.</param>
    /// <param name="queueId">拖拉机处理的队列。</param>
    /// <param name="dueTime">队列泵计时器的初始到期时间。</param>
    /// <param name="period">队列泵计时器周期。</param>
    public sealed class PullingAgentStarted(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId,
        TimeSpan dueTime,
        TimeSpan period) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 由拉车代理处理的队列
        /// </summary>
        public readonly QueueId QueueId = queueId;

        /// <summary>
        /// 队列泵计时器的初始到期时间 。
        /// </summary>
        public readonly TimeSpan DueTime = dueTime;

        /// <summary>
        /// 排队泵计时周期。
        /// </summary>
        public readonly TimeSpan Period = period;
    }

    /// <summary>
    /// 当持续流拉动剂停止排队时, 事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo.</param>
    /// <param name="queueId">拖拉机处理的队列。</param>
    public sealed class PullingAgentStopped(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 由拉车代理处理的队列
        /// </summary>
        public readonly QueueId QueueId = queueId;
    }

    /// <summary>
    /// 当持续流动拉动剂队列接收器初始化时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo.</param>
    /// <param name="queueId">初始化队列。</param>
    public sealed class QueueReceiverInitialized(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 初始化队列 。
        /// </summary>
        public readonly QueueId QueueId = queueId;
    }

    /// <summary>
    /// 当持续流动拉动剂队列接收器无法初始化时的事件有效载荷 。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo.</param>
    /// <param name="queueId">无法初始化的队列。</param>
    /// <param name="exception">初始化例外。</param>
    public sealed class QueueReceiverInitializationFailed(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId,
        Exception exception) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 无法初始化的队列 。
        /// </summary>
        public readonly QueueId QueueId = queueId;

        /// <summary>
        /// 初始化例外。
        /// </summary>
        public readonly Exception Exception = exception;
    }

    /// <summary>
    /// 当持久性流流拉动剂登记本地流条目时,事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo.</param>
    /// <param name="queueId">拖拉机处理的队列。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="producerGrainId">拖拉机代理 grain 身份证明在酒吧注册为制片人</param>
    /// <param name="subscriberCount">公共空间分机返回的无过失用户数量。</param>
    public sealed class PullingAgentStreamRegistered(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId,
        StreamId streamId,
        GrainId producerGrainId,
        int subscriberCount) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 由拉车代理处理的队列
        /// </summary>
        public readonly QueueId QueueId = queueId;

        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 在酒吧注册为生产者的拉动剂 grain 身份识别证。
        /// </summary>
        public readonly GrainId ProducerGrainId = producerGrainId;

        /// <summary>
        /// 公共空间分机返回的无过失用户数量。
        /// </summary>
        public readonly int SubscriberCount = subscriberCount;
    }

    /// <summary>
    /// 当持久性流流拉动剂未能注册本地流条目时, 事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流体提供者的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo.</param>
    /// <param name="queueId">拖拉机处理的队列。</param>
    /// <param name="streamId">流代号。</param>
    /// <param name="exception">登记例外。</param>
    public sealed class PullingAgentStreamRegistrationFailed(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId,
        StreamId streamId,
        Exception exception) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 由拉车代理处理的队列
        /// </summary>
        public readonly QueueId QueueId = queueId;

        /// <summary>
        /// 溪的身份证
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 登记例外。
        /// </summary>
        public readonly Exception Exception = exception;
    }

    internal static bool IsPullingAgentStartedEnabled() => Listener.IsEnabled(nameof(PullingAgentStarted));

    internal static void EmitPullingAgentStarted(string streamProviderName, SiloAddress? siloAddress, QueueId queueId, TimeSpan dueTime, TimeSpan period)
    {
        if (!IsPullingAgentStartedEnabled())
        {
            return;
        }

        Emit(streamProviderName, siloAddress, queueId, dueTime, period);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, SiloAddress? siloAddress, QueueId queueId, TimeSpan dueTime, TimeSpan period)
        {
            Listener.Write(nameof(PullingAgentStarted), new PullingAgentStarted(
                streamProviderName,
                siloAddress,
                queueId,
                dueTime,
                period));
        }
    }

    internal static bool IsPullingAgentStoppedEnabled() => Listener.IsEnabled(nameof(PullingAgentStopped));

    internal static void EmitPullingAgentStopped(string streamProviderName, SiloAddress? siloAddress, QueueId queueId)
    {
        if (!IsPullingAgentStoppedEnabled())
        {
            return;
        }

        Emit(streamProviderName, siloAddress, queueId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, SiloAddress? siloAddress, QueueId queueId)
        {
            Listener.Write(nameof(PullingAgentStopped), new PullingAgentStopped(
                streamProviderName,
                siloAddress,
                queueId));
        }
    }

    internal static bool IsQueueReceiverInitializedEnabled() => Listener.IsEnabled(nameof(QueueReceiverInitialized));

    internal static void EmitQueueReceiverInitialized(string streamProviderName, SiloAddress? siloAddress, QueueId queueId)
    {
        if (!IsQueueReceiverInitializedEnabled())
        {
            return;
        }

        Emit(streamProviderName, siloAddress, queueId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, SiloAddress? siloAddress, QueueId queueId)
        {
            Listener.Write(nameof(QueueReceiverInitialized), new QueueReceiverInitialized(
                streamProviderName,
                siloAddress,
                queueId));
        }
    }

    internal static bool IsQueueReceiverInitializationFailedEnabled() => Listener.IsEnabled(nameof(QueueReceiverInitializationFailed));

    internal static void EmitQueueReceiverInitializationFailed(string streamProviderName, SiloAddress? siloAddress, QueueId queueId, Exception exception)
    {
        if (!IsQueueReceiverInitializationFailedEnabled())
        {
            return;
        }

        Emit(streamProviderName, siloAddress, queueId, exception);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, SiloAddress? siloAddress, QueueId queueId, Exception exception)
        {
            Listener.Write(nameof(QueueReceiverInitializationFailed), new QueueReceiverInitializationFailed(
                streamProviderName,
                siloAddress,
                queueId,
                exception));
        }
    }

    internal static bool IsPullingAgentStreamRegisteredEnabled() => Listener.IsEnabled(nameof(PullingAgentStreamRegistered));

    internal static void EmitPullingAgentStreamRegistered(string streamProviderName, SiloAddress? siloAddress, QueueId queueId, StreamId streamId, GrainId producerGrainId, int subscriberCount)
    {
        if (!IsPullingAgentStreamRegisteredEnabled())
        {
            return;
        }

        Emit(streamProviderName, siloAddress, queueId, streamId, producerGrainId, subscriberCount);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, SiloAddress? siloAddress, QueueId queueId, StreamId streamId, GrainId producerGrainId, int subscriberCount)
        {
            Listener.Write(nameof(PullingAgentStreamRegistered), new PullingAgentStreamRegistered(
                streamProviderName,
                siloAddress,
                queueId,
                streamId,
                producerGrainId,
                subscriberCount));
        }
    }

    internal static bool IsPullingAgentStreamRegistrationFailedEnabled() => Listener.IsEnabled(nameof(PullingAgentStreamRegistrationFailed));

    internal static void EmitPullingAgentStreamRegistrationFailed(string streamProviderName, SiloAddress? siloAddress, QueueId queueId, StreamId streamId, Exception exception)
    {
        if (!IsPullingAgentStreamRegistrationFailedEnabled())
        {
            return;
        }

        Emit(streamProviderName, siloAddress, queueId, streamId, exception);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, SiloAddress? siloAddress, QueueId queueId, StreamId streamId, Exception exception)
        {
            Listener.Write(nameof(PullingAgentStreamRegistrationFailed), new PullingAgentStreamRegistrationFailed(
                streamProviderName,
                siloAddress,
                queueId,
                streamId,
                exception));
        }
    }

    internal static void EmitMessageDelivered(string streamProviderName, StreamConsumerData consumerData, IBatchContainer batch, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(MessageDelivered)))
        {
            return;
        }

        Emit(streamProviderName, consumerData, batch, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamConsumerData consumerData, IBatchContainer batch, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(MessageDelivered), new MessageDelivered(
                streamProviderName,
                consumerData.StreamId.StreamId,
                consumerData.SubscriptionId.Guid,
                siloAddress,
                consumerData.StreamConsumer,
                batch));
        }
    }

    internal static void EmitStreamInactive(string streamProviderName, StreamId streamId, TimeSpan inactivityPeriod, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(StreamInactive)))
        {
            return;
        }

        Emit(streamProviderName, streamId, inactivityPeriod, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, TimeSpan inactivityPeriod, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(StreamInactive), new StreamInactive(
                streamProviderName,
                streamId,
                inactivityPeriod,
                siloAddress));
        }
    }

    internal static void EmitSubscriptionAdded(string streamProviderName, StreamId streamId, Guid subscriptionId, GrainId consumerGrainId, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(SubscriptionAdded)))
        {
            return;
        }

        Emit(streamProviderName, streamId, subscriptionId, consumerGrainId, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, Guid subscriptionId, GrainId consumerGrainId, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(SubscriptionAdded), new SubscriptionAdded(
                streamProviderName,
                streamId,
                subscriptionId,
                consumerGrainId,
                siloAddress));
        }
    }

    internal static void EmitSubscriptionRegistered(string streamProviderName, StreamId streamId, Guid subscriptionId, GrainId consumerGrainId, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(SubscriptionRegistered)))
        {
            return;
        }

        Emit(streamProviderName, streamId, subscriptionId, consumerGrainId, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, Guid subscriptionId, GrainId consumerGrainId, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(SubscriptionRegistered), new SubscriptionRegistered(
                streamProviderName,
                streamId,
                subscriptionId,
                consumerGrainId,
                siloAddress));
        }
    }

    internal static void EmitSubscriptionAttached(string streamProviderName, StreamId streamId, Guid subscriptionId, GrainId consumerGrainId, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(SubscriptionAttached)))
        {
            return;
        }

        Emit(streamProviderName, streamId, subscriptionId, consumerGrainId, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, Guid subscriptionId, GrainId consumerGrainId, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(SubscriptionAttached), new SubscriptionAttached(
                streamProviderName,
                streamId,
                subscriptionId,
                consumerGrainId,
                siloAddress));
        }
    }

    internal static void EmitSubscriptionRemoved(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(SubscriptionRemoved)))
        {
            return;
        }

        Emit(streamProviderName, streamId, subscriptionId, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(SubscriptionRemoved), new SubscriptionRemoved(
                streamProviderName,
                streamId,
                subscriptionId,
                siloAddress));
        }
    }

    internal static void EmitSubscriptionUnregistered(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(SubscriptionUnregistered)))
        {
            return;
        }

        Emit(streamProviderName, streamId, subscriptionId, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(SubscriptionUnregistered), new SubscriptionUnregistered(
                streamProviderName,
                streamId,
                subscriptionId,
                siloAddress));
        }
    }

    internal static void EmitSubscriptionDetached(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(SubscriptionDetached)))
        {
            return;
        }

        Emit(streamProviderName, streamId, subscriptionId, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(SubscriptionDetached), new SubscriptionDetached(
                streamProviderName,
                streamId,
                subscriptionId,
                siloAddress));
        }
    }

    internal static void EmitProducerRegistered(string streamProviderName, StreamId streamId, GrainId producerGrainId, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(ProducerRegistered)))
        {
            return;
        }

        Emit(streamProviderName, streamId, producerGrainId, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, GrainId producerGrainId, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(ProducerRegistered), new ProducerRegistered(
                streamProviderName,
                streamId,
                producerGrainId,
                siloAddress));
        }
    }

    internal static void EmitProducerUnregistered(string streamProviderName, StreamId streamId, GrainId producerGrainId, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(ProducerUnregistered)))
        {
            return;
        }

        Emit(streamProviderName, streamId, producerGrainId, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, GrainId producerGrainId, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(ProducerUnregistered), new ProducerUnregistered(
                streamProviderName,
                streamId,
                producerGrainId,
                siloAddress));
        }
    }

    internal static void EmitConsumerCursorDrained(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(ConsumerCursorDrained)))
        {
            return;
        }

        Emit(streamProviderName, streamId, subscriptionId, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(ConsumerCursorDrained), new ConsumerCursorDrained(
                streamProviderName,
                streamId,
                subscriptionId,
                siloAddress));
        }
    }

    internal static void EmitItemDelivered(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress, string clusterId, StreamSequenceToken? sequenceToken)
    {
        if (!Listener.IsEnabled(nameof(ItemDelivered)))
        {
            return;
        }

        Emit(streamProviderName, streamId, subscriptionId, siloAddress, clusterId, sequenceToken);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, StreamId streamId, Guid subscriptionId, SiloAddress? siloAddress, string clusterId, StreamSequenceToken? sequenceToken)
        {
            Listener.Write(nameof(ItemDelivered), new ItemDelivered(
                streamProviderName,
                streamId,
                subscriptionId,
                siloAddress,
                clusterId,
                sequenceToken));
        }
    }

    internal static bool IsBalancerChangedEnabled() => Listener.IsEnabled(nameof(BalancerChanged));

    internal static void EmitQueueChange(string streamProviderName, SiloAddress? siloAddress, QueueId[] oldQueues, QueueId[] newQueues, IStreamQueueBalancer queueBalancer)
    {
        if (!IsBalancerChangedEnabled())
        {
            return;
        }

        Emit(streamProviderName, siloAddress, oldQueues, newQueues, queueBalancer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(
            string streamProviderName,
            SiloAddress? siloAddress,
            QueueId[] oldQueues,
            QueueId[] newQueues,
            IStreamQueueBalancer queueBalancer)
        {
            Listener.Write(nameof(BalancerChanged), new BalancerChanged(
                streamProviderName,
                siloAddress,
                oldQueues,
                newQueues,
                queueBalancer));
        }
    }

    internal static void EmitPullingAgentManagerState(string streamProviderName, SiloAddress? siloAddress, IEnumerable<QueueId> currentQueues, int runningAgents)
    {
        if (!Listener.IsEnabled(nameof(PullingAgentManagerState)))
        {
            return;
        }

        Emit(streamProviderName, siloAddress, currentQueues, runningAgents);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string streamProviderName, SiloAddress? siloAddress, IEnumerable<QueueId> currentQueues, int runningAgents)
        {
            Listener.Write(nameof(PullingAgentManagerState), new PullingAgentManagerState(
                streamProviderName,
                siloAddress,
                [.. currentQueues],
                runningAgents));
        }
    }

    internal static void EmitQueueBalancerMaturityCompleted(
        string streamProviderName,
        SiloAddress? siloAddress,
        SiloAddress maturedSiloAddress,
        bool isLocalSilo,
        IStreamQueueBalancer queueBalancer)
    {
        if (!Listener.IsEnabled(nameof(QueueBalancerMaturityCompleted)))
        {
            return;
        }

        Emit(streamProviderName, siloAddress, maturedSiloAddress, isLocalSilo, queueBalancer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(
            string streamProviderName,
            SiloAddress? siloAddress,
            SiloAddress maturedSiloAddress,
            bool isLocalSilo,
            IStreamQueueBalancer queueBalancer)
        {
            Listener.Write(nameof(QueueBalancerMaturityCompleted), new QueueBalancerMaturityCompleted(
                streamProviderName,
                siloAddress,
                maturedSiloAddress,
                isLocalSilo,
                queueBalancer));
        }
    }

    private sealed class Observable : IObservable<StreamingEvent>
    {
        public IDisposable Subscribe(IObserver<StreamingEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<StreamingEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();
            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is StreamingEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
