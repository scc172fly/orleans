using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 提供程序启动失败时抛出的异常。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ProviderStartException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="ProviderStartException"/> 类的新实例。
        /// </summary>
        public ProviderStartException()
        { }

        /// <summary>
        /// 初始化 <see cref="ProviderStartException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        public ProviderStartException(string message)
            : base(message)
        { }

        /// <summary>
        /// 初始化 <see cref="ProviderStartException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        public ProviderStartException(string message, Exception innerException)
            : base(message, innerException)
        { }

        /// <summary>
        /// 初始化 <see cref="ProviderStartException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private ProviderStartException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }
}
