using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 此异常表示流订阅中发生错误，导致该订阅进入
    /// 故障状态。应放弃对故障订阅的处理。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class FaultedSubscriptionException : OrleansException
    {
        private const string ErrorStringFormat =
            "Subscription is in a Faulted state.  Subscription:{0}, Stream:{1}";

        /// <summary>
        /// 初始化 <see cref="FaultedSubscriptionException"/> 类的新实例。
        /// </summary>
        public FaultedSubscriptionException() { }

        /// <summary>
        /// 初始化 <see cref="FaultedSubscriptionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        public FaultedSubscriptionException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="FaultedSubscriptionException"/> 类的新实例。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">流标识符。</param>
        internal FaultedSubscriptionException(GuidId subscriptionId, QualifiedStreamId streamId)
            : base(string.Format(ErrorStringFormat, subscriptionId.Guid, streamId)) { }

        /// <summary>
        /// 初始化 <see cref="FaultedSubscriptionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        public FaultedSubscriptionException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="FaultedSubscriptionException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private FaultedSubscriptionException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
