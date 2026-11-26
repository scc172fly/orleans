using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示有人试图援引 grain 用于 grain 尚未安装该扩展名的。
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
        /// 信息
        /// </param>
        public GrainExtensionNotInstalledException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GrainExtensionNotInstalledException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        /// <param name="innerException">
        /// 内在的例外
        /// </param>
        public GrainExtensionNotInstalledException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GrainExtensionNotInstalledException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        [Obsolete]
        private GrainExtensionNotInstalledException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

