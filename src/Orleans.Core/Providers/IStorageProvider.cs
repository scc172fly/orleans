using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Storage
{
    /// <summary>
    /// 发生时的例外 grain 尝试在错误/ 缺失存储服务提供商配置设置的情况下拨打电话 grain.
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
        /// <param name="msg">描述错误的电文。</param>
        public BadProviderConfigException(string msg)
            : base(msg)
        { }

        /// <summary>
        /// 初始化 <see cref="BadProviderConfigException"/> 类的新实例。
        /// </summary>
        /// <param name="msg">描述错误的电文。</param>
        /// <param name="exc">造成当前例外的例外。</param>
        public BadProviderConfigException(string msg, Exception exc)
            : base(msg, exc)
        { }

        [Obsolete]
        private BadProviderConfigException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }
}
