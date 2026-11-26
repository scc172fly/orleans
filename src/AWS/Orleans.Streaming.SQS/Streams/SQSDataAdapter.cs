using System;
using System.Collections.Generic;
using Orleans.Runtime;
using Orleans.Streams;
using OrleansAWSUtils.Streams;
using SQSMessage = Amazon.SQS.Model.Message;

namespace Orleans.Streaming.SQS.Streams;

/// <summary>
/// 默认 SQS 流数据适配器。 用户可以将事件数据覆盖为流图的子类 。
/// </summary>

public class SQSDataAdapter : ISQSDataAdapter
{
    private readonly Serialization.Serializer serializer;

    /// <summary>
    /// 初始化 <see cref="SQSDataAdapter"/> 类的新实例。
    /// </summary>
    /// <param name="serializer">用于串行和分批清除流体的序列器。</param>
    public SQSDataAdapter(Serialization.Serializer serializer)
    {
        this.serializer = serializer;
    }

    /// <summary>
    /// 将 SQS 消息转换为 Orleans 串流批量。
    /// </summary>
    /// <param name="sqsMessage">SQS 信件要转换。</param>
    /// <param name="sequenceId">流批量的本地分配序列号 。</param>
    /// <returns>流组代表的批次 <paramref name="sqsMessage"/>.</returns>
    public virtual IBatchContainer FromQueueMessage(SQSMessage sqsMessage, long sequenceId)
    {
        return SQSBatchContainer.FromSQSMessage(
            serializer.GetSerializer<SQSBatchContainer>(),
            sqsMessage,
            sequenceId);
    }

    /// <summary>
    /// 将批量的 Orleans 流事件转换成 SQS 消息。
    /// </summary>
    /// <typeparam name="T">流活动的类型。</typeparam>
    /// <param name="streamId">流体识别符号。</param>
    /// <param name="events">信件中要包含的流事件。</param>
    /// <param name="token">流序序列符号, 必须是 <see langword="null"/>。</param>
    /// <param name="requestContext">请求在电文中包含上下文。</param>
    /// <returns>包含串联流批量的 SQS 信息。</returns>
    /// <exception cref="ArgumentException"><paramref name="token"/> 不是 <see langword="null"/>。</exception>
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
