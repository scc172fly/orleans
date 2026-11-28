using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 一个不对故障采取任何操作的 <see cref="IStreamFailureHandler"/>。
    /// </summary>
    public class NoOpStreamDeliveryFailureHandler : IStreamFailureHandler
    {
        /// <summary>
        /// 初始化 <see cref="NoOpStreamDeliveryFailureHandler"/> 类的新实例。
        /// </summary>
        public NoOpStreamDeliveryFailureHandler()
            : this(false)
        {
        }

        /// <summary>
        /// 初始化 <see cref="NoOpStreamDeliveryFailureHandler"/> 类的新实例。
        /// </summary>
        /// <param name="faultOnError">用于 <see cref="ShouldFaultSubsriptionOnError"/> 的值。</param>
        public NoOpStreamDeliveryFailureHandler(bool faultOnError)
        {
            ShouldFaultSubsriptionOnError = faultOnError;
        }

        /// <inheritdoc/>
        public bool ShouldFaultSubsriptionOnError { get; }

        /// <inheritdoc/>
        public Task OnDeliveryFailure(GuidId subscriptionId, string streamProviderName, StreamId streamId,
            StreamSequenceToken? sequenceToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task OnSubscriptionFailure(GuidId subscriptionId, string streamProviderName, StreamId streamId,
            StreamSequenceToken? sequenceToken)
        {
            return Task.CompletedTask;
        }
    }
}
