using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime.Messaging
{
    /// <summary>
    /// 指示连接失败。
    /// </summary>
    public class ConnectionFailedException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="ConnectionFailedException"/> 类的新实例。
        /// </summary>
        public ConnectionFailedException()
        {
        }

        /// <summary>
        /// 初始化 <see cref="ConnectionFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        public ConnectionFailedException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ConnectionFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        public ConnectionFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ConnectionFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        protected ConnectionFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
