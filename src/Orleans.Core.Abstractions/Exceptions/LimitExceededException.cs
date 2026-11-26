using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示 grain 处于超载状态,目前超过某些运行时限规定,
    /// 因而, grain 无法接收正在发送的信件。
    /// </summary>
    /// <remarks>
    /// 这种情况往往是暂时性的。
    /// 如果该电文在稍后时间再次发送,本 grain 可能接受。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public sealed class LimitExceededException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="LimitExceededException"/> 类的新实例。
        /// </summary>
        public LimitExceededException()
            : base("Limit exceeded")
        {
        }

        /// <summary>
        /// 初始化 <see cref="LimitExceededException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        public LimitExceededException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="LimitExceededException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        /// <param name="innerException">
        /// 内在的例外
        /// </param>
        public LimitExceededException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="LimitExceededException"/> 类的新实例。
        /// </summary>
        /// <param name="limitName">
        /// 限号
        /// </param>
        /// <param name="current">
        /// 现值。
        /// </param>
        /// <param name="threshold">
        /// 门槛值。
        /// </param>
        /// <param name="extraInfo">
        /// 额外,描述性信息。
        /// </param>
        public LimitExceededException(string limitName, int current, int threshold, object extraInfo)
            : base($"Limit exceeded {limitName} Current={current} Threshold={threshold} {extraInfo}")
        {
        }

        /// <summary>
        /// 初始化 <see cref="LimitExceededException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        [Obsolete]
        private LimitExceededException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

