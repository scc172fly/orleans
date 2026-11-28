using System;
using System.Globalization;
using System.Runtime.Serialization;

namespace Orleans.Streams
{
    /// <summary>
    /// 表示所请求的消息不在队列缓存中的异常。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class QueueCacheMissException : DataNotAvailableException
    {
        private const string MESSAGE_FORMAT = "Item not found in cache.  Requested: {0}, Low: {1}, High: {2}";

        /// <summary>
        /// 获取请求的序列令牌。
        /// </summary>
        /// <value>请求的序列令牌。</value>
        [Id(0)]
        public string? Requested { get; private set; }

        /// <summary>
        /// 获取最早可用的序列令牌。
        /// </summary>
        [Id(1)]
        public string? Low { get; private set; }

        /// <summary>
        /// 获取最新可用的序列令牌。
        /// </summary>
        [Id(2)]
        public string? High { get; private set; }

        /// <summary>
        /// 初始化 <see cref="QueueCacheMissException"/> 类的新实例。
        /// </summary>
        public QueueCacheMissException() : this("Item no longer in cache") { }

        /// <summary>
        /// 初始化 <see cref="QueueCacheMissException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        public QueueCacheMissException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="QueueCacheMissException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        public QueueCacheMissException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="QueueCacheMissException"/> 类的新实例。
        /// </summary>
        /// <param name="requested">请求的序列令牌。</param>
        /// <param name="low">最早可用的序列令牌。</param>
        /// <param name="high">最新可用的序列令牌。</param>
        public QueueCacheMissException(StreamSequenceToken requested, StreamSequenceToken low, StreamSequenceToken high)
            : this(GetTokenString(requested, nameof(requested)), GetTokenString(low, nameof(low)), GetTokenString(high, nameof(high)))
        {
        }

        private static string? GetTokenString(StreamSequenceToken token, string parameterName)
        {
            ArgumentNullException.ThrowIfNull(token, parameterName);
            return token.ToString();
        }

        /// <summary>
        /// 初始化 <see cref="QueueCacheMissException"/> 类的新实例。
        /// </summary>
        /// <param name="requested">请求的序列令牌。</param>
        /// <param name="low">最早可用的序列令牌。</param>
        /// <param name="high">最新可用的序列令牌。</param>
        public QueueCacheMissException(string? requested, string? low, string? high)
            : this(string.Format(CultureInfo.InvariantCulture, MESSAGE_FORMAT, requested, low, high))
        {
            Requested = requested;
            Low = low;
            High = high;
        }

        /// <summary>
        /// 初始化 <see cref="QueueCacheMissException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private QueueCacheMissException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            Requested = info.GetString("Requested");
            Low = info.GetString("Low");
            High = info.GetString("High");
        }

        /// <inheritdoc/>
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info);
            info.AddValue("Requested", Requested);
            info.AddValue("Low", Low);
            info.AddValue("High", High);
            base.GetObjectData(info, context);
        }
    }
}
