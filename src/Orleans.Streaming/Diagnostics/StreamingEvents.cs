using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Streaming.Diagnostics;

/// <summary>
/// 提供 Orleans 流处理事件的诊断侦听器及事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公开的，但可能在次版本之间发生变化。它们面向
/// 模拟测试和诊断等高级场景。
/// </remarks>
public static class StreamingEvents
{
    /// <summary>
    /// 流处理事件诊断侦听器的名称。
    /// </summary>
    public const string ListenerName = "Orleans.Streaming";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获取所有流处理事件的可观察序列。
    /// </summary>
    public static IObservable<StreamingEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 流处理诊断事件使用的基类。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果存在）。</param>
    public abstract class StreamingEvent(
        string streamProvider,
        SiloAddress? siloAddress)
    {
        /// <summary>
        /// 流提供程序的名称。
        /// </summary>
        public readonly string StreamProvider = streamProvider;

        /// <summary>
        /// 与事件关联的 Silo 地址（如果有）。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;
    }

    /// <summary>
    /// 流消息投递给消费者时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="subscriptionId">消费者的订阅 ID。</param>
    /// <param name="siloAddress">处理此次投递的 Silo 地址。</param>
    /// <param name="consumer">消费者端点。</param>
    /// <param name="batch">已投递的批次。</param>
    public sealed class MessageDelivered(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress,
        IAddressable consumer,
        IBatchContainer batch) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 消费者的订阅 ID。
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 消费者端点。
        /// </summary>
        public readonly IAddressable Consumer = consumer;

