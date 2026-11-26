using System;
using System.Globalization;
using Newtonsoft.Json;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace OrleansAWSUtils.Streams;

/// <summary>
/// 跟踪序列编号和事件索引的串流序列符号
/// </summary>
[Serializable]
[GenerateSerializer]
public class SQSFIFOSequenceToken : StreamSequenceToken
{
    /// <summary>
    /// 获取 SQS FIFO 序列符号的 StreamId
    /// 以当为 SqsSequenceNumber 分割由 StreamId (SQS MessageGroupId)
    /// </summary>
    [Id(2)]
    [JsonProperty]
    public StreamId StreamId { get; set; }

    /// <summary>
    /// 在其信息组中获取此信件的 Amazon SQS FIFO 序列号。
    /// </summary>
    [Id(0)]
    [JsonProperty]
    public UInt128 SqsSequenceNumber { get; set; }


    /// <summary>
    /// 获取用于队列缓存订购和进度的接收器- 本地序列编号。
    /// </summary>
    [Id(3)]
    [JsonProperty]
    public override long SequenceNumber { get; protected set; }

    /// <summary>
    /// 获取此事件之前的批次事件数量
    /// </summary>
    [Id(1)]
    [JsonProperty]
    public override int EventIndex { get; protected set; }

    /// <summary>
    /// 初始化 <see cref="SQSFIFOSequenceToken"/> 类的新实例。
    /// </summary>
    /// <param name="streamId">与该符号相关的流。</param>
    /// <param name="seqNumber">序列号。</param>
    /// <param name="sequenceNumber">接收器- 本地序列号。</param>
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
    /// <param name="streamId">此符号相关的流代号。</param>
    /// <param name="seqNumber">序列号。</param>
    /// <param name="sequenceNumber">接收器- 本地序列号。</param>
    /// <param name="eventInd">事件索引,用于一系列活动的一部分。</param>
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
    /// 此构建器仅用于序列器 。
    /// </remarks>
    public SQSFIFOSequenceToken()
    {
    }

    /// <summary>
    /// 为当前批量中特定事件创建序列符号
    /// </summary>
    /// <param name="eventInd">事件索引。</param>
    /// <returns>新的序列符号。</returns>
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
