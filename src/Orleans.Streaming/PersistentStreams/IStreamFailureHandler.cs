using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 提供处理流故障的功能。
    /// </summary>
    public interface IStreamFailureHandler
    {
        /// <summary>
        /// 获取一个值，指示发生错误时订阅是否应进入故障状态。
        /// </summary>
        /// <value>如果发生错误时订阅应进入故障状态，则为 <see langword="true" />；否则为 <see langword="false" />。</value>
        bool ShouldFaultSubsriptionOnError { get; }

        /// <summary>
        /// 向消费者投递事件的所有措施均已用尽后调用。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="streamIdentity">流标识。</param>
        /// <param name="sequenceToken">序列令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task OnDeliveryFailure(GuidId subscriptionId, string streamProviderName, StreamId streamIdentity, StreamSequenceToken? sequenceToken);

        /// <summary>
        /// 建立订阅失败时应调用。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="streamIdentity">流标识。</param>
        /// <param name="sequenceToken">序列令牌。</param>
        /// <returns>表示该操作的 <see cref="Task" />。</returns>
        Task OnSubscriptionFailure(GuidId subscriptionId, string streamProviderName, StreamId streamIdentity, StreamSequenceToken? sequenceToken);
    }
}
