using Orleans.Streams;
using SQSMessage = Amazon.SQS.Model.Message;

namespace Orleans.Streaming.SQS.Streams;

/// <summary>
/// 转换 Orleans 流集到亚马逊 SQS 信息。
/// </summary>
public interface ISQSDataAdapter : IQueueDataAdapter<SQSMessage, IBatchContainer>;
