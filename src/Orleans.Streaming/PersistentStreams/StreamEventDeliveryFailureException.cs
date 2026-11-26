using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 这一例外表明,流事件没有成功地向消费者提供。
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
        /// <param name="message">电文。</param>
        public StreamEventDeliveryFailureException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="StreamEventDeliveryFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        internal StreamEventDeliveryFailureException(QualifiedStreamId streamId)
            : base(string.Format(ErrorStringFormat, streamId.GetNamespace(), streamId.StreamId)) { }

        /// <summary>
        /// 初始化 <see cref="StreamEventDeliveryFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="innerException">内在的例外</param>
        public StreamEventDeliveryFailureException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="StreamEventDeliveryFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        public StreamEventDeliveryFailureException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
