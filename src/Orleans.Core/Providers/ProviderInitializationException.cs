using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Providers
{
    /// <summary>
    /// 供应商未能启动时,就提出例外。
    /// </summary>
    [Serializable, GenerateSerializer]
    public sealed class ProviderInitializationException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="ProviderInitializationException"/> 类的新实例。
        /// </summary>
        public ProviderInitializationException()
        { }

        /// <summary>
        /// 初始化 <see cref="ProviderInitializationException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        public ProviderInitializationException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ProviderInitializationException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="innerException">内在的例外</param>
        public ProviderInitializationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ProviderInitializationException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private ProviderInitializationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
