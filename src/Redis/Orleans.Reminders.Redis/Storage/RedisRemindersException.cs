using System;
using System.Runtime.Serialization;

namespace Orleans.Reminders.Redis
{
    /// <summary>
    /// 被扔出 <see cref="RedisReminderTable"/>.
    /// </summary>
    [GenerateSerializer]
    public class RedisRemindersException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="RedisRemindersException"/>新实例。
        /// </summary>
        public RedisRemindersException()
        {
        }

        /// <summary>
        /// 初始化 <see cref="RedisRemindersException"/>新实例。
        /// </summary>
        /// <param name="message">解释例外原因的错误信息。</param>
        public RedisRemindersException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="RedisRemindersException"/>新实例。
        /// </summary>
        /// <param name="message">解释例外原因的错误信息。</param>
        /// <param name="inner">造成目前例外的原因,或者如果没有具体说明内部例外,则无效引用(《视觉基本》中没有任何内容)。</param>
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
