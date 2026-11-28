using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Providers
{
    /// <summary>
    /// 当提供程序初始化失败时引发的异常。
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
        /// <param name="message">消息。</param>
        public ProviderInitializationException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ProviderInitializationException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        public ProviderInitializationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ProviderInitializationException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private ProviderInitializationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
