using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Streams.Core;

namespace Orleans.Streams
{
    /// <summary>
    /// 流源供应商管理器提供方-向导界面
    /// </summary>
    internal interface IStreamProviderRuntime : IProviderRuntime
    {
        /// <summary>
        /// 获取当前执行的 grain 或客户端对象的不透明身份。 
        /// </summary>
        /// <remarks>用于伐木目的的</remarks>。
        string ExecutingEntityIdentity();

        /// <summary>
        /// 返回流目录。
        /// </summary>
        /// <returns>流体目录。</returns>
        StreamDirectory GetStreamDirectory();

        /// <summary>
        /// 备用运行界面
        /// </summary>
        /// <returns></returns>
        IStreamPubSub? PubSub(StreamPubSubType pubSubType);
    }

    /// <summary>
    /// 流源供应商管理器提供方-向导界面
    /// </summary>
    internal interface ISiloSideStreamProviderRuntime : IStreamProviderRuntime
    {
        /// <summary>启动给定的持久性流提供者的拉动剂。</summary>
        Task<IPersistentStreamPullingManager> InitializePullingAgents(
            string streamProviderName,
            IQueueAdapterFactory adapterFactory,
            IQueueAdapter queueAdapter,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// 确定流源供应商可以使用的出版/订阅系统类型。
    /// </summary>
    public enum StreamPubSubType
    {
        /// <summary>
        /// 支持在运行时创建的明确订阅和以 grain 类型宣布的默示订阅。
        /// </summary>
        ExplicitGrainBasedAndImplicit,

        /// <summary>
        /// 只支持运行时创建的明确订阅。
        /// </summary>
        ExplicitGrainBasedOnly,

        /// <summary>
        /// 支持以 grain 类型宣布的默示订阅,并从最低限度公用/子控制机间接费用的 grain 元数据中解决这些订阅问题。
        /// 明确订阅使用 <see cref="ExplicitGrainBasedAndImplicit"/> 或 <see cref="ExplicitGrainBasedOnly"/>。
        /// </summary>
        ImplicitOnly,
    }

    /// <summary>
    /// 提供供应源代码的酒吧/子控制机,用于登记生产商和消费者以及询问订阅国。
    /// </summary>
    public interface IStreamPubSub // 比较与: IPubSubRendezvousGrain
    {
        /// <summary>
        /// 登记流的生产者,并返回生产者应当与之连接的积极消费者登记。
        /// </summary>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <param name="streamProducer">制作人的身份识别符</param>
        /// <returns>流流的活性消费者登记。</returns>
        Task<ISet<PubSubSubscriptionState>> RegisterProducer(QualifiedStreamId streamId, GrainId streamProducer);

        /// <summary>
        /// 注册一个流体生产者。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamProducer">生产者识别码。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>串流订阅集。</returns>
        Task<ISet<PubSubSubscriptionState>> RegisterProducer(QualifiedStreamId streamId, GrainId streamProducer, CancellationToken cancellationToken)
            => RegisterProducer(streamId, streamProducer);

        /// <summary>
        /// 使一个小溪的生产者不再注册
        /// </summary>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <param name="streamProducer">制作人的身份识别符</param>
        /// <returns>代表操作的任务。</returns>
        Task UnregisterProducer(QualifiedStreamId streamId, GrainId streamProducer);

        /// <summary>
        /// 不再登记一个流的生产者。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamProducer">生产者识别码。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task UnregisterProducer(QualifiedStreamId streamId, GrainId streamProducer, CancellationToken cancellationToken)
            => UnregisterProducer(streamId, streamProducer);

        /// <summary>
        /// 注册用户订阅流。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <param name="streamConsumer">消费者的身份识别符</param>
        /// <param name="filterData">与订阅有关的序列式过滤数据。</param>
        /// <returns>代表操作的任务。</returns>
        Task RegisterConsumer(GuidId subscriptionId, QualifiedStreamId streamId, GrainId streamConsumer, string? filterData);

        /// <summary>
        /// 注册了流生消费者。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamConsumer">消费者识别资料</param>
        /// <param name="filterData">可选过滤数据。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task RegisterConsumer(GuidId subscriptionId, QualifiedStreamId streamId, GrainId streamConsumer, string? filterData, CancellationToken cancellationToken)
            => RegisterConsumer(subscriptionId, streamId, streamConsumer, filterData);

        /// <summary>
        /// 拒绝注册用户订阅的一条流。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <returns>代表操作的任务。</returns>
        Task UnregisterConsumer(GuidId subscriptionId, QualifiedStreamId streamId);

        /// <summary>
        /// 拒绝注册 流消费者。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task UnregisterConsumer(GuidId subscriptionId, QualifiedStreamId streamId, CancellationToken cancellationToken)
            => UnregisterConsumer(subscriptionId, streamId);

        /// <summary>
        /// 获取流的注册制片人数量 。
        /// </summary>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <returns>注册生产者人数。</returns>
        Task<int> ProducerCount(QualifiedStreamId streamId);

        /// <summary>
        /// 获取注册的制作者数量。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>生产者的数量。</returns>
        Task<int> ProducerCount(QualifiedStreamId streamId, CancellationToken cancellationToken)
            => ProducerCount(streamId);

        /// <summary>
        /// 获取流的注册消费者数量。
        /// </summary>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <returns>注册消费者人数。</returns>
        Task<int> ConsumerCount(QualifiedStreamId streamId);

        /// <summary>
        /// 获取注册的用户数量。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>消费者人数。</returns>
        Task<int> ConsumerCount(QualifiedStreamId streamId, CancellationToken cancellationToken)
            => ConsumerCount(streamId);

        /// <summary>
        /// 获得流流的活跃订阅,可选择限于特定消费者。
        /// </summary>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <param name="streamConsumer">
        /// 返回订阅的消费者的身份识别符,或返回所有订阅的默认值。
        /// </param>
        /// <returns>匹配的当前订阅。</returns>
        Task<List<StreamSubscription>> GetAllSubscriptions(QualifiedStreamId streamId, GrainId streamConsumer = default);

        /// <summary>
        /// 获得一条流的订阅。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamConsumer">消费者识别特征的任择。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>流流订阅。</returns>
        Task<List<StreamSubscription>> GetAllSubscriptions(QualifiedStreamId streamId, GrainId streamConsumer, CancellationToken cancellationToken)
            => GetAllSubscriptions(streamId, streamConsumer);

        /// <summary>
        /// 为流消费者创建订阅标识符。
        /// </summary>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <param name="streamConsumer">消费者的身份识别符</param>
        /// <returns>订阅标识符。</returns>
        GuidId CreateSubscriptionId(QualifiedStreamId streamId, GrainId streamConsumer);

        /// <summary>
        /// 标记订阅为错误 。
        /// </summary>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <returns><see langword="true"/> 当订阅由本执行部分/子执行部分处理时;否则, <see langword="false"/>.</returns>
        Task<bool> FaultSubscription(QualifiedStreamId streamId, GuidId subscriptionId);

        /// <summary>
        /// 标记订阅为错误 。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns><see langword="true"/> 如果订阅有误;否则, <see langword="false"/>。</returns>
        Task<bool> FaultSubscription(QualifiedStreamId streamId, GuidId subscriptionId, CancellationToken cancellationToken)
            => FaultSubscription(streamId, subscriptionId);
    }
}
