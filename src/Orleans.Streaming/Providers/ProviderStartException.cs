using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 供应商未能启动时,就提出例外。
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
        /// <param name="message">电文。</param>
        public ProviderStartException(string message)
            : base(message)
        { }

        /// <summary>
        /// 初始化 <see cref="ProviderStartException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="innerException">内在的例外</param>
        public ProviderStartException(string message, Exception innerException)
            : base(message, innerException)
        { }

        /// <summary>
        /// 初始化 <see cref="ProviderStartException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private ProviderStartException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }
}
