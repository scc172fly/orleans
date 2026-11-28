using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示某个 Grain 处于过载状态，当前已超出运行时的某项限制设置，
    /// 因而该 Grain 目前无法接收正在发送的消息。
    /// </summary>
    /// <remarks>
    /// 这种情况通常是暂时的。
    /// 如果稍后重新发送该消息，该 Grain 很可能会接收它。
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
        /// 消息。
        /// </param>
        public LimitExceededException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="LimitExceededException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        /// <param name="innerException">
        /// 内部异常。
        /// </param>
        public LimitExceededException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="LimitExceededException"/> 类的新实例。
        /// </summary>
        /// <param name="limitName">
        /// 限制项名称。
        /// </param>
        /// <param name="current">
        /// 当前值。
        /// </param>
        /// <param name="threshold">
        /// 阈值。
        /// </param>
        /// <param name="extraInfo">
        /// 额外的描述性信息。
        /// </param>
        public LimitExceededException(string limitName, int current, int threshold, object extraInfo)
            : base($"Limit exceeded {limitName} Current={current} Threshold={threshold} {extraInfo}")
        {
        }

        /// <summary>
        /// 初始化 <see cref="LimitExceededException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        [Obsolete]
        private LimitExceededException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