        /// <summary>
        /// 已投递的批次。
        /// </summary>
        public readonly IBatchContainer Batch = batch;
    }

    /// <summary>
    /// 流因没有活动而变为非活动状态时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="inactivityPeriod">配置的非活动时长。</param>
    /// <param name="siloAddress">发生此事件的 Silo 地址。</param>
    public sealed class StreamInactive(
        string streamProvider,
        StreamId streamId,
        TimeSpan inactivityPeriod,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 配置的非活动时长。
        /// </summary>
        public readonly TimeSpan InactivityPeriod = inactivityPeriod;
    }

    /// <summary>
    /// 添加流订阅时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="subscriptionId">订阅 ID。</param>
    /// <param name="consumerGrainId">消费者的 Grain ID。</param>
    /// <param name="siloAddress">处理此订阅的 Silo 地址。</param>
    public sealed class SubscriptionAdded(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        GrainId consumerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅 ID。
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 消费者的 Grain ID。
        /// </summary>
        public readonly GrainId ConsumerGrainId = consumerGrainId;
    }

    /// <summary>
    /// 在发布/订阅状态中持久注册流订阅时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="subscriptionId">订阅 ID。</param>
    /// <param name="consumerGrainId">消费者的 Grain ID。</param>
    /// <param name="siloAddress">处理此次注册的 Silo 地址。</param>
    public sealed class SubscriptionRegistered(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        GrainId consumerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅 ID。
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 消费者的 Grain ID。
        /// </summary>
        public readonly GrainId ConsumerGrainId = consumerGrainId;
    }

    /// <summary>
    /// 流订阅附加到拉取代理并准备接收数据时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="subscriptionId">订阅 ID。</param>
    /// <param name="consumerGrainId">消费者的 Grain ID。</param>
    /// <param name="siloAddress">处理此次附加操作的 Silo 地址。</param>
    public sealed class SubscriptionAttached(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        GrainId consumerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅 ID。
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 消费者的 Grain ID。
        /// </summary>
        public readonly GrainId ConsumerGrainId = consumerGrainId;
    }

    /// <summary>
    /// 移除流订阅时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="subscriptionId">订阅 ID。</param>
    /// <param name="siloAddress">处理此订阅的 Silo 地址。</param>
    public sealed class SubscriptionRemoved(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅 ID。
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;
    }

    /// <summary>
    /// 从发布/订阅状态中持久移除流订阅时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="subscriptionId">订阅 ID。</param>
    /// <param name="siloAddress">处理此次移除操作的 Silo 地址。</param>
    public sealed class SubscriptionUnregistered(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅 ID。
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;
    }

    /// <summary>
    /// 流订阅从拉取代理分离时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="subscriptionId">订阅 ID。</param>
    /// <param name="siloAddress">处理此次分离操作的 Silo 地址。</param>
    public sealed class SubscriptionDetached(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅 ID。
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;
    }

    /// <summary>
    /// 为流在发布/订阅状态中持久注册生产者时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="producerGrainId">生产者的 Grain ID。</param>
    /// <param name="siloAddress">处理此次注册的 Silo 地址。</param>
    public sealed class ProducerRegistered(
        string streamProvider,
        StreamId streamId,
        GrainId producerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 生产者的 Grain ID。
        /// </summary>
        public readonly GrainId ProducerGrainId = producerGrainId;
    }

    /// <summary>
    /// 从流的发布/订阅状态中持久移除生产者时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="producerGrainId">生产者的 Grain ID。</param>
    /// <param name="siloAddress">处理此次移除操作的 Silo 地址。</param>
    public sealed class ProducerUnregistered(
        string streamProvider,
        StreamId streamId,
        GrainId producerGrainId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 生产者的 Grain ID。
        /// </summary>
        public readonly GrainId ProducerGrainId = producerGrainId;
    }

    /// <summary>
    /// 消费者游标已读尽且当前没有更多可用工作时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="subscriptionId">订阅 ID。</param>
    /// <param name="siloAddress">处理此游标的 Silo 地址。</param>
    public sealed class ConsumerCursorDrained(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 订阅 ID。
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;
    }

    /// <summary>
    /// 流批次中的单个数据项投递给消费者时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="subscriptionId">消费者的订阅 ID。</param>
    /// <param name="siloAddress">处理此次投递的 Silo 地址。</param>
    /// <param name="clusterId">处理此次投递的集群标识符。</param>
    /// <param name="sequenceToken">已投递数据项的序列令牌。</param>
    public sealed class ItemDelivered(
        string streamProvider,
        StreamId streamId,
        Guid subscriptionId,
        SiloAddress? siloAddress,
        string clusterId,
        StreamSequenceToken? sequenceToken) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 初始化不含集群标识的事件。
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
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 消费者的订阅 ID。
        /// </summary>
        public readonly Guid SubscriptionId = subscriptionId;

        /// <summary>
        /// 与投递关联的集群标识符。
        /// </summary>
        public readonly string ClusterId = clusterId;

        /// <summary>
        /// 已投递数据项的序列令牌。
        /// </summary>
        public readonly StreamSequenceToken? SequenceToken = sequenceToken;
    }

    /// <summary>
    /// 再均衡完成后队列所有权发生变化时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">Silo 的地址。</param>
    /// <param name="previousQueues">变化前拥有的队列。</param>
    /// <param name="currentQueues">变化后拥有的队列。</param>
    /// <param name="queueBalancer">队列均衡器实例。</param>
    public sealed class BalancerChanged(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId[] previousQueues,
        QueueId[] currentQueues,
        IStreamQueueBalancer queueBalancer) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 变化前拥有的队列。
        /// </summary>
        public readonly QueueId[] PreviousQueues = previousQueues;

        /// <summary>
        /// 变化后拥有的队列。
        /// </summary>
        public readonly QueueId[] CurrentQueues = currentQueues;

        /// <summary>
        /// 队列均衡器实例。
        /// </summary>
        public readonly IStreamQueueBalancer QueueBalancer = queueBalancer;
    }

    /// <summary>
    /// 拉取代理管理器报告其当前队列分配时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">Silo 的地址。</param>
    /// <param name="currentQueues">管理器当前拥有的队列。</param>
    /// <param name="runningAgents">正在运行的拉取代理数量。</param>
    public sealed class PullingAgentManagerState(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId[] currentQueues,
        int runningAgents) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 管理器当前拥有的队列。
        /// </summary>
        public readonly QueueId[] CurrentQueues = currentQueues;

        /// <summary>
        /// 正在运行的拉取代理数量。
        /// </summary>
        public readonly int RunningAgents = runningAgents;
    }

    /// <summary>
    /// 基于部署的队列均衡器完成 Silo 成熟状态转换时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">运行队列均衡器的 Silo 地址。</param>
    /// <param name="maturedSiloAddress">成熟期已结束的 Silo。</param>
    /// <param name="isLocalSilo">已成熟的 Silo 是否为本地 Silo。</param>
    /// <param name="queueBalancer">队列均衡器实例。</param>
    public sealed class QueueBalancerMaturityCompleted(
        string streamProvider,
        SiloAddress? siloAddress,
        SiloAddress maturedSiloAddress,
        bool isLocalSilo,
        IStreamQueueBalancer queueBalancer) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 成熟期已结束的 Silo。
        /// </summary>
        public readonly SiloAddress MaturedSiloAddress = maturedSiloAddress;

        /// <summary>
        /// 已成熟的 Silo 是否为本地 Silo。
        /// </summary>
        public readonly bool IsLocalSilo = isLocalSilo;

        /// <summary>
        /// 队列均衡器实例。
        /// </summary>
        public readonly IStreamQueueBalancer QueueBalancer = queueBalancer;
    }

    /// <summary>
    /// 为队列启动持久化流拉取代理时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">Silo 的地址。</param>
    /// <param name="queueId">拉取代理处理的队列。</param>
    /// <param name="dueTime">队列泵定时器的初始触发延迟。</param>
    /// <param name="period">队列泵定时器的周期。</param>
    public sealed class PullingAgentStarted(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId,
        TimeSpan dueTime,
        TimeSpan period) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 拉取代理所处理的队列。
        /// </summary>
        public readonly QueueId QueueId = queueId;

        /// <summary>
        /// 队列泵定时器的初始触发延迟。
        /// </summary>
        public readonly TimeSpan DueTime = dueTime;

        /// <summary>
        /// 队列泵定时器的周期。
        /// </summary>
        public readonly TimeSpan Period = period;
    }

    /// <summary>
    /// 为队列停止持久化流拉取代理时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">Silo 的地址。</param>
    /// <param name="queueId">拉取代理处理的队列。</param>
    public sealed class PullingAgentStopped(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 拉取代理所处理的队列。
        /// </summary>
        public readonly QueueId QueueId = queueId;
    }

    /// <summary>
    /// 持久化流拉取代理的队列接收器初始化时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">Silo 的地址。</param>
    /// <param name="queueId">已初始化的队列。</param>
    public sealed class QueueReceiverInitialized(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 已初始化的队列。
        /// </summary>
        public readonly QueueId QueueId = queueId;
    }

    /// <summary>
    /// 持久化流拉取代理的队列接收器初始化失败时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">Silo 的地址。</param>
    /// <param name="queueId">初始化失败的队列。</param>
    /// <param name="exception">初始化异常。</param>
    public sealed class QueueReceiverInitializationFailed(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId,
        Exception exception) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 初始化失败的队列。
        /// </summary>
        public readonly QueueId QueueId = queueId;

        /// <summary>
        /// 初始化异常。
        /// </summary>
        public readonly Exception Exception = exception;
    }

    /// <summary>
    /// 持久化流拉取代理注册本地流条目时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">Silo 的地址。</param>
    /// <param name="queueId">拉取代理处理的队列。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="producerGrainId">在发布/订阅系统中注册为生产者的拉取代理 Grain ID。</param>
    /// <param name="subscriberCount">发布/订阅系统返回的未处于故障状态的订阅者数量。</param>
    public sealed class PullingAgentStreamRegistered(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId,
        StreamId streamId,
        GrainId producerGrainId,
        int subscriberCount) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 拉取代理所处理的队列。
        /// </summary>
        public readonly QueueId QueueId = queueId;

        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 在发布/订阅系统中注册为生产者的拉取代理 Grain ID。
        /// </summary>
        public readonly GrainId ProducerGrainId = producerGrainId;

        /// <summary>
        /// 发布/订阅系统返回的未处于故障状态的订阅者数量。
        /// </summary>
        public readonly int SubscriberCount = subscriberCount;
    }

    /// <summary>
    /// 持久化流拉取代理注册本地流条目失败时的事件有效载荷。
    /// </summary>
    /// <param name="streamProvider">流提供程序名称。</param>
    /// <param name="siloAddress">Silo 的地址。</param>
    /// <param name="queueId">拉取代理处理的队列。</param>
    /// <param name="streamId">流 ID。</param>
    /// <param name="exception">注册异常。</param>
    public sealed class PullingAgentStreamRegistrationFailed(
        string streamProvider,
        SiloAddress? siloAddress,
        QueueId queueId,
        StreamId streamId,
        Exception exception) : StreamingEvent(streamProvider, siloAddress)
    {
        /// <summary>
        /// 拉取代理所处理的队列。
        /// </summary>
        public readonly QueueId QueueId = queueId;

        /// <summary>
        /// 流 ID。
        /// </summary>
        public readonly StreamId StreamId = streamId;

        /// <summary>
        /// 注册异常。
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
