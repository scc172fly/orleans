using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 例外是指没有所要求的数据。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class DataNotAvailableException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="DataNotAvailableException"/> 类的新实例。
        /// </summary>
        public DataNotAvailableException() : this("Data not found") { }

        /// <summary>
        /// 初始化 <see cref="DataNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        public DataNotAvailableException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="DataNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="inner">内心</param>
        public DataNotAvailableException(string message, Exception inner) : base(message, inner) { }

        /// <summary>
        /// 初始化 <see cref="DataNotAvailableException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        protected DataNotAvailableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示队列信件缓存已满 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class CacheFullException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="CacheFullException"/> 类的新实例。
        /// </summary>
        public CacheFullException() : this("Queue message cache is full") { }

        /// <summary>
        /// 初始化 <see cref="CacheFullException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        public CacheFullException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="CacheFullException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="inner">内心</param>
        public CacheFullException(string message, Exception inner) : base(message, inner) { }

        /// <summary>
        /// 初始化 <see cref="CacheFullException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private CacheFullException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
