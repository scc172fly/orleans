using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// Orleans 运行时用于报告错误的异常类。
    /// </summary>
    /// <remarks>
    /// 它也是 Orleans 运行时抛出的各种更具体异常的
    /// 基类。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public class OrleansException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="OrleansException"/> 类的新实例。
        /// </summary>
        public OrleansException()
            : base("Unexpected error.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        public OrleansException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        /// <param name="innerException">
        /// 内部异常。
        /// </param>
        public OrleansException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansException"/> 类的新实例。
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
        protected OrleansException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
