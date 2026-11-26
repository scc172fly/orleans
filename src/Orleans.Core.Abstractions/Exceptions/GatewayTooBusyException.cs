using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示连接器 silo 目前处于超载/负负荷停止状态
    /// 无法接受此信件发送。
    /// </summary>
    /// <remarks>
    /// 这种情况通常是暂时性的。
    /// 如果该电文稍后再发送,则本端或另一网关可能接受。
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
        /// 信息
        /// </param>
        public GatewayTooBusyException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GatewayTooBusyException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        /// <param name="innerException">
        /// 内在的例外
        /// </param>
        public GatewayTooBusyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GatewayTooBusyException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        [Obsolete]
        private GatewayTooBusyException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

