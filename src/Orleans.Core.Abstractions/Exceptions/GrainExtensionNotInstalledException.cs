using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示尝试调用某个 Grain 的扩展方法，但该 Grain 未安装相应扩展。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class GrainExtensionNotInstalledException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="GrainExtensionNotInstalledException"/> 类的新实例。
        /// </summary>
        public GrainExtensionNotInstalledException()
            : base("GrainExtensionNotInstalledException")
        {
        }

        /// <summary>
        /// 初始化 <see cref="GrainExtensionNotInstalledException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        public GrainExtensionNotInstalledException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GrainExtensionNotInstalledException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        /// <param name="innerException">
        /// 内部异常。
        /// </param>
        public GrainExtensionNotInstalledException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GrainExtensionNotInstalledException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        [Obsolete]
        private GrainExtensionNotInstalledException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

