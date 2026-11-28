using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 指示生命周期已被取消，原因可能是收到取消请求，也可能是观察者出错。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansLifecycleCanceledException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansLifecycleCanceledException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        internal OrleansLifecycleCanceledException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansLifecycleCanceledException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        /// <param name="innerException">
        /// 内部异常。
        /// </param>
        internal OrleansLifecycleCanceledException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansLifecycleCanceledException"/> 类的新实例。
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
        private OrleansLifecycleCanceledException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

