using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 指定耐久的队列。
    /// 使用 Orleans 流扩展。
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
        /// 初始化 <see cref="QueueId"/> 类的新实例。
        /// </summary>
        /// <param name="queuePrefix">队列前缀。</param>
        /// <param name="id">识别符号。</param>
        /// <param name="hash">杂草。</param>
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
        /// <param name="hash">杂草。</param>
        /// <returns>队列标识符。</returns>
        public static QueueId GetQueueId(string queueName, uint queueId, uint hash) => new(queueName, queueId, hash);

        /// <summary>
        /// 获取队列名称前缀。
        /// </summary>
        /// <returns>队列名称前缀。</returns>
        public string GetStringNamePrefix() => queueNamePrefix;

        /// <summary>
        /// 获得数字识别码。
        /// </summary>
        /// <returns>数字识别符号。</returns>
        public uint GetNumericId() => queueId;

        /// <inheritdoc/>
        public uint GetUniformHashCode() => uniformHashCache;

        /// <summary>
        /// 获得一个值,以显示此实例是否为默认实例。
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
        /// 比较了两个队列平等标识符。
        /// </summary>
        /// <param name="left">第一个队列标识符。</param>
        /// <param name="right">第二个队列标识符。</param>
        /// <returns><see langword="true"/> ,如果两个识别符号相等;否则, <see langword="false"/>。</returns>
        public static bool operator ==(QueueId left, QueueId right) => left.Equals(right);

        /// <summary>
        /// 将不平等的队列识别码比作两个。
        /// </summary>
        /// <param name="left">第一个队列标识符。</param>
        /// <param name="right">第二个队列标识符。</param>
        /// <returns><see langword="true"/> ,如果识别符号不同;否则, <see langword="false"/>。</returns>
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
        /// 返回此实例的字符串表示, 包括统一的散列代码。
        /// </summary>
        /// <returns>此实例的字符串表示, 包括统一的散列代码。</returns>
        public string ToStringWithHashCode() => $"{this:H}";
    }
}
