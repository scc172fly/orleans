using System;
using System.Globalization;
using Newtonsoft.Json;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Orleans.Streaming.Redis;

/// <summary>
/// 表示 Redis 流条目的位置及该条目中事件的位置。
/// </summary>
[Serializable]
[GenerateSerializer]
internal sealed class RedisStreamSequenceToken : EventSequenceTokenV2
{
    /// <summary>
    /// 初始化 <see cref="RedisStreamSequenceToken"/> 类的新实例。
    /// </summary>
    /// <param name="entryId">Redis 条目标识符。</param>
    /// <param name="sequenceNumber">Redis 条目标识符的毫秒部分。</param>
    /// <param name="redisSequenceNumber">Redis 每毫秒内的序列号。</param>
    /// <param name="eventIndex">事件在 Orleans 批次中的索引。</param>
    public RedisStreamSequenceToken(string entryId, long sequenceNumber, long redisSequenceNumber, int eventIndex)
        : base(sequenceNumber, eventIndex)
    {
        EntryId = entryId;
        RedisSequenceNumber = redisSequenceNumber;
    }

    /// <summary>
    /// 初始化 <see cref="RedisStreamSequenceToken"/> 类的新实例。
    /// </summary>
    public RedisStreamSequenceToken()
    {
    }

    /// <summary>
    /// 获取 Redis 条目标识符。
    /// </summary>
    [Id(2)]
    [JsonProperty]
    public string EntryId { get; private set; } = string.Empty;

    /// <summary>
    /// 获取 Redis 每毫秒内的序列号。
    /// </summary>
    [Id(3)]
    [JsonProperty]
    public long RedisSequenceNumber { get; private set; }

    /// <inheritdoc />
    protected override Type SequenceTokenCompatibilityDomain => typeof(RedisStreamSequenceToken);

    /// <summary>
    /// 为同一个 Redis 条目中的某个事件创建令牌。
    /// </summary>
    /// <param name="eventIndex">事件在 Orleans 批次中的索引。</param>
    /// <returns>指定事件的令牌。</returns>
    public override RedisStreamSequenceToken CreateSequenceTokenForEvent(int eventIndex)
        => new(EntryId, SequenceNumber, RedisSequenceNumber, eventIndex);

    /// <inheritdoc />
    public override bool Equals(StreamSequenceToken? other)
    {
        return other is RedisStreamSequenceToken token
            && token.SequenceNumber == SequenceNumber
            && token.RedisSequenceNumber == RedisSequenceNumber
            && token.EventIndex == EventIndex
            && string.Equals(token.EntryId, EntryId, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override int CompareTo(StreamSequenceToken? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (other is not RedisStreamSequenceToken token)
        {
            throw new ArgumentOutOfRangeException(nameof(other));
        }

        var difference = SequenceNumber.CompareTo(other.SequenceNumber);
        if (difference != 0)
        {
            return difference;
        }

        difference = RedisSequenceNumber.CompareTo(token.RedisSequenceNumber);
        if (difference != 0)
        {
            return difference;
        }

        difference = EventIndex.CompareTo(other.EventIndex);
        return difference != 0
            ? difference
            : string.CompareOrdinal(EntryId, token.EntryId);
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(EntryId, SequenceNumber, RedisSequenceNumber, EventIndex);

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "[RedisStreamSequenceToken: EntryId={0}, SeqNum={1}, RedisSeqNum={2}, EventIndex={3}]",
            EntryId,
            SequenceNumber,
            RedisSequenceNumber,
            EventIndex);
    }
}
