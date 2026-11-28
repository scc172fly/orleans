using System;
using System.Runtime.Serialization;

namespace Orleans.Persistence.Redis
{
    /// <summary>
    /// Redis Grain 存储抛出的异常。
    /// </summary>
    [GenerateSerializer]
    public class RedisStorageException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="RedisStorageException"/> 的新实例。
        /// </summary>
        public RedisStorageException()
        {
        }

        /// <summary>
        /// 初始化 <see cref="RedisStorageException"/> 的新实例。
        /// </summary>
        /// <param name="message">解释异常原因的错误消息。</param>
        public RedisStorageException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="RedisStorageException"/> 的新实例。
        /// </summary>
        /// <param name="message">解释异常原因的错误消息。</param>
        /// <param name="inner">导致当前异常的异常；如果未指定内部异常，则为空引用（在 Visual Basic 中为 Nothing）。</param>
        public RedisStorageException(string message, Exception inner) : base(message, inner)
        {
        }

        /// <inheritdoc />
        [Obsolete]
        protected RedisStorageException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }
}
