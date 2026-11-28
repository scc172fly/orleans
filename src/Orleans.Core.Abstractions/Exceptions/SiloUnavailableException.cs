using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示由于目标 Silo 不可用，请求已被取消。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class SiloUnavailableException : OrleansMessageRejectionException
    {
        /// <summary>
        /// 初始化 <see cref="SiloUnavailableException"/> 类的新实例。
        /// </summary>
        public SiloUnavailableException()
            : base("Silo unavailable")
        {
        }

        /// <summary>
        /// 初始化 <see cref="SiloUnavailableException"/> 类的新实例。
        /// </summary>
        /// <param name="msg">
        /// 消息。
        /// </param>
        public SiloUnavailableException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SiloUnavailableException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        /// <param name="innerException">
        /// 内部异常。
        /// </param>
        public SiloUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SiloUnavailableException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        /// <exception cref="SerializationException">类名为 <see langword="null" />，或者 <see cref="P:System.Exception.HResult" /> 为零（0）。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="info" /> 为 <see langword="null" />。</exception>
        [Obsolete]
        private SiloUnavailableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

