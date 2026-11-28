using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{

    /// <summary>
    /// 表示尝试在无效的 SchedulingContext 中执行操作。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    internal sealed class InvalidSchedulingContextException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="InvalidSchedulingContextException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        public InvalidSchedulingContextException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="InvalidSchedulingContextException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        /// <param name="innerException">
        /// 内部异常。
        /// </param>
        public InvalidSchedulingContextException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="InvalidSchedulingContextException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        [Obsolete]
        private InvalidSchedulingContextException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}

