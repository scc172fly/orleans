using System;
using System.Runtime.Serialization;

namespace Orleans.Persistence.Redis
{
    /// <summary>
    /// 丢弃 Redis grain 存储的例外。
    /// </summary>
    [GenerateSerializer]
    public class RedisStorageException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="RedisStorageException"/>新实例。
        /// </summary>
        public RedisStorageException()
        {
        }

        /// <summary>
        /// 初始化 <see cref="RedisStorageException"/>新实例。
        /// </summary>
        /// <param name="message">解释例外原因的错误信息。</param>
        public RedisStorageException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="RedisStorageException"/>新实例。
        /// </summary>
        /// <param name="message">解释例外原因的错误信息。</param>
        /// <param name="inner">造成目前例外的原因,或者如果没有具体说明内部例外,则无效引用(《视觉基本》中没有任何内容)。</param>
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
