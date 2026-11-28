using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 此异常表示流事件未成功投递给消费者。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class StreamEventDeliveryFailureException : OrleansException
    {
        private const string ErrorStringFormat =
            "Stream provider failed to deliver an event.  StreamProvider:{0}  Stream:{1}";

        /// <summary>
        /// 初始化 <see cref="StreamEventDeliveryFailureException"/> 类的新实例。
        /// </summary>
        public StreamEventDeliveryFailureException() { }

        /// <summary>
        /// 初始化 <see cref="StreamEventDeliveryFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        public StreamEventDeliveryFailureException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="StreamEventDeliveryFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        internal StreamEventDeliveryFailureException(QualifiedStreamId streamId)
            : base(string.Format(ErrorStringFormat, streamId.GetNamespace(), streamId.StreamId)) { }

        /// <summary>
        /// 初始化 <see cref="StreamEventDeliveryFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        public StreamEventDeliveryFailureException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="StreamEventDeliveryFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        public StreamEventDeliveryFailureException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
