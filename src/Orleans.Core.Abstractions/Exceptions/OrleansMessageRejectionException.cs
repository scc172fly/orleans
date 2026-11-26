using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示拒绝 Orleans 电文。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class OrleansMessageRejectionException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansMessageRejectionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        internal OrleansMessageRejectionException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansMessageRejectionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        /// <param name="innerException">
        /// 内在的例外
        /// </param>
        internal OrleansMessageRejectionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansMessageRejectionException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        /// <exception cref="SerializationException">
        /// 类名为 <see langword="null"/> 或 <see cref="P:System.Exception.HResult"/> 零( 0) 。
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="info"/> 是 <see langword="null"/>。
        /// </exception>
        [Obsolete]
        protected OrleansMessageRejectionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

