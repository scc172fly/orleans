using Orleans.Streams;
using SQSMessage = Amazon.SQS.Model.Message;

namespace Orleans.Streaming.SQS.Streams;

/// <summary>
/// 在 Orleans 流批次与 Amazon SQS 消息之间转换。
/// </summary>
public interface ISQSDataAdapter : IQueueDataAdapter<SQSMessage, IBatchContainer>;
