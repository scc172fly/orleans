using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Streams.Core;

namespace Orleans.Streams
{
    /// <summary>
    /// 流提供程序管理器面向提供程序的接口。
    /// </summary>
    internal interface IStreamProviderRuntime : IProviderRuntime
    {
        /// <summary>
        /// 获取当前正在执行的 Grain 或客户端对象的不透明标识。
        /// </summary>
        /// <remarks>公开此信息以供日志记录使用。</remarks>
        string ExecutingEntityIdentity();

        /// <summary>
        /// 返回流目录。
        /// </summary>
        /// <returns>流目录。</returns>
        StreamDirectory GetStreamDirectory();

        /// <summary>
        /// 发布/订阅运行时接口。
        /// </summary>
        /// <returns></returns>
        IStreamPubSub? PubSub(StreamPubSubType pubSubType);
    }

    /// <summary>
    /// 流提供程序管理器面向提供程序的接口。
    /// </summary>
    internal interface ISiloSideStreamProviderRuntime : IStreamProviderRuntime
    {
        /// <summary>启动指定持久化流提供程序的拉取代理。</summary>
        Task<IPersistentStreamPullingManager> InitializePullingAgents(
            string streamProviderName,
            IQueueAdapterFactory adapterFactory,
            IQueueAdapter queueAdapter,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// 标识流提供程序可以使用的发布/订阅系统类型。
    /// </summary>
    public enum StreamPubSubType
    {
        /// <summary>
        /// 支持运行时创建的显式订阅和在 Grain 类型上声明的隐式订阅。
        /// </summary>
        ExplicitGrainBasedAndImplicit,

        /// <summary>
        /// 仅支持运行时创建的显式订阅。
        /// </summary>
        ExplicitGrainBasedOnly,

        /// <summary>
        /// 支持在 Grain 类型上声明的隐式订阅，并通过 Grain 元数据解析这些订阅，以尽量减少发布/订阅控制平面开销。
        /// 显式订阅使用 <see cref="ExplicitGrainBasedAndImplicit"/> 或 <see cref="ExplicitGrainBasedOnly"/>。
        /// </summary>
        ImplicitOnly,
    }

    /// <summary>
    /// 提供流提供程序的发布/订阅控制平面，用于注册生产者和消费者，以及查询订阅状态。
    /// </summary>
    public interface IStreamPubSub // 可对照 IPubSubRendezvousGrain。
    {
        /// <summary>
        /// 为流注册生产者，并返回该生产者应连接的活动消费者注册记录。
        /// </summary>
        /// <param name="streamId">限定流标识符。</param>
        /// <param name="streamProducer">生产者的标识符。</param>
        /// <returns>该流的活动消费者注册记录。</returns>
        Task<ISet<PubSubSubscriptionState>> RegisterProducer(QualifiedStreamId streamId, GrainId streamProducer);

        /// <summary>
        /// 注册流生产者。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamProducer">生产者标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>流订阅集合。</returns>
        Task<ISet<PubSubSubscriptionState>> RegisterProducer(QualifiedStreamId streamId, GrainId streamProducer, CancellationToken cancellationToken)
            => RegisterProducer(streamId, streamProducer);

        /// <summary>
        /// 从流注销生产者。
        /// </summary>
        /// <param name="streamId">限定流标识符。</param>
        /// <param name="streamProducer">生产者的标识符。</param>
        /// <returns>表示该操作的任务。</returns>
        Task UnregisterProducer(QualifiedStreamId streamId, GrainId streamProducer);

        /// <summary>
        /// 注销流生产者。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamProducer">生产者标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task UnregisterProducer(QualifiedStreamId streamId, GrainId streamProducer, CancellationToken cancellationToken)
            => UnregisterProducer(streamId, streamProducer);

        /// <summary>
        /// 为流注册消费者订阅。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">限定流标识符。</param>
        /// <param name="streamConsumer">消费者的标识符。</param>
        /// <param name="filterData">与该订阅关联的序列化筛选数据。</param>
        /// <returns>表示该操作的任务。</returns>
        Task RegisterConsumer(GuidId subscriptionId, QualifiedStreamId streamId, GrainId streamConsumer, string? filterData);

        /// <summary>
        /// 注册流消费者。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamConsumer">消费者标识符。</param>
        /// <param name="filterData">可选的筛选数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task RegisterConsumer(GuidId subscriptionId, QualifiedStreamId streamId, GrainId streamConsumer, string? filterData, CancellationToken cancellationToken)
            => RegisterConsumer(subscriptionId, streamId, streamConsumer, filterData);

        /// <summary>
        /// 从流注销消费者订阅。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">限定流标识符。</param>
        /// <returns>表示该操作的任务。</returns>
        Task UnregisterConsumer(GuidId subscriptionId, QualifiedStreamId streamId);

        /// <summary>
        /// 注销流消费者。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task UnregisterConsumer(GuidId subscriptionId, QualifiedStreamId streamId, CancellationToken cancellationToken)
            => UnregisterConsumer(subscriptionId, streamId);

        /// <summary>
        /// 获取流的已注册生产者数量。
        /// </summary>
        /// <param name="streamId">限定流标识符。</param>
        /// <returns>已注册的生产者数量。</returns>
        Task<int> ProducerCount(QualifiedStreamId streamId);

        /// <summary>
        /// 获取为流注册的生产者数量。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>生产者数量。</returns>
        Task<int> ProducerCount(QualifiedStreamId streamId, CancellationToken cancellationToken)
            => ProducerCount(streamId);

        /// <summary>
        /// 获取流的已注册消费者数量。
        /// </summary>
        /// <param name="streamId">限定流标识符。</param>
        /// <returns>已注册的消费者数量。</returns>
        Task<int> ConsumerCount(QualifiedStreamId streamId);

        /// <summary>
        /// 获取为流注册的消费者数量。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>消费者数量。</returns>
        Task<int> ConsumerCount(QualifiedStreamId streamId, CancellationToken cancellationToken)
            => ConsumerCount(streamId);

        /// <summary>
        /// 获取流的活动订阅，可选择仅返回特定消费者的订阅。
        /// </summary>
        /// <param name="streamId">限定流标识符。</param>
        /// <param name="streamConsumer">
        /// 要返回其订阅的消费者的标识符；传入默认值则返回所有订阅。
        /// </param>
        /// <returns>匹配的活动订阅。</returns>
        Task<List<StreamSubscription>> GetAllSubscriptions(QualifiedStreamId streamId, GrainId streamConsumer = default);

        /// <summary>
        /// 获取流的订阅。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamConsumer">可选的消费者标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>流订阅。</returns>
        Task<List<StreamSubscription>> GetAllSubscriptions(QualifiedStreamId streamId, GrainId streamConsumer, CancellationToken cancellationToken)
            => GetAllSubscriptions(streamId, streamConsumer);

        /// <summary>
        /// 为流消费者创建订阅标识符。
        /// </summary>
        /// <param name="streamId">限定流标识符。</param>
        /// <param name="streamConsumer">消费者的标识符。</param>
        /// <returns>订阅标识符。</returns>
        GuidId CreateSubscriptionId(QualifiedStreamId streamId, GrainId streamConsumer);

        /// <summary>
        /// 将订阅标记为故障状态。
        /// </summary>
        /// <param name="streamId">限定流标识符。</param>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <returns>如果订阅由此发布/订阅实现处理，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        Task<bool> FaultSubscription(QualifiedStreamId streamId, GuidId subscriptionId);

        /// <summary>
        /// 将订阅标记为故障状态。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果订阅已被标记为故障状态，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        Task<bool> FaultSubscription(QualifiedStreamId streamId, GuidId subscriptionId, CancellationToken cancellationToken)
            => FaultSubscription(streamId, subscriptionId);
    }
}
