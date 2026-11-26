using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示存在周期因请求或观察员错误而被取消。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansLifecycleCanceledException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansLifecycleCanceledException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        internal OrleansLifecycleCanceledException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansLifecycleCanceledException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        /// <param name="innerException">
        /// 内在的例外
        /// </param>
        internal OrleansLifecycleCanceledException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansLifecycleCanceledException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        /// <exception cref="SerializationException">类名为 <see langword="null" /> 或 <see cref="P:System.Exception.HResult" /> 为零(0)</exception>
        /// <exception cref="ArgumentNullException"><paramref name="info" /> 是 <see langword="null" />.</exception>
        [Obsolete]
        private OrleansLifecycleCanceledException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

