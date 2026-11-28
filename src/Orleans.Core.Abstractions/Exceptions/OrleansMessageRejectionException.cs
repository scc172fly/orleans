using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 指示 Orleans 消息已被拒绝。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class OrleansMessageRejectionException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansMessageRejectionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        internal OrleansMessageRejectionException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansMessageRejectionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        /// <param name="innerException">
        /// 内部异常。
        /// </param>
        internal OrleansMessageRejectionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansMessageRejectionException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        /// <exception cref="SerializationException">
        /// 类名为 <see langword="null"/>，或者 <see cref="P:System.Exception.HResult"/> 为零（0）。
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="info"/> 为 <see langword="null"/>。
        /// </exception>
        [Obsolete]
        protected OrleansMessageRejectionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

