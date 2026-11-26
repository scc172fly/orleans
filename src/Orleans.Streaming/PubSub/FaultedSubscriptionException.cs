using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 此例外表示,订阅时使用
    ///  错误订阅的工作应该放弃。
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
        /// <param name="message">电文。</param>
        public FaultedSubscriptionException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="FaultedSubscriptionException"/> 类的新实例。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">流体识别符号。</param>
        internal FaultedSubscriptionException(GuidId subscriptionId, QualifiedStreamId streamId)
            : base(string.Format(ErrorStringFormat, subscriptionId.Guid, streamId)) { }

        /// <summary>
        /// 初始化 <see cref="FaultedSubscriptionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="innerException">内在的例外</param>
        public FaultedSubscriptionException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="FaultedSubscriptionException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private FaultedSubscriptionException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
