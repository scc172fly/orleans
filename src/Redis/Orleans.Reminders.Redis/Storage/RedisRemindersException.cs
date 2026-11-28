using System;
using System.Runtime.Serialization;

namespace Orleans.Reminders.Redis
{
    /// <summary>
    /// 从 <see cref="RedisReminderTable"/> 抛出的异常。
    /// </summary>
    [GenerateSerializer]
    public class RedisRemindersException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="RedisRemindersException"/> 的新实例。
        /// </summary>
        public RedisRemindersException()
        {
        }

        /// <summary>
        /// 初始化 <see cref="RedisRemindersException"/> 的新实例。
        /// </summary>
        /// <param name="message">解释异常原因的错误消息。</param>
        public RedisRemindersException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="RedisRemindersException"/> 的新实例。
        /// </summary>
        /// <param name="message">解释异常原因的错误消息。</param>
        /// <param name="inner">导致当前异常的异常；如果未指定内部异常，则为空引用（在 Visual Basic 中为 Nothing）。</param>
        public RedisRemindersException(string message, Exception inner) : base(message, inner)
        {
        }

        /// <inheritdoc />
        [Obsolete]
        protected RedisRemindersException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }
}
