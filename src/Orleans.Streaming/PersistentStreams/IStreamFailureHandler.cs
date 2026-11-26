using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 处理流流故障的功能。
    /// </summary>
    public interface IStreamFailureHandler
    {
        /// <summary>
        /// 获得一个值,显示在错误发生时订阅是否应该失败。
        /// </summary>
        /// <value><see langword="true" /> 如果订阅错误时发生故障,则该用户应出错;否则, <see langword="false" />。</value>
        bool ShouldFaultSubsriptionOnError { get; }

        /// <summary>
        /// 一旦用尽了向消费者提供活动的所有措施,便要求采取一切措施。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="streamIdentity">流体身份。</param>
        /// <param name="sequenceToken">序列符号。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task OnDeliveryFailure(GuidId subscriptionId, string streamProviderName, StreamId streamIdentity, StreamSequenceToken? sequenceToken);

        /// <summary>
        /// 建立订阅失败时应该调用 。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="streamIdentity">流体身份。</param>
        /// <param name="sequenceToken">序列符号。</param>
        /// <returns>A <see cref="Task" /> 代表操作。</returns>
        Task OnSubscriptionFailure(GuidId subscriptionId, string streamProviderName, StreamId streamIdentity, StreamSequenceToken? sequenceToken);
    }
}
