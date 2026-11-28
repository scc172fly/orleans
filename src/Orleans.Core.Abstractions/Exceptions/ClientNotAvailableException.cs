using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 指示客户端已无法访问。
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
        /// 客户端 ID。
        /// </param>
        internal ClientNotAvailableException(GrainId clientId)
            : base($"No activation for client {clientId}")
        {
        }

        /// <summary>
        /// 初始化 <see cref="ClientNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 异常消息。
        /// </param>
        internal ClientNotAvailableException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ClientNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        /// <param name="innerException">
        /// 内部异常。
        /// </param>
        internal ClientNotAvailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ClientNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        [Obsolete]
        private ClientNotAvailableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

