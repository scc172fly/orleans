using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示网关 Silo 当前处于过载或负载削减状态，
    /// 因而目前无法接收正在发送的这条消息。
    /// </summary>
    /// <remarks>
    /// 这种情况通常是暂时的。
    /// 如果稍后重新发送该消息，此网关或其他网关很可能会接收它。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public sealed class GatewayTooBusyException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="GatewayTooBusyException"/> 类的新实例。
        /// </summary>
        public GatewayTooBusyException()
            : base("Gateway too busy")
        {
        }

        /// <summary>
        /// 初始化 <see cref="GatewayTooBusyException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        public GatewayTooBusyException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GatewayTooBusyException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        /// <param name="innerException">
        /// 内部异常。
        /// </param>
        public GatewayTooBusyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GatewayTooBusyException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        [Obsolete]
        private GatewayTooBusyException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

