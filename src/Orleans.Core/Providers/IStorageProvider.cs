using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Storage
{
    /// <summary>
    /// 当尝试调用 Grain 时，如果该 Grain 的存储提供程序配置错误或缺失，则引发此异常。
    /// </summary>
    [Serializable, GenerateSerializer]
    public sealed class BadProviderConfigException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="BadProviderConfigException"/> 类的新实例。
        /// </summary>
        public BadProviderConfigException()
        { }

        /// <summary>
        /// 初始化 <see cref="BadProviderConfigException"/> 类的新实例。
        /// </summary>
        /// <param name="msg">描述错误的消息。</param>
        public BadProviderConfigException(string msg)
            : base(msg)
        { }

        /// <summary>
        /// 初始化 <see cref="BadProviderConfigException"/> 类的新实例。
        /// </summary>
        /// <param name="msg">描述错误的消息。</param>
        /// <param name="exc">导致当前异常的异常。</param>
        public BadProviderConfigException(string msg, Exception exc)
            : base(msg, exc)
        { }

        [Obsolete]
        private BadProviderConfigException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }
}
