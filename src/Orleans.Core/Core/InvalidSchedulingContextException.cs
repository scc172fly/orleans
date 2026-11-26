using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{

    /// <summary>
    /// 表示试图对无效的 SchedulingContext进行操作。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    internal sealed class InvalidSchedulingContextException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="InvalidSchedulingContextException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        public InvalidSchedulingContextException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="InvalidSchedulingContextException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        /// <param name="innerException">
        /// 内在的例外
        /// </param>
        public InvalidSchedulingContextException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="InvalidSchedulingContextException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        [Obsolete]
        private InvalidSchedulingContextException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}

