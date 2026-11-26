using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示一项请求因目标 silo 无法满足而被取消。
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
        /// 密歇根州
        /// </param>
        public SiloUnavailableException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SiloUnavailableException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        /// <param name="innerException">
        /// 内在的例外
        /// </param>
        public SiloUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SiloUnavailableException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 资料
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        /// <exception cref="SerializationException">类名为 <see langword="null" /> 或 <see cref="P:System.Exception.HResult" /> 为零(0)</exception>
        /// <exception cref="ArgumentNullException"><paramref name="info" /> 是 <see langword="null" />.</exception>
        [Obsolete]
        private SiloUnavailableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

