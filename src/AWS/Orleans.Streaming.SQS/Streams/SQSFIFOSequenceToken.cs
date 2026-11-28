using System;
using System.Globalization;
using Newtonsoft.Json;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace OrleansAWSUtils.Streams;

/// <summary>
/// 跟踪序列号和事件索引的流序列令牌。
/// </summary>
[Serializable]
[GenerateSerializer]
public class SQSFIFOSequenceToken : StreamSequenceToken
{
    /// <summary>
    /// 获取此 SQS FIFO 序列令牌关联的 StreamId，
    /// 因为 SqsSequenceNumber 按 StreamId（SQS MessageGroupId）划分。
    /// </summary>
    [Id(2)]
    [JsonProperty]
    public StreamId StreamId { get; set; }

    /// <summary>
    /// 获取此消息在其消息组内的 Amazon SQS FIFO 序列号。
    /// </summary>
    [Id(0)]
    [JsonProperty]
    public UInt128 SqsSequenceNumber { get; set; }


    /// <summary>
    /// 获取用于队列缓存排序及进度跟踪的接收器本地序列号。
    /// </summary>
    [Id(3)]
    [JsonProperty]
    public override long SequenceNumber { get; protected set; }

    /// <summary>
    /// 获取同一批次中位于此事件之前的事件数量（即此事件在批次中的索引）。
    /// </summary>
    [Id(1)]
    [JsonProperty]
    public override int EventIndex { get; protected set; }

    /// <summary>
    /// 初始化 <see cref="SQSFIFOSequenceToken"/> 类的新实例。
    /// </summary>
    /// <param name="streamId">与令牌关联的流。</param>
    /// <param name="seqNumber">SQS 序列号。</param>
    /// <param name="sequenceNumber">接收器本地序列号。</param>
    public SQSFIFOSequenceToken(StreamId streamId, UInt128 seqNumber, long sequenceNumber)
    {
        StreamId = streamId;
        SqsSequenceNumber = seqNumber;
        SequenceNumber = sequenceNumber;
        EventIndex = 0;
    }

    /// <summary>
    /// 初始化 <see cref="SQSFIFOSequenceToken"/> 类的新实例。
    /// </summary>
    /// <param name="streamId">此令牌关联的流标识。</param>
    /// <param name="seqNumber">SQS 序列号。</param>
    /// <param name="sequenceNumber">接收器本地序列号。</param>
    /// <param name="eventInd">事件索引，适用于属于某个事件批次的事件。</param>
    public SQSFIFOSequenceToken(StreamId streamId, UInt128 seqNumber, long sequenceNumber, int eventInd)
    {
        StreamId = streamId;
        SqsSequenceNumber = seqNumber;
        SequenceNumber = sequenceNumber;
        EventIndex = eventInd;
    }

    /// <summary>
    /// 初始化 <see cref="SQSFIFOSequenceToken"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// 此构造函数仅供序列化器使用。
    /// </remarks>
    public SQSFIFOSequenceToken()
    {
    }

    /// <summary>
    /// 为当前批次中的指定事件创建序列令牌。
    /// </summary>
    /// <param name="eventInd">事件索引。</param>
    /// <returns>新的序列令牌。</returns>
    public SQSFIFOSequenceToken CreateSequenceTokenForEvent(int eventInd)
    {
        return new SQSFIFOSequenceToken(StreamId, SqsSequenceNumber, SequenceNumber, eventInd);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return Equals(obj as SQSFIFOSequenceToken);
    }

    /// <inheritdoc/>
    public override bool Equals(StreamSequenceToken? other)
    {
        var token = other as SQSFIFOSequenceToken;
        if (token == null)
            return false;

        if (token.StreamId != StreamId)
            return false;

        return (token.SqsSequenceNumber == SqsSequenceNumber &&
                token.EventIndex == EventIndex);
    }

    /// <inheritdoc/>
    public override int CompareTo(StreamSequenceToken? other)
    {
        if (other == null)
            return 1;

        var token = other as SQSFIFOSequenceToken;
        if (token == null)
            throw new ArgumentOutOfRangeException(nameof(other));

        if (token.StreamId != StreamId)
        {
            var sequenceDifference = SequenceNumber.CompareTo(token.SequenceNumber);
            return sequenceDifference != 0 ? sequenceDifference : StreamId.CompareTo(token.StreamId);
        }

        var difference = SqsSequenceNumber.CompareTo(token.SqsSequenceNumber);
        return difference != 0 ? difference : EventIndex.CompareTo(token.EventIndex);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(StreamId, SqsSequenceNumber, EventIndex);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "[SQSFIFOSequenceToken: SeqNum={0}, EventIndex={1}, StreamId={2}]", SqsSequenceNumber, EventIndex, StreamId);
    }
}
