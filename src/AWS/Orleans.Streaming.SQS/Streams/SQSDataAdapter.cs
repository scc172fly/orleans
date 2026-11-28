using System;
using System.Collections.Generic;
using Orleans.Runtime;
using Orleans.Streams;
using OrleansAWSUtils.Streams;
using SQSMessage = Amazon.SQS.Model.Message;

namespace Orleans.Streaming.SQS.Streams;

/// <summary>
/// 默认 SQS 流数据适配器。用户可通过派生类重写事件数据到流的映射。
/// </summary>

public class SQSDataAdapter : ISQSDataAdapter
{
    private readonly Serialization.Serializer serializer;

    /// <summary>
    /// 初始化 <see cref="SQSDataAdapter"/> 类的新实例。
    /// </summary>
    /// <param name="serializer">用于序列化和反序列化流批次的序列化器。</param>
    public SQSDataAdapter(Serialization.Serializer serializer)
    {
        this.serializer = serializer;
    }

    /// <summary>
    /// 将 SQS 消息转换为 Orleans 流批次。
    /// </summary>
    /// <param name="sqsMessage">要转换的 SQS 消息。</param>
    /// <param name="sequenceId">在本地为流批次分配的序列号。</param>
    /// <returns>由 <paramref name="sqsMessage"/> 表示的流批次。</returns>
    public virtual IBatchContainer FromQueueMessage(SQSMessage sqsMessage, long sequenceId)
    {
        return SQSBatchContainer.FromSQSMessage(
            serializer.GetSerializer<SQSBatchContainer>(),
            sqsMessage,
            sequenceId);
    }

    /// <summary>
    /// 将一批 Orleans 流事件转换为 SQS 消息。
    /// </summary>
    /// <typeparam name="T">流事件的类型。</typeparam>
    /// <param name="streamId">流标识符。</param>
    /// <param name="events">要包含在消息中的流事件。</param>
    /// <param name="token">流序列令牌，必须为 <see langword="null"/>。</param>
    /// <param name="requestContext">要包含在消息中的请求上下文。</param>
    /// <returns>包含序列化流批次的 SQS 消息。</returns>
    /// <exception cref="ArgumentException"><paramref name="token"/> 不为 <see langword="null"/>。</exception>
    public virtual SQSMessage ToQueueMessage<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext)
    {
        if (token != null) throw new ArgumentException("SQS streams currently does not support non-null StreamSequenceToken.", nameof(token));
        return SQSBatchContainer.ToSQSMessage(
            serializer.GetSerializer<SQSBatchContainer>(),
            streamId,
            events,
            requestContext);
    }
}
