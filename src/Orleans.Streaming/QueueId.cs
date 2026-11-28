using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 持久化队列的标识符。
    /// 由 Orleans 流处理扩展使用。
    /// </summary>
    [Serializable]
    [Immutable]
    [GenerateSerializer]
    public readonly struct QueueId : IEquatable<QueueId>, IComparable<QueueId>, ISpanFormattable
    {
        [Id(0)]
        private readonly string queueNamePrefix;
        [Id(1)]
        private readonly uint queueId;
        [Id(2)]
        private readonly uint uniformHashCache;

        /// <summary>
        /// 初始化 <see cref="QueueId"/> 类的新实例。（译注：当前 QueueId 实际声明为只读结构体。）
        /// </summary>
        /// <param name="queuePrefix">队列前缀。</param>
        /// <param name="id">标识符。</param>
        /// <param name="hash">哈希值。</param>
        private QueueId(string queuePrefix, uint id, uint hash)
        {
            queueNamePrefix = queuePrefix ?? throw new ArgumentNullException(nameof(queuePrefix));
            queueId = id;
            uniformHashCache = hash;
        }

        /// <summary>
        /// 获取队列标识符。
        /// </summary>
        /// <param name="queueName">队列名称。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="hash">哈希值。</param>
        /// <returns>队列标识符。</returns>
        public static QueueId GetQueueId(string queueName, uint queueId, uint hash) => new(queueName, queueId, hash);

        /// <summary>
        /// 获取队列名称前缀。
        /// </summary>
        /// <returns>队列名称前缀。</returns>
        public string GetStringNamePrefix() => queueNamePrefix;

        /// <summary>
        /// 获取数值标识符。
        /// </summary>
        /// <returns>数值标识符。</returns>
        public uint GetNumericId() => queueId;

        /// <inheritdoc/>
        public uint GetUniformHashCode() => uniformHashCache;

        /// <summary>
        /// 获取一个值，指示此实例是否为默认实例。
        /// </summary>
        public bool IsDefault => queueNamePrefix is null;

        /// <inheritdoc/>
        public int CompareTo(QueueId other)
        {
            if (queueId != other.queueId)
                return queueId.CompareTo(other.queueId);

            var cmp = string.CompareOrdinal(queueNamePrefix, other.queueNamePrefix);
            if (cmp != 0) return cmp;

            return uniformHashCache.CompareTo(other.uniformHashCache);
        }

        /// <inheritdoc/>
        public bool Equals(QueueId other) => queueId == other.queueId && uniformHashCache == other.uniformHashCache && queueNamePrefix == other.queueNamePrefix;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is QueueId queueId && Equals(queueId);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(queueId, uniformHashCache, queueNamePrefix);

        /// <summary>
        /// 比较两个队列标识符是否相等。
        /// </summary>
        /// <param name="left">第一个队列标识符。</param>
        /// <param name="right">第二个队列标识符。</param>
        /// <returns>如果两个标识符相等，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        public static bool operator ==(QueueId left, QueueId right) => left.Equals(right);

        /// <summary>
        /// 比较两个队列标识符是否不相等。
        /// </summary>
        /// <param name="left">第一个队列标识符。</param>
        /// <param name="right">第二个队列标识符。</param>
        /// <returns>如果两个标识符不同，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        public static bool operator !=(QueueId left, QueueId right) => !(left == right);

        /// <inheritdoc/>
        public override string ToString() => $"{this}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            var len = queueNamePrefix.AsSpan().ToLowerInvariant(destination);
            if (len >= 0 && destination[len..].TryWrite($"-{queueId}", out var len2))
            {
                len += len2;

                if (format.Length == 1 && format[0] == 'H')
                {
                    if (!destination[len..].TryWrite($"-0x{uniformHashCache:X8}", out len2))
                    {
                        charsWritten = 0;
                        return false;
                    }
                    len += len2;
                }

                charsWritten = len;
                return true;
            }

            charsWritten = 0;
            return false;
        }

        /// <summary>
        /// 返回此实例的字符串表示形式，其中包含均匀哈希码。
        /// </summary>
        /// <returns>此实例的字符串表示形式，其中包含均匀哈希码。</returns>
        public string ToStringWithHashCode() => $"{this:H}";
    }
}
