using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 显示客户无法更长久地接触。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
#pragma warning disable RCS1194 // Implement exception constructors.
    public sealed class ClientNotAvailableException : OrleansException
#pragma warning restore RCS1194 // Implement exception constructors.
    {
        /// <summary>
        /// 初始化 <see cref="ClientNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="clientId">
        /// 客户身份
        /// </param>
        internal ClientNotAvailableException(GrainId clientId)
            : base($"No activation for client {clientId}")
        {
        }

        /// <summary>
        /// 初始化 <see cref="ClientNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 例外信息。
        /// </param>
        internal ClientNotAvailableException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ClientNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        /// <param name="innerException">
        /// 内在的例外
        /// </param>
        internal ClientNotAvailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ClientNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 资料
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        [Obsolete]
        private ClientNotAvailableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

